namespace Ti2026.Ingest.Analytics;

/// <summary>
/// Một trận đã biết kết quả, dùng để cập nhật rating.
/// <paramref name="Patch"/> là chỉ số bản game của OpenDota (60 = 7.41); null khi chưa biết.
/// </summary>
public readonly record struct RatedMatch(
    DateTime StartTime, int WinnerTeamId, int LoserTeamId, int? Patch = null);

public readonly record struct TeamRating(int TeamId, double Elo, int Games);

/// <summary>Một dự đoán hồi tố. Giữ mốc thời gian để tách được tập huấn luyện / kiểm định.</summary>
public readonly record struct BacktestRecord(
    DateTime StartTime, double PredictedProbability, bool Correct);

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

    // Giữ lại cho mã cũ và cho các test đọc thẳng hằng số; nguồn sự thật là EloOptions.
    public const double KFactor = 24;
    public const double UpdateScale = 400;
    public const double DefaultProbabilityScale = EloOptions.Calibrated.ProbabilityScale;

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
        IEnumerable<RatedMatch> matches, IEnumerable<int> allTeamIds, EloOptions? options = null)
    {
        var opt = options ?? EloOptions.Default;

        var ratings = allTeamIds.ToDictionary(id => id, _ => InitialRating);
        var games = ratings.Keys.ToDictionary(id => id, _ => 0);
        int? currentPatch = null;

        foreach (var m in matches.OrderBy(x => x.StartTime))
        {
            currentPatch = ApplyPatchShift(ratings, currentPatch, m.Patch, opt.PatchRegression);

            if (!ratings.ContainsKey(m.WinnerTeamId) || !ratings.ContainsKey(m.LoserTeamId))
                continue;

            var ra = ratings[m.WinnerTeamId];
            var rb = ratings[m.LoserTeamId];
            var expectedWinner = ExpectedScore(ra, rb, opt.UpdateScale);

            // Tổng điểm bảo toàn: bên thắng nhận đúng phần bên thua mất
            var delta = opt.KFactor * (1.0 - expectedWinner);
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
    public static List<BacktestRecord> Backtest(
        IEnumerable<RatedMatch> matches,
        IEnumerable<int> allTeamIds,
        int warmupGamesPerTeam = 5,
        EloOptions? options = null)
    {
        var opt = options ?? EloOptions.Default;

        var ratings = allTeamIds.ToDictionary(id => id, _ => InitialRating);
        var games = ratings.Keys.ToDictionary(id => id, _ => 0);
        var results = new List<BacktestRecord>();
        int? currentPatch = null;

        foreach (var m in matches.OrderBy(x => x.StartTime))
        {
            // Kéo về trung bình TRƯỚC khi dự đoán: sang bản mới thì hiểu biết cũ đã bớt giá
            // trị ngay tại trận đầu tiên của bản đó, chứ không phải sau khi đã đánh xong.
            currentPatch = ApplyPatchShift(ratings, currentPatch, m.Patch, opt.PatchRegression);

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
                var pFavourite = ExpectedScore(
                    Math.Max(rw, rl), Math.Min(rw, rl), opt.ProbabilityScale);
                results.Add(new BacktestRecord(m.StartTime, pFavourite * 100, favouriteIsWinner));
            }

            // Cập nhật rating luôn dùng thang cập nhật, độc lập với thang công bố
            var delta = opt.KFactor * (1.0 - ExpectedScore(rw, rl, opt.UpdateScale));
            ratings[m.WinnerTeamId] = rw + delta;
            ratings[m.LoserTeamId] = rl - delta;
            games[m.WinnerTeamId]++;
            games[m.LoserTeamId]++;
        }

        return results;
    }

    /// <summary>
    /// Kéo mọi rating về mốc trung bình khi game lên bản mới, một lần cho mỗi bậc patch.
    /// Trả về chỉ số patch hiện hành sau khi xử lý.
    ///
    /// Chỉ xử lý khi patch TĂNG. Dữ liệu vẫn có thể lệch thứ tự (một trận cũ được nạp bổ
    /// sung, hoặc OpenDota gán patch không khớp mốc thời gian); nếu tụt lại thì bỏ qua chứ
    /// không "kéo ngược", vì kéo ngược không có nghĩa gì và sẽ làm rating nhảy loạn.
    /// </summary>
    private static int? ApplyPatchShift(
        Dictionary<int, double> ratings, int? currentPatch, int? matchPatch, double regression)
    {
        if (matchPatch is null) return currentPatch;
        if (currentPatch is null) return matchPatch;
        if (matchPatch <= currentPatch) return currentPatch;

        if (regression > 0)
        {
            var keep = Math.Pow(1 - regression, matchPatch.Value - currentPatch.Value);
            foreach (var id in ratings.Keys.ToList())
                ratings[id] = InitialRating + (ratings[id] - InitialRating) * keep;
        }

        return matchPatch;
    }
}
