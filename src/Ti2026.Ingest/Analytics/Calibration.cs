namespace Ti2026.Ingest.Analytics;

public readonly record struct CalibrationBucket(
    string Range, double Predicted, double Actual, int Samples);

public readonly record struct GridPoint(
    double ProbabilityScale, double PatchRegression, double Brier, int Samples);

public sealed record CalibrationReport(
    int Evaluated, double HitRate, double Brier, List<CalibrationBucket> Buckets);

/// <summary>
/// Đo xem mô hình có nói thật không.
///
/// Một mô hình "chính xác" vẫn có thể dối: nói 80% mà chỉ đúng 65% thì mọi quyết định dựa
/// trên con số đó đều sai lệch theo cùng một hướng. Hiệu chuẩn đo đúng điều này.
///
/// KỶ LUẬT NGOÀI MẪU: tham số phải được chọn trên tập HUẤN LUYỆN rồi báo cáo trên tập KIỂM
/// ĐỊNH mà tập đó không tham gia việc chọn. Chọn và báo cáo trên cùng một tập thì con số đẹp
/// lên chỉ vì đã ngắm vào nó — với lưới vài chục tổ hợp thì đó là chuyện có thật, không phải
/// lo xa.
/// </summary>
public static class Calibration
{
    /// <summary>Sai số bình phương trung bình. 0 là hoàn hảo, 0.25 tương đương tung đồng xu.</summary>
    public static double Brier(IReadOnlyCollection<BacktestRecord> records) =>
        records.Count == 0
            ? double.NaN
            : records.Average(r => Math.Pow(r.PredictedProbability / 100 - (r.Correct ? 1 : 0), 2));

    /// <summary>
    /// Mốc thời gian chia tập: <paramref name="trainFraction"/> số trận cũ nhất là huấn luyện.
    /// Chia theo THỜI GIAN chứ không ngẫu nhiên — chia ngẫu nhiên sẽ để trận tương lai lọt
    /// vào tập huấn luyện, đúng thứ mà ngoài đời không bao giờ có.
    /// </summary>
    public static DateTime? SplitCutoff(IReadOnlyCollection<RatedMatch> matches, double trainFraction)
    {
        if (matches.Count == 0) return null;

        var ordered = matches.OrderBy(m => m.StartTime).ToList();
        var index = Math.Clamp((int)(ordered.Count * trainFraction), 1, ordered.Count - 1);
        return ordered[index].StartTime;
    }

    public static CalibrationReport Summarize(IReadOnlyCollection<BacktestRecord> records)
    {
        if (records.Count == 0) return new CalibrationReport(0, 0, double.NaN, []);

        var buckets = records
            .GroupBy(r => Math.Clamp((int)(r.PredictedProbability / 10) * 10, 50, 90))
            .OrderBy(g => g.Key)
            .Select(g => new CalibrationBucket(
                Range: $"{g.Key}–{g.Key + 10}%",
                Predicted: Math.Round(g.Average(x => x.PredictedProbability), 1),
                Actual: Math.Round(g.Count(x => x.Correct) * 100.0 / g.Count(), 1),
                Samples: g.Count()))
            .ToList();

        return new CalibrationReport(
            Evaluated: records.Count,
            HitRate: Math.Round(records.Count(r => r.Correct) * 100.0 / records.Count, 1),
            Brier: Math.Round(Brier(records), 4),
            Buckets: buckets);
    }

    /// <summary>
    /// Quét lưới tham số, chấm điểm CHỈ trên các dự đoán rơi vào tập huấn luyện.
    ///
    /// Backtest vẫn chạy qua toàn bộ trận vì rating là thứ tích luỹ, nhưng điểm số chỉ lấy từ
    /// phần huấn luyện — nên tập kiểm định không hề tham gia vào việc chọn tham số.
    /// </summary>
    public static List<GridPoint> Grid(
        IReadOnlyCollection<RatedMatch> matches,
        IReadOnlyCollection<int> teamIds,
        IEnumerable<double> scales,
        IEnumerable<double> regressions,
        DateTime? trainUntil)
    {
        var grid = new List<GridPoint>();

        foreach (var scale in scales)
        foreach (var regression in regressions)
        {
            var options = EloOptions.Default with
            {
                ProbabilityScale = scale,
                PatchRegression = regression,
            };

            var train = Backtest(matches, teamIds, options)
                .Where(r => trainUntil is null || r.StartTime < trainUntil.Value)
                .ToList();

            grid.Add(new GridPoint(scale, regression, Math.Round(Brier(train), 4), train.Count));
        }

        return grid;
    }

    /// <summary>Điểm tốt nhất trên lưới, bỏ qua các điểm không có mẫu nào để chấm.</summary>
    public static GridPoint? Best(IEnumerable<GridPoint> grid) =>
        grid.Where(g => g.Samples > 0 && !double.IsNaN(g.Brier))
            .OrderBy(g => g.Brier)
            .Cast<GridPoint?>()
            .FirstOrDefault();

    private static List<BacktestRecord> Backtest(
        IReadOnlyCollection<RatedMatch> matches,
        IReadOnlyCollection<int> teamIds,
        EloOptions options) =>
        EloEngine.Backtest(matches, teamIds, options: options);
}
