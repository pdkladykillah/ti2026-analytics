namespace Ti2026.Ingest.Analytics;

/// <summary>Một cặp đấu đã biết đủ hai đội và Elo của cả hai.</summary>
public readonly record struct BracketPair(
    int NodeId, string? Name, string TeamA, string TeamB, double EloA, double EloB, int BestOf);

/// <param name="Pick">Đội nên chọn theo chiến lược đang dùng.</param>
/// <param name="PickIsFavourite">Chọn theo cửa trên hay chọn ngược kèo.</param>
/// <param name="ProbA">Xác suất đội A thắng CẢ SERIES, không phải một ván.</param>
/// <param name="Cost">
/// Phần xác suất phải trả khi chọn ngược kèo — chênh lệch giữa cửa trên và cửa dưới. 0 khi chọn
/// theo cửa trên.
/// </param>
public readonly record struct BracketCall(
    int NodeId, string? Name, string TeamA, string TeamB,
    double ProbA, double ProbB, double EloGap, int BestOf,
    string Pick, bool PickIsFavourite, bool IsClose, double Cost, string Reason);

/// <summary>
/// Gợi ý chọn đội đi tiếp trong bảng đấu, cho phần dự đoán của fantasy.
///
/// HAI CHIẾN LƯỢC, VÀ CHÚNG KHÔNG CÙNG MỘT MỤC TIÊU. Chọn theo cửa trên cho điểm kỳ vọng cao
/// nhất, nhưng đó cũng là thứ đa số chọn — đúng hết vẫn không vượt lên được ai. Đánh cược có
/// tính toán thì nhận điểm kỳ vọng thấp hơn một chút để đổi lấy phương sai: chỉ chọn ngược ở
/// những cặp SÁT NHAU, nơi cái giá phải trả nhỏ mà phần thưởng khác biệt thì lớn.
///
/// CHUYỂN XÁC SUẤT MỘT VÁN THÀNH XÁC SUẤT CẢ SERIES. Elo đo trên từng ván, còn bảng đấu tính
/// theo series — và Bo3 khuếch đại lợi thế: đội thắng 55% mỗi ván thắng 57,5% một series Bo3.
/// Không đổi thang thì mọi cặp trông sát nhau hơn thực tế, và chiến lược "chỉ đánh cược ở cặp
/// sát nhau" sẽ đánh cược ở cả những cặp vốn không sát.
/// </summary>
public static class BracketAdvice
{
    /// <summary>
    /// Trong khoảng này quanh 50% thì coi là cặp SÁT NHAU.
    ///
    /// 0,10 nghĩa là từ 40% tới 60%. Rộng hơn thì "đánh cược có tính toán" thành đánh bạc: ở mức
    /// 65-35 thì cái giá phải trả đã là 30 điểm phần trăm cho một lần đoán.
    /// </summary>
    public const double CloseBand = 0.10;

    /// <summary>Thang Elo chuẩn: chênh 400 điểm nghĩa là thắng 10 lần trong 11.</summary>
    public const double EloScale = 400.0;

    /// <summary>Xác suất A thắng MỘT ván theo chênh lệch Elo.</summary>
    public static double MapProbability(double eloA, double eloB) =>
        1.0 / (1.0 + Math.Pow(10, (eloB - eloA) / EloScale));

    /// <summary>
    /// Xác suất thắng CẢ SERIES từ xác suất mỗi ván.
    ///
    /// Bo1 giữ nguyên. Bo3 là "thắng 2 trong 3": p² + 2p²(1−p) = p²(3 − 2p). Bo5 là "thắng 3
    /// trong 5". Bo2 không có trong bảng loại trực tiếp nên không xử lý — gặp thì trả nguyên p,
    /// và như vậy còn hơn im lặng dùng công thức Bo3 cho một thể thức khác.
    /// </summary>
    public static double SeriesProbability(double p, int bestOf) => bestOf switch
    {
        3 => p * p * (3 - 2 * p),
        5 => p * p * p * (10 - 15 * p + 6 * p * p),
        _ => p,
    };

    /// <param name="contrarian">
    /// true = đánh cược có tính toán: chọn ngược ở cặp sát nhau. false = luôn chọn cửa trên.
    /// </param>
    public static List<BracketCall> Build(IReadOnlyList<BracketPair> pairs, bool contrarian)
    {
        var calls = new List<BracketCall>(pairs.Count);

        foreach (var x in pairs)
        {
            var pMap = MapProbability(x.EloA, x.EloB);
            var pA = SeriesProbability(pMap, x.BestOf);
            var pB = 1 - pA;

            var favouriteIsA = pA >= 0.5;
            var close = Math.Abs(pA - 0.5) <= CloseBand;

            // Chỉ đánh cược ở cặp sát nhau. Ở cặp chênh lệch rõ thì ngược kèo là vứt điểm đi,
            // không phải là chiến thuật.
            var pickUnderdog = contrarian && close;

            var pick = pickUnderdog
                ? (favouriteIsA ? x.TeamB : x.TeamA)
                : (favouriteIsA ? x.TeamA : x.TeamB);

            var cost = pickUnderdog ? Math.Abs(pA - pB) : 0;

            var reason = pickUnderdog
                ? $"Cặp sát nhau ({pA:P0}–{pB:P0}), chênh Elo chỉ {Math.Abs(x.EloA - x.EloB):0} điểm. "
                  + $"Chọn ngược chỉ đắt {cost:P0} xác suất nhưng khác hẳn số đông."
                : close
                    ? $"Cặp sát nhau ({pA:P0}–{pB:P0}) nhưng đang theo cửa trên."
                    : $"Chênh lệch rõ ({Math.Max(pA, pB):P0}), chênh Elo {Math.Abs(x.EloA - x.EloB):0} điểm — "
                      + "chọn ngược ở đây là vứt điểm đi.";

            calls.Add(new BracketCall(
                x.NodeId, x.Name, x.TeamA, x.TeamB, pA, pB,
                Math.Abs(x.EloA - x.EloB), x.BestOf,
                pick, !pickUnderdog, close, cost, reason));
        }

        return calls.OrderByDescending(c => c.IsClose).ThenBy(c => c.NodeId).ToList();
    }
}
