using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Nạp match detail (matches/{id}) cho những ván mới chỉ có dữ liệu mức đội.
///
/// Đây là bước biến 5 chỉ số từ "số biên tập nhập tay" thành "số đo thật": assists,
/// first blood, mốc 10 mạng, và hai tỷ lệ có điều kiện đi kèm. Đồng thời điền SeriesId
/// (gom Bo3/Bo5 cho H2H) và tạo bảng MatchPlayer làm nền cho phân tích cá nhân.
///
/// Mỗi ván tốn MỘT request, nên có trần số ván mỗi vòng: một lần nạp bù toàn bộ lịch sử
/// kéo dài nhiều chục phút, và giữ transaction mở suốt thời gian đó là cách chắc chắn để
/// khoá DB. Nạp dần qua nhiều vòng thì mỗi vòng vẫn commit được.
/// </summary>
public class MatchDetailIngester(
    Ti2026DbContext db,
    OpenDotaClient client,
    ILogger<MatchDetailIngester> logger)
{
    /// <summary>
    /// Bộ trường đang trích từ match detail. TĂNG con số này khi thêm trường mới, và mọi ván
    /// cũ sẽ tự được nạp lại qua scheduler — không cần sửa SQL trên production.
    ///
    /// 1 = chỉ số cơ bản + first blood + mốc 10 mạng
    /// 2 = thêm bàn draft, mốc mua đồ, chỉ số lane
    /// 3 = thêm chỉ số hỗ trợ, mốc Roshan đầu tiên, và vàng dẫn trước bị mất
    /// 4 = thêm chỉ số fantasy: phá trụ, hạ Roshan/courier/mắt, và first blood theo người
    /// 5 = thêm hoa sen, watcher, smoke, túi madstone, Tormentor — năm chỉ số từng bị kết
    ///     luận nhầm là "OpenDota không có". Chúng nằm trong item_uses/ability_uses/killed.
    /// 6 = Roshan đếm lại từ killed[npc_dota_roshan] vì trường roshan_kills đếm dư (đã kiểm
    ///     ba chiều với objectives trên hai ván). Nâng ở đây để mọi ván đã nạp được sửa lại.
    /// 7 = thêm hoa sen đếm theo MÓN, song song với cách quy về bông gốc — chưa ai biết Valve
    ///     đếm kiểu nào và hai cách chênh 6 lần, nên nạp sẵn cả hai.
    /// 8 = thêm số lần CHẾT VÌ Tormentor (killed_by), cho suffix "the Tormented".
    /// </summary>
    public const int SchemaVersion = 8;

    /// <summary>
    /// Số lỗi LIÊN TIẾP thì dừng mẻ. Lỗi rải rác là chuyện thường (một ván OpenDota chưa parse
    /// xong), nhưng ba lỗi liền nhau gần như luôn là hết hạn mức trong ngày hoặc nguồn đang
    /// sập — cố thêm 190 lần nữa chỉ làm tệ hơn và có thể bị chặn IP.
    /// </summary>
    public const int MaxConsecutiveFailures = 3;

    public async Task<int> IngestAsync(int maxMatchesPerRun, CancellationToken ct)
    {
        await RelinkOrphanPlayersAsync(ct);

        // Ván mới nhất trước: phong độ gần đây là thứ đáng có sớm nhất, và nếu vì lý do gì
        // đó việc nạp bù không bao giờ hoàn tất thì phần thiếu là quá khứ xa, ít giá trị hơn.
        var pending = await db.Matches
            .Where(m => m.DetailsIngestedAt == null || m.DetailSchemaVersion < SchemaVersion)
            .OrderByDescending(m => m.StartTime)
            .Take(maxMatchesPerRun)
            .Select(m => m.Id)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            logger.LogInformation("Không còn ván nào cần nạp match detail");
            return 0;
        }

        var remaining = await db.Matches.CountAsync(
            m => m.DetailsIngestedAt == null || m.DetailSchemaVersion < SchemaVersion, ct);
        logger.LogInformation(
            "Nạp match detail cho {Batch} ván (còn tổng cộng {Remaining} ván chưa có detail)",
            pending.Count, remaining);

        // Bản đồ account_id -> Player của ta, để gắn MatchPlayer về đúng tuyển thủ
        var playersByAccount = await db.Players
            .Where(p => p.OpenDotaAccountId != null)
            .ToDictionaryAsync(p => p.OpenDotaAccountId!.Value, p => p.Id, ct);

        var done = 0;
        var failuresInARow = 0;

        foreach (var matchId in pending)
        {
            ct.ThrowIfCancellationRequested();

            OpenDotaMatchDetail detail;
            try
            {
                detail = await client.GetMatchAsync(matchId, ct);
                failuresInARow = 0;
            }
            // Host đang tắt: phải để lan ra, không được coi là lỗi tạm thời.
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            // Mọi lỗi mạng tạm thời khác. Bắt rộng có chủ đích: bản trước chỉ bắt
            // HttpRequestException, và một TaskCanceledException do timeout đã lọt qua rồi xoá
            // sạch công của hơn một trăm ván đã tải xong — vì orchestrator rollback cả mẻ.
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                             or TimeoutException)
            {
                failuresInARow++;
                logger.LogWarning(ex,
                    "Không lấy được match detail {MatchId} (lỗi liên tiếp thứ {Count}/{Max})",
                    matchId, failuresInARow, MaxConsecutiveFailures);

                // Dừng ĐẸP thay vì cố thêm 190 lần nữa. Thoát bằng break để hàm trả về bình
                // thường, nhờ đó orchestrator COMMIT phần đã nạp được. Ném ra ở đây thì phần
                // đó mất trắng — mà nguyên nhân thường chỉ là hết hạn mức trong ngày, tức là
                // thử lại vòng sau chắc chắn được.
                if (failuresInARow >= MaxConsecutiveFailures)
                {
                    logger.LogWarning(
                        "Dừng mẻ sau {Count} lỗi liên tiếp — giữ {Done} ván đã nạp, phần còn lại "
                        + "để vòng sau. Thường là do hết hạn mức của nguồn.",
                        failuresInARow, done);
                    break;
                }

                continue;
            }

            await ApplyAsync(matchId, detail, playersByAccount, ct);

            // Đẩy xuống DB theo TỪNG ván rồi xoá change tracker, thay vì dồn tới cuối mẻ.
            // Lý do là mốc mua đồ: một ván sinh ~570 hàng, dồn 200 ván là hơn 100 nghìn thực
            // thể trong change tracker — chậm dần theo cấp số và ăn hết bộ nhớ của container
            // 512 MB.
            //
            // LƯU Ý phạm vi: IngestOrchestrator bọc cả vòng trong MỘT transaction, nên đây
            // KHÔNG phải là commit. Mất mạng giữa mẻ vẫn rollback toàn bộ mẻ đó. Cái thu được
            // ở đây là bộ nhớ, không phải tính bền vững — và trần maxMatchesPerRun mới là thứ
            // giới hạn thiệt hại khi mẻ dài bị gãy.
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            done++;
        }

        logger.LogInformation("Đã nạp detail cho {Done}/{Batch} ván", done, pending.Count);
        return done;
    }

    /// <summary>
    /// Nối lại MatchPlayer chưa gắn được về Player nào.
    ///
    /// Cần thiết vì thứ tự: match detail có thể được nạp TRƯỚC khi Player có account_id
    /// (account_id đến từ players.json qua seeder). Không có bước này thì những hàng nạp sớm
    /// nằm mồ côi vĩnh viễn và phân tích cá nhân trống rỗng dù dữ liệu đã có đủ.
    ///
    /// Chỉ đụng hàng PlayerId == null nên chạy lại bao nhiêu lần cũng vô hại.
    /// </summary>
    private async Task RelinkOrphanPlayersAsync(CancellationToken ct)
    {
        var known = await db.Players
            .Where(p => p.OpenDotaAccountId != null)
            .ToDictionaryAsync(p => p.OpenDotaAccountId!.Value, p => p.Id, ct);

        if (known.Count == 0) return;

        var accounts = known.Keys.ToList();
        var orphans = await db.MatchPlayers
            .Where(mp => mp.PlayerId == null && mp.AccountId != null
                         && accounts.Contains(mp.AccountId.Value))
            .ToListAsync(ct);

        if (orphans.Count == 0) return;

        foreach (var mp in orphans) mp.PlayerId = known[mp.AccountId!.Value];
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Nối lại {Count} bản ghi MatchPlayer về tuyển thủ", orphans.Count);
    }

    private async Task ApplyAsync(
        long matchId,
        OpenDotaMatchDetail detail,
        Dictionary<long, int> playersByAccount,
        CancellationToken ct)
    {
        var match = await db.Matches
            .Include(m => m.Players)
            .FirstOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null) return;

        await ApplyDraftAsync(matchId, detail, ct);
        await ApplyPurchasesAsync(matchId, detail, ct);

        var facts = MatchDetailAnalyzer.Analyze(detail);

        match.RadiantHadFirstBlood = facts.RadiantHadFirstBlood;
        match.RadiantReachedTenFirst = facts.RadiantReachedTenFirst;
        match.FirstBloodTimeSeconds = facts.FirstBloodTimeSeconds;
        match.SeriesId = detail.SeriesId;
        match.PatchVersion = detail.Patch?.ToString();
        match.DetailsIngestedAt = DateTime.UtcNow;
        match.DetailSchemaVersion = SchemaVersion;
        match.FirstRoshanSeconds = MatchDetailAnalyzer.FirstRoshanSeconds(detail);
        match.ThrowGold = detail.Throw;
        match.ComebackGold = detail.Comeback;

        foreach (var p in detail.Players)
        {
            var existing = match.Players.FirstOrDefault(x => x.AccountId == p.AccountId);
            if (existing is null)
            {
                existing = new MatchPlayer { MatchId = matchId, AccountId = p.AccountId };
                match.Players.Add(existing);
            }

            existing.HeroId = p.HeroId;
            existing.IsRadiant = p.OnRadiant;
            existing.Kills = p.Kills;
            existing.Deaths = p.Deaths;
            existing.Assists = p.Assists;
            existing.GoldPerMin = p.GoldPerMin;
            existing.XpPerMin = p.XpPerMin;
            existing.KillsFirst10Min = MatchDetailAnalyzer.KillsWithinMinutes(p, 10);
            existing.PlayerId = p.AccountId is long acc && playersByAccount.TryGetValue(acc, out var pid)
                ? pid
                : null;

            // lane_role = 0 nghĩa là OpenDota không xác định được, không phải "vai trò số 0".
            existing.LaneRole = p.LaneRole is > 0 ? p.LaneRole : null;
            existing.Lane = p.Lane is > 0 ? p.Lane : null;
            existing.LaneEfficiencyPct = p.LaneEfficiencyPct;
            existing.LastHits = p.LastHits;
            existing.Denies = p.Denies;
            existing.NetWorth = p.NetWorth;
            existing.HeroDamage = p.HeroDamage;
            existing.TowerDamage = p.TowerDamage;
            existing.ObserversPlaced = p.ObserversPlaced;
            existing.SentriesPlaced = p.SentriesPlaced;
            existing.CampsStacked = p.CampsStacked;
            existing.RunePickups = p.RunePickups;
            existing.Buybacks = p.Buybacks;
            existing.StunSeconds = p.StunSeconds;
            existing.TeamfightParticipation = p.TeamfightParticipation;
            existing.TowerKills = p.TowerKills;
            existing.CourierKills = p.CourierKills;
            existing.ObserverKills = p.ObserverKills;
            existing.SentryKills = p.SentryKills;

            // OpenDota trả 0/1; ván chưa parse thì thiếu hẳn trường và phải giữ null
            existing.FirstBloodClaimed = p.FirstBloodClaimed is int fb ? fb != 0 : null;

            // Năm chỉ số từng tưởng là không có nguồn — xem FantasyFields để biết vì sao
            // tìm theo tên hiển thị thì không bao giờ thấy chúng.
            existing.Lotuses = FantasyFields.Lotuses(p.ItemUses);
            existing.LotusItems = FantasyFields.LotusItems(p.ItemUses);
            existing.Watchers = FantasyFields.Watchers(p.AbilityUses);
            existing.Smokes = FantasyFields.Smokes(p.ItemUses);
            existing.MadstoneBundles = FantasyFields.MadstoneBundles(p.ItemUses);
            existing.TormentorKills = FantasyFields.TormentorKills(p.Killed);
            existing.DeathsToTormentor = FantasyFields.DeathsToTormentor(p.KilledBy);

            // Roshan lấy từ killed chứ KHÔNG từ trường tổng hợp roshan_kills — trường đó đếm
            // dư, đã kiểm ba chiều với objectives trên hai ván (xem FantasyFields.RoshanKills).
            // Ván chưa parse thì không có killed, khi đó đành dùng trường tổng hợp: một con số
            // hơi dư vẫn hơn là không có gì, nhưng chỉ ở đúng trường hợp không còn lựa chọn.
            var roshanFromKilled = FantasyFields.RoshanKills(p.Killed);
            existing.RoshanKills = roshanFromKilled ?? p.RoshanKills;

            if (roshanFromKilled is int rk && p.RoshanKills is int rf && rk != rf)
            {
                logger.LogInformation(
                    "Ván {MatchId} hero {HeroId}: roshan_kills={Field} đếm dư, dùng killed={Killed}",
                    matchId, p.HeroId, rf, rk);
            }
        }
    }

    /// <summary>
    /// Ghi lại bàn draft. Xoá sạch theo trận rồi ghi lại thay vì cập nhật từng lượt: bàn draft
    /// là một khối bất biến, và "xoá rồi ghi" luôn cho kết quả giống hệt dù chạy lại bao nhiêu
    /// lần — trong khi cập nhật từng phần sẽ để lại rác nếu lần trước nạp thiếu.
    /// </summary>
    private async Task ApplyDraftAsync(long matchId, OpenDotaMatchDetail detail, CancellationToken ct)
    {
        if (detail.PicksBans is not { Count: > 0 }) return;

        var old = await db.DraftEvents.Where(d => d.MatchId == matchId).ToListAsync(ct);
        if (old.Count > 0) db.DraftEvents.RemoveRange(old);

        // Trong một trận, order phải là duy nhất. OpenDota đôi khi trả lượt lặp ở dữ liệu cũ;
        // giữ lượt đầu tiên thay vì để SaveChanges vỡ vì trùng khoá.
        foreach (var pb in detail.PicksBans.GroupBy(x => x.Order).Select(g => g.First()))
        {
            db.DraftEvents.Add(new DraftEvent
            {
                MatchId = matchId,
                Order = pb.Order,
                IsPick = pb.IsPick,
                HeroId = pb.HeroId,
                IsRadiant = pb.Team == 0,
            });
        }
    }

    private async Task ApplyPurchasesAsync(long matchId, OpenDotaMatchDetail detail, CancellationToken ct)
    {
        var any = detail.Players.Any(p => p.PurchaseLog is { Count: > 0 });
        if (!any) return;

        var old = await db.ItemPurchases.Where(x => x.MatchId == matchId).ToListAsync(ct);
        if (old.Count > 0) db.ItemPurchases.RemoveRange(old);

        foreach (var p in detail.Players)
        {
            if (p.PurchaseLog is not { Count: > 0 }) continue;

            foreach (var buy in p.PurchaseLog)
            {
                if (string.IsNullOrWhiteSpace(buy.Key)) continue;

                db.ItemPurchases.Add(new ItemPurchase
                {
                    MatchId = matchId,
                    PlayerSlot = p.PlayerSlot,
                    HeroId = p.HeroId,
                    IsRadiant = p.OnRadiant,
                    ItemKey = buy.Key,
                    TimeSeconds = buy.Time,
                });
            }
        }
    }
}
