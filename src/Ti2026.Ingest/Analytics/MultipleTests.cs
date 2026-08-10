namespace Ti2026.Ingest.Analytics;

/// <summary>
/// Kiểm nhiều giả thuyết cùng lúc mà không rơi vào một trong HAI cái bẫy đối nghịch.
///
/// BẪY THỨ NHẤT — KHÔNG HIỆU CHỈNH. Xét 125 hero với ngưỡng 0,05 thì riêng may rủi đã cho ra
/// khoảng 6 hero "đáng kể". Đây là lỗi đã mắc thật ở phần khắc tinh của các đội: 11/16 đội có
/// khắc tinh, và sau khi hiệu chỉnh thì còn 0.
///
/// BẪY THỨ HAI — TƯỞNG RẰNG CÓ CÁCH HIỆU CHỈNH "NHẸ TAY HƠN" ĐỂ CỨU VÃN. Bản đầu của tệp này
/// đổi từ Bonferroni sang Benjamini–Hochberg với lý do "Bonferroni quá chặt nên cột đáng kể
/// không bao giờ bật". Lý do đó SAI ở hai tầng:
///
/// • BH KHÔNG nới lỏng cho p nhỏ nhất. Ngưỡng của hạng 1 trong BH là đúng q/m — bằng y hệt
///   Bonferroni. BH chỉ mạnh hơn khi có NHIỀU phép so cùng nhỏ, vì lúc đó các hạng sau được
///   ngưỡng rộng dần. Một tín hiệu đơn độc thì hai phép cho cùng một kết luận.
///
/// • Và quan trọng hơn: đo trên dữ liệu thật của người dùng, p nhỏ nhất trong 125 hero là
///   0,0032. Trong 125 phép so hoàn toàn ngẫu nhiên, p nhỏ nhất trung bình đã vào khoảng
///   1/126 ≈ 0,008. Tức 0,0032 là chuyện BÌNH THƯỜNG với cực trị của 125 phép so. Không hero
///   nào nổi bật thật, và cột không bật CHÍNH LÀ kết luận đúng — không phải lỗi cần sửa.
///
/// Bài học giữ lại: khi một phép kiểm không cho ra phát hiện nào, việc cần làm là NÓI RA vì sao
/// (mẫu mỗi hero quá mỏng so với mức nhiễu), chứ không phải đi tìm một ngưỡng dễ dãi hơn cho tới
/// khi có thứ gì đó sáng lên.
///
/// Vẫn dùng BH vì nó không bao giờ tệ hơn Bonferroni và mạnh hơn hẳn khi có nhiều tín hiệu
/// thật — nhưng dùng nó với đúng kỳ vọng.
/// </summary>
public static class MultipleTests
{
    /// <summary>Tỷ lệ phát hiện sai chấp nhận được.</summary>
    public const double DefaultQ = 0.05;

    /// <summary>
    /// Đánh dấu những phép so vượt ngưỡng Benjamini–Hochberg. Trả về mảng cùng thứ tự với đầu vào.
    ///
    /// Cách làm: sắp p tăng dần, tìm hạng k LỚN NHẤT thoả p₍ₖ₎ ≤ (k/m)·q, rồi nhận mọi phép so
    /// có p ≤ p₍ₖ₎. Phải lấy hạng lớn nhất chứ không phải dừng ở hạng đầu tiên trượt — bỏ qua
    /// điều này là biến BH thành một phép kiểm chặt hơn hẳn mà vẫn mang tên BH.
    /// </summary>
    public static bool[] BenjaminiHochberg(IReadOnlyList<double> pValues, double q = DefaultQ)
    {
        var m = pValues.Count;
        var keep = new bool[m];
        if (m == 0) return keep;

        var ordered = pValues
            .Select((p, i) => (P: double.IsNaN(p) ? 1.0 : Math.Clamp(p, 0, 1), Index: i))
            .OrderBy(x => x.P)
            .ToList();

        var cutoff = -1.0;

        for (var rank = m; rank >= 1; rank--)
        {
            if (ordered[rank - 1].P <= rank * q / m)
            {
                cutoff = ordered[rank - 1].P;
                break;
            }
        }

        if (cutoff < 0) return keep;

        foreach (var (p, i) in ordered)
            if (p <= cutoff)
                keep[i] = true;

        return keep;
    }

