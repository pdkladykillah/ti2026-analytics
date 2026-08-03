using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// Dự đoán, phân tích và kể chuyện dữ liệu. Tất cả đọc từ dữ liệu pipeline đã nạp,
/// không gọi ra mạng ngoài.
/// </summary>
public static class AnalyticsEndpoints
{
    /// <summary>Dưới ngưỡng này thì mọi kết luận chỉ là nhiễu, và endpoint phải nói ra.</summary>
    private const int MinMatchesForConfidence = 20;

    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // ---------- Bảng Elo ----------
        api.MapGet("/ratings", async (Ti2026DbContext db) =>
        {
            var latest = await LatestSnapshotDateAsync(db);
            if (latest is null) return Results.Ok(Array.Empty<object>());

            var rows = await db.TeamStatSnapshots
                .Where(s => s.CapturedOn == latest && s.WindowDays == 180 && s.Elo != null)
                .Include(s => s.Team)
                .OrderByDescending(s => s.Elo)
                .Select(s => new
                {
                    teamSlug = s.Team!.Slug,
                    teamName = s.Team.Name,
                    logo = s.Team.LogoUrl,
                    elo = Math.Round(s.Elo!.Value),
                    maps = s.Maps,
                    winrate = Math.Round(s.Winrate),
                })
                .ToListAsync();

            return Results.Ok(rows);
        });

