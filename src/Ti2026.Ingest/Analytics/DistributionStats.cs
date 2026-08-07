namespace Ti2026.Ingest.Analytics;

public readonly record struct Distribution(
    int Count,
    double Mean,
    double Median,
    double P25,
    double P75,
    double Min,
    double Max);

/// <summary>
/// Thống kê phân phối cho các kèo over/under.
///
/// VÌ SAO KHÔNG DÙNG TRUNG BÌNH: kèo hỏi "khả năng vượt 48.5 là bao nhiêu", mà trung bình
/// không trả lời được câu đó. Hai đội cùng trung bình 52 kills nhưng một đội luôn 50-54 còn
/// đội kia dao động 30-75 thì xác suất vượt mốc khác hẳn nhau. Trung vị và tứ phân vị nói
/// lên độ phân tán; ProbabilityOver trả lời thẳng câu hỏi.
/// </summary>
public static class DistributionStats
{
    public static Distribution Describe(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return default;

        var sorted = values.OrderBy(v => v).ToArray();
        return new Distribution(
            Count: sorted.Length,
            Mean: Math.Round(sorted.Average(), 2),
            Median: Math.Round(Percentile(sorted, 0.50), 2),
            P25: Math.Round(Percentile(sorted, 0.25), 2),
            P75: Math.Round(Percentile(sorted, 0.75), 2),
            // Làm tròn như bốn giá trị trên. Không làm tròn thì thời lượng trận ra
            // "13.733333333333333 phút" trên trang — đúng về số học và vô dụng khi đọc.
            Min: Math.Round(sorted[0], 2),
            Max: Math.Round(sorted[^1], 2));
    }

    /// <summary>
    /// Tỷ lệ quan sát vượt ngưỡng — ước lượng thực nghiệm, không giả định phân phối chuẩn.
    ///
    /// Trả null khi mẫu quá nhỏ: 3 trận cho ra "67% vượt mốc" nghe như một con số, nhưng nó
    /// chỉ là 2/3 và đảo chiều sau đúng một trận nữa.
    /// </summary>
    public static double? ProbabilityOver(IReadOnlyList<double> values, double line, int minSample = 10)
    {
        if (values.Count < minSample) return null;
        return Math.Round(values.Count(v => v > line) * 100.0 / values.Count, 1);
    }

    /// <summary>Nội suy tuyến tính giữa hai phần tử lân cận.</summary>
    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];

        var pos = (sorted.Length - 1) * p;
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        return lo == hi ? sorted[lo] : sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }
}
