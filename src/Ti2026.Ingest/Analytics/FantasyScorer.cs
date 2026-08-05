namespace Ti2026.Ingest.Analytics;

/// <summary>
/// Một chỉ số chấm điểm: <c>Base + raw / Per × Points</c>.
///
/// Base tồn tại vì luật TI2026 chấm điểm chết là "1950 − 195 × số lần chết" — một hàm bậc
/// nhất CÓ HẰNG SỐ, không phải phép nhân đơn thuần. Bản đầu của engine chỉ có phép nhân, nên
/// nếu lắp bảng hệ số vào mà không sửa thì điểm chết sẽ ra âm ở mọi ván và không ai biết vì sao.
/// </summary>
public readonly record struct FantasyStat(
    string Key, string Label, double Per, double? Points, double Base = 0);

/// <summary>
/// Bảng hệ số fantasy. <see cref="Ready"/> false nghĩa là chưa điền đủ — khi đó KHÔNG được
/// tính điểm, vì hệ số thiếu sẽ lặng lẽ thành 0 và cả bảng xếp hạng sai mà nhìn vẫn hợp lý.
/// </summary>
public sealed record FantasyConfig(
    IReadOnlyList<FantasyStat> Stats,
    int CountBestGames,
    string? Source,
    DateTime? UpdatedAt)
{
    public IReadOnlyList<string> MissingCoefficients =>
        Stats.Where(s => s.Points is null).Select(s => s.Key).ToList();

    public bool Ready => Stats.Count > 0 && MissingCoefficients.Count == 0;
}

/// <summary>Chỉ số thô của một người trong MỘT ván.</summary>
public readonly record struct FantasyGame(
    long MatchId,
    long? SeriesId,
    DateTime StartTime,
    IReadOnlyDictionary<string, double?> Values);

public readonly record struct FantasyBreakdown(string Key, string Label, double? Raw, double? Points);

public readonly record struct FantasyGameScore(
    long MatchId, long? SeriesId, DateTime StartTime,
    double Points, IReadOnlyList<FantasyBreakdown> Parts);

/// <summary>
/// Tính điểm fantasy.
///
/// Hai luật của TI2026 phải làm đúng, nếu không mọi con số phía sau đều lệch:
///
/// 1. ĐIỂM MỘT TRẬN = tổng N VÁN CAO NHẤT trong series, không phải tổng mọi ván. Một người
///    chơi Bo3 thắng 2-0 và một người chơi Bo3 thua 1-2 có số ván khác nhau; cộng hết thì
///    người thua lại được cộng nhiều hơn chỉ vì series dài hơn.
///
/// 2. GIÁ TRỊ CỦA MỘT NGƯỜI = TRUNG BÌNH điểm mỗi trận, không phải tổng. Cộng dồn thì người
///    thi đấu nhiều giải hơn luôn đứng đầu, bất kể chơi hay dở.
///
/// Chỉ số CHƯA ĐO ĐƯỢC (null) không được coi là 0: nó bị loại khỏi phép tính và khai ra ở
/// phần breakdown, để không ai nhầm "chưa nạp" với "làm được 0".
/// </summary>
public static class FantasyScorer
{
    /// <summary>Điểm của MỘT ván, kèm phân rã từng thành phần.</summary>
    public static FantasyGameScore ScoreGame(FantasyGame game, FantasyConfig config)
    {
        var parts = new List<FantasyBreakdown>(config.Stats.Count);
        double total = 0;

        foreach (var stat in config.Stats)
        {
            game.Values.TryGetValue(stat.Key, out var raw);

            if (raw is null || stat.Points is null)
            {
                parts.Add(new FantasyBreakdown(stat.Key, stat.Label, raw, null));
                continue;
            }

            // Base + raw/Per × Points. Base chỉ khác 0 ở "deaths" (1950 − 195 × số lần chết).
            var points = stat.Base
                       + raw.Value / (stat.Per == 0 ? 1 : stat.Per) * stat.Points.Value;

            parts.Add(new FantasyBreakdown(stat.Key, stat.Label, raw, Math.Round(points, 3)));
            total += points;
        }

        return new FantasyGameScore(
            game.MatchId, game.SeriesId, game.StartTime, Math.Round(total, 3), parts);
    }

    /// <summary>
    /// Gom ván thành trận rồi chỉ giữ N ván cao nhất mỗi trận.
    ///
    /// Ván KHÔNG có SeriesId được tính là một trận riêng — đó là ván đơn thật, không phải dữ
    /// liệu thiếu; gom hết chúng vào một nhóm sẽ tạo ra một "series" khổng lồ giả tạo.
    /// </summary>
    public static List<double> MatchScores(IEnumerable<FantasyGameScore> games, int countBestGames)
    {
        var best = Math.Max(1, countBestGames);

        return games
            .GroupBy(g => g.SeriesId is long s and > 0 ? $"s{s}" : $"m{g.MatchId}")
            .Select(g => g.OrderByDescending(x => x.Points).Take(best).Sum(x => x.Points))
            .ToList();
    }

    /// <summary>Trung bình điểm mỗi trận. null khi không có trận nào — không phải 0.</summary>
    public static double? AverageMatchScore(IEnumerable<FantasyGameScore> games, int countBestGames)
    {
        var scores = MatchScores(games, countBestGames);
        return scores.Count == 0 ? null : Math.Round(scores.Average(), 2);
    }
}
