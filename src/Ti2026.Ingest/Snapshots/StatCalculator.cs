namespace Ti2026.Ingest.Snapshots;

/// <summary>
/// Kết quả một ván dưới góc nhìn của MỘT đội.
///
/// Các field nullable là những thứ endpoint teams/{id}/matches của OpenDota KHÔNG trả về:
/// assists, first blood, và mốc đội nào đạt 10 kill trước. Chúng chỉ có trong match detail
/// (matches/{id}), tức một request cho mỗi trận — việc của Giai đoạn 3.
///
/// Chúng phải là nullable, không được mặc định bằng 0/false: "không biết" mà quy về "bằng
/// không" sẽ cho ra FirstBloodRate = 0% cho cả 16 đội và hiển thị y như số thật. Một con số
/// sai trông giống số đúng thì tệ hơn một ô để trống.
/// </summary>
public readonly record struct MatchOutcome(
    DateOnly Date,
    bool Won,
    int Kills,
    int Deaths,
    int? Assists,
    bool? HadFirstBlood,
    bool? ReachedTenFirst,
    int DurationSeconds);

public readonly record struct TeamWindowStats(
    int Maps,
    int Wins,
    int Losses,
    double Winrate,
    double AvgKills,
    double AvgDeaths,
    double? AvgAssists,
    double KillDiff,
    double TotalKills,
    double? FirstBloodRate,
    double? F10Rate,
    double? WinWhenFbRate,
    double? WinWhenF10Rate,
    double AvgDurationMinutes);

/// <summary>
/// Hàm thuần, không I/O. Đây là phần đáng test nhất của cả hệ thống: tính sai winrate
/// thì không có gì báo lỗi, chỉ có số sai hiển thị lên trang.
/// </summary>
public static class StatCalculator
{
    public static TeamWindowStats Compute(IReadOnlyList<MatchOutcome> matches)
    {
        if (matches.Count == 0) return default;

        var n = matches.Count;
        var wins = matches.Count(m => m.Won);
        var avgKills = matches.Average(m => (double)m.Kills);
        var avgDeaths = matches.Average(m => (double)m.Deaths);

        // Chỉ tính trên những ván THỰC SỰ biết giá trị. Ván không biết bị loại khỏi cả tử
        // số lẫn mẫu số, thay vì bị đếm là "không có first blood".
        var fbKnown = matches.Where(m => m.HadFirstBlood.HasValue).ToList();
        var withFb = fbKnown.Where(m => m.HadFirstBlood!.Value).ToList();

        var f10Known = matches.Where(m => m.ReachedTenFirst.HasValue).ToList();
        var withF10 = f10Known.Where(m => m.ReachedTenFirst!.Value).ToList();

        var assistsKnown = matches.Where(m => m.Assists.HasValue).ToList();

        return new TeamWindowStats(
            Maps: n,
            Wins: wins,
            Losses: n - wins,
            Winrate: Pct(wins, n),
            AvgKills: avgKills,
            AvgDeaths: avgDeaths,
            AvgAssists: assistsKnown.Count == 0
                ? null
                : assistsKnown.Average(m => (double)m.Assists!.Value),
            KillDiff: avgKills - avgDeaths,
            TotalKills: avgKills + avgDeaths,
            FirstBloodRate: NullablePct(withFb.Count, fbKnown.Count),
            F10Rate: NullablePct(withF10.Count, f10Known.Count),
            // Tỷ lệ CÓ ĐIỀU KIỆN: mẫu số là số ván CÓ first blood, không phải tổng số ván
            WinWhenFbRate: NullablePct(withFb.Count(m => m.Won), withFb.Count),
            WinWhenF10Rate: NullablePct(withF10.Count(m => m.Won), withF10.Count),
            AvgDurationMinutes: matches.Average(m => m.DurationSeconds) / 60.0);
    }

    /// <summary>
    /// Phần trăm, trả 0 khi mẫu số bằng 0 — không bao giờ trả NaN.
    /// NaN serialize sang JSON sẽ làm JSON.parse ở browser chết, và trang trắng.
    /// </summary>
    private static double Pct(int numerator, int denominator) =>
        denominator == 0 ? 0 : numerator * 100.0 / denominator;

    /// <summary>
    /// Như Pct nhưng trả null khi không có mẫu nào để tính — dùng cho các chỉ số mà nguồn dữ
    /// liệu có thể không cung cấp. null nghĩa là "chưa biết", 0 nghĩa là "đã đo và bằng 0";
    /// gộp hai thứ này lại là cách tạo ra số liệu sai trông như thật.
    /// </summary>
    private static double? NullablePct(int numerator, int denominator) =>
        denominator == 0 ? null : numerator * 100.0 / denominator;
}
