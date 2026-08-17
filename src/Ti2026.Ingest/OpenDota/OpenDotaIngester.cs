using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Media;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Nạp ván đấu từ OpenDota cho 16 đội. Ghi vào bảng Match; việc tính chỉ số là của
/// SnapshotWriter.
/// </summary>
public class OpenDotaIngester(
    Ti2026DbContext db,
    OpenDotaClient client,
    TeamResolver resolver,
    MediaCache media,
    MediaPaths mediaPaths,
    ILogger<OpenDotaIngester> logger)
{
    public async Task<int> IngestAsync(CancellationToken ct)
    {
        // Gọi /teams khi còn đội chưa phân giải HOẶC còn đội chưa có logo dùng được.
        // Response ~250 KB nên không tải lại mỗi 6 giờ khi mọi thứ đã đủ.
        //
        // Logo: dltv.org chặn hotlink theo referrer nên ảnh của họ KHÔNG hiện trên trình
        // duyệt (chỉ ra chữ cái thay thế), và robots.txt của họ cấm /uploads/ nên cũng
        // không được phép cache về. OpenDota trả logo_url trỏ Steam CDN, tải trực tiếp
        // được — dùng nguồn đó.
        var needTeams = await db.Teams.AnyAsync(
            t => t.OpenDotaTeamId == null || t.LogoUrl == null || !t.LogoUrl.Contains("steam"), ct);

        if (needTeams)
        {
            var odTeams = await client.GetTeamsAsync(ct);
            logger.LogInformation("OpenDota trả về {Count} đội", odTeams.Count);
            await resolver.ResolveAsync(odTeams, ct);
            await UpdateLogosAsync(odTeams, ct);
        }

        await UpdateHeroesAsync(ct);
        await UpdateItemsAsync(ct);
        await UpdateHeroStatsAsync(ct);
        await UpdatePlayerAvatarsAsync(ct);
        await UpdateLeaguesAsync(ct);

        var teams = await db.Teams
            .Include(t => t.OpenDotaIds)
            .Where(t => t.OpenDotaTeamId != null || t.OpenDotaIds.Count > 0)
            .ToListAsync(ct);

        if (teams.Count == 0)
        {
            logger.LogWarning("Không có đội nào phân giải được OpenDotaTeamId — bỏ qua vòng này");
            return 0;
        }

        // Mỗi đội có thể có NHIỀU team_id trên OpenDota: một roster đổi tổ chức hoặc đăng ký
        // lại thì OpenDota sinh bản ghi mới, bản ghi cũ ngừng nhận ván. Nạp theo đúng một id
        // là cách lặng lẽ mất trắng dữ liệu của một đội — đã xảy ra thật với PariVision, mất
        // nguyên giải EWC 2026 mà họ vô địch.
        var idsOf = teams.ToDictionary(
            t => t.Id,
            t => t.OpenDotaIds.Select(x => x.OpenDotaTeamId)
                  .Concat(t.OpenDotaTeamId is int p ? [p] : Array.Empty<int>())
                  .Distinct()
                  .ToList());

        // Bản đồ ngược để ánh xạ opposing_team_id về đội của ta — cũng phải phủ MỌI id, không
        // thì chính đội của ta xuất hiện như "đối thủ ngoài 16 đội" và ván bị loại khỏi H2H.
        var byOpenDotaId = idsOf
            .SelectMany(kv => kv.Value.Select(id => (id, teamId: kv.Key)))
            .ToDictionary(x => x.id, x => x.teamId);

        var written = 0;

        foreach (var team in teams)
        {
            foreach (var openDotaId in idsOf[team.Id])
            {
                var matches = await client.GetTeamMatchesAsync(openDotaId, ct);
                written += await UpsertMatchesAsync(team, matches, byOpenDotaId, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Nạp xong {Written} ván từ {Teams} đội", written, teams.Count);
        return written;
    }

    /// <summary>
    /// Nạp bảng hero: id, tên, và ảnh chân dung.
    ///
    /// Bảng Heroes tồn tại từ đầu nhưng CHƯA BAO GIỜ được nạp — phát hiện khi bảng ưu tiên
    /// cấm/chọn hiện ra 30 dòng "hero 80" thay vì tên hero. Client đã có GetHeroesAsync từ
    /// trước, chỉ là không ai gọi. Một bảng rỗng không làm gì đổ vỡ, nên nó nằm im được lâu.
    ///
    /// Chỉ gọi khi bảng còn thiếu hero: response nhỏ nhưng vẫn là một request, và danh sách
    /// hero gần như không đổi giữa các bản.
    /// </summary>
    private async Task UpdateHeroesAsync(CancellationToken ct)
    {
        // Ngưỡng theo số hero thực tế của Dota 2 (trên 120 và còn tăng). Dùng "có ít hơn
        // ngưỡng" thay vì "rỗng" để tự nạp bù khi Valve thêm hero mới.
        const int expectedAtLeast = 120;

        if (await db.Heroes.CountAsync(ct) >= expectedAtLeast) return;

        var heroes = await client.GetHeroesAsync(ct);
        if (heroes.Count == 0)
        {
            logger.LogWarning("OpenDota trả về 0 hero — giữ nguyên bảng cũ");
            return;
        }

        var existing = await db.Heroes.ToDictionaryAsync(h => h.Id, ct);

        foreach (var h in heroes)
        {
            if (!existing.TryGetValue(h.Id, out var row))
            {
                row = new Hero { Id = h.Id, Name = h.Name };
                db.Heroes.Add(row);
            }

            row.Name = h.Name;
            row.LocalizedName = h.LocalizedName;

            // name của OpenDota có dạng "npc_dota_hero_antimage"; ảnh trên Steam CDN dùng
            // đúng phần đuôi sau tiền tố đó.
            // KHÔNG lưu URL ảnh. Nó là hàm thuần của h.Name nên lưu xuống chỉ tạo một bản sao
            // có thể lỗi thời: đổi host phải nạp lại cả bảng mới có tác dụng — và đó đúng là
            // tình huống đã gặp. Nơi đọc dùng DotaImages.Hero(Name) để dựng tại chỗ.
            row.ImageUrl = null;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Nạp {Count} hero vào bảng Heroes", heroes.Count);
    }

    /// <summary>
    /// Nạp số liệu tổng hợp mỗi hero: pub theo bậc rank, và pro theo cách đo của OpenDota.
    ///
    /// Đây là nguồn PUB duy nhất của cả dự án, và là thứ biến tier list từ một bản chụp đóng
    /// băng thành bảng tự cập nhật. Tier list biên tập cũ lấy winrate pub từ Dotabuff nhập tay
    /// nên nó đứng yên kể từ ngày nhập.
    ///
    /// Gọi MỖI VÒNG, không gác: response ~165 KB và số liệu này đổi liên tục — gác lại thì
    /// đúng cái nó sinh ra để giải quyết lại quay về.
    /// </summary>
    private async Task UpdateHeroStatsAsync(CancellationToken ct)
    {
        var stats = await client.GetHeroStatsAsync(ct);
        if (stats.Count == 0)
        {
            logger.LogWarning("OpenDota trả về 0 hero stat — giữ nguyên bảng cũ");
            return;
        }

        var existing = await db.HeroStats.ToDictionaryAsync(x => x.HeroId, ct);
        var now = DateTime.UtcNow;

        foreach (var s in stats)
        {
            if (!existing.TryGetValue(s.Id, out var row))
            {
                row = new HeroStat { HeroId = s.Id };
                db.HeroStats.Add(row);
            }

            row.ProPick = s.ProPick ?? 0;
            row.ProWin = s.ProWin ?? 0;
            row.ProBan = s.ProBan ?? 0;
            row.PubPick = s.PubPick ?? 0;
            row.PubWin = s.PubWin ?? 0;

            // Bậc 7 + 8 = Divine + Immortal. Pub toàn bậc trộn cả người mới chơi, mà hero mạnh
            // ở bậc thấp thường chỉ là hero tự chơi được một mình — không nói gì về meta.
            row.HighPick = s.SumBrackets("pick", 7, 8);
            row.HighWin = s.SumBrackets("win", 7, 8);

            row.FetchedAt = now;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Nạp số liệu cho {Count} hero", stats.Count);
    }

    /// <summary>
    /// Nạp avatar Steam cho tuyển thủ.
    ///
    /// VÌ SAO CẦN: players.json chỉ có ảnh trỏ dltv.org/uploads/, mà dltv chặn hotlink theo
    /// referrer — nên mọi thẻ img đó đều vỡ trên trình duyệt, và robots.txt của họ cấm cache
    /// về. Avatar Steam là nguồn ảnh duy nhất hiển thị được một cách hợp lệ.
    ///
    /// Mỗi tuyển thủ MỘT request, nên chỉ gọi cho người còn thiếu avatar và có trần mỗi vòng.
    /// 96 tuyển thủ sẽ xong sau hai vòng, rồi không bao giờ gọi lại.
    /// </summary>
    private async Task UpdatePlayerAvatarsAsync(CancellationToken ct)
    {
        const int maxPerRun = 60;

        // Gác trên PhotoMediaAssetId — thứ THẬT SỰ cần — chứ không trên AvatarUrl.
        //
        // Bản trước gác trên AvatarUrl == null, và nó im lặng không làm gì: lượt deploy trước đó
        // đã điền AvatarUrl cho cả 96 người, nên "còn ai cần làm" trả về rỗng trong khi chưa một
        // tệp ảnh nào được tải. Cổng chặn phải gác trên đích, không gác trên bước trung gian.
        var missing = await db.Players
            .Where(p => p.OpenDotaAccountId != null && p.PhotoMediaAssetId == null)
            .OrderBy(p => p.Id)
            .Take(maxPerRun)
            .ToListAsync(ct);

        if (missing.Count == 0) return;

        var done = 0;

        foreach (var player in missing)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Đã biết URL từ vòng trước thì KHÔNG gọi lại hồ sơ — tiết kiệm đúng 96 request.
                var avatar = player.AvatarUrl;

                if (string.IsNullOrWhiteSpace(avatar))
                {
                    var profile = await client.GetPlayerAsync(player.OpenDotaAccountId!.Value, ct);
                    avatar = profile?.Profile?.AvatarFull;

                    // Hồ sơ để riêng tư thì không có avatar. Bỏ qua, để null, và vòng sau thử
                    // lại — đánh dấu "đã thử" bằng chuỗi rỗng sẽ khiến ảnh không bao giờ về nữa
                    // nếu sau này người đó mở hồ sơ.
                    if (string.IsNullOrWhiteSpace(avatar)) continue;

                    player.AvatarUrl = avatar;
                }

                // Tải về máy mình. Avatar Steam CHỈ có trên avatars.steamstatic.com — đường
                // akamai chỉ 301 trả về đúng host đó — mà cả họ *.steamstatic.com không tới được
                // từ mạng người dùng. Không có host thay thế, nên hotlink là bế tắc.
                var asset = await media.EnsureAsync(avatar, mediaPaths.MediaDirectory, ct);
                if (asset is null) continue;

                player.PhotoMediaAssetId = asset.Id;
                done++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                             or TimeoutException or System.Text.Json.JsonException)
            {
                logger.LogInformation(ex,
                    "Chưa lấy được avatar của {Nick}, sẽ thử lại vòng sau", player.Nick);
            }
        }

        if (done > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Nạp avatar Steam cho {Done} tuyển thủ", done);
        }
    }

    /// <summary>
    /// Nạp bảng item: tên hiển thị và giá.
    ///
    /// Giá là thứ thật sự cần: bảng mốc lên đồ sắp theo tần suất, và nếu không lọc được linh
    /// kiện thì Iron Branch với Circlet sẽ chiếm hết chỗ của Black King Bar. Tên hiển thị thì
    /// thay cho việc tự làm đẹp khoá kỹ thuật — cách tự làm cho ra "Ring OF Basilius".
    /// </summary>
    private async Task UpdateItemsAsync(CancellationToken ct)
    {
        // Dota có trên 300 mục trong constants/items; dưới ngưỡng này là bảng chưa đủ.
        const int expectedAtLeast = 200;

        // BỎ QUA CHỈ KHI BẢNG VỪA ĐỦ DÒNG VỪA ĐỦ CỘT.
        //
        // Cửa cũ chỉ đếm số dòng, và điều đó đúng cho tới lúc bảng có thêm cột: thêm
        // OpenDotaItemId xong thì 501 dòng cũ vẫn thoả "đủ 200 dòng" nên danh mục không bao giờ
        // được nạp lại, và cột mới đứng null vĩnh viễn. Đã mắc thật — bảng điểm hiện ra sáu ô
        // đồ trống cho cả mười người mà không có gì báo là thiếu dữ liệu.
        //
        // Điều kiện thứ hai tự tắt: nạp một lượt là mọi dòng có id, và từ đó lại bỏ qua như cũ.
        // Tốn đúng một lời gọi, đúng một lần.
        if (await db.Items.CountAsync(ct) >= expectedAtLeast
            && !await db.Items.AnyAsync(i => i.OpenDotaItemId == null, ct))
            return;

        var items = await client.GetItemsAsync(ct);
        if (items.Count == 0)
        {
            logger.LogWarning("OpenDota trả về 0 item — giữ nguyên bảng cũ");
            return;
        }

        var existing = await db.Items.ToDictionaryAsync(x => x.Key, ct);

        foreach (var (key, value) in items)
        {
            if (!existing.TryGetValue(key, out var row))
            {
                row = new Item { Key = key };
                db.Items.Add(row);
            }

            row.Name = value.DisplayName;
            row.Cost = value.Cost;
            row.Quality = value.Quality;
            row.OpenDotaItemId = value.Id;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Nạp {Count} item vào bảng Items", items.Count);
    }

    /// <summary>
    /// Nạp danh sách giải kèm hạng, để lọc trận nào được tính vào Elo.
    ///
    /// Chỉ gọi khi có leagueid trong Match mà chưa có trong bảng League — response ~1 MB,
    /// không cần tải lại mỗi 6 giờ khi đã đủ.
    /// </summary>
    private async Task UpdateLeaguesAsync(CancellationToken ct)
    {
        var haveIds = await db.Leagues.Select(l => l.Id).ToListAsync(ct);
        var missing = await db.Matches
            .Where(m => m.LeagueId != null && !haveIds.Contains(m.LeagueId.Value))
            .Select(m => m.LeagueId!.Value)
            .Distinct()
            .AnyAsync(ct);

        // Lần đầu chạy thì bảng rỗng và cũng chưa có Match nào -> vẫn phải nạp một lần
        if (!missing && haveIds.Count > 0) return;

        var leagues = await client.GetLeaguesAsync(ct);
        if (leagues.Count == 0) return;

        var existing = await db.Leagues.ToDictionaryAsync(l => l.Id, ct);
        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var l in leagues)
        {
            if (existing.TryGetValue(l.LeagueId, out var row))
            {
                row.Name = l.Name;
                row.Tier = l.Tier;
                row.UpdatedAt = now;
            }
            else
            {
                db.Leagues.Add(new League
                {
                    Id = l.LeagueId, Name = l.Name, Tier = l.Tier, UpdatedAt = now
                });
                added++;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Nạp {Total} giải ({Added} mới)", leagues.Count, added);
    }

    /// <summary>
    /// Thay logo dltv (bị chặn hotlink) bằng logo Steam CDN của OpenDota.
    /// Chỉ ghi đè khi OpenDota thực sự có logo — không xoá logo cũ để lấy chỗ trống.
    /// </summary>
    private async Task UpdateLogosAsync(List<OpenDotaTeam> odTeams, CancellationToken ct)
    {
        var byId = odTeams.Where(t => !string.IsNullOrWhiteSpace(t.LogoUrl))
                          .ToDictionary(t => t.TeamId, t => t.LogoUrl!);

        var updated = 0;
        foreach (var team in await db.Teams.Where(t => t.OpenDotaTeamId != null).ToListAsync(ct))
        {
            if (!byId.TryGetValue(team.OpenDotaTeamId!.Value, out var logo)) continue;
            if (team.LogoUrl == logo) continue;

            team.LogoUrl = logo;
            updated++;
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Cập nhật logo Steam CDN cho {Count} đội", updated);
        }
    }

    private async Task<int> UpsertMatchesAsync(
        Team team,
        List<OpenDotaTeamMatch> matches,
        Dictionary<int, int> byOpenDotaId,
        CancellationToken ct)
    {
        var written = 0;

        foreach (var m in matches)
        {
            // Không biết đội được hỏi ở phe nào thì không thể quy kết kết quả cho ai — bỏ qua
            // thay vì đoán. Đoán ở đây làm sai winrate của cả hai đội.
            if (m.Radiant is null) continue;

            // Đối thủ ngoài 16 đội thì VẪN LƯU, để phe kia null.
            //
            // Bản trước bỏ hẳn những ván này, và cái giá thì lớn hơn ghi chú cũ thừa nhận: chỉ
            // riêng EWC 2026 đã mất 77/130 ván có đội của ta thi đấu. Toàn bộ bàn draft và mọi
            // chỉ số cá nhân trong đó biến mất theo — trong khi đó là dữ liệu thật về hero nào
            // đang mạnh và tuyển thủ nào đang có phong độ.
            //
            // Không có dữ liệu nào là phế vật, nhưng phải dùng đúng chỗ. Một phe null nghĩa là:
            //   DÙNG ĐƯỢC  — tier list, ưu tiên cấm/chọn, mốc lên đồ, chỉ số cá nhân, fantasy
            //   KHÔNG DÙNG — Elo, đối đầu, winrate đội, phân phối kèo, series
            //
            // Vế thứ hai được bảo vệ bằng điều kiện `RadiantTeamId != null && DireTeamId != null`
            // ở SnapshotWriter và các endpoint mức đội. Đã rà toàn bộ 26 chỗ truy vấn Matches
            // khi mở luồng này; chỗ duy nhất còn hở là phân phối kèo tổng kill, đã vá cùng lúc.
            int? opponentTeamId = m.OpposingTeamId is int opp
                                  && byOpenDotaId.TryGetValue(opp, out var mapped)
                ? mapped
                : null;

            var isRadiant = m.Radiant.Value;
            var radiantTeamId = isRadiant ? team.Id : opponentTeamId;
            var direTeamId = isRadiant ? opponentTeamId : team.Id;

            var entity = await db.Matches.FirstOrDefaultAsync(x => x.Id == m.MatchId, ct)
                         ?? db.Matches.Local.FirstOrDefault(x => x.Id == m.MatchId);

            if (entity is null)
            {
                entity = new Match { Id = m.MatchId };
                db.Matches.Add(entity);
                written++;
            }

            entity.StartTime = DateTimeOffset.FromUnixTimeSeconds(m.StartTime).UtcDateTime;
            entity.DurationSeconds = m.Duration;
            entity.LeagueId = m.LeagueId;
            entity.LeagueName = m.LeagueName;
            entity.RadiantTeamId = radiantTeamId;
            entity.DireTeamId = direTeamId;
            entity.RadiantWin = m.RadiantWin;
            entity.RadiantScore = m.RadiantScore;
            entity.DireScore = m.DireScore;
            entity.IngestedAt = DateTime.UtcNow;

            // RadiantHadFirstBlood / RadiantReachedTenFirst / SeriesId để nguyên null:
            // endpoint này không cung cấp chúng. Xem ghi chú trong OpenDotaTeamMatch.
        }

        return written;
    }
}
