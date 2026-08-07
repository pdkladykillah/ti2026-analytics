using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Nạp NỐT những ván của các giải cấp cao mà cả hai bên đều ngoài 16 đội.
///
/// VÌ SAO CẦN. Ingest chính chạy theo từng đội (teams/{id}/matches), nên ván nào không có đội
/// nào của ta thì vĩnh viễn không vào DB. Với phân tích ĐỘI thì đúng — không liên quan. Nhưng
/// tier list đo meta HERO, và ở đó ván của đội ngoài 16 vẫn là bằng chứng hợp lệ. Đo thực tế ở
/// bản game 60: OpenDota có 2.127 ván pro, ta chỉ có 948 (44,6%).
///
/// VÌ SAO KHÔNG NẠP HẾT. Trong 1.179 ván thiếu, chỉ 126 thuộc giải cấp cao; 1.053 ván còn lại
/// đến từ Division 2, vòng loại mở và giải khu vực — một mặt bằng trình độ khác, và meta ở đó
/// không nói lên điều gì về TI. "Không có dữ liệu nào là phế vật, nhưng phải dùng đúng chỗ."
///
/// VÌ SAO KHÔNG VIẾT CỨNG DANH SÁCH GIẢI. Danh sách viết cứng sẽ lạc hậu ngay khi có giải mới
/// — kể cả chính TI2026. Để dữ liệu tự nói: giải nào có từ <see cref="MinTiTeams"/> đội TI2026
/// góp mặt thì đó là giải ở đúng mặt bằng cần đo.
///
/// Trường League.Tier của OpenDota KHÔNG dùng thay được: nó gắn "professional" cho cả
/// DreamLeague Division 2 lẫn vòng loại mở.
/// </summary>
public class LeagueBackfillIngester(
    Ti2026DbContext db, OpenDotaClient client, ILogger<LeagueBackfillIngester> logger)
{
    /// <summary>Từ ngần này đội TI2026 góp mặt thì coi là giải ở mặt bằng của TI.</summary>
    public const int MinTiTeams = 8;

    /// <summary>
    /// Chỉ xét giải có ván trong ngần này ngày. Tier list chỉ đọc bản game hiện tại, nên nạp
    /// bù cả lịch sử là tốn tiền cho dữ liệu không ai đọc.
    /// </summary>
    public const int RecentDays = 120;

    public async Task<int> IngestAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-RecentDays);

        // Giải "cấp cao" đọc từ chính dữ liệu: đếm số đội của ta đã ra sân ở đó.
        //
        // Gom nhóm ở phía ứng dụng chứ không trong SQL: đếm số đội phân biệt qua HAI cột đòi
        // hỏi trải hai cột thành một tập, mà EF không dịch được biểu thức đó sang SQL. Trong
        // cửa sổ 120 ngày chỉ có vài trăm hàng ba cột nên đọc lên là rẻ hơn nhiều so với việc
        // bẻ cong truy vấn cho vừa bộ dịch.
        var recent = await db.Matches
            .Where(m => m.LeagueId != null && m.StartTime >= since)
            .Select(m => new { LeagueId = m.LeagueId!.Value, m.RadiantTeamId, m.DireTeamId })
            .ToListAsync(ct);

        var leagues = recent
            .GroupBy(m => m.LeagueId)
            .Where(g => g.SelectMany(m => new[] { m.RadiantTeamId, m.DireTeamId })
                         .Where(x => x is not null)
                         .Distinct()
                         .Count() >= MinTiTeams)
            .Select(g => g.Key)
            .ToList();

        if (leagues.Count == 0)
        {
            logger.LogInformation("Chưa có giải nào đạt mức {Min} đội TI2026 — không nạp bù", MinTiTeams);
            return 0;
        }

        var mapped = await db.TeamOpenDotaIds
            .Select(x => new { x.OpenDotaTeamId, x.TeamId })
            .ToDictionaryAsync(x => x.OpenDotaTeamId, x => x.TeamId, ct);

        foreach (var t in await db.Teams.Where(t => t.OpenDotaTeamId != null).ToListAsync(ct))
            mapped.TryAdd(t.OpenDotaTeamId!.Value, t.Id);

        var added = 0;

        foreach (var leagueId in leagues)
        {
            var remote = await client.GetLeagueMatchesAsync(leagueId, ct);
            if (remote.Count == 0) continue;

            var ids = remote.Select(m => m.MatchId).ToList();
            var known = await db.Matches
                .Where(m => ids.Contains(m.Id))
                .Select(m => m.Id)
                .ToListAsync(ct);

            var knownSet = known.ToHashSet();
            var name = await db.Matches
                .Where(m => m.LeagueId == leagueId && m.LeagueName != null)
                .Select(m => m.LeagueName)
                .FirstOrDefaultAsync(ct);

            var fresh = 0;

            foreach (var m in remote)
            {
                if (knownSet.Contains(m.MatchId)) continue;

                // CHỈ THÊM, không bao giờ ghi đè ván đã có: ingest theo đội là nguồn chuẩn cho
                // những ván đó và nó xử lý được nhiều trường hợp hơn. Ghi đè ở đây là để hai
                // nguồn giành nhau cùng một hàng.
                db.Matches.Add(new Match
                {
                    Id = m.MatchId,
                    SeriesId = m.SeriesId,
                    StartTime = DateTimeOffset.FromUnixTimeSeconds(m.StartTime).UtcDateTime,
                    DurationSeconds = m.Duration,
                    LeagueId = leagueId,
                    LeagueName = name,

                    // Phần lớn là null cả hai phe — đó chính là những ván ta đang thiếu. Mọi
                    // truy vấn mức ĐỘI đều lọc "cả hai phe khác null" nên chúng không lọt vào
                    // Elo, form hay đối đầu; chỉ phân tích hero mới đọc tới.
                    RadiantTeamId = m.RadiantTeamId is int r && mapped.TryGetValue(r, out var rid) ? rid : null,
                    DireTeamId = m.DireTeamId is int d && mapped.TryGetValue(d, out var did) ? did : null,

                    RadiantWin = m.RadiantWin,
                    RadiantScore = m.RadiantScore,
                    DireScore = m.DireScore,
                    IngestedAt = DateTime.UtcNow,
                });

                knownSet.Add(m.MatchId);
                fresh++;
            }

            if (fresh > 0)
            {
                logger.LogInformation(
                    "Giải {League} ({Name}): thêm {Fresh} ván mà cả hai bên đều ngoài 16 đội",
                    leagueId, name, fresh);
                added += fresh;
            }
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        return added;
    }
}
