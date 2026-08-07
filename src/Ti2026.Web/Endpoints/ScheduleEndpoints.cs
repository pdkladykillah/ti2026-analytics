using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// api/schedule — lịch thi đấu TI, kết quả tự điền.
///
/// LỊCH là dữ liệu biên tập (data/schedule.json) vì không có nguồn nào cho trận sắp diễn ra:
/// OpenDota chỉ có trận đã đá, và giải chính TI2026 còn chưa có trong danh mục giải của họ.
/// KẾT QUẢ thì đo được, nên không nhập tay — nhập tay sẽ tạo hai nguồn sự thật cho cùng một
/// tỷ số, và chúng sẽ lệch nhau đúng vào lúc giải đang diễn ra.
/// </summary>
public static class ScheduleEndpoints
{
    /// <summary>Nhận diện giải TI trong LeagueName. Đủ dùng và không cần ai khai id trước.</summary>
    private const string EventMarker = "International 2026";

    public static void MapScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/schedule", async (Ti2026DbContext db, Ti2026Paths paths) =>
        {
            var slugs = await db.Teams.ToDictionaryAsync(t => t.Id, t => t.Slug);
            var names = await db.Teams.ToDictionaryAsync(t => t.Slug, t => t.Name);
            var logos = await db.Teams.ToDictionaryAsync(t => t.Slug, t => t.LogoUrl);

            var played = (await db.Matches
                    .Where(m => m.LeagueName != null && m.LeagueName.Contains(EventMarker)
                                && m.RadiantTeamId != null && m.DireTeamId != null)
                    .Select(m => new
                    {
                        m.Id, m.SeriesId, m.StartTime, m.RadiantTeamId, m.DireTeamId,
                        m.RadiantWin, m.LeagueName,
                    })
                    .ToListAsync())
                .Select(m => new PlayedGame(
                    m.Id, m.SeriesId, m.StartTime,
                    slugs[m.RadiantTeamId!.Value], slugs[m.DireTeamId!.Value], m.RadiantWin))
                .ToList();

            var doc = ReadSchedule(paths, out var readError);
            var fixtures = doc.Fixtures;
            var now = DateTime.UtcNow;

            var rows = fixtures
                .Select(f => ScheduleBuilder.Build(f, played, now))
                .OrderBy(r => r.StartsAt)
                .Select(r => new
                {
                    startsAt = r.StartsAt,
                    stage = r.Stage,
                    format = r.Format,
                    status = r.Status,
                    winsA = r.WinsA,
                    winsB = r.WinsB,
                    gamesPlayed = r.GamesPlayed,
                    target = r.Target,
                    text = r.Text,
                    a = TeamDto(r.SlugA),
                    b = TeamDto(r.SlugB),
                })
                .ToList();

            // Ván đã đá mà không khớp cặp nào trong lịch. Khi lịch chưa nhập thì đây là toàn bộ
            // nội dung của trang — bỏ đi thì tab trống trơn trong lúc dữ liệu nằm sẵn trong DB.
            var loose = ScheduleBuilder.Unscheduled(fixtures, played)
                .GroupBy(g => g.SeriesId is long s && s > 0 ? $"s{s}" : $"m{g.MatchId}")
                .Select(g =>
                {
                    var first = g.OrderBy(x => x.StartTime).First();
                    var winsA = g.Count(x => x.SlugA == first.SlugA ? x.AWon : !x.AWon);
                    return new
                    {
                        startsAt = first.StartTime,
                        games = g.Count(),
                        winsA,
                        winsB = g.Count() - winsA,
                        a = TeamDto(first.SlugA),
                        b = TeamDto(first.SlugB),
                    };
                })
                .OrderByDescending(x => x.startsAt)
                .ToList();

            return Results.Ok(new
            {
                updatedAt = DateTime.UtcNow,
                @event = doc.Event,
                fixtureCount = fixtures.Count,
                serverTime = now,

                // Nói RÕ vì sao lịch phải nhập tay, ngay trong payload — người mở trang thấy tab
                // trống mà không có lời giải thích sẽ hiểu là hệ thống hỏng.
                source = "Lịch nhập tay ở data/schedule.json; tỷ số đọc từ ván thật của OpenDota.",
                whyManual = "OpenDota không có endpoint nào cho trận sắp diễn ra, và giải chính "
                          + "TI2026 còn chưa xuất hiện trong danh mục giải của họ — mới chỉ có 5 "
                          + "giải vòng loại khu vực.",
                readError,

                fixtures = rows,
                unscheduled = loose,
            });

            object TeamDto(string slug) => new
            {
                slug,
                name = names.GetValueOrDefault(slug, slug),
                logo = logos.GetValueOrDefault(slug),
            };
        });
    }

    private sealed record ScheduleDoc(string? Event, List<Fixture> Fixtures);

    /// <summary>
    /// Đọc schedule.json. File hỏng thì trả lịch RỖNG kèm lời báo lỗi, KHÔNG ném.
    ///
    /// Cùng lý do với try/catch quanh EditorialSeeder: file này được sửa tay, nên một dấu phẩy
    /// thừa là chuyện sẽ xảy ra. Để nó hạ cả endpoint thì mất luôn phần kết quả tự động vốn
    /// không liên quan gì tới lỗi cú pháp đó.
    /// </summary>
    private static ScheduleDoc ReadSchedule(Ti2026Paths paths, out string? error)
    {
        error = null;
        var path = paths.EditorialFile("schedule.json");
        if (!File.Exists(path)) return new ScheduleDoc(null, []);

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;

            var name = root.TryGetProperty("event", out var e) ? e.GetString() : null;
            var list = new List<Fixture>();

            if (root.TryGetProperty("fixtures", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in arr.EnumerateArray())
                {
                    if (!f.TryGetProperty("startsAt", out var s) || !s.TryGetDateTime(out var when))
                        continue;
                    if (!f.TryGetProperty("a", out var a) || !f.TryGetProperty("b", out var b))
                        continue;

                    list.Add(new Fixture(
                        when.ToUniversalTime(),
                        f.TryGetProperty("stage", out var st) ? st.GetString() ?? "" : "",
                        a.GetString() ?? "",
                        b.GetString() ?? "",
                        f.TryGetProperty("format", out var fm) ? fm.GetString() : null));
                }
            }

            return new ScheduleDoc(name, list);
        }
        catch (Exception ex)
        {
            error = $"schedule.json không đọc được ({ex.Message}) — trang vẫn hiện phần kết quả "
                  + "đo được, chỉ thiếu lịch.";
            return new ScheduleDoc(null, []);
        }
    }
}
