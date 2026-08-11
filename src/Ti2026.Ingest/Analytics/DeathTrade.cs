namespace Ti2026.Ingest.Analytics;

/// <summary>Phần đổi chác giao tranh của một ván đã parse.</summary>
/// <param name="Role">Mã vai trò của ván đó, để tách bảng — xem <see cref="RoleResolver"/>.</param>
public readonly record struct TradeGame(
    int? FightsDied, int? FightsDiedAhead, int? FightSwingDied,
    int? FightsSurvived, int? FightSwingSurvived,
    int? TradeMyGold, int? TradeFoeGold, int? TradeFoeDeaths,
    string Role = "", string RoleLabel = "");

/// <summary>Đổi chác của riêng một vai trò.</summary>
public readonly record struct TradeByRole(
    string Role, string Label, int Trades, int MyGold, int FoeGold, int GoldEdge);

/// <param name="AheadShare">Tỷ lệ pha có ta chết mà đội VẪN lời vàng, tính theo phần trăm.</param>
/// <param name="SwingDied">Chênh lệch vàng trung bình mỗi pha có ta chết. Dương = đội vẫn lời.</param>
/// <param name="SwingSurvived">Như trên, ở những pha ta sống — nhóm đối chứng.</param>
/// <param name="MyGold">Độ giàu trung bình của ta lúc chết trong giao tranh.</param>
/// <param name="FoeGold">Độ giàu trung bình của kẻ địch chết trong CHÍNH những pha đó.</param>
public readonly record struct TradeReading(
    int Matches, int Fights, double AheadShare, int SwingDied, int SwingSurvived,
    int MyGold, int FoeGold, int GoldEdge, int Trades, string Text,
    List<TradeByRole> ByRole);

/// <summary>
/// CÁI CHẾT CỦA TA ĐỔI ĐƯỢC GÌ, đo ở mức TỪNG PHA GIAO TRANH.
///
/// VÌ SAO CẦN TẦNG NÀY khi đã có <see cref="DeathEffect"/>. Tầng kia hỏi "ván ta chết nhiều thì
/// đồng đội có farm tốt hơn không" và đo được +5 điểm phân vị — thật, nhưng vẫn là con số CẢ
/// VÁN. Nó không phân biệt được một cái chết vô ích với một cái chết kéo 2-3 người địch đi xa để
/// đồng đội dọn nốt phần còn lại.
///
/// Pha giao tranh thì phân biệt được. Cộng vàng cộng thêm của 5 người mỗi phe TRONG pha đó là
/// biết pha đó ai lời — và câu hỏi "cái chết này có đáng không" chính là câu hỏi về pha đó.
///
/// HAI CON SỐ, TRẢ LỜI HAI VẾ CỦA CÙNG MỘT Ý:
///
/// 1. TỶ LỆ PHA TA CHẾT MÀ ĐỘI VẪN LỜI. Nếu cái chết kéo được người đi và đồng đội dọn được
///    phần còn lại thì pha đó vẫn lời dù ta nằm xuống.
///
/// 2. ĐỘ GIÀU ĐEM ĐỔI. Tiền thưởng của Dota tỉ lệ với net worth nạn nhân, nên một cái chết rẻ
///    đổi lấy một cái chết đắt là một cuộc đổi chác có lời — kể cả khi số người chết là một đổi
///    một. Đây đúng là điều người dùng mô tả: "tôi thì không giàu, còn đồng đội giết được người
///    giàu nhất bên kia".
///
/// GIỚI HẠN, phải nói ra: chỉ có ở ván ĐÃ PARSE. Replay của Valve hết hạn sau khoảng 2 tháng nên
/// phần lịch sử xa không bao giờ có dữ liệu này, và mẫu ở đây luôn nhỏ hơn hẳn phần còn lại của
/// trang. Và đây vẫn là TƯƠNG QUAN: pha ta chết mà đội lời cũng có thể là pha đội vốn đã mạnh
/// hơn, chứ không phải nhờ cái chết.
/// </summary>
public static class DeathTrade
{
    /// <summary>Dưới ngần này pha có ta chết thì mọi tỷ lệ đều là nhiễu.</summary>
    public const int MinFights = 40;

    /// <summary>Dưới ngần này lượt đổi chác thì không so được độ giàu.</summary>
    public const int MinTrades = 30;

