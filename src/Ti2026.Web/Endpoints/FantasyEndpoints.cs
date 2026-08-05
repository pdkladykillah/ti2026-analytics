using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// Fantasy TI2026: điểm của từng tuyển thủ tính từ trận thật, và bộ chọn đội hình.
///
/// LUẬT QUAN TRỌNG NHẤT Ở ĐÂY: khi bảng hệ số chưa điền đủ thì KHÔNG tính điểm. Trả về một
/// bảng xếp hạng toàn số 0 sẽ trông y hệt một bảng đã đo, và người đọc không có cách nào biết
/// mình đang nhìn vào chỗ trống.
/// </summary>
public static class FantasyEndpoints
{
    /// <summary>Dưới mức này thì trung bình mỗi trận dao động quá mạnh để xếp hạng.</summary>
    private const int MinMatchesForRanking = 5;

    public static void MapFantasyEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/fantasy");

        // ---------- Bảng hệ số đang dùng ----------
        api.MapGet("/config", (Ti2026Paths paths) =>
        {
            var config = LoadConfig(paths, out var error);
            if (config is null) return Results.Ok(new { ready = false, error });

            return Results.Ok(new
            {
                ready = config.Ready,
                source = config.Source,
                updatedAt = config.UpdatedAt,
                countBestGames = config.CountBestGames,
                missingCoefficients = config.MissingCoefficients,
                stats = config.Stats.Select(s => new { s.Key, s.Label, s.Per, s.Points }),
                note = config.Ready
                    ? null
                    : "Chưa điền đủ hệ số nên chưa tính được điểm. Sửa data/fantasy.json.",
            });
        });

        // ---------- Điểm từng tuyển thủ ----------
        api.MapGet("/players", async (Ti2026DbContext db, Ti2026Paths paths, int days = 120) =>
        {
            var config = LoadConfig(paths, out var error);
            if (config is null)
                return Results.Ok(Blocked($"Không đọc được data/fantasy.json: {error}"));

            if (!config.Ready)
                return Results.Ok(Blocked(
                    "Bảng hệ số chưa điền đủ — còn thiếu: "
                    + string.Join(", ", config.MissingCoefficients)
                    + ". Chưa tính điểm, vì một bảng toàn số 0 trông y hệt một bảng đã đo."));

            var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 7, 400));

            var rows = await db.MatchPlayers
                .Where(mp => mp.PlayerId != null && mp.Match!.StartTime >= since)
                .Select(mp => new
                {
                    mp.PlayerId,
                    Nick = mp.Player!.Nick,
                    mp.MatchId,
                    mp.Match!.SeriesId,
                    mp.Match.StartTime,
                    mp.LaneRole,
                    mp.NetWorth,
                    mp.IsRadiant,

                    mp.Kills, mp.Deaths, mp.GoldPerMin,
                    mp.LastHits, mp.Denies,
                    mp.ObserversPlaced, mp.CampsStacked, mp.RunePickups,
                    mp.StunSeconds, mp.TeamfightParticipation,
                    mp.TowerKills, mp.RoshanKills, mp.CourierKills, mp.FirstBloodClaimed,
                })
                .ToListAsync();

            if (rows.Count == 0) return Results.Ok(Blocked("Chưa có ván nào trong cửa sổ này."));

            // Vị trí suy từ lane + tài sản, cùng phép đã kiểm chứng ở tier list
            var position = rows
                .GroupBy(r => new { r.MatchId, r.IsRadiant, r.LaneRole })
                .SelectMany(g => PositionInference.InferGroup(g, x => x.LaneRole, x => x.NetWorth))
                .ToDictionary(x => (x.Row.MatchId, x.Row.PlayerId), x => x.Position);

            var players = rows
                .GroupBy(r => new { r.PlayerId, r.Nick })
                .Select(g =>
                {
                    var games = g.Select(r => FantasyScorer.ScoreGame(
                        new FantasyGame(r.MatchId, r.SeriesId, r.StartTime, Values(r)), config))
                        .ToList();

                    var avg = FantasyScorer.AverageMatchScore(games, config.CountBestGames);

                    // Vị trí hay gặp nhất — người đổi vai giữa các ván thì lấy vai chính
                    var positions = g
                        .Select(r => position.GetValueOrDefault((r.MatchId, r.PlayerId)))
                        .Where(p => p > 0)
                        .GroupBy(p => p)
                        .OrderByDescending(x => x.Count())
                        .ToList();

                    return new
                    {
                        playerId = g.Key.PlayerId,
                        nick = g.Key.Nick,
                        position = positions.Count > 0 ? positions[0].Key : (int?)null,
                        positionName = positions.Count > 0
                            ? PositionInference.Name(positions[0].Key) : null,

                        games = games.Count,
                        matches = FantasyScorer.MatchScores(games, config.CountBestGames).Count,
                        avgPerMatch = avg,

                        // Phân rã trung bình từng thành phần: người đọc phải thấy điểm đến từ
                        // đâu, nếu không thì đây chỉ là một con số phải tin
                        parts = config.Stats.Select(s => new
                        {
                            s.Key, s.Label,
                            avgPoints = Avg(games, s.Key),
                        }),
                    };
                })
                .Where(p => p.matches >= MinMatchesForRanking && p.avgPerMatch != null)
                .OrderByDescending(p => p.avgPerMatch)
                .ToList();