    /// <summary>
    /// Xác suất hai phía của việc lệch khỏi p₀ ít nhất bằng mức đã quan sát, theo nhị thức chính xác.
    ///
    /// TÍNH TRONG KHÔNG GIAN LOGARIT. Cách viết thẳng — bắt đầu từ (1−p)ⁿ rồi nhân dần — tràn số
    /// xuống 0 ngay khi n vài trăm, và điều nguy hiểm là nó tràn ÂM THẦM: mọi số hạng sau đó đều
    /// bằng 0, tổng bằng 0, và bằng 0 thì luôn "đáng kể". Tức là ở đúng những hero chơi nhiều
    /// nhất — nơi có nhiều dữ liệu nhất — hàm sẽ tuyên bố mọi thứ đều có ý nghĩa.
    /// </summary>
    public static double BinomialTwoSided(int n, int k, double p)
    {
        if (n <= 0) return 1;
        if (p <= 0) return k > 0 ? 0 : 1;
        if (p >= 1) return k < n ? 0 : 1;
        if (k < 0 || k > n) return 1;

        var logs = new double[n + 1];
        var logP = Math.Log(p);
        var logQ = Math.Log(1 - p);

        logs[0] = n * logQ;
        for (var i = 0; i < n; i++)
            logs[i + 1] = logs[i] + Math.Log((double)(n - i) / (i + 1)) + logP - logQ;

        // Mọi kết cục KHÔNG có khả năng xảy ra cao hơn kết cục đã quan sát. Đây là định nghĩa
        // hai phía của Fisher — đúng cả khi phân phối lệch, khác với lối nhân đôi một phía.
        var observed = logs[k] + 1e-9;
        var acc = double.NegativeInfinity;

        for (var i = 0; i <= n; i++)
            if (logs[i] <= observed)
                acc = LogAdd(acc, logs[i]);

        return Math.Min(1, Math.Exp(acc));
    }

    /// <summary>
    /// Mốc z hai phía cho một mức ý nghĩa, bằng phép nghịch đảo phân phối chuẩn của Acklam.
    ///
    /// Tự tính chứ không tra bảng vì mức ý nghĩa ở đây đã bị chia cho số phép so, nên nó là một
    /// số bất kỳ chứ không phải 0,05 hay 0,01. Viết cứng 1,96 rồi gọi đó là hiệu chỉnh so sánh
    /// bội thì chính là không hiệu chỉnh gì cả.
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

    /// <summary>
    /// Cần bao nhiêu ván trên MỘT hero thì một cách biệt <paramref name="gapPoints"/> điểm phần
    /// trăm mới đứng vững, khi đang xét cả pool <paramref name="poolSize"/> hero.
    ///
    /// Tồn tại để trang trả lời được câu "vì sao không hero nào được đánh dấu" bằng một con số
    /// thay vì bằng im lặng. Một bảng toàn ô trống mà không giải thích sẽ bị đọc thành "hệ thống
    /// hỏng", trong khi sự thật là mẫu mỗi hero còn quá mỏng so với mức nhiễu.
    /// </summary>
    public static int GamesNeeded(int poolSize, double gapPoints, double q = DefaultQ)
    {
        if (gapPoints <= 0) return int.MaxValue;

        var z = ZFor(q / Math.Max(poolSize, 1));
        return (int)Math.Ceiling(Math.Pow(z * 50 / gapPoints, 2));
    }

    private static double LogAdd(double a, double b)
    {
        if (double.IsNegativeInfinity(a)) return b;
        if (double.IsNegativeInfinity(b)) return a;

        var hi = Math.Max(a, b);
        return hi + Math.Log(1 + Math.Exp(-Math.Abs(a - b)));
    }
}
