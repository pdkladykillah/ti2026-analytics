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

    /// <summary>
    /// Vì sao có hai con số điểm, và vì sao con số cũ không phải điểm fantasy.
    /// </summary>
    private const string BannerNote =
        "Điểm banner là điểm luật THẬT trả: mỗi vị trí chỉ có ba ô emblem, mỗi ô một màu cố "
        + "định, và người chơi chỉ ăn điểm ở những chỉ số đặt được lên ô của mình — ba chỉ số, "
        + "không phải mười tám. Cột 'tổng 18 chỉ số' giữ lại để thấy mức chơi toàn diện, nhưng "
        + "KHÔNG phải điểm fantasy: nó luôn cao hơn nhiều và xếp hạng theo nó sẽ ưu ái người "
        + "giỏi đều thay vì người có ba chỉ số đúng màu cao nhất. "
        + "Tier là thứ quay trúng chứ không phải thứ chọn được, nên tier hiện thành khoảng "
        + "từ sàn (tier I, +10%) tới trần (tier V, +150%) thay vì gộp vào một con số.";

    /// <summary>Giải TI2025 trên OpenDota. Là kỳ TI DUY NHẤT dùng được làm mốc — xem TiBaselineNote.</summary>
    private const long Ti2025LeagueId = 18324;

    /// <summary>
    /// Ở cửa sổ TI2025 mỗi người chỉ có vài series, nên ngưỡng phải thấp hơn hẳn — nhưng vì
    /// thấp nên số series LUÔN phải hiện kèm, không được để người đọc tưởng đây là mẫu lớn.
    /// </summary>
    private const int MinMatchesForTiBaseline = 2;

    private sealed record ScoredPlayer(
        int PlayerId, string Nick, int? Position, int Games, int Matches,
        double? Average, List<FantasyGameScore> Games_,
        int? TeamId = null, string? TeamName = null);

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
        api.MapGet("/players", async (
            Ti2026DbContext db, Ti2026Paths paths, int days = 120, bool playoff = false) =>
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

            var colors = LoadBannerColors(paths, playoff);
            var banners = scored.ToDictionary(p => p.PlayerId, p => BannerOf(p, config, colors));

            // Xếp theo ĐIỂM BANNER, vì đó mới là điểm luật trả. Ai chưa suy được vị trí thì
            // chưa biết banner nào, xếp xuống cuối thay vì trộn lẫn với người đã tính được.
            var ordered = scored
                .OrderByDescending(p => banners[p.PlayerId]?.BasePoints ?? double.MinValue)
                .ToList();

            return Results.Ok(new
            {
                ready = true,
                days,
                playoff,
                countBestGames = config.CountBestGames,
                minMatches = MinMatchesForRanking,
                source = config.Source,
                bias = BiasWarning(config),
                bannerNote = BannerNote,

                players = ordered.Select(p => new
                {
                    playerId = p.PlayerId,
                    nick = p.Nick,
                    position = p.Position,
                    positionName = p.Position is int pos ? PositionInference.Name(pos) : null,
                    group = p.Position is int g ? GroupOf(g) : null,
                    games = p.Games,
                    matches = p.Matches,

                    // Điểm luật THẬT trả: chỉ những chỉ số đặt được lên emblem của vị trí này.
                    banner = BannerJson(banners[p.PlayerId]),

                    // Tổng cả 18 chỉ số. KHÔNG phải điểm fantasy — giữ lại làm thước đo "chơi
                    // toàn diện đến đâu", và để thấy rõ banner cắt đi bao nhiêu.
                    allStatsTotal = p.Average,

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
    /// Chọn đội hình điểm cao nhất HỢP LỆ theo luật TI2026: một cặp core cùng đội, một cặp hỗ
    /// trợ cùng đội, một mid tự do. Kèm mốc đối chiếu TI2025.
    /// </summary>
    private static void MapOptimize(RouteGroupBuilder api)
    {
        api.MapGet("/optimize", async (
            Ti2026DbContext db, Ti2026Paths paths, int days = 120, bool playoff = false) =>
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

            var colors = LoadBannerColors(paths, playoff);
            var scoredNow = await ScorePlayersAsync(db, config, days);
            var scoredTi = await ScorePlayersAsync(db, config, days, Ti2025LeagueId);

            var now = BuildLineup(scoredNow, config, colors);
            var ti2025 = BuildLineup(scoredTi, config, colors);

            // Hai cửa sổ có thể đang ở hai mức schema khác nhau. Khi đó cộng hai tổng ra để
            // cạnh nhau là mời người đọc kết luận sai, nên phải tự kiểm trước khi bày ra.
            var gaps = NotComparable(config, Coverage(scoredNow), Coverage(scoredTi));

            return Results.Ok(new
            {
                ready = true,
                days,
                slots = slots.Select(s => new { group = s.Key, count = s.Value }),
                slotsConfirmed = SlotsConfirmed(paths),
                roster = now.Picked,
                bias = BiasWarning(config),
                projectedTotal = now.Total,
                shortfall = now.Shortfall,

                // Mốc đối chiếu: đội hình cao nhất CỦA CHÍNH TI2025, chấm bằng hệ số TI2026.
                // Không phải điểm fantasy TI2025 thật — hệ số năm đó khác, cộng thẳng vào là
                // so hai thang đo khác nhau. Ở đây chỉ mượn lại số liệu thô của các trận đó.
                baseline = new
                {
                    label = "The International 2025",
                    roster = ti2025.Picked,

                    // Chỉ đưa tổng ra khi hai bên thật sự cùng thang đo. Còn thiếu chỉ số thì
                    // trả null: một ô trống buộc người đọc dừng lại, còn một con số thấp thì
                    // họ sẽ đọc thành "năm ngoái các tuyển thủ chơi kém hơn" và không bao giờ
                    // biết mình vừa so hai thứ khác nhau.
                    projectedTotal = gaps.Count == 0 ? ti2025.Total : (double?)null,
                    comparable = gaps.Count == 0,
                    missingStats = gaps,
                    incomparableNote = gaps.Count == 0 ? null
                        : "CHƯA so được tổng điểm. Các ván TI2025 đang ở mức nạp cũ nên thiếu hẳn: "
                        + string.Join(", ", gaps)
                        + ". Bên thiếu chỉ số luôn thấp hơn một cách có hệ thống, nên đặt hai tổng "
                        + "cạnh nhau lúc này sẽ dẫn tới kết luận sai. Đợt nạp bù v5 sẽ đưa cả hai "
                        + "cửa sổ về cùng mức, khi đó tổng sẽ tự hiện ra.",

                    shortfall = ti2025.Shortfall,
                    note = TiBaselineNote,
                },

                method = "Duyệt hết các đội để tìm cặp core (carry + offlane) cùng đội và cặp hỗ "
                       + "trợ (số 4 + số 5) cùng đội cho tổng điểm cao nhất, cộng một mid tự do. "
                       + "Chỉ khoảng 16 đội nên duyệt hết là ra đáp án tối ưu THẬT, không heuristic.",
                limitation = "Chưa mô hình hoá tầng emblem/tier/trait và cặp prefix–suffix. Đó là "
                           + "nơi phần lớn tối ưu hoá thật sự nằm, nên đây là đội hình tốt nhất "
                           + "theo điểm gốc, chưa phải theo điểm cuối cùng.",
            });
        });
    }

    /// <summary>
    /// Tính điểm cho mọi tuyển thủ. Dùng CHUNG cho /players và /optimize — hai nơi tự tính
    /// riêng là hai nơi sẽ lệch nhau, và người dùng sẽ thấy bảng xếp hạng không khớp đội hình.
    /// </summary>
    private static async Task<List<ScoredPlayer>> ScorePlayersAsync(
        Ti2026DbContext db, FantasyConfig config, int days, long? leagueId = null)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 7, 400));

        var query = leagueId is long lg
            ? db.MatchPlayers.Where(mp => mp.PlayerId != null && mp.Match!.LeagueId == lg)
            : db.MatchPlayers.Where(mp => mp.PlayerId != null && mp.Match!.StartTime >= since);

        var rows = await query
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

                // Đội lấy từ CHÍNH VÁN ĐẤU chứ không phải roster hiện tại. Đó là khác biệt duy
                // nhất khiến mốc TI2025 có nghĩa: ở TI2025 nhiều người còn thi đấu cho đội khác,
                // và ràng buộc "cặp cùng đội" phải hiểu theo đội của họ LÚC ĐÓ.
                TeamId = mp.IsRadiant ? mp.Match.RadiantTeamId : mp.Match.DireTeamId,

                mp.Kills, mp.Deaths, mp.GoldPerMin, mp.LastHits, mp.Denies,
                mp.ObserversPlaced, mp.CampsStacked, mp.RunePickups,
                mp.StunSeconds, mp.TeamfightParticipation,
                mp.TowerKills, mp.RoshanKills, mp.CourierKills, mp.FirstBloodClaimed,
                mp.Lotuses, mp.Watchers, mp.Smokes, mp.MadstoneBundles, mp.TormentorKills,
            })
            .ToListAsync();

        if (rows.Count == 0) return [];

        var teamNames = await db.Teams.ToDictionaryAsync(t => t.Id, t => t.Name);

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

                // Đội hay khoác nhất trong cửa sổ này — người chuyển đội giữa chừng lấy đội chính
                var team = g.Select(r => r.TeamId)
                    .Where(t => t != null)
                    .GroupBy(t => t!.Value)
                    .OrderByDescending(x => x.Count())
                    .FirstOrDefault();

                return new ScoredPlayer(
                    g.Key.PlayerId, g.Key.Nick,
                    top?.Key,
                    games.Count,
                    FantasyScorer.MatchScores(games, config.CountBestGames).Count,
                    FantasyScorer.AverageMatchScore(games, config.CountBestGames),
                    games,
                    team?.Key,
                    team is null ? null : teamNames.GetValueOrDefault(team.Key));
            })
            .Where(p => p.Matches >= (leagueId is null ? MinMatchesForRanking : MinMatchesForTiBaseline)
                        && p.Average != null)
            .OrderByDescending(p => p.Average)
            .ToList();
    }

    /// <summary>
    /// Vì sao chỉ lấy TI2025 làm mốc, không lấy thêm TI2024 cho nhiều mẫu hơn.
    ///
    /// Madstone mới có từ bản 7.38, tức là SAU TI2024. Chấm các ván TI2024 bằng hệ số TI2026
    /// thì mọi người đều được 0 điểm Madstone — không phải vì họ không nhặt, mà vì cơ chế đó
    /// chưa tồn tại. Đó là số 0 do cấu trúc, và trộn nó vào sẽ dìm điểm cả một kỳ TI xuống một
    /// cách có hệ thống mà bảng vẫn trông hoàn toàn bình thường.
    ///
    /// TI2025 là kỳ TI duy nhất đã có đủ Madstone, watcher, lotus và Tormentor như TI2026.
    /// </summary>
    private const string TiBaselineNote =
        "Đội hình cao nhất của chính TI2025, chấm bằng hệ số TI2026 để hai con số cùng thang đo. "
        + "Đội lấy theo đội mà người đó khoác LÚC ĐÓ, không phải đội hiện tại. "
        + "Chỉ dùng TI2025 chứ không thêm TI2024: Madstone mới có từ bản 7.38, sau TI2024, nên "
        + "chấm TI2024 bằng hệ số năm nay sẽ ra 0 điểm Madstone cho tất cả — số 0 do cấu trúc. "
        + "Mẫu ở đây rất nhỏ (mỗi người vài series), nên đọc như một mốc tham khảo, không phải "
        + "một bảng xếp hạng.";

    private static string GroupOf(int position) => position switch
    {
        1 => "core",
        2 => "mid",
        3 => "core",
        _ => "support",
    };

    /// <summary>
    /// Tỷ lệ ĐO ĐƯỢC của từng chỉ số trong một tập ván: bao nhiêu phần trăm số ô có số thật
    /// thay vì null.
    ///
    /// Cần thứ này vì hai cửa sổ thời gian có thể được nạp ở hai mức schema khác nhau, và khi
    /// đó tổng điểm của chúng KHÔNG so sánh được — bên nào thiếu chỉ số sẽ thấp hơn một cách
    /// có hệ thống, mà cả hai con số đều trông hoàn toàn bình thường. Đó đúng là kiểu sai đã
    /// suýt lọt: TI2025 đang ở schema v2 nên thiếu hẳn tham chiến, stun, stack và rune —
    /// riêng tham chiến đã là hệ số lớn nhất bảng (2124 điểm).
    /// </summary>
    private static Dictionary<string, double> Coverage(IEnumerable<ScoredPlayer> scored)
    {
        var parts = scored.SelectMany(p => p.Games_).SelectMany(g => g.Parts).ToList();
        if (parts.Count == 0) return [];

        return parts
            .GroupBy(p => p.Key)
            .ToDictionary(g => g.Key, g => (double)g.Count(x => x.Points is not null) / g.Count());
    }

    /// <summary>
    /// Chỉ số mà cửa sổ đối chiếu gần như không đo được trong khi cửa sổ hiện tại thì có.
    /// Còn cái nào trong danh sách này thì hai tổng điểm chưa so được với nhau.
    /// </summary>
    private static List<string> NotComparable(
        FantasyConfig config,
        Dictionary<string, double> now,
        Dictionary<string, double> baseline)
    {
        return config.Stats
            .Where(s => now.GetValueOrDefault(s.Key) >= 0.5
                        && baseline.GetValueOrDefault(s.Key) < 0.25)
            .Select(s => s.Label)
            .ToList();
    }

    /// <summary>
    /// Gói kết quả xếp đội hình thành JSON. Luật xếp nằm ở <see cref="FantasyLineup"/> chứ
    /// không phải ở đây — tầng HTTP chỉ định dạng, không giữ luật chơi.
    /// </summary>
    private static (List<object> Picked, double Total, IReadOnlyList<string> Shortfall) BuildLineup(
        List<ScoredPlayer> scored, FantasyConfig config, Dictionary<string, List<string>> colors)
    {
        // Xếp đội hình theo ĐIỂM BANNER, không phải tổng 18 chỉ số. Hai bảng xếp hạng này khác
        // nhau thật sự: tổng 18 ưu ái người giỏi đều, còn luật chỉ trả cho ba ô đúng màu.
        var banners = scored.ToDictionary(p => p.PlayerId, p => BannerOf(p, config, colors));

        var result = FantasyLineup.Build(scored.Select(p => new LineupCandidate(
            p.PlayerId, p.Nick, p.Position, p.TeamId, p.TeamName,
            banners[p.PlayerId]?.BasePoints, p.Matches)));

        var picked = result.Picks.Select(x => (object)new
        {
            slot = x.Slot,
            playerId = x.Player.PlayerId,
            nick = x.Player.Nick,
            teamId = x.Player.TeamId,
            teamName = x.Player.TeamName,
            position = x.Player.Position,
            positionName = x.Player.Position is int pos ? PositionInference.Name(pos) : null,
            avgPerMatch = x.Player.Average,
            matches = x.Player.Matches,
            banner = BannerJson(banners.GetValueOrDefault(x.Player.PlayerId)),
        }).ToList();

        return (picked, result.Total, result.Shortfall);
    }

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

    /// <summary>
    /// Màu các ô emblem theo nhóm vị trí. <paramref name="playoff"/> đổi sang banner 5 ô.
    /// Thiếu cấu hình thì dùng đúng bố cục đã đối chiếu với luật công bố.
    /// </summary>
    private static Dictionary<string, List<string>> LoadBannerColors(Ti2026Paths paths, bool playoff)
    {
        var key = playoff ? "bannerSlotColorsPlayoff" : "bannerSlotColors";

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(paths.EditorialFile("fantasy.json")));
            if (doc.RootElement.TryGetProperty(key, out var b))
            {
                var map = b.EnumerateObject()
                    .Where(p => !p.Name.StartsWith('_') && p.Value.ValueKind == JsonValueKind.Array)
                    .ToDictionary(
                        p => p.Name,
                        p => p.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToList());

                if (map.Count > 0) return map;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }

        return playoff
            ? new Dictionary<string, List<string>>
            {
                ["core"] = ["red", "green", "red", "green", "red"],
                ["mid"] = ["red", "blue", "green", "red", "green"],
                ["support"] = ["blue", "green", "blue", "green", "blue"],
            }
            : new Dictionary<string, List<string>>
            {
                ["core"] = ["red", "green", "red"],
                ["mid"] = ["red", "blue", "green"],
                ["support"] = ["blue", "green", "blue"],
            };
    }

    /// <summary>
    /// Điểm banner của một người: chỉ cộng những chỉ số đặt được lên emblem của vị trí đó.
    /// Trả null khi chưa suy được vị trí — không có vị trí thì không biết banner nào.
    /// </summary>
    private static BannerResult? BannerOf(
        ScoredPlayer p, FantasyConfig config, Dictionary<string, List<string>> colors)
    {
        var group = FantasyBanner.GroupOf(p.Position);
        if (group is null || !colors.TryGetValue(group, out var slots)) return null;

        var points = config.Stats.ToDictionary(s => s.Key, s => AvgPart(p.Games_, s.Key));
        return FantasyBanner.Build(slots, points, config.Stats);
    }

    private static object? BannerJson(BannerResult? b) => b is null ? null : new
    {
        basePoints = b.BasePoints,
        tierIPoints = b.TierIPoints,
        tierVPoints = b.TierVPoints,
        emptySlots = b.EmptySlots,
        slots = b.Picks.Select(x => new
        {
            slot = x.Slot, color = x.Color, statKey = x.StatKey,
            statLabel = x.StatLabel, points = x.BasePoints,
        }),
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