        // ---------- Dự đoán một cặp đấu ----------
        api.MapGet("/predict", async (Ti2026DbContext db, string a, string b) =>
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return Results.BadRequest(new { error = "cần tham số a và b là slug hai đội" });
            if (a == b)
                return Results.BadRequest(new { error = "hai đội phải khác nhau" });

            var teamA = await db.Teams.FirstOrDefaultAsync(t => t.Slug == a);
            var teamB = await db.Teams.FirstOrDefaultAsync(t => t.Slug == b);
            if (teamA is null || teamB is null) return Results.NotFound(new { error = "không tìm thấy đội" });

            var latest = await LatestSnapshotDateAsync(db);
            var snaps = latest is null
                ? []
                : await db.TeamStatSnapshots
                    .Where(s => s.CapturedOn == latest && s.WindowDays == 180
                                && (s.TeamId == teamA.Id || s.TeamId == teamB.Id))
                    .ToDictionaryAsync(s => s.TeamId);

            var eloA = snaps.TryGetValue(teamA.Id, out var sa) ? sa.Elo : null;
            var eloB = snaps.TryGetValue(teamB.Id, out var sb) ? sb.Elo : null;

            double? probA = eloA is double ea && eloB is double eb
                ? Math.Round(EloEngine.ExpectedScore(ea, eb) * 100, 1)
                : null;

            // Đối đầu trực tiếp
            var h2h = await db.Matches
                .Where(m => (m.RadiantTeamId == teamA.Id && m.DireTeamId == teamB.Id)
                            || (m.RadiantTeamId == teamB.Id && m.DireTeamId == teamA.Id))
                .OrderByDescending(m => m.StartTime)
                .ToListAsync();

            var aWins = h2h.Count(m => (m.RadiantTeamId == teamA.Id) == m.RadiantWin);

            // Phân phối cho kèo over/under — gộp trận của CẢ HAI đội, không chỉ đối đầu:
            // hai đội thường chỉ gặp nhau vài lần, quá ít để nói gì về tổng kills.
            var pool = await db.Matches
                .Where(m => m.RadiantTeamId == teamA.Id || m.DireTeamId == teamA.Id
                            || m.RadiantTeamId == teamB.Id || m.DireTeamId == teamB.Id)
                .Select(m => new { m.RadiantScore, m.DireScore, m.DurationSeconds })
                .ToListAsync();

            var totalKills = pool.Select(m => (double)(m.RadiantScore + m.DireScore)).ToList();
            var durations = pool.Select(m => m.DurationSeconds / 60.0).ToList();

            var killDist = DistributionStats.Describe(totalKills);
            var durDist = DistributionStats.Describe(durations);

            return Results.Ok(new
            {
                teamA = new { slug = teamA.Slug, name = teamA.Name, logo = teamA.LogoUrl, elo = eloA },
                teamB = new { slug = teamB.Slug, name = teamB.Name, logo = teamB.LogoUrl, elo = eloB },

                probabilityA = probA,
                probabilityB = probA is double p ? Math.Round(100 - p, 1) : (double?)null,

                // Nói thẳng mức tin cậy thay vì để người đọc tự đoán từ một con số trần trụi
                confidence = new
                {
                    matchesConsidered = pool.Count,
                    lowSample = pool.Count < MinMatchesForConfidence,
                    note = pool.Count < MinMatchesForConfidence
                        ? "Mẫu nhỏ — con số dao động mạnh, đừng đặt nặng."
                        : null,
                },

                headToHead = new
                {
                    played = h2h.Count,
                    aWins,
                    bWins = h2h.Count - aWins,
                    recent = h2h.Take(6).Select(m => new
                    {
                        date = m.StartTime.ToString("yyyy-MM-dd"),
                        league = m.LeagueName,
                        aWon = (m.RadiantTeamId == teamA.Id) == m.RadiantWin,
                        score = $"{m.RadiantScore}-{m.DireScore}",
                    }),
                },

                totalKills = new
                {
                    dist = killDist,
                    lines = OverUnderLines(totalKills, [44.5, 48.5, 52.5, 56.5]),
                },
                duration = new
                {
                    dist = durDist,
                    lines = OverUnderLines(durations, [34.5, 38.5, 42.5, 46.5]),
                },
            });
        });

        // ---------- Biến động: kể chuyện dữ liệu ----------
        api.MapGet("/changes", async (Ti2026DbContext db, int days = 7) =>
        {
            if (days is < 1 or > 180)
                return Results.BadRequest(new { error = "days phải trong khoảng 1..180" });

            var latest = await LatestSnapshotDateAsync(db);
            if (latest is null) return Results.Ok(new { baseline = (string?)null, changes = Array.Empty<object>() });

            var target = latest.Value.AddDays(-days);

            // Lấy snapshot gần ngày mục tiêu nhất chứ không đòi đúng ngày: pipeline có thể
            // lỡ một vòng, và khi đó "không có dữ liệu" là câu trả lời vô ích.
            var baseline = await db.TeamStatSnapshots
                .Where(s => s.WindowDays == 180 && s.CapturedOn <= target)
                .OrderByDescending(s => s.CapturedOn)
                .Select(s => (DateOnly?)s.CapturedOn)
                .FirstOrDefaultAsync();

            if (baseline is null)
                return Results.Ok(new
                {
                    baseline = (string?)null,
                    latest = latest.Value.ToString("yyyy-MM-dd"),
                    changes = Array.Empty<object>(),
                    note = "Chưa đủ lịch sử để so sánh. Mỗi ngày pipeline chạy sẽ thêm một mốc.",
                });

            var after = await SnapshotMapAsync(db, latest.Value);
            var before = await SnapshotMapAsync(db, baseline.Value);

            var all = new List<TeamChange>();
            foreach (var (teamId, a) in after)
            {
                if (!before.TryGetValue(teamId, out var bsnap)) continue;

                var metrics = ChangeDetector.Metrics(k => Metric(bsnap, k), k => Metric(a, k));
                all.AddRange(ChangeDetector.Detect(a.Team!.Slug, a.Team.Name, metrics));
            }

            return Results.Ok(new
            {
                baseline = baseline.Value.ToString("yyyy-MM-dd"),
                latest = latest.Value.ToString("yyyy-MM-dd"),
                days,
                changes = all.OrderByDescending(c => c.Magnitude).Take(20),
                note = all.Count == 0
                    ? "Không có biến động nào vượt ngưỡng đáng chú ý trong khoảng này."
                    : null,
            });
        });

        // ---------- Hero pool / draft ----------
        api.MapGet("/heroes", async (Ti2026DbContext db, string? team = null, int minGames = 3) =>
        {
            var q = db.MatchPlayers
                .Include(mp => mp.Match)
                .Where(mp => mp.Match!.RadiantTeamId != null && mp.Match.DireTeamId != null);

            if (!string.IsNullOrWhiteSpace(team))
            {
                var t = await db.Teams.FirstOrDefaultAsync(x => x.Slug == team);
                if (t is null) return Results.NotFound(new { error = "không tìm thấy đội" });

                q = q.Where(mp => mp.IsRadiant
                    ? mp.Match!.RadiantTeamId == t.Id
                    : mp.Match!.DireTeamId == t.Id);
            }

            var rows = await q
                .Select(mp => new
                {
                    mp.HeroId,
                    Won = mp.IsRadiant == mp.Match!.RadiantWin,
                })
                .ToListAsync();

            var heroes = rows
                .GroupBy(r => r.HeroId)
                .Where(g => g.Count() >= minGames)
                .Select(g => new
                {
                    heroId = g.Key,
                    games = g.Count(),
                    wins = g.Count(x => x.Won),
                    winrate = Math.Round(g.Count(x => x.Won) * 100.0 / g.Count(), 1),
                })
                .OrderByDescending(h => h.games)
                .Take(40);

            return Results.Ok(new { team, minGames, heroes });
        });

        // ---------- Chỉ số cá nhân, gồm nhịp 10 phút đầu ----------
        api.MapGet("/player-stats", async (Ti2026DbContext db, string? team = null) =>
        {
            var q = db.MatchPlayers
                .Include(mp => mp.Player)
                .Where(mp => mp.PlayerId != null);

            if (!string.IsNullOrWhiteSpace(team))
            {
                var t = await db.Teams.FirstOrDefaultAsync(x => x.Slug == team);
                if (t is null) return Results.NotFound(new { error = "không tìm thấy đội" });

                var ids = await db.RosterEntries
                    .Where(r => r.TeamId == t.Id && r.ValidTo == null)
                    .Select(r => r.PlayerId).ToListAsync();

                q = q.Where(mp => ids.Contains(mp.PlayerId!.Value));
            }

            var rows = await q.Select(mp => new
            {
                mp.PlayerId,
                Nick = mp.Player!.Nick,
                mp.Kills, mp.Deaths, mp.Assists, mp.GoldPerMin, mp.XpPerMin, mp.KillsFirst10Min,
            }).ToListAsync();

            var players = rows.GroupBy(r => new { r.PlayerId, r.Nick })
                .Where(g => g.Count() >= 5)
                .Select(g =>
                {
                    var early = g.Where(x => x.KillsFirst10Min.HasValue).ToList();
                    return new
                    {
                        nick = g.Key.Nick,
                        games = g.Count(),
                        kills = Math.Round(g.Average(x => (double)x.Kills), 2),
                        deaths = Math.Round(g.Average(x => (double)x.Deaths), 2),
                        assists = Math.Round(g.Average(x => (double)x.Assists), 2),
                        gpm = Math.Round(g.Average(x => (double)x.GoldPerMin)),
                        xpm = Math.Round(g.Average(x => (double)x.XpPerMin)),
                        // null khi chưa ván nào được parse — không quy về 0
                        killsFirst10 = early.Count == 0
                            ? (double?)null
                            : Math.Round(early.Average(x => (double)x.KillsFirst10Min!.Value), 2),
                        earlySample = early.Count,
                    };
                })
                .OrderByDescending(p => p.kills);

            return Results.Ok(new { team, players });
        });

        // ---------- Series Bo3/Bo5 ----------
        api.MapGet("/series", async (Ti2026DbContext db) =>
        {
            var games = await db.Matches
                .Where(m => m.SeriesId != null && m.RadiantTeamId != null && m.DireTeamId != null)
                .OrderBy(m => m.StartTime)
                .Select(m => new { m.SeriesId, m.StartTime, m.RadiantTeamId, m.DireTeamId, m.RadiantWin })
                .ToListAsync();

            var multi = games.GroupBy(g => g.SeriesId!.Value).Where(g => g.Count() > 1).ToList();

            var game1PredictsSeries = 0;
            var decided = 0;

            foreach (var s in multi)
            {
                var ordered = s.OrderBy(g => g.StartTime).ToList();
                var g1Winner = ordered[0].RadiantWin ? ordered[0].RadiantTeamId : ordered[0].DireTeamId;

                var tally = new Dictionary<int, int>();
                foreach (var g in ordered)
                {
                    var w = g.RadiantWin ? g.RadiantTeamId!.Value : g.DireTeamId!.Value;
                    tally[w] = tally.GetValueOrDefault(w) + 1;
                }

                var best = tally.OrderByDescending(kv => kv.Value).ToList();
                if (best.Count < 2 || best[0].Value != best[1].Value)
                {
                    decided++;
                    if (best[0].Key == g1Winner) game1PredictsSeries++;
                }
            }

            return Results.Ok(new
            {
                seriesCount = multi.Count,
                gamesInSeries = multi.Sum(s => s.Count()),
                decided,
                // Câu chuyện: thắng ván 1 có nói lên điều gì về cả series không?
                game1PredictsSeriesPct = decided == 0
                    ? (double?)null
                    : Math.Round(game1PredictsSeries * 100.0 / decided, 1),
            });
        });

        // ---------- Hiệu chuẩn dự đoán ----------
        api.MapGet("/calibration", async (Ti2026DbContext db) =>
        {
            var resolved = await db.Predictions
                .Where(p => p.TeamAWon != null)
                .Select(p => new { p.ProbabilityA, p.TeamAWon })
                .ToListAsync();

            if (resolved.Count == 0)
                return Results.Ok(new
                {
                    resolved = 0,
                    buckets = Array.Empty<object>(),
                    note = "Chưa có dự đoán nào được đối chiếu kết quả. Mô hình chưa thể coi là "
                         + "đã kiểm chứng — một dự đoán không đo được là một dự đoán không bác bỏ được.",
                });

            var buckets = resolved
                .GroupBy(p => Math.Clamp((int)(p.ProbabilityA / 10) * 10, 0, 90))
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    range = $"{g.Key}-{g.Key + 10}%",
                    predicted = Math.Round(g.Average(x => x.ProbabilityA), 1),
                    actual = Math.Round(g.Count(x => x.TeamAWon!.Value) * 100.0 / g.Count(), 1),
                    samples = g.Count(),
                });

            return Results.Ok(new { resolved = resolved.Count, buckets });
        });
    }

    // ---------- tiện ích ----------

    private static async Task<DateOnly?> LatestSnapshotDateAsync(Ti2026DbContext db) =>
        await db.TeamStatSnapshots
            .Where(s => s.WindowDays == 180)
            .OrderByDescending(s => s.CapturedOn)
            .Select(s => (DateOnly?)s.CapturedOn)
            .FirstOrDefaultAsync();

    private static async Task<Dictionary<int, TeamStatSnapshot>> SnapshotMapAsync(
        Ti2026DbContext db, DateOnly date) =>
        await db.TeamStatSnapshots
            .Where(s => s.CapturedOn == date && s.WindowDays == 180)
            .Include(s => s.Team)
            .ToDictionaryAsync(s => s.TeamId);

    private static double? Metric(TeamStatSnapshot s, string key) => key switch
    {
        "winrate" => s.Winrate,
        "killDiff" => s.KillDiff,
        "kills" => s.AvgKills,
        "deaths" => s.AvgDeaths,
        "firstBlood" => s.FirstBloodRate,
        "f10" => s.F10Rate,
        "duration" => s.AvgDurationMinutes,
        "elo" => s.Elo,
        _ => null,
    };

    private static object[] OverUnderLines(List<double> values, double[] lines) =>
        lines.Select(l => (object)new
        {
            line = l,
            overPct = DistributionStats.ProbabilityOver(values, l),
        }).ToArray();
}
