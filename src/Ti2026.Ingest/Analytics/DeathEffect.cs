namespace Ti2026.Ingest.Analytics;

/// <summary>Một ván, rút gọn còn những gì cần để hỏi cái chết có đổi được gì không.</summary>
public readonly record struct DeathGame(
    bool Won, int? PctDeaths, int? MatesPctGpm, int? TeamNetWorth, int? EnemyNetWorth,
    int DurationSeconds);

/// <param name="MatesFarmGap">
/// Mức farm của đồng đội ở nhóm ta chết NHIỀU, trừ đi nhóm ta chết ÍT. Dương = ván ta chết nhiều
/// thì đồng đội giàu hơn, tức đúng chiều của lối chơi hi sinh.
/// </param>
/// <param name="LeadGap">Chênh lệch kinh tế hai phe, so giữa hai nhóm đó. Đơn vị: vàng.</param>
/// <param name="Games">Số ván CÒN LẠI sau khi lọc về cùng độ dài, không phải tổng số ván.</param>
/// <param name="MedianMinutes">Độ dài trung vị của nhóm kết quả này, tâm của dải đang xét.</param>
public readonly record struct DeathSplit(
    string Outcome, int Games, int MedianMinutes,
    int HighDeathMatesFarm, int LowDeathMatesFarm,
    int MatesFarmGap, int HighDeathLead, int LowDeathLead, int LeadGap, double PValue);

/// <param name="Verdict">ho-tro | phan-bac | khong-du-du-lieu | khong-ro</param>
public readonly record struct DeathEffectReading(
    string Verdict, string Text, List<DeathSplit> Splits);

/// <summary>
/// CÁI CHẾT CÓ ĐỔI ĐƯỢC GÌ KHÔNG — đo bằng kinh tế của ĐỒNG ĐỘI, không mượn chỉ số hỗ trợ.
///
/// VÌ SAO KHÔNG DÙNG SỐ HỖ TRỢ. Bản trước dùng nó làm proxy và người dùng bác đúng: hỗ trợ chỉ
/// ghi nhận việc CÓ MẶT trong bán kính lúc kill. Một cái chết mua thời gian cho đồng đội đi
/// farm hay đẩy trụ thì hoàn toàn không để lại dấu vết nào trong đó — thậm chí ngược lại, người
/// đã chết thì không thể có mặt ở pha kill sau đó.
///
/// Thứ đo được đúng điều cần biết là mức farm của bốn người kia TRONG CHÍNH VÁN ẤY. Nếu lối chơi
/// hi sinh có hiệu quả thì những ván ta chết nhiều phải là những ván đồng đội giàu hơn thường lệ.
///
/// SO TRONG CÙNG MỘT KẾT QUẢ TRẬN. Đây là điều kiện bắt buộc, không phải tinh chỉnh. Thắng thua
/// chi phối mọi chỉ số: đo trên hai tài khoản thật thì phân vị của mọi cột đều tụt 11–40 điểm khi
/// thua, và mức tụt của hai người gần như trùng khít. Trộn thắng với thua thì phép so chỉ đo lại
/// việc thắng hay thua, và mọi cái chết đều sẽ trông như vô ích.
///
/// KÈM CHÊNH LỆCH KINH TẾ HAI PHE. Thiếu nó thì không tách được "đồng đội giàu vì ta tạo được
/// khoảng trống" khỏi "cả hai phe cùng giàu vì ván kéo dài".
///
/// GIỚI HẠN PHẢI NÓI RA: đây là tương quan trong cùng một ván, KHÔNG phải nhân quả. Ván mà đồng
/// đội farm tốt cũng có thể là ván ta dám lao vào hơn vì biết đội đang mạnh. Dữ liệu này không
/// phân biệt được hai chiều đó, và trang phải nói vậy chứ không được viết "cái chết của bạn tạo
/// ra khoảng trống".
/// </summary>
public static class DeathEffect
{
    /// <summary>Mỗi nhóm kết quả cần ngần này ván CÒN LẠI sau khi lọc độ dài thì mới so được.</summary>
    public const int MinGamesPerOutcome = 120;

