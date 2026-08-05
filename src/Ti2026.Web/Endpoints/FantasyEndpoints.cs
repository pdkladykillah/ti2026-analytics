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

    private sealed record ScoredPlayer(
        int PlayerId, string Nick, int? Position, int Games, int Matches,
        double? Average, List<FantasyGameScore> Games_);

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
                slots = LoadSlots(paths).Select(s => new { group = s.Key, count = s.Value }),
                slotsConfirmed = SlotsConfirmed(paths),
                bias = BiasWarning(config),
                stats = config.Stats.Select(s => new { s.Key, s.Label, s.Per, s.Points, s.Color, sourced = !string.IsNullOrEmpty(s.Field) }),

                // Giới hạn của NGUỒN, không phải của bảng hệ số — điền hệ số cũng không cứu được
                unavailable = new[]
                {
                    "Madstone / Lotus — OpenDota không lộ sự kiện nhặt",
                    "First blood trước tiếng còi",
                    "Giết ở fountain",
                },

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

            var scored = await ScorePlayersAsync(db, config, days);
            if (scored.Count == 0) return Results.Ok(Blocked("Chưa có ván nào trong cửa sổ này."));

            return Results.Ok(new
            {
                ready = true,
                days,
                countBestGames = config.CountBestGames,
                minMatches = MinMatchesForRanking,
                source = config.Source,
                bias = BiasWarning(config),

                players = scored.Select(p => new
                {
                    playerId = p.PlayerId,
                    nick = p.Nick,
                    position = p.Position,
                    positionName = p.Position is int pos ? PositionInference.Name(pos) : null,
                    group = p.Position is int g ? GroupOf(g) : null,
                    games = p.Games,
                    matches = p.Matches,
                    avgPerMatch = p.Average,

                    // Phân rã: người đọc phải thấy điểm đến từ đâu, nếu không thì đây chỉ là
                    // một con số phải tin
                    parts = config.Stats.Select(s => new
                    {
                        s.Key, s.Label,
                        avgPoints = AvgPart(p.Games_, s.Key),
                    }),
                }),

                method = $"Điểm một trận = tổng {config.CountBestGames} ván cao nhất trong series. "
                       + "Giá trị một người = TRUNG BÌNH điểm mỗi trận, không phải tổng — cộng dồn "
                       + "thì ai thi đấu nhiều giải hơn luôn đứng đầu bất kể chơi hay dở.",
                caveat = "Chỉ số chưa nạp được để null và bị loại khỏi phép tính, không quy về 0. "
                       + "Xem cột phân rã để biết thành phần nào đang trống.",
            });
        });

        MapOptimize(api);
    }

    /// <summary>
    /// Chọn đội hình điểm cao nhất theo số suất mỗi nhóm vị trí.
    ///
    /// GIỚI HẠN PHẢI NÓI RA: chọn top N mỗi nhóm là đáp án tối ưu KHI các tuyển thủ độc lập với
    /// nhau. Luật TI2026 có thưởng cho CẶP CÙNG ĐỘI, và thưởng đó làm các lựa chọn phụ thuộc
    /// lẫn nhau. Chưa có bảng luật thì chưa mô hình hoá được — nên endpoint khai rõ thay vì gọi
    /// kết quả là "tối ưu".
    /// </summary>
    private static void MapOptimize(RouteGroupBuilder api)
    {
        api.MapGet("/optimize", async (Ti2026DbContext db, Ti2026Paths paths, int days = 120) =>
        {
            var config = LoadConfig(paths, out var error);
            if (config is null || !config.Ready)
                return Results.Ok(new
                {
                    ready = false,
                    roster = Array.Empty<object>(),
                    note = error is not null
                        ? $"Không đọc được data/fantasy.json: {error}"
                        : "Bảng hệ số chưa điền đủ nên chưa xếp được đội hình. Còn thiếu: "
                          + string.Join(", ", config!.MissingCoefficients),
                });

            var slots = LoadSlots(paths);
            var scored = await ScorePlayersAsync(db, config, days);

            var byGroup = scored
                .Where(p => p.Position is int)
                .GroupBy(p => GroupOf(p.Position!.Value))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Average).ToList());

            var picked = new List<object>();
            var shortfall = new List<string>();
            double total = 0;

            foreach (var (group, count) in slots)
            {
                var pool = byGroup.GetValueOrDefault(group) ?? [];
                if (pool.Count < count)
                    shortfall.Add($"{group}: cần {count}, chỉ có {pool.Count} người đủ mẫu");

                foreach (var p in pool.Take(count))
                {
                    picked.Add(new
                    {
                        group,
                        playerId = p.PlayerId,
                        nick = p.Nick,
                        position = p.Position,
                        positionName = p.Position is int pos ? PositionInference.Name(pos) : null,
                        avgPerMatch = p.Average,
                        matches = p.Matches,
                    });
                    total += p.Average ?? 0;
                }
            }

            return Results.Ok(new
            {
                ready = true,
                days,
                slots = slots.Select(s => new { group = s.Key, count = s.Value }),
                slotsConfirmed = SlotsConfirmed(paths),
                roster = picked,
                bias = BiasWarning(config),
                projectedTotal = Math.Round(total, 2),
                shortfall,

                method = "Chọn top N mỗi nhóm vị trí theo điểm trung bình mỗi trận. Khi các tuyển "
                       + "thủ độc lập thì đây CHÍNH LÀ đáp án tối ưu, không cần thuật toán phức tạp hơn.",
                limitation = "CHƯA tính thưởng cặp cùng đội — luật TI2026 có thưởng đó và nó làm các "
                           + "lựa chọn phụ thuộc lẫn nhau, nên đây chưa phải đội hình tối ưu thật sự.",
            });
        });
    }

    /// <summary>
    /// Tính điểm cho mọi tuyển thủ. Dùng CHUNG cho /players và /optimize — hai nơi tự tính
    /// riêng là hai nơi sẽ lệch nhau, và người dùng sẽ thấy bảng xếp hạng không khớp đội hình.
    /// </summary>
    private static async Task<List<ScoredPlayer>> ScorePlayersAsync(
        Ti2026DbContext db, FantasyConfig config, int days)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 7, 400));

        var rows = await db.MatchPlayers
            .Where(mp => mp.PlayerId != null && mp.Match!.StartTime >= since)
            .Select(mp => new
            {
                PlayerId = mp.PlayerId!.Value,
                Nick = mp.Player!.Nick,
                mp.MatchId,
                mp.Match!.SeriesId,
                mp.Match.StartTime,
                mp.LaneRole,
                mp.NetWorth,
                mp.IsRadiant,

                mp.Kills, mp.Deaths, mp.GoldPerMin, mp.LastHits, mp.Denies,
                mp.ObserversPlaced, mp.CampsStacked, mp.RunePickups,
                mp.StunSeconds, mp.TeamfightParticipation,
                mp.TowerKills, mp.RoshanKills, mp.CourierKills, mp.FirstBloodClaimed,
                mp.Lotuses, mp.Watchers, mp.Smokes, mp.MadstoneBundles, mp.TormentorKills,
            })
            .ToListAsync();

        if (rows.Count == 0) return [];

        var position = rows
            .GroupBy(r => new { r.MatchId, r.IsRadiant, r.LaneRole })
            .SelectMany(g => PositionInference.InferGroup(g, x => x.LaneRole, x => x.NetWorth))
            .ToDictionary(x => (x.Row.MatchId, x.Row.PlayerId), x => x.Position);

        return rows
            .GroupBy(r => new { r.PlayerId, r.Nick })
            .Select(g =>
            {
                var games = g.Select(r => FantasyScorer.ScoreGame(
                        new FantasyGame(r.MatchId, r.SeriesId, r.StartTime, Values(r)), config))
                    .ToList();

                // Vị trí hay gặp nhất — người đổi vai giữa các ván thì lấy vai chính
                var top = g.Select(r => position.GetValueOrDefault((r.MatchId, r.PlayerId)))
                    .Where(p => p > 0)
                    .GroupBy(p => p)
                    .OrderByDescending(x => x.Count())
                    .FirstOrDefault();

                return new ScoredPlayer(
                    g.Key.PlayerId, g.Key.Nick,
                    top?.Key,
                    games.Count,
                    FantasyScorer.MatchScores(games, config.CountBestGames).Count,
                    FantasyScorer.AverageMatchScore(games, config.CountBestGames),
                    games);
            })
            .Where(p => p.Matches >= MinMatchesForRanking && p.Average != null)
            .OrderByDescending(p => p.Average)
            .ToList();
    }

    private static string GroupOf(int position) => position switch
    {
        1 => "core",
        2 => "mid",
        3 => "core",
        _ => "support",
    };

    private static double? AvgPart(List<FantasyGameScore> games, string key)
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
    /// Khoá KHÔNG có ở đây thì scorer để null và khai ra ở phần phân rã, chứ không quy về 0 —
    /// điền 0 thì cả bảng lặng lẽ thành "chưa ai làm bao giờ".
    /// </summary>
    private static Dictionary<string, double?> Values(dynamic r) => new()
    {
        ["kills"] = r.Kills,
        ["deaths"] = r.Deaths,
        ["gpm"] = r.GoldPerMin,
        ["creeps"] = r.LastHits is int lh ? lh + (r.Denies as int? ?? 0) : (double?)null,
        ["wards"] = r.ObserversPlaced,
        ["stacks"] = r.CampsStacked,
        ["runes"] = r.RunePickups,
        ["stuns"] = r.StunSeconds,
        ["teamfight"] = r.TeamfightParticipation,
        ["towers"] = r.TowerKills,
        ["roshan"] = r.RoshanKills,
        ["courier"] = r.CourierKills,
        ["firstBlood"] = r.FirstBloodClaimed is bool fb ? (fb ? 1.0 : 0.0) : (double?)null,

        // Nam chi so tung bi ket luan nham la "OpenDota khong co". Chung nam trong cung payload
        // matches/{id}, chi la trong item_uses/ability_uses/killed. Xem FantasyFields.
        ["lotuses"] = r.Lotuses,
        ["watchers"] = r.Watchers,
        ["smokes"] = r.Smokes,
        ["tormentor"] = r.TormentorKills,

        // GAN DUNG: so tui madstone da dung, khong phai so madstone nhat duoc. Danh dau
        // approx trong fantasy.json de moi cho hien thi deu noi ro dieu do.
        ["madstones"] = r.MadstoneBundles,
    };

    /// <summary>
    /// Cảnh báo thiên lệch: chỉ số nào có hệ số nhưng chưa có nguồn, và chúng dồn về màu nào.
    ///
    /// Đây KHÔNG phải chuyện nhỏ. Màu xanh dương là chỉ số của hỗ trợ; thiếu ba trong sáu chỉ số
    /// đó nghĩa là mọi người chơi hỗ trợ bị chấm thiếu điểm một cách CÓ HỆ THỐNG, và đội hình
    /// gợi ý sẽ nghiêng về core mà người đọc không thấy vì sao. Nhiễu thì trung bình sẽ bù,
    /// thiên lệch thì không bao giờ.
    /// </summary>
    private static object? BiasWarning(FantasyConfig config)
    {
        var missing = config.UnsourcedStats;
        var approx = config.ApproximateStats;
        if (missing.Count == 0 && approx.Count == 0) return null;

        var byColor = missing
            .GroupBy(s => s.Color ?? "?")
            .ToDictionary(g => g.Key, g => g.Select(x => x.Label).ToList());

        var colorName = new Dictionary<string, string>
        {
            ["red"] = "đỏ (core)", ["blue"] = "xanh dương (hỗ trợ)", ["green"] = "xanh lá (chung)",
        };

        return new
        {
            count = missing.Count,
            total = config.Stats.Count,
            stats = missing.Select(s => new { s.Key, s.Label, s.Color }),
            byColor = byColor.Select(kv => new
            {
                color = kv.Key,
                colorLabel = colorName.GetValueOrDefault(kv.Key, kv.Key),
                stats = kv.Value,
            }),
            message = missing.Count == 0
                ? null
                : "Những chỉ số này CÓ hệ số nhưng CHƯA có nguồn dữ liệu, nên bị bỏ khỏi "
                + "phép tính. Đây là thiên lệch có hệ thống chứ không phải nhiễu: nhóm vị trí "
                + "nào dùng nhiều chỉ số đang thiếu sẽ bị chấm thấp hơn thực tế.",

            // Loại sai thứ hai, và là loại khó thấy hơn: chỉ số VẪN được tính, vẫn hiện ra
            // một con số trông bình thường, nhưng nguồn của nó chỉ gần đúng. Gộp nó chung với
            // "chưa có nguồn" thành một con số duy nhất sẽ giấu mất đúng cái cần nói.
            approximate = approx.Count == 0 ? null : new
            {
                count = approx.Count,
                stats = approx.Select(s => new { s.Key, s.Label, s.Color }),
                message = "Những chỉ số này CÓ được tính, nhưng nguồn chỉ gần đúng nên con số "
                        + "thấp hơn thực tế. Chúng trông y hệt số đo thật — đó là lý do phải "
                        + "nói riêng ra.",
            },
        };
    }

    private static object Blocked(string note) => new
    {
        ready = false,
        players = Array.Empty<object>(),
        note,
    };

    private static Dictionary<string, int> LoadSlots(Ti2026Paths paths)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(paths.EditorialFile("fantasy.json")));
            if (doc.RootElement.TryGetProperty("roster", out var r)
                && r.TryGetProperty("slots", out var s))
            {
                return s.EnumerateObject()
                    .Where(p => !p.Name.StartsWith('_'))
                    .ToDictionary(p => p.Name, p => p.Value.GetInt32());
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }

        return new Dictionary<string, int> { ["core"] = 2, ["mid"] = 1, ["support"] = 2 };
    }

    private static bool SlotsConfirmed(Ti2026Paths paths)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(paths.EditorialFile("fantasy.json")));
            return doc.RootElement.TryGetProperty("roster", out var r)
                   && r.TryGetProperty("_confirmed", out var c)
                   && c.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is JsonException or IOException) { return false; }
    }

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
                            : null,
                        s.Value.TryGetProperty("base", out var bs)
                            && bs.ValueKind == JsonValueKind.Number
                            ? bs.GetDouble()
                            : 0,
                        s.Value.TryGetProperty("color", out var cl) ? cl.GetString() : null,
                        s.Value.TryGetProperty("field", out var fd)
                            && fd.ValueKind == JsonValueKind.String
                            ? fd.GetString()
                            : null,
                        s.Value.TryGetProperty("approx", out var ap)
                            && ap.ValueKind == JsonValueKind.True));
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