    public static TradeReading? Read(IEnumerable<TradeGame> games)
    {
        var g = games.Where(x => x.FightsDied is int).ToList();
        if (g.Count == 0) return null;

        var fights = g.Sum(x => x.FightsDied!.Value);
        if (fights < MinFights) return null;

        var ahead = g.Sum(x => x.FightsDiedAhead ?? 0);
        var survived = g.Sum(x => x.FightsSurvived ?? 0);

        var swingDied = fights == 0 ? 0 : g.Sum(x => x.FightSwingDied ?? 0) / fights;
        var swingSurv = survived == 0 ? 0 : g.Sum(x => x.FightSwingSurvived ?? 0) / survived;

        var trades = g.Sum(x => x.TradeFoeDeaths ?? 0);
        var myGold = trades == 0 ? 0 : g.Sum(x => x.TradeMyGold ?? 0) / trades;
        var foeGold = trades == 0 ? 0 : g.Sum(x => x.TradeFoeGold ?? 0) / trades;

        var share = fights == 0 ? 0 : ahead * 100.0 / fights;

        return new TradeReading(
            g.Count, fights, Math.Round(share, 1), swingDied, swingSurv,
            myGold, foeGold, foeGold - myGold, trades,
            Describe(fights, share, swingDied, swingSurv, trades, myGold, foeGold),
            ByRole(g));
    }

    /// <summary>
    /// Tách đổi chác theo VAI TRÒ — và đây là lát cắt duy nhất đọc được của phần này.
    ///
    /// Đo trên dữ liệu thật, chênh độ giàu khi đổi mạng gần như HOÀN TOÀN do vai trò quyết định,
    /// giống hệt nhau ở cả hai người được theo dõi:
    ///
    ///   carry −4.027 / −1.928   mid −2.410 / −1.003   pos4 +931 / +2.765   pos5 +2.603 / +2.351
    ///
    /// Nghĩa là con số gộp không đo "cái chết của người này có đáng không" — nó đo "người này hay
    /// chơi vai trò nào". Hỗ trợ vốn nghèo hơn theo định nghĩa nên chết rẻ là chuyện đương nhiên,
    /// không phải thành tích.
    ///
    /// Vẫn hiện bảng vì tách ra thì nó nói đúng điều nó đo được; và ghi rõ giới hạn đó ngay cạnh,
    /// thay vì để con số gộp một mình gợi ra một kết luận nó không đỡ nổi.
    /// </summary>
    private static List<TradeByRole> ByRole(List<TradeGame> games)
    {
        return games
            .Where(x => (x.TradeFoeDeaths ?? 0) > 0 && !string.IsNullOrEmpty(x.Role))
            .GroupBy(x => (x.Role, x.RoleLabel))
            .Select(g =>
            {
                var n = g.Sum(x => x.TradeFoeDeaths!.Value);
                var my = g.Sum(x => x.TradeMyGold ?? 0) / n;
                var foe = g.Sum(x => x.TradeFoeGold ?? 0) / n;
                return new TradeByRole(g.Key.Role, g.Key.RoleLabel, n, my, foe, foe - my);
            })
            .Where(r => r.Trades >= MinTrades)
            .OrderByDescending(r => r.Trades)
            .ToList();
    }

    private static string Describe(
        int fights, double share, int swingDied, int swingSurv, int trades, int my, int foe)
    {
        var part1 =
            $"Trong {fights:N0} pha giao tranh có bạn chết, {share:0}% số pha đội vẫn LỜI vàng so "
            + $"với địch. Trung bình mỗi pha như vậy đội {(swingDied >= 0 ? "hơn" : "kém")} "
            + $"{Math.Abs(swingDied):N0} vàng, so với {(swingSurv >= 0 ? "hơn" : "kém")} "
            + $"{Math.Abs(swingSurv):N0} vàng ở những pha bạn sống sót.";

        if (trades < MinTrades) return part1;

        var edge = foe - my;

        return part1
            + $" Còn về đổi chác: lúc bạn ngã xuống bạn có trung bình {my:N0} vàng, trong khi kẻ "
            + $"địch chết cùng pha có {foe:N0} — "
            + (edge > 0
                ? $"tức bạn thường đổi mạng rẻ lấy mạng đắt hơn {edge:N0} vàng."
                : $"tức mạng bạn đắt hơn mạng đổi được {Math.Abs(edge):N0} vàng.");
    }
}
