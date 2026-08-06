using Microsoft.EntityFrameworkCore;
using Ti2026.Data;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// H2H tính trực tiếp từ Match + SeriesId, KHÔNG có bảng dẫn xuất riêng. Bảng dẫn xuất là
/// thứ sẽ lệch khỏi nguồn khi ingest chạy lại, và ở quy mô 16 đội thì query trực tiếp
/// nhanh hơn ngưỡng cảm nhận rất nhiều.
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

            var matches = await db.Matches
                .Where(m => m.RadiantTeamId != null && m.DireTeamId != null)
                .OrderByDescending(m => m.StartTime)
                .ToListAsync();

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

                pairs[group.Key] = new
                {
                    // Giữ tên `n` cho tương thích, nhưng thêm hai tên nói rõ nó là gì
                    n = games,
                    games,
                    seriesCount,

                    firstMet = group.Min(x => x.Match.StartTime).ToString("yyyy-MM-dd"),
                    lastMet = group.Max(x => x.Match.StartTime).ToString("yyyy-MM-dd"),

                    series = group.Select(x => new object[]
                    {
                        x.Match.StartTime.ToString("yyyy-MM-dd"),
                        x.Match.LeagueName ?? "",
                        x.Match.RadiantScore,
                        x.Match.DireScore,
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
                // nhưng nhãn phải nói đúng điều đang làm.
                window = "toàn bộ lịch sử đã nạp",
                order = Order,
                pairs,
            });
        });
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
