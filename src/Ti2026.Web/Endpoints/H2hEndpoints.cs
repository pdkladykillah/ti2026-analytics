using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// H2H tính trực tiếp từ Match + SeriesId, KHÔNG có bảng dẫn xuất riêng. Bảng dẫn xuất là
/// thứ sẽ lệch khỏi nguồn khi ingest chạy lại, và ở quy mô 16 đội thì query trực tiếp
/// nhanh hơn ngưỡng cảm nhận rất nhiều.
///
/// Số người của đội hình TI2026 còn lại trong mỗi ván cũng tính tại đây chứ không lưu sẵn vào
/// Match: roster còn có thể đổi trước giờ khai mạc, mà lưu sẵn thì mọi ván cũ giữ nguyên con số
/// của roster hôm nạp và lặng lẽ sai đi. Tính lúc đọc thì luôn khớp với roster đang hiệu lực.
/// </summary>
public static class H2hEndpoints
{
    /// <summary>
    /// Đúng thứ tự 10 chỉ số trong field "order" của h2h.json hiện tại.
    /// UI H2H đọc mảng này để biết vẽ hàng nào trước.
    /// </summary>
    private static readonly string[] Order =
    [
        "winrate", "kills", "deaths", "killDiff", "totalKills",
        "firstBlood", "f10", "winWhenFb", "winWhenF10", "duration",
    ];

    public static void MapH2hEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/h2h", async (Ti2026DbContext db) =>
        {
            var slugs = await db.Teams.ToDictionaryAsync(t => t.Id, t => t.Slug);
            var names = await db.Teams.ToDictionaryAsync(t => t.Id, t => t.Name);

            // Đội hình ĐANG hiệu lực, bỏ HLV vì HLV không ra sân nên không bao giờ xuất hiện
            // trong MatchPlayers — tính vào mẫu số thì mọi đội mãi mãi chỉ đạt 5/6.
            var roster = (await db.RosterEntries
                    .Where(r => r.ValidTo == null && r.Role != "COACH")
                    .Select(r => new { r.TeamId, r.PlayerId })
                    .ToListAsync())
                .GroupBy(r => r.TeamId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.PlayerId).ToHashSet());

            var matches = await db.Matches
                .Where(m => m.RadiantTeamId != null && m.DireTeamId != null)
                .OrderByDescending(m => m.StartTime)
                .ToListAsync();

            // Chỉ lấy hàng đã khớp được về Player của ta: 13.6k thay vì 26k hàng, và hàng không
            // khớp thì chắc chắn không thuộc đội hình nào đang theo dõi.
            var playerRows = await (
                from p in db.MatchPlayers
                join m in db.Matches on p.MatchId equals m.Id
                where p.PlayerId != null && m.RadiantTeamId != null && m.DireTeamId != null
                select new { p.MatchId, PlayerId = p.PlayerId!.Value, p.IsRadiant })
                .ToListAsync();

            var byMatch = playerRows
                .GroupBy(p => p.MatchId)
                .ToDictionary(g => g.Key, g => g.ToList());

            int Kept(long matchId, int teamId, bool radiantSide) =>
                !byMatch.TryGetValue(matchId, out var rows) || !roster.TryGetValue(teamId, out var five)
                    ? 0
                    : rows.Count(p => p.IsRadiant == radiantSide && five.Contains(p.PlayerId));

            // Từ ngày nào mỗi đội mới ra sân với đủ 5 người của TI2026, và đá cùng nhau bao nhiêu
            // ván. Đây là con số nói thẳng vì sao lịch sử lại ngắn đến thế: 12/16 đội mãi tới
            // năm 2026 mới lần đầu đủ mặt.
            var together = new Dictionary<string, (string First, int Games)>();
            foreach (var m in matches)
            {
                foreach (var (teamId, radiantSide) in
                         new[] { (m.RadiantTeamId!.Value, true), (m.DireTeamId!.Value, false) })
                {
                    if (Kept(m.Id, teamId, radiantSide) < 5 || !slugs.TryGetValue(teamId, out var s))
                        continue;

                    var d = m.StartTime.ToString("yyyy-MM-dd");
                    together[s] = together.TryGetValue(s, out var cur)
                        ? (string.CompareOrdinal(d, cur.First) < 0 ? d : cur.First, cur.Games + 1)
                        : (d, 1);
                }
            }

            var pairs = new Dictionary<string, object>();

