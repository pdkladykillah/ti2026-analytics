using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

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
    public async Task<int> IngestAsync(int maxMatchesPerRun, CancellationToken ct)
    {
        await RelinkOrphanPlayersAsync(ct);

        // Ván mới nhất trước: phong độ gần đây là thứ đáng có sớm nhất, và nếu vì lý do gì
        // đó việc nạp bù không bao giờ hoàn tất thì phần thiếu là quá khứ xa, ít giá trị hơn.
        var pending = await db.Matches
            .Where(m => m.DetailsIngestedAt == null)
            .OrderByDescending(m => m.StartTime)
            .Take(maxMatchesPerRun)
            .Select(m => m.Id)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            logger.LogInformation("Không còn ván nào cần nạp match detail");
            return 0;
        }

        var remaining = await db.Matches.CountAsync(m => m.DetailsIngestedAt == null, ct);
        logger.LogInformation(
            "Nạp match detail cho {Batch} ván (còn tổng cộng {Remaining} ván chưa có detail)",
            pending.Count, remaining);

        // Bản đồ account_id -> Player của ta, để gắn MatchPlayer về đúng tuyển thủ
        var playersByAccount = await db.Players
            .Where(p => p.OpenDotaAccountId != null)
            .ToDictionaryAsync(p => p.OpenDotaAccountId!.Value, p => p.Id, ct);

        var done = 0;

        foreach (var matchId in pending)
        {
            ct.ThrowIfCancellationRequested();

            OpenDotaMatchDetail detail;
            try
            {
                detail = await client.GetMatchAsync(matchId, ct);
            }
            catch (HttpRequestException ex)
            {
                // Một ván lỗi không được phép làm hỏng cả mẻ. Không đánh dấu đã nạp để
                // vòng sau thử lại.
                logger.LogWarning(ex, "Không lấy được match detail {MatchId}, sẽ thử lại vòng sau", matchId);
                continue;
            }

            await ApplyAsync(matchId, detail, playersByAccount, ct);
            done++;
        }

        await db.SaveChangesAsync(ct);
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

        var facts = MatchDetailAnalyzer.Analyze(detail);

        match.RadiantHadFirstBlood = facts.RadiantHadFirstBlood;
        match.RadiantReachedTenFirst = facts.RadiantReachedTenFirst;
        match.FirstBloodTimeSeconds = facts.FirstBloodTimeSeconds;
        match.SeriesId = detail.SeriesId;
        match.PatchVersion = detail.Patch?.ToString();
        match.DetailsIngestedAt = DateTime.UtcNow;

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
        }
    }
}
