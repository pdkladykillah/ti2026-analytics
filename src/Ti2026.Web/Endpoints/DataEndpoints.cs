using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// Các endpoint trả ĐÚNG shape JSON mà index.html đang parse (dòng 254-269, 284, 295).
/// Nhờ vậy frontend chỉ phải đổi URL, không sửa logic render và không sờ vào CSS.
/// Sai một tên field ở đây là trang trắng — xem ApiContractTests.
/// </summary>
public static class DataEndpoints
{
    public static void MapDataEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/meta", async (Ti2026DbContext db) =>
        {
            var lastRun = await db.IngestRuns
                .Where(r => r.Status == IngestStatus.Succeeded)
                .OrderByDescending(r => r.FinishedAt)
                .FirstOrDefaultAsync();

            var latestDate = await LatestSnapshotDateAsync(db);

            var teamsTotal = await db.Teams.CountAsync();
            var teamsWithData = latestDate is null
                ? 0
                : await db.TeamStatSnapshots
                    .Where(s => s.CapturedOn == latestDate && s.WindowDays == 180)
                    .Select(s => s.TeamId)
                    .Distinct()
                    .CountAsync();

            return Results.Ok(new
            {
                updatedAt = lastRun?.FinishedAt ?? DateTime.UtcNow,
                window = "6 tháng gần nhất",
                source = lastRun is null ? "data/*.json (mồi)" : "OpenDota + dltv.org",
                teamsWithData,
                teamsTotal,
                // index.html:267 hiện nhãn "(seed)" khi cờ này bật
                seed = lastRun is null,
            });
        });

        api.MapGet("/teams", async (Ti2026DbContext db) =>
        {
            var teams = await db.Teams.OrderBy(t => t.Name).ToListAsync();

            var latestDate = await LatestSnapshotDateAsync(db);
            var snapshots = latestDate is null
                ? new Dictionary<int, TeamStatSnapshot>()
                : await db.TeamStatSnapshots
                    .Where(s => s.CapturedOn == latestDate && s.WindowDays == 180)
                    .ToDictionaryAsync(s => s.TeamId);

            return Results.Ok(new
            {
                @event = "The International 2026",
                source = "OpenDota + dltv.org",
                teams = teams.Select(t => new
                {
                    name = t.Name,
                    @short = t.ShortName,
                    slug = t.Slug,
                    region = t.Region,
                    qualification = t.Qualification,
                    logo = t.LogoUrl,
                    // null khi chưa có snapshot: index.html:255 xử lý sẵn (x.stats ? ... : null)
                    stats = snapshots.TryGetValue(t.Id, out var s) ? ToStatsDto(s) : null,
                }),
            });
        });

        api.MapGet("/rosters", async (Ti2026DbContext db) =>
        {
            var rows = await db.RosterEntries
                .Where(r => r.ValidTo == null)
                .Include(r => r.Team)
                .Include(r => r.Player)
                .ToListAsync();

            var rosters = rows
                .Where(r => r.Team is not null && r.Player is not null)
                .GroupBy(r => r.Team!.Slug)
                .ToDictionary(g => g.Key, g => g.Select(r => new
                {
                    nick = r.Player!.Nick,
                    real = r.Player.RealName,
                    role = r.Role,

                    // AvatarUrl (Steam), KHÔNG phải PhotoUrl (dltv.org/uploads). Ảnh dltv bị
                    // chặn hotlink theo referrer nên mọi thẻ img đó đều vỡ trên trình duyệt —
                    // trả về URL không hiện được thì tệ hơn trả null, vì null cho UI biết để
                    // hiện chữ cái thay thế.
                    photo = r.Player.AvatarUrl,
                }).ToList());

            return Results.Ok(new { source = "dltv.org", rosters });
        });

        // players.json và tiers.json là dữ liệu biên tập thuần ở giai đoạn này, phục vụ
        // trực tiếp từ file. tiers chuyển sang bảng TierEntry ở M3; players chuyển sang DB
        // khi Giai đoạn 3 làm phân tích cá nhân.
        api.MapGet("/players", (Ti2026Paths paths) => ServeEditorialFile(paths, "players.json"));

        api.MapGet("/tiers", (Ti2026Paths paths) => ServeEditorialFile(paths, "tiers.json"));
    }

    private static async Task<DateOnly?> LatestSnapshotDateAsync(Ti2026DbContext db) =>
        await db.TeamStatSnapshots
            .Where(s => s.WindowDays == 180)
            .OrderByDescending(s => s.CapturedOn)
            .Select(s => (DateOnly?)s.CapturedOn)
            .FirstOrDefaultAsync();

    private static IResult ServeEditorialFile(Ti2026Paths paths, string fileName)
    {
        var path = paths.EditorialFile(fileName);
        return File.Exists(path)
            ? Results.Text(File.ReadAllText(path), "application/json")
            : Results.NotFound();
    }

    /// <summary>
    /// Làm tròn để khớp ĐƠN VỊ của JSON cũ: phần trăm là số nguyên, số trung bình 2 chữ số
    /// thập phân, duration là phút nguyên. index.html:284 gọi v.toFixed(2) trên kills/deaths
    /// nên chúng phải là số, không phải chuỗi.
    /// </summary>
    private static object ToStatsDto(TeamStatSnapshot s) => new
    {
        maps = s.Maps,
        winrate = Math.Round(s.Winrate),
        kills = Math.Round(s.AvgKills, 2),
        deaths = Math.Round(s.AvgDeaths, 2),
        killDiff = Math.Round(s.KillDiff, 2),
        totalKills = Math.Round(s.TotalKills, 2),
        duration = Math.Round(s.AvgDurationMinutes),

        // null đi thẳng ra JSON thành null để UI hiện "—". KHÔNG quy về 0: một ô trống nói
        // "chưa đo được", còn số 0 nói "đo rồi và bằng không" — trộn hai thứ là dựng số liệu sai.
        assists = Round(s.AvgAssists, 2),
        firstBlood = Round(s.FirstBloodRate),
        f10 = Round(s.F10Rate),
        winWhenFb = Round(s.WinWhenFbRate),
        winWhenF10 = Round(s.WinWhenF10Rate),

        source = s.Source,
    };

    private static double? Round(double? value, int digits = 0) =>
        value.HasValue ? Math.Round(value.Value, digits) : null;
}