    /// <summary>
    /// Chỉ so những ván dài xấp xỉ nhau, lệch không quá ngần này so với trung vị.
    ///
    /// ĐÂY LÀ ĐIỀU KIỆN QUYẾT ĐỊNH DẤU CỦA KẾT QUẢ, không phải một bộ lọc cho gọn. Bản đầu chỉ
    /// khống chế thắng/thua và cho ra: ván thua mà ta chết nhiều thì đồng đội farm KÉM hơn 13
    /// điểm — nghe như bằng chứng phản bác lối chơi hi sinh.
    ///
    /// Nhưng hai nhóm đó không so được với nhau: ván thua ta chết ít dài trung bình 47 phút (thua
    /// dai dẳng, ai cũng kịp farm), còn ván thua ta chết nhiều chỉ 35 phút (bị đè). Phép so đó
    /// thực chất đang so ĐỘ DÀI VÁN.
    ///
    /// Lọc về cùng dải độ dài rồi so lại thì dấu ĐẢO: từ −13 thành +8, cùng chiều với ván thắng.
    /// Đo trên người thứ hai cũng vậy: −9 thành +8. Tức kết luận đúng ngược hẳn với kết luận
    /// chưa khống chế.
    ///
    /// 12% là đánh đổi: hẹp hơn thì mẫu mỏng, rộng hơn thì độ dài lại lọt vào phép so.
    /// </summary>
    public const double DurationBand = 0.12;

    /// <summary>Tỷ lệ mỗi đầu khi cắt nhóm chết nhiều nhất và chết ít nhất.</summary>
    public const double TailShare = 0.25;

    /// <summary>Chênh dưới ngần này điểm phân vị thì không đáng nói, dù mẫu lớn tới đâu.</summary>
    public const int MinFarmGap = 5;

    public static DeathEffectReading Read(IReadOnlyList<DeathGame> games)
    {
        var usable = games
            .Where(g => g.PctDeaths is int && g.MatesPctGpm is int)
            .ToList();

        var splits = new List<DeathSplit>();

        foreach (var (won, label) in new[] { (true, "thắng"), (false, "thua") })
        {
            var g = usable.Where(x => x.Won == won && x.DurationSeconds > 0).ToList();
            if (g.Count == 0) continue;

            // Lọc về CÙNG DẢI ĐỘ DÀI trước khi cắt theo số chết. Xem DurationBand — bỏ bước này
            // thì phép so biến thành phép so độ dài ván, và dấu của kết quả đảo ngược.
            var mid = Median(g.Select(x => x.DurationSeconds).ToList());
            var band = g.Where(x => Math.Abs(x.DurationSeconds - mid) <= mid * DurationBand).ToList();

            if (band.Count < MinGamesPerOutcome) continue;

            // Sắp theo SỐ CHẾT giảm dần: đầu danh sách là những ván chết nhiều nhất.
            var byDeaths = band.OrderByDescending(x => x.PctDeaths!.Value).ToList();
            var take = Math.Max((int)(byDeaths.Count * TailShare), 1);

            var high = byDeaths.Take(take).ToList();
            var low = byDeaths.TakeLast(take).ToList();

            var hf = high.Select(x => x.MatesPctGpm!.Value).ToList();
            var lf = low.Select(x => x.MatesPctGpm!.Value).ToList();

            splits.Add(new DeathSplit(
                label, band.Count, (int)Math.Round(mid / 60.0),
                Median(hf), Median(lf), Median(hf) - Median(lf),
                Lead(high), Lead(low), Lead(high) - Lead(low),
                MannWhitney(hf, lf)));
        }

        return new DeathEffectReading(Verdict(splits), Describe(splits), splits);
    }

    /// <summary>
    /// Kết luận chỉ được đưa ra khi CẢ ván thắng lẫn ván thua cùng chỉ một hướng.
    ///
    /// Một hướng ở ván thắng và hướng ngược ở ván thua thì không phải phát hiện — đó là hai
    /// hiệu ứng khác nhau bị gộp làm một, và chọn lấy nửa nào hợp ý mình là cách chắc chắn để
    /// tìm ra thứ mình muốn thấy.
    /// </summary>
    private static string Verdict(List<DeathSplit> s)
    {
        if (s.Count < 2) return "khong-du-du-lieu";

        var real = s.Where(x => x.PValue < 0.05 && Math.Abs(x.MatesFarmGap) >= MinFarmGap).ToList();
        if (real.Count < 2) return "khong-ro";

        if (real.All(x => x.MatesFarmGap > 0)) return "ho-tro";
        if (real.All(x => x.MatesFarmGap < 0)) return "phan-bac";

        return "khong-ro";
    }

