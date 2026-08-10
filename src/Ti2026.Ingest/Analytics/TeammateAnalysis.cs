namespace Ti2026.Ingest.Analytics;

/// <summary>Một ván, rút gọn còn ai cùng phe và kết quả.</summary>
public readonly record struct MateGame(bool Won, IReadOnlyList<MatePresence> Mates);

/// <param name="SameParty">true = cùng nhóm, false = ghép trúng.</param>
public readonly record struct MatePresence(long AccountId, string? Name, bool SameParty, int? RankTier);

/// <summary>Thành tích khi chơi cùng một người, so với khi vắng người đó.</summary>
/// <param name="Lift">Chênh lệch điểm phần trăm. Dương = chơi cùng thì thắng nhiều hơn.</param>
/// <param name="Notable">Chênh đã vượt được cả nhiễu lẫn phép hiệu chỉnh so sánh bội.</param>
public readonly record struct TeammateLine(
    long AccountId, string Name, int Games, int Wins, double Winrate,
    int PartyGames, int WithoutGames, double WithoutWinrate, double Lift,
    bool Notable, int? RankTier);

/// <summary>
/// "Chơi với ai thì thắng" — phần đồng đội, dựng trên chính lịch sử pub đã lưu.
///
/// PHÉP SO PHẢI CÓ ĐỐI CHỨNG. Nói "thắng 62% khi chơi với A" là chưa nói gì: nếu người này thắng
/// 62% ở mọi ván thì A không đóng góp gì cả. Con số duy nhất có nghĩa là CHÊNH LỆCH giữa những
/// ván có A và những ván không có A. Nên mỗi dòng ở đây đều mang theo nhóm đối chứng của nó.
///
/// LẠI LÀ SO SÁNH BỘI. Một người chơi lâu năm có hàng chục đồng đội quen. Chấm "người hợp nhất"
/// bằng ngưỡng dành cho MỘT phép so thì gần như chắc chắn có vài người vượt ngưỡng chỉ nhờ may —
/// đúng cái lỗi đã mắc ở phần "khắc tinh" của các đội, nơi 11/16 đội có khắc tinh cho tới khi
/// hiệu chỉnh. Ở đây ngưỡng được chia cho số người thực sự đem ra xét.
///
/// RIÊNG TƯ. Chỉ nêu những người ĐÃ CHỦ ĐỘNG ĐI CÙNG NHÓM đủ nhiều, xem
/// <see cref="MinPartyGames"/>. Người ghép ngẫu nhiên trúng vài ván không tự nguyện xuất hiện
/// trên một trang công khai, và họ cũng chẳng đủ số ván để nói được điều gì — nên ranh giới
/// riêng tư và ngưỡng thống kê ở đây trùng nhau.
///
/// KHÔNG LÀM ĐỐI THỦ. Payload có đủ cả 10 người, nhưng trong pub thì gặp lại cùng một đối thủ
/// là ngẫu nhiên và thưa, nên mọi bảng "khắc tinh" dựng trên đó sẽ là nhiễu đội lốt phát hiện.
/// </summary>
public static class TeammateAnalysis
{
    /// <summary>Số ván CÙNG NHÓM tối thiểu để một người được nêu tên.</summary>
    public const int MinPartyGames = 20;

    /// <summary>Nhóm đối chứng cũng phải đủ dày, nếu không thì chênh lệch là so với hư không.</summary>
    public const int MinWithoutGames = 30;

    /// <summary>Chênh dưới ngần này điểm phần trăm thì không đáng nói, dù có ý nghĩa thống kê.</summary>
    public const double MinLift = 4.0;

