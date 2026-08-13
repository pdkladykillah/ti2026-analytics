using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// api/schedule — bảng đấu và lịch thi đấu The International.
///
/// NGUỒN LÀ API CHÍNH CHỦ CỦA VALVE (www.dota2.com/webapi/IDOTA2League/GetLeagueData), không
/// phải dữ liệu nhập tay. Valve công bố sẵn khung bảng đấu — Swiss, Elimination Round,
/// Playoff — kèm giờ và tỷ số từng nút, và cập nhật trong lúc giải diễn ra.
///
/// Đã cân nhắc hai nguồn khác và loại: OpenDota chỉ có trận ĐÃ đánh, không có endpoint nào cho
/// trận sắp diễn ra; Liquipedia có lịch nhưng robots.txt của họ ghi thẳng
/// "Disallow: /dota2/api.php" dưới User-agent: * nên không được phép lấy tự động.
/// </summary>
public static class ScheduleEndpoints
{
    public static void MapScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/schedule", async (Ti2026DbContext db) =>
        {
            var rows = await db.ScheduledSeries
                .Include(s => s.Team1)
                .Include(s => s.Team2)
                .OrderBy(s => s.ScheduledAt == null)
                .ThenBy(s => s.ScheduledAt)
                .ThenBy(s => s.NodeId)
                .ToListAsync();

            if (rows.Count == 0)
            {
                return Results.Ok(new
                {
                    updatedAt = DateTime.UtcNow,
                    source = Source,
                    ready = false,
                    note = "Chưa nạp được bảng đấu. Vòng ingest tới sẽ lấy về từ Valve.",
                    stages = Array.Empty<object>(),
                });
            }

            var now = DateTime.UtcNow;

            var stages = rows
                .GroupBy(s => s.GroupName ?? "Khác")
                .Select(g => new
                {
                    name = g.Key,
                    total = g.Count(),
                    done = g.Count(x => x.IsCompleted),

                    // Giờ sớm nhất/muộn nhất mà Valve ĐÃ xếp cho vòng này. null khi chưa xếp —
                    // và "chưa xếp" là trạng thái thật, không phải thiếu dữ liệu.
                    from = g.Where(x => x.ScheduledAt != null).Min(x => x.ScheduledAt),
                    to = g.Where(x => x.ScheduledAt != null).Max(x => x.ScheduledAt),

                    series = g.Select(s => new
                    {
                        nodeId = s.NodeId,
                        name = s.Name,
                        scheduledAt = s.ScheduledAt,
                        actualAt = s.ActualAt,
                        wins1 = s.Wins1,
                        wins2 = s.Wins2,
                        status = Status(s.IsCompleted, s.HasStarted, s.ScheduledAt, now),

                        // Nút chưa biết đội nào vào thì nói NÓ NHẬN ĐỘI TỪ ĐÂU, thay vì để
                        // trống — đó là thông tin thật của một bảng đấu loại trực tiếp.
                        from1 = s.TeamId1 == null && s.ValveTeamId1 == null ? s.IncomingNodeId1 : null,
                        from2 = s.TeamId2 == null && s.ValveTeamId2 == null ? s.IncomingNodeId2 : null,

                        // CẠNH CỦA ĐỒ THỊ, trả về LUÔN LUÔN — khác hẳn from1/from2 ở trên vốn
                        // chỉ là chữ hiển thị khi chưa biết đội và biến mất ngay khi biết.
                        //
                        // Không có bốn trường này thì không dựng được nhánh đấu: hình dạng của
                        // một bảng loại kép nằm ở chỗ ai đi tiếp và ai rơi xuống nhánh thua,
                        // mà đó chính là winTo/loseTo. Và phải giữ cả sau khi trận đã đánh xong,
                        // vì cây vẫn phải vẽ được khi mọi ô đã điền đội.
                        in1 = s.IncomingNodeId1,
                        in2 = s.IncomingNodeId2,
                        winTo = s.WinningNodeId,
                        loseTo = s.LosingNodeId,

                        team1 = TeamDto(s.Team1?.Slug, s.Team1?.Name, s.Team1?.LogoUrl, s.ValveTeamId1),
                        team2 = TeamDto(s.Team2?.Slug, s.Team2?.Name, s.Team2?.LogoUrl, s.ValveTeamId2),
                    }).ToList(),
                })
                .ToList();

            return Results.Ok(new
            {
                updatedAt = DateTime.UtcNow,
                source = Source,
                ready = true,
                leagueId = rows[0].LeagueId,
                syncedAt = rows.Max(r => r.SyncedAt),

                totalSeries = rows.Count,
                scheduledSeries = rows.Count(r => r.ScheduledAt != null),
                completedSeries = rows.Count(r => r.IsCompleted),

                // Nói rõ khi Valve mới chỉ dựng khung mà chưa xếp giờ. Không có câu này thì một
                // bảng đấu 27 nút trống trơn trông y hệt một lỗi tải dữ liệu.
                note = rows.All(r => r.ScheduledAt == null)
                    ? "Valve đã công bố khung bảng đấu nhưng chưa xếp giờ và chưa điền đội. "
                      + "Các ô sẽ tự đầy lên khi họ cập nhật — không cần ai nhập tay."
                    : null,

                stages,
            });

            static object? TeamDto(string? slug, string? name, string? logo, int? valveId) =>
                slug is null && valveId is null
                    ? null
                    : new { slug, name = name ?? (valveId is null ? null : $"#{valveId}"), logo };
        });
    }

    private const string Source =
        "Bảng đấu và lịch lấy từ API chính chủ của Valve (dota2.com), tự cập nhật mỗi vòng ingest.";

    private static string Status(bool completed, bool started, DateTime? scheduled, DateTime now) =>
        completed ? "da-xong"
        : started ? "dang-dien-ra"
        : scheduled is null ? "chua-xep-gio"
        : scheduled > now ? "sap-toi"
        : "cho-ket-qua";
}
