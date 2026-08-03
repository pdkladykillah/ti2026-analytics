namespace Ti2026.Ingest.Snapshots;

/// <summary>Kết quả một ván dưới góc nhìn của MỘT đội.</summary>
public readonly record struct MatchOutcome(
    DateOnly Date,
    bool Won,
    int Kills,
    int Deaths,
    int Assists,
    bool HadFirstBlood,
    bool ReachedTenFirst,
    int DurationSeconds);

public readonly record struct TeamWindowStats(
    int Maps,
    int Wins,
    int Losses,
    double Winrate,
    double AvgKills,
    double AvgDeaths,
    double AvgAssists,
    double KillDiff,
    double TotalKills,
    double FirstBloodRate,
    double F10Rate,
    double WinWhenFbRate,
    double WinWhenF10Rate,
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

        var withFb = matches.Where(m => m.HadFirstBlood).ToList();
        var withF10 = matches.Where(m => m.ReachedTenFirst).ToList();

        return new TeamWindowStats(
            Maps: n,
            Wins: wins,
            Losses: n - wins,
            Winrate: Pct(wins, n),
            AvgKills: avgKills,
            AvgDeaths: avgDeaths,
            AvgAssists: matches.Average(m => (double)m.Assists),
            KillDiff: avgKills - avgDeaths,
            TotalKills: avgKills + avgDeaths,
            FirstBloodRate: Pct(withFb.Count, n),
            F10Rate: Pct(withF10.Count, n),
            // Tỷ lệ CÓ ĐIỀU KIỆN: mẫu số là số ván có first blood, không phải tổng số ván
            WinWhenFbRate: Pct(withFb.Count(m => m.Won), withFb.Count),
            WinWhenF10Rate: Pct(withF10.Count(m => m.Won), withF10.Count),
            AvgDurationMinutes: matches.Average(m => m.DurationSeconds) / 60.0);
    }

    /// <summary>
    /// Phần trăm, trả 0 khi mẫu số bằng 0 — không bao giờ trả NaN.
    /// NaN serialize sang JSON sẽ làm JSON.parse ở browser chết, và trang trắng.
    /// </summary>
    private static double Pct(int numerator, int denominator) =>
        denominator == 0 ? 0 : numerator * 100.0 / denominator;
}