    public static List<TeammateLine> Read(
        IReadOnlyList<MateGame> games, int minPartyGames = MinPartyGames)
    {
        if (games.Count == 0) return [];

        var totalWins = games.Count(g => g.Won);

        var agg = new Dictionary<long, (int Games, int Wins, int Party, string? Name, int? Rank)>();

        foreach (var g in games)
        {
            // Cùng một người có thể xuất hiện hai lần trong danh sách nếu dữ liệu nguồn lặp;
            // đếm theo tập duy nhất để một ván không bao giờ tính hai lần cho một người.
            foreach (var acc in g.Mates.Select(m => m.AccountId).Distinct())
            {
                var m = g.Mates.First(x => x.AccountId == acc);
                var cur = agg.GetValueOrDefault(acc);

                agg[acc] = (cur.Games + 1,
                    cur.Wins + (g.Won ? 1 : 0),
                    cur.Party + (m.SameParty ? 1 : 0),
                    m.Name ?? cur.Name,
                    m.RankTier ?? cur.Rank);
            }
        }

        var shown = agg.Where(x => x.Value.Party >= minPartyGames).ToList();
        if (shown.Count == 0) return [];

        var lines = new List<TeammateLine>();

        foreach (var (acc, v) in shown)
        {
            var withoutGames = games.Count - v.Games;
            var withoutWins = totalWins - v.Wins;
            if (withoutGames < MinWithoutGames) continue;

            var wr = v.Wins * 100.0 / v.Games;
            var wrWithout = withoutWins * 100.0 / withoutGames;
            var lift = wr - wrWithout;

            // Kiểm hai tỷ lệ, rồi chia ngưỡng cho số người đang xét. Cần CẢ hai điều kiện: tách
            // được khỏi nhiễu, VÀ đủ lớn để đáng nói — ở vài nghìn ván thì 1,5 điểm phần trăm có
            // thể "có ý nghĩa thống kê" mà chẳng có ý nghĩa gì với người đọc.
            var se = Math.Sqrt(2500.0 / v.Games + 2500.0 / withoutGames);
            var z = ZFor(0.05 / shown.Count);
            var notable = Math.Abs(lift) > z * se && Math.Abs(lift) >= MinLift;

            lines.Add(new TeammateLine(
                acc, v.Name ?? $"#{acc}", v.Games, v.Wins, Math.Round(wr, 1),
                v.Party, withoutGames, Math.Round(wrWithout, 1), Math.Round(lift, 1),
                notable, v.Rank));
        }

        return lines.OrderByDescending(l => l.Games).ToList();
    }

    /// <summary>
    /// Mốc z hai phía cho một mức ý nghĩa, bằng phép nghịch đảo phân phối chuẩn của Acklam.
    ///
    /// Tự tính chứ không tra bảng vì mức ý nghĩa ở đây đã bị chia cho số người đang xét, nên nó
    /// là một số bất kỳ chứ không phải 0,05 hay 0,01. Viết cứng 1,96 rồi gọi đó là hiệu chỉnh
    /// so sánh bội thì chính là không hiệu chỉnh gì cả.
    /// </summary>
    public static double ZFor(double twoSidedAlpha)
    {
        var p = 1 - twoSidedAlpha / 2;
        if (p <= 0 || p >= 1) return 1.96;

        double[] a = [-39.69683028665376, 220.9460984245205, -275.9285104469687,
                      138.3577518672690, -30.66479806614716, 2.506628277459239];
        double[] b = [-54.47609879822406, 161.5858368580409, -155.6989798598866,
                      66.80131188771972, -13.28068155288572];
        double[] c = [-0.007784894002430293, -0.3223964580411365, -2.400758277161838,
                      -2.549732539343734, 4.374664141464968, 2.938163982698783];
        double[] d = [0.007784695709041462, 0.3224671290700398, 2.445134137142996, 3.754408661907416];

        const double low = 0.02425;

        if (p < low)
        {
            var q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                   / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }

        if (p > 1 - low)
        {
            var q = Math.Sqrt(-2 * Math.Log(1 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                   / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }

        var r = p - 0.5;
        var s = r * r;
        return (((((a[0] * s + a[1]) * s + a[2]) * s + a[3]) * s + a[4]) * s + a[5]) * r
               / (((((b[0] * s + b[1]) * s + b[2]) * s + b[3]) * s + b[4]) * s + 1);
    }
}