            return Results.Ok(new
            {
                ready = true,
                days,
                countBestGames = config.CountBestGames,
                minMatches = MinMatchesForRanking,
                source = config.Source,
                players,
                method = $"Điểm một trận = tổng {config.CountBestGames} ván cao nhất trong series. "
                       + "Giá trị một người = TRUNG BÌNH điểm mỗi trận, không phải tổng — cộng dồn "
                       + "thì ai thi đấu nhiều giải hơn luôn đứng đầu bất kể chơi hay dở.",
                caveat = "Chỉ số chưa nạp được để null và bị loại khỏi phép tính, không quy về 0. "
                       + "Xem cột phân rã để biết thành phần nào đang trống.",
            });
        });
    }

    private static object Blocked(string note) => new
    {
        ready = false,
        players = Array.Empty<object>(),
        note,
    };

    private static double? Avg(List<FantasyGameScore> games, string key)
    {
        var vals = games
            .SelectMany(g => g.Parts)
            .Where(p => p.Key == key && p.Points is not null)
            .Select(p => p.Points!.Value)
            .ToList();

        return vals.Count == 0 ? null : Math.Round(vals.Average(), 2);
    }

    /// <summary>
    /// Ánh xạ cột DB sang khoá trong bảng hệ số.
    ///
    /// Khoá KHÔNG có ở đây thì scorer để null và khai ra ở phần phân rã — đúng thứ cần cho
    /// Madstone: OpenDota không lộ sự kiện nhặt lotus, không ở cấp người chơi lẫn cấp trận,
    /// nên không ai đo chính xác được (công cụ fantasy khác cũng tự nhận là ước lượng).
    /// Điền 0 cho nó thì cả bảng lặng lẽ thành "chưa ai nhặt lotus bao giờ".
    /// </summary>
    private static Dictionary<string, double?> Values(dynamic r) => new()
    {
        ["kills"] = r.Kills,
        ["deaths"] = r.Deaths,
        ["gpm"] = r.GoldPerMin,
        ["creeps"] = r.LastHits is int lh ? lh + (r.Denies as int? ?? 0) : (double?)null,
        ["wards"] = r.ObserversPlaced,
        ["camps"] = r.CampsStacked,
        ["runes"] = r.RunePickups,
        ["stuns"] = r.StunSeconds,
        ["teamfight"] = r.TeamfightParticipation,

        ["towerKills"] = r.TowerKills,
        ["roshanKills"] = r.RoshanKills,
        ["courierKills"] = r.CourierKills,
        ["firstBlood"] = r.FirstBloodClaimed is bool fb ? (fb ? 1.0 : 0.0) : (double?)null,
    };

    private static FantasyConfig? LoadConfig(Ti2026Paths paths, out string? error)
    {
        error = null;

        try
        {
            var path = paths.EditorialFile("fantasy.json");
            if (!File.Exists(path)) { error = "không tìm thấy tệp"; return null; }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            var stats = new List<FantasyStat>();
            if (root.TryGetProperty("stats", out var statsEl))
            {
                foreach (var s in statsEl.EnumerateObject())
                {
                    if (s.Name.StartsWith('_')) continue;

                    stats.Add(new FantasyStat(
                        s.Name,
                        s.Value.TryGetProperty("label", out var l) ? l.GetString() ?? s.Name : s.Name,
                        s.Value.TryGetProperty("per", out var p) ? p.GetDouble() : 1,
                        s.Value.TryGetProperty("points", out var pt)
                            && pt.ValueKind == JsonValueKind.Number
                            ? pt.GetDouble()
                            : null));
                }
            }

            var best = root.TryGetProperty("series", out var se)
                       && se.TryGetProperty("countBestGames", out var cb)
                ? cb.GetInt32()
                : 2;

            return new FantasyConfig(
                stats,
                best,
                root.TryGetProperty("source", out var src) ? src.GetString() : null,
                root.TryGetProperty("updatedAt", out var ua) && ua.ValueKind == JsonValueKind.String
                    ? ua.GetDateTime()
                    : null);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            error = ex.Message;
            return null;
        }
    }
}
