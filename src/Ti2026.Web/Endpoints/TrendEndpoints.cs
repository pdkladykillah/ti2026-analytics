using Microsoft.EntityFrameworkCore;
using Ti2026.Data;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// Giai đoạn 2: biến động chỉ số theo thời gian. Đọc thẳng từ bảng snapshot mà pipeline đã
/// tích luỹ — đây là thứ trang tĩnh hoàn toàn không làm được, vì JSON bị ghi đè mỗi lần cập nhật.
/// </summary>
public static class TrendEndpoints
{
    private static readonly int[] AllowedWindows = [30, 90, 180];

    public static void MapTrendEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/trend", async (
            Ti2026DbContext db,
            string? team = null,
            int window = 30,
            DateOnly? from = null,
            DateOnly? to = null) =>
        {
            if (!AllowedWindows.Contains(window))
                return Results.BadRequest(new
                {
                    error = "window chỉ nhận 30, 90 hoặc 180",
                    allowed = AllowedWindows,
                });

            var until = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var since = from ?? until.AddDays(-90);

            if (since > until)
                return Results.BadRequest(new { error = "from phải trước to" });

            var q = db.TeamStatSnapshots
                .Where(s => s.WindowDays == window
                            && s.CapturedOn >= since
                            && s.CapturedOn <= until);

            // team tuỳ chọn: không truyền thì trả cả 16 đội, dùng cho biểu đồ nhiều đường
            if (!string.IsNullOrWhiteSpace(team))
                q = q.Where(s => s.Team!.Slug == team);

            var rows = await q
                .OrderBy(s => s.CapturedOn)
                .ThenBy(s => s.Team!.Slug)
                .Select(s => new
                {
                    teamSlug = s.Team!.Slug,
                    teamName = s.Team.Name,
                    date = s.CapturedOn,
                    maps = s.Maps,
                    winrate = Math.Round(s.Winrate, 1),
                    kills = Math.Round(s.AvgKills, 2),
                    deaths = Math.Round(s.AvgDeaths, 2),
                    killDiff = Math.Round(s.KillDiff, 2),
                    totalKills = Math.Round(s.TotalKills, 2),
                    duration = Math.Round(s.AvgDurationMinutes, 1),
                    source = s.Source,
                })
                .ToListAsync();

            // Ngày không có snapshot thì KHUYẾT DÒNG, không trả 0. Một số 0 giả sẽ vẽ thành
            // cú sụt phong độ không hề tồn tại — phía client phải ngắt đường ở chỗ khuyết.
            return Results.Ok(rows);
        });
    }
}