    private static string Describe(List<DeathSplit> s)
    {
        if (s.Count < 2)
            return $"Chưa đủ {MinGamesPerOutcome} ván ở cả hai nhóm thắng và thua để so được.";

        var parts = s.Select(x =>
            $"ván {x.Outcome} dài quanh {x.MedianMinutes} phút ({x.Games} ván): đồng đội farm ở "
            + $"phân vị {x.HighDeathMatesFarm} trong nhóm bạn chết nhiều nhất, so với "
            + $"{x.LowDeathMatesFarm} trong nhóm bạn chết ít nhất "
            + $"({(x.MatesFarmGap >= 0 ? "+" : "")}{x.MatesFarmGap})");

        return string.Join("; ", parts) + ".";
    }

    /// <summary>Chênh lệch kinh tế hai phe, trung vị. Dương = phe ta đang giàu hơn.</summary>
    private static int Lead(List<DeathGame> g)
    {
        var v = g.Where(x => x.TeamNetWorth is int && x.EnemyNetWorth is int)
            .Select(x => x.TeamNetWorth!.Value - x.EnemyNetWorth!.Value).ToList();

        return v.Count == 0 ? 0 : Median(v);
    }

    private static int Median(List<int> v)
    {
        if (v.Count == 0) return 0;
        var s = v.OrderBy(x => x).ToList();
        return s.Count % 2 == 1
            ? s[s.Count / 2]
            : (int)Math.Round((s[s.Count / 2 - 1] + s[s.Count / 2]) / 2.0);
    }

    /// <summary>
    /// Mann–Whitney U hai phía, có hiệu chỉnh đồng hạng.
    ///
    /// Dùng phép kiểm THỨ HẠNG chứ không phải kiểm trung bình: phân vị là thang thứ hạng, bị
    /// chặn hai đầu ở 0 và 100, và phân bố lệch — trung bình trên thang đó không có nghĩa rõ
    /// ràng, còn thứ hạng thì luôn có.
    ///
    /// Hiệu chỉnh đồng hạng là bắt buộc ở đây chứ không phải cho đẹp: phân vị làm tròn về số
    /// nguyên nên chỉ có 101 giá trị khả dĩ, và với vài trăm ván thì đồng hạng dày đặc. Bỏ qua
    /// nó sẽ ước lượng phương sai cao hơn thực tế và làm phép kiểm quá dễ dãi.
    /// </summary>
    public static double MannWhitney(List<int> a, List<int> b)
    {
        if (a.Count == 0 || b.Count == 0) return 1;

        var all = a.Select(v => (v, g: 0)).Concat(b.Select(v => (v, g: 1)))
            .OrderBy(x => x.v).ToList();

        var ranks = new double[all.Count];
        var tieSum = 0.0;

        for (var i = 0; i < all.Count;)
        {
            var j = i;
            while (j < all.Count && all[j].v == all[i].v) j++;

            var avg = (i + j + 1) / 2.0;   // hạng trung bình cho khối đồng hạng
            for (var k = i; k < j; k++) ranks[k] = avg;

            var t = j - i;
            tieSum += (double)t * t * t - t;
            i = j;
        }

        double rankA = 0;
        for (var i = 0; i < all.Count; i++) if (all[i].g == 0) rankA += ranks[i];

        double n1 = a.Count, n2 = b.Count, n = n1 + n2;
        var u = rankA - n1 * (n1 + 1) / 2;
        var mean = n1 * n2 / 2;

        var varU = n1 * n2 / 12.0 * (n + 1 - tieSum / (n * (n - 1)));
        if (varU <= 0) return 1;

        var z = (u - mean) / Math.Sqrt(varU);
        return 2 * (1 - NormalCdf(Math.Abs(z)));
    }

    /// <summary>Hàm phân phối chuẩn tích luỹ, qua xấp xỉ Abramowitz–Stegun 7.1.26.</summary>
    private static double NormalCdf(double x)
    {
        var t = 1 / (1 + 0.2316419 * x);
        var d = 0.3989422804014327 * Math.Exp(-x * x / 2);
        var p = d * t * (0.319381530 + t * (-0.356563782 + t * (1.781477937
                + t * (-1.821255978 + t * 1.330274429))));

        return 1 - p;
    }
}
