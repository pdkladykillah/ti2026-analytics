namespace Ti2026.Ingest.Analytics;

/// <summary>Một trận đã biết kết quả, dùng để cập nhật rating.</summary>
public readonly record struct RatedMatch(DateTime StartTime, int WinnerTeamId, int LoserTeamId);

public readonly record struct TeamRating(int TeamId, double Elo, int Games);

/// <summary>
/// Elo cho 16 đội TI2026.
///
/// VÌ SAO CẦN: winrate không biết đối thủ là ai. Một đội 67% có thể chỉ toàn gặp đội yếu,
/// còn đội 56% có thể toàn gặp top. Elo tự động thưởng khi thắng đội mạnh và phạt nhẹ khi
/// thua đội mạnh, nên hai con số mới so sánh được với nhau.
///
/// GIỚI HẠN CẦN BIẾT: ingest chỉ lưu trận giữa hai đội TRONG 16 đội đang theo dõi, nên đây
/// là hệ rating KHÉP KÍN. Không so được với đội ngoài giải, và tổng điểm toàn hệ luôn không
/// đổi. Với mục đích dự đoán các cặp đấu tại TI thì đó lại đúng cái ta cần.
/// </summary>
public static class EloEngine
{
    public const double InitialRating = 1500;

    /// <summary>
    /// Hệ số K. 24 là mức trung dung cho esports: đủ nhạy để bắt kịp thay đổi đội hình trong
    /// một mùa giải, nhưng không nhảy loạn sau một trận bất ngờ. K quá cao biến rating thành
    /// "kết quả trận gần nhất"; quá thấp thì một đội sa sút vẫn giữ điểm cũ hàng tháng.
    /// </summary>
    public const double KFactor = 24;

    /// <summary>
    /// Xác suất đội A thắng, theo công thức Elo chuẩn.
    /// Chênh 100 điểm ≈ 64%, chênh 200 điểm ≈ 76%.
    /// </summary>
    public static double ExpectedScore(double ratingA, double ratingB) =>
        1.0 / (1.0 + Math.Pow(10, (ratingB - ratingA) / 400.0));

    /// <summary>
    /// Chạy toàn bộ trận theo THỨ TỰ THỜI GIAN và trả rating cuối cùng.
    ///
    /// Thứ tự là bắt buộc: Elo phụ thuộc đường đi, xử lý sai thứ tự cho ra con số khác hẳn
    /// và không có gì báo lỗi. Hàm tự sắp xếp thay vì tin vào đầu vào.
    /// </summary>
    public static Dictionary<int, TeamRating> Compute(
        IEnumerable<RatedMatch> matches, IEnumerable<int> allTeamIds)
    {
        var ratings = allTeamIds.ToDictionary(id => id, _ => InitialRating);
        var games = ratings.Keys.ToDictionary(id => id, _ => 0);

        foreach (var m in matches.OrderBy(x => x.StartTime))
        {
            if (!ratings.ContainsKey(m.WinnerTeamId) || !ratings.ContainsKey(m.LoserTeamId))
                continue;

            var ra = ratings[m.WinnerTeamId];
            var rb = ratings[m.LoserTeamId];
            var expectedWinner = ExpectedScore(ra, rb);

            // Tổng điểm bảo toàn: bên thắng nhận đúng phần bên thua mất
            var delta = KFactor * (1.0 - expectedWinner);
            ratings[m.WinnerTeamId] = ra + delta;
            ratings[m.LoserTeamId] = rb - delta;

            games[m.WinnerTeamId]++;
            games[m.LoserTeamId]++;
        }

        return ratings.ToDictionary(
            kv => kv.Key,
            kv => new TeamRating(kv.Key, Math.Round(kv.Value, 1), games[kv.Key]));
    }
}
