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

    /// <summary>
    /// Mô phỏng CẢ NHÁNH tới chung kết, không chỉ vòng đầu.
    ///
    /// VÌ SAO BẮT BUỘC PHẢI CÓ. Dự đoán bảng đấu khoá một lần cho toàn giải, nên chỉ đoán bốn cặp
    /// tứ kết là bỏ trống mười nút còn lại — trong đó có chung kết, nút đáng giá nhất. Muốn điền
    /// được nút sâu thì phải biết ai tới đó, mà điều đó lại phụ thuộc vào chính những lựa chọn
    /// phía trên.
    ///
    /// HAI CON SỐ, KHÔNG PHẢI MỘT, và đây là điểm dễ đọc nhầm nhất. Xác suất của riêng một nút
    /// giả định cặp đấu đó DIỄN RA; còn xác suất nó diễn ra lại là tích của mọi lựa chọn phía
    /// trên. Một dự đoán chung kết "70%" thực chất chỉ đúng khoảng 15% nếu bốn nút trước nó mỗi
    /// nút đúng 60%. Không tách hai con số thì các nút sâu trông chắc chắn ngang các nút đầu.
    ///
    /// Đội THUA rơi xuống nhánh thua chứ không biến mất: nút nhận người thua được xác định qua
    /// LoseTo, nên mô phỏng phải mang cả hai đội đi tiếp, không chỉ đội thắng.
    /// </summary>
    public static List<BracketStep> Simulate(
        IReadOnlyList<BracketNode> nodes,
        IReadOnlyDictionary<string, double> elo,
        bool contrarian,
        int bestOf,
        int finalBestOf)
    {
        var byId = nodes.ToDictionary(n => n.NodeId);

        var winner = new Dictionary<int, string>();
        var loser = new Dictionary<int, string>();
        var reached = new Dictionary<int, double>();
        var depths = new Dictionary<int, int>();
        var steps = new List<BracketStep>();

        // Thứ tự phụ thuộc: một nút chỉ giải được khi cả hai nút nuôi nó đã xong. Lặp cho tới
        // khi không tiến thêm được — an toàn hơn tự sắp topo, và tự dừng khi đồ thị thiếu cạnh.
        var pending = nodes.Select(n => n.NodeId).ToHashSet();
        var round = 0;

        while (pending.Count > 0)
        {
            round++;
            var solvedThisRound = new List<int>();

            foreach (var id in pending.OrderBy(x => x))
            {
                var n = byId[id];

                var a = Participant(n, n.In1, byId, winner, loser, n.Team1);
                var b = Participant(n, n.In2, byId, winner, loser, n.Team2);

                if (a is null || b is null) continue;
                if (!elo.TryGetValue(a, out var ea) || !elo.TryGetValue(b, out var eb)) continue;

                // Chung kết tổng đá Bo5; mọi nút khác Bo3. Nút không đi tiếp đâu nữa là chung kết.
                var bo = n.WinTo is null ? finalBestOf : bestOf;

                var call = Build([new BracketPair(n.NodeId, n.Name, a, b, ea, eb, bo)], contrarian)[0];

                winner[id] = call.Pick;
                loser[id] = call.Pick == a ? b : a;

                var upstream = new[] { n.In1, n.In2 }
                    .Where(x => x is int)
                    .Select(x => reached.GetValueOrDefault(x!.Value, 1.0))
                    .DefaultIfEmpty(1.0)
                    .Aggregate(1.0, (acc, x) => acc * x);

                var pPick = call.Pick == a ? call.ProbA : call.ProbB;
                reached[id] = upstream * pPick;

                // VÒNG = ĐỘ SÂU TRONG CÂY, không phải lượt giải của vòng lặp bên ngoài.
                //
                // Vòng lặp giải được nhiều nút trong cùng một lượt (giải xong nút 14 thì nút 18
                // cũng giải được ngay trong lượt đó), nên lấy số lượt làm số vòng sẽ dồn gần hết
                // bảng đấu vào "vòng 1" — đã thấy thật: 13 trong 14 nút cùng mang nhãn vòng 1.
                var depth = 1 + new[] { n.In1, n.In2 }
                    .Where(x => x is int)
                    .Select(x => depths.GetValueOrDefault(x!.Value, 0))
                    .DefaultIfEmpty(0)
                    .Max();

                depths[id] = depth;

                steps.Add(new BracketStep(
                    call, depth, upstream,
                    TeamsKnown: n.Team1 is not null && n.Team2 is not null));

                solvedThisRound.Add(id);
            }

            if (solvedThisRound.Count == 0) break;   // thiếu Elo hoặc thiếu cạnh: dừng, không treo
            foreach (var id in solvedThisRound) pending.Remove(id);
        }

        return steps.OrderBy(s => s.Round).ThenBy(s => s.Call.NodeId).ToList();
    }

    /// <summary>
    /// Đội vào một ô của nút: đã biết sẵn thì lấy luôn, chưa biết thì lấy từ nút nuôi.
    ///
    /// Nút nuôi gửi NGƯỜI THẮNG hay NGƯỜI THUA sang là do chính nó quyết định qua WinTo/LoseTo,
    /// không phải do nút nhận. Đọc ngược chiều đó thì nhánh thua sẽ nhận nhầm người thắng, và cả
    /// nửa dưới bảng đấu sai mà hình vẽ vẫn hợp lệ.
    /// </summary>
    private static string? Participant(
        BracketNode self, int? feederId, IReadOnlyDictionary<int, BracketNode> byId,
        IReadOnlyDictionary<int, string> winner, IReadOnlyDictionary<int, string> loser,
        string? known)
    {
        if (known is not null) return known;
        if (feederId is not int fid || !byId.TryGetValue(fid, out var feeder)) return null;

        if (feeder.WinTo == self.NodeId) return winner.GetValueOrDefault(fid);
        if (feeder.LoseTo == self.NodeId) return loser.GetValueOrDefault(fid);

        return null;
    }
}

/// <summary>Một nút bảng đấu kèm cạnh đồ thị, dùng để mô phỏng tới tận chung kết.</summary>
public readonly record struct BracketNode(
    int NodeId, string? Name, string? Group,
    int? In1, int? In2, int? WinTo, int? LoseTo,
    string? Team1, string? Team2);

/// <param name="Reached">
/// Xác suất cặp đấu này DIỄN RA ĐÚNG NHƯ DỰ ĐOÁN — tích của mọi lựa chọn phía trên nó. Đây là
/// con số quyết định mức tin của các nút sâu, và nó luôn nhỏ hơn xác suất của riêng nút đó.
/// </param>
public readonly record struct BracketStep(
    BracketCall Call, int Round, double Reached, bool TeamsKnown);