            foreach (var group in matches
                         .Select(m => (Match: m, Key: PairKey(slugs, m.RadiantTeamId!.Value, m.DireTeamId!.Value)))
                         .Where(x => x.Key is not null)
                         .GroupBy(x => x.Key!))
            {
                // VÁN và TRẬN là hai con số khác nhau, và trước đây chỉ có một con số duy nhất
                // tên là `n` — UI đọc nó rồi ghi "71 trận" trong khi 71 là số ván, còn số trận
                // thật chỉ là 32. Một Bo3 đếm thành ba trận thì mọi cặp đấu trông như đã gặp
                // nhau gấp đôi ba lần thực tế.
                var games = group.Count();
                var withSeries = group.Where(x => x.Match.SeriesId is long s && s > 0).ToList();
                var seriesCount = withSeries.Select(x => x.Match.SeriesId).Distinct().Count()
                                  + (games - withSeries.Count);

                var slugA = group.Key.Split('|')[0];
                var slugB = group.Key.Split('|')[1];

                var rows = group.Select(x =>
                {
                    var m = x.Match;
                    var radSlug = slugs[m.RadiantTeamId!.Value];
                    var radKept = Kept(m.Id, m.RadiantTeamId.Value, true);
                    var direKept = Kept(m.Id, m.DireTeamId!.Value, false);

                    return (
                        Match: m,
                        RadSlug: radSlug,
                        RadKept: radKept,
                        DireKept: direKept,
                        KeptA: radSlug == slugA ? radKept : direKept,
                        KeptB: radSlug == slugA ? direKept : radKept,
                        WinnerSlug: m.RadiantWin ? radSlug : slugs[m.DireTeamId.Value]);
                }).ToList();

                var verdict = LineupContinuity.Read(
                    rows.Select(r => new H2hGame(
                        r.Match.StartTime.ToString("yyyy-MM-dd"),
                        r.Match.LeagueName ?? "",
                        r.WinnerSlug, r.KeptA, r.KeptB)).ToList(),
                    NameOf(names, slugs, slugA), NameOf(names, slugs, slugB), slugA);

                pairs[group.Key] = new
                {
                    // Giữ tên `n` cho tương thích, nhưng thêm hai tên nói rõ nó là gì
                    n = games,
                    games,
                    seriesCount,

                    firstMet = group.Min(x => x.Match.StartTime).ToString("yyyy-MM-dd"),
                    lastMet = group.Max(x => x.Match.StartTime).ToString("yyyy-MM-dd"),

                    // Nhận định của hệ thống trên tập ván ĐÁNG DÙNG, kèm lý do loại phần còn lại
                    verdict,

                    // Đội hình hiện tại của từng bên đã đá cùng nhau từ bao giờ
                    lineup = new Dictionary<string, object?>
                    {
                        [slugA] = Together(together, slugA),
                        [slugB] = Together(together, slugB),
                    },

                    series = rows.Select(r => new object[]
                    {
                        r.Match.StartTime.ToString("yyyy-MM-dd"),
                        r.Match.LeagueName ?? "",
                        r.Match.RadiantScore,
                        r.Match.DireScore,

                        // Bốn field dưới là mới. Thiếu RadSlug thì cột tỷ số "12 – 8" không cho
                        // biết ai là 12 — bảng cũ bày điểm mà không nói được của bên nào.
                        r.RadSlug,
                        r.RadKept,
                        r.DireKept,
                        r.WinnerSlug,
                    }).ToList(),
                };
            }

            return Results.Ok(new
            {
                updatedAt = DateTime.UtcNow,
                source = "OpenDota",

                // "6 tháng gần nhất" là SAI và đã sai từ đầu: truy vấn ở trên không hề có bộ
                // lọc thời gian. Cặp Falcons–Liquid trải từ 12/2023 tới nay. Đối đầu thì cố ý
                // lấy toàn bộ lịch sử — vài lần gặp nhau trong sáu tháng là quá ít để nói gì —
                // nhưng nhãn phải nói đúng điều đang làm, và việc chắt lọc là do đội hình quyết
                // định chứ không do ngày tháng.
                window = "toàn bộ lịch sử đã nạp, lọc theo đội hình",
                order = Order,
                pairs,
            });
        });
    }

    private static object? Together(
        Dictionary<string, (string First, int Games)> together, string slug) =>
        together.TryGetValue(slug, out var t)
            ? new { since = t.First, games = t.Games }
            : null;

    private static string NameOf(
        Dictionary<int, string> names, Dictionary<int, string> slugs, string slug)
    {
        var id = slugs.FirstOrDefault(kv => kv.Value == slug).Key;
        return names.TryGetValue(id, out var n) ? n : slug;
    }

    /// <summary>
    /// Khoá cặp đấu: hai slug sắp theo alphabet, nối bằng '|' — đúng định dạng của
    /// h2h.json hiện tại, ví dụ "betboom-team|parivision".
    /// </summary>
    private static string? PairKey(Dictionary<int, string> slugs, int a, int b)
    {
        if (!slugs.TryGetValue(a, out var sa) || !slugs.TryGetValue(b, out var sb)) return null;
        return string.CompareOrdinal(sa, sb) <= 0 ? $"{sa}|{sb}" : $"{sb}|{sa}";
    }
}
