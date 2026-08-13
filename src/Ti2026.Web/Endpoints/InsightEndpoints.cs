using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// api/insights — những gì hệ thống RÚT RA được về từng đội, không phải dữ liệu thô.
///
/// Tách khỏi api/trend vì hai thứ khác nhau: trend trả chuỗi thời gian để vẽ, còn đây trả câu
/// trả lời. Trang phong độ trước đây chỉ có một câu duy nhất — Elo đang lên hay xuống — nên
/// về cơ bản là một biểu đồ kèm nhãn hướng, còn mọi thứ khác nằm sẵn trong dữ liệu thì bắt
/// người đọc tự ghép.
/// </summary>
public static class InsightEndpoints
{
    /// <summary>Số ván gần nhất lấy để tính chuỗi thắng/thua.</summary>
    private const int RecentWindow = 20;

    public static void MapInsightEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/insights", async (Ti2026DbContext db) =>
        {
            var teams = await db.Teams.OrderBy(t => t.Slug).ToListAsync();
            if (teams.Count == 0) return Results.Ok(new { teams = Array.Empty<object>() });

            var latest = await db.TeamStatSnapshots
                .Where(s => s.WindowDays == 180)
                .MaxAsync(s => (DateOnly?)s.CapturedOn);

            var snaps = latest is null
                ? []
                : await db.TeamStatSnapshots
                    .Where(s => s.CapturedOn == latest && s.WindowDays == 180)
                    .ToDictionaryAsync(s => s.TeamId);

            var lineups = await LineupLookup.LoadAsync(db);

            // Mọi ván có ít nhất một đội của ta, để đếm chuỗi và bên sân. Lọc "đúng đội hình"
            // làm ở dưới, theo từng đội — cùng luật với Elo và form.
            var matches = await db.Matches
                .Where(m => m.RadiantTeamId != null || m.DireTeamId != null)
                .OrderByDescending(m => m.StartTime)
                .Select(m => new
                {
                    m.Id, m.StartTime, m.RadiantTeamId, m.DireTeamId, m.RadiantWin,
                })
                .ToListAsync();

            var facts = new List<TeamFacts>();

            foreach (var team in teams)
            {
                var mine = matches
                    .Where(m => m.RadiantTeamId == team.Id || m.DireTeamId == team.Id)
                    .Select(m =>
                    {
                        var isRadiant = m.RadiantTeamId == team.Id;
                        return new
                        {
                            m.Id, m.StartTime, IsRadiant = isRadiant,
                            Won = isRadiant == m.RadiantWin,
                            Kept = lineups.Kept(m.Id, team.Id, isRadiant),
                            Opponent = isRadiant ? m.DireTeamId : m.RadiantTeamId,
                            OpponentKept = isRadiant
                                ? (m.DireTeamId is int dd ? lineups.Kept(m.Id, dd, false) : 0)
                                : (m.RadiantTeamId is int rr ? lineups.Kept(m.Id, rr, true) : 0),
                        };
                    })
                    .Where(x => x.Kept >= 5)
                    .ToList();

                var snap = snaps.GetValueOrDefault(team.Id);

                // Khắc tinh: chỉ xét ván mà CẢ HAI bên đều đúng đội hình — cùng luật với đối đầu.
                var head = mine
                    .Where(x => x.Opponent is not null && x.OpponentKept >= 5)
                    .GroupBy(x => x.Opponent!.Value)
                    .Select(g => new
                    {
                        TeamId = g.Key,
                        Games = g.Count(),
                        Losses = g.Count(x => !x.Won),
                    })
                    .Where(g => g.Games >= 4)
                    .OrderByDescending(g => g.Losses * 1.0 / g.Games)
                    .ThenByDescending(g => g.Games)
                    .FirstOrDefault();

                var nemesis = head is null
                    ? null
                    : teams.FirstOrDefault(x => x.Id == head.TeamId);

                facts.Add(new TeamFacts(
                    Slug: team.Slug,
                    Name: team.Name,
                    Maps: snap?.Maps ?? 0,
                    Winrate: snap?.Winrate ?? 0,
                    KillDiff: snap?.KillDiff ?? 0,
                    AvgDurationMinutes: snap?.AvgDurationMinutes ?? 0,
                    FirstBloodRate: snap?.FirstBloodRate,
                    WinWhenFbRate: snap?.WinWhenFbRate,
                    F10Rate: snap?.F10Rate,
                    WinWhenF10Rate: snap?.WinWhenF10Rate,
                    Elo: snap?.Elo,
                    EloGames: snap?.EloGames ?? 0,
                    LineupGames: mine.Count,
                    LineupSince: mine.Count == 0
                        ? null
                        : mine.Min(x => x.StartTime).ToString("yyyy-MM-dd"),
                    RecentResults: mine.Take(RecentWindow).Select(x => x.Won).ToList(),
                    RadiantGames: mine.Count(x => x.IsRadiant),
                    RadiantWins: mine.Count(x => x.IsRadiant && x.Won),
                    DireGames: mine.Count(x => !x.IsRadiant),
                    DireWins: mine.Count(x => !x.IsRadiant && x.Won),
                    NemesisName: nemesis?.Name,
                    NemesisLosses: head?.Losses ?? 0,
                    NemesisGames: head?.Games ?? 0));
            }

            var rows = facts.Select(f => new
            {
                slug = f.Slug,
                name = f.Name,
                lineupGames = f.LineupGames,
                lineupSince = f.LineupSince,
                insights = TeamInsights.For(f, facts).Select(i => new
                {
                    kind = i.Kind, tone = i.Tone, text = i.Text,
                }).ToList(),
            }).ToList();

            return Results.Ok(new
            {
                updatedAt = DateTime.UtcNow,
                capturedOn = latest,

                method = "Chỉ tính ván mà đội ra trận đúng đội hình TI2026 — cùng luật với Elo và "
                       + "phong độ. Mỗi nhận định chỉ hiện khi vượt được ngưỡng nhiễu của chính "
                       + "nó, nên đội không có nhận định nào nghĩa là không có gì tách được khỏi "
                       + "may rủi, chứ không phải thiếu dữ liệu.",

                teams = rows,
            });
        });
    }
}
