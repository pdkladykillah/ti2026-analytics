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
    /// Thang quy đổi chênh lệch rating sang xác suất khi CẬP NHẬT rating.
    /// Giữ 400 chuẩn ở đây để rating có thang quen thuộc, so được với hệ Elo khác.
    /// </summary>
    public const double UpdateScale = 400;

    /// <summary>
    /// Thang quy đổi khi CÔNG BỐ xác suất ra ngoài — cố tình khác UpdateScale.
    ///
    /// Hiệu chuẩn hồi tố trên 1715 trận thật cho thấy thang 400 làm mô hình tự tin quá mức,
    /// và càng tự tin càng sai: nói 70–80% thì thực tế 65%, nói 80–90% thì thực tế 67%.
    /// Dota biến động cao, đặc biệt Bo1, nên chênh lệch rating không chuyển thành ưu thế
    /// mạnh như công thức chuẩn giả định.
    ///
    /// Giá trị này được CHỌN TỪ DỮ LIỆU (xem api/calibration, phần scaleComparison) chứ
    /// không phải đặt theo cảm tính.
    /// </summary>
    public const double DefaultProbabilityScale = 600;

    /// <summary>Xác suất đội A thắng. Thang càng lớn thì dự đoán càng dè dặt.</summary>
    public static double ExpectedScore(double ratingA, double ratingB, double scale = UpdateScale) =>
        1.0 / (1.0 + Math.Pow(10, (ratingB - ratingA) / scale));

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

    /// <summary>
    /// Hiệu chuẩn HỒI TỐ: với mỗi trận, dự đoán bằng rating TẠI THỜI ĐIỂM TRƯỚC TRẬN ĐÓ, rồi
    /// so với kết quả thật.
    ///
    /// Đây là cách duy nhất kiểm chứng mô hình ngay lập tức thay vì chờ hàng tháng. Điều
    /// quan trọng: dự đoán phải dùng rating TRƯỚC khi cập nhật bằng chính trận đó — nếu
    /// không thì mô hình đang nhìn trộm đáp án, và mọi đường hiệu chuẩn sẽ đẹp một cách giả tạo.
    ///
    /// Bỏ qua N trận đầu của mỗi đội: khi cả hai còn ở 1500 thì "dự đoán 50%" không phản ánh
    /// hiểu biết nào, chỉ làm loãng kết quả.
    /// </summary>
    public static List<(double PredictedProbability, bool Correct)> Backtest(
        IEnumerable<RatedMatch> matches,
        IEnumerable<int> allTeamIds,
        int warmupGamesPerTeam = 5,
        double? probabilityScale = null)
    {
        var scale = probabilityScale ?? DefaultProbabilityScale;

        var ratings = allTeamIds.ToDictionary(id => id, _ => InitialRating);
        var games = ratings.Keys.ToDictionary(id => id, _ => 0);
        var results = new List<(double, bool)>();

        foreach (var m in matches.OrderBy(x => x.StartTime))
        {
            if (!ratings.ContainsKey(m.WinnerTeamId) || !ratings.ContainsKey(m.LoserTeamId))
                continue;

            var rw = ratings[m.WinnerTeamId];
            var rl = ratings[m.LoserTeamId];

            var warmedUp = games[m.WinnerTeamId] >= warmupGamesPerTeam
                        && games[m.LoserTeamId] >= warmupGamesPerTeam;

            if (warmedUp)
            {
                // Ghi lại dưới góc nhìn của đội ĐƯỢC ĐÁNH GIÁ CAO HƠN, để bucket xác suất
                // trải đều 50-100% thay vì đối xứng quanh 50 và triệt tiêu lẫn nhau.
                var favouriteIsWinner = rw >= rl;
                var pFavourite = ExpectedScore(Math.Max(rw, rl), Math.Min(rw, rl), scale);
                results.Add((pFavourite * 100, favouriteIsWinner));
            }

            // Cập nhật rating luôn dùng thang chuẩn, độc lập với thang công bố
            var delta = KFactor * (1.0 - ExpectedScore(rw, rl, UpdateScale));
            ratings[m.WinnerTeamId] = rw + delta;
            ratings[m.LoserTeamId] = rl - delta;
            games[m.WinnerTeamId]++;
            games[m.LoserTeamId]++;
        }

        return results;
    }
}
