namespace Ti2026.Ingest.Analytics;

/// <summary>
/// Bộ tham số của mô hình Elo. Gom lại một chỗ để có thể quét lưới tham số trong hiệu chuẩn
/// thay vì rải hằng số khắp nơi — con số nào cũng phải trả lời được câu "vì sao là số này".
/// </summary>
public sealed record EloOptions
{
    /// <summary>
    /// Hệ số K. 24 là mức trung dung cho esports: đủ nhạy để bắt kịp thay đổi đội hình trong
    /// một mùa giải, nhưng không nhảy loạn sau một trận bất ngờ. K quá cao biến rating thành
    /// "kết quả trận gần nhất"; quá thấp thì một đội sa sút vẫn giữ điểm cũ hàng tháng.
    /// </summary>
    public double KFactor { get; init; } = 24;

    /// <summary>
    /// Thang quy đổi chênh lệch rating sang xác suất khi CẬP NHẬT rating.
    /// Giữ 400 chuẩn ở đây để rating có thang quen thuộc, so được với hệ Elo khác.
    /// </summary>
    public double UpdateScale { get; init; } = 400;

    /// <summary>
    /// Thang quy đổi khi CÔNG BỐ xác suất ra ngoài — cố tình khác <see cref="UpdateScale"/>.
    ///
    /// Hiệu chuẩn hồi tố cho thấy thang 400 làm mô hình tự tin quá mức, và càng tự tin càng
    /// sai: nói 70–80% thì thực tế 65%, nói 80–90% thì thực tế 67%. Dota biến động cao, đặc
    /// biệt Bo1, nên chênh lệch rating không chuyển thành ưu thế mạnh như công thức chuẩn
    /// giả định. Giá trị mặc định được CHỌN TỪ DỮ LIỆU, không đặt theo cảm tính.
    /// </summary>
    public double ProbabilityScale { get; init; } = Calibrated.ProbabilityScale;

    /// <summary>
    /// Mức "quên" khi game lên bản mới, áp cho MỖI bậc patch (7.40 → 7.41 là một bậc).
    ///
    /// VÌ SAO CẦN: rating tích luỹ từ những trận đánh trên một meta đã không còn tồn tại.
    /// Một đội mạnh ở 7.39 nhờ đội hình đẩy trụ nhanh có thể không còn ưu thế đó ở 7.41.
    /// Nhưng cũng không được vứt sạch quá khứ: kỹ năng cá nhân, kỷ luật, khả năng phối hợp
    /// đi xuyên các bản. Vì vậy đây là hệ số kéo về mốc trung bình chứ không phải bộ lọc.
    ///
    /// Cách áp: tại ranh giới patch, rating của MỌI đội bị kéo về <see cref="EloEngine.InitialRating"/>
    ///   r ← 1500 + (r − 1500) × (1 − PatchRegression)
    /// nên khoảng cách giữa các đội co lại, nhưng thứ hạng giữ nguyên. 0 = tắt hoàn toàn.
    ///
    /// Cách này CÓ NHÂN QUẢ: chỉ dùng thông tin đã biết tại thời điểm đó, nên hiệu chuẩn hồi
    /// tố vẫn trung thực. Cách làm ngây thơ — nhân trọng số cho trận cũ theo khoảng cách tới
    /// patch HIỆN TẠI — sẽ khiến quá khứ được đánh giá bằng thông tin của tương lai.
    /// </summary>
    public double PatchRegression { get; init; } = Calibrated.PatchRegression;

    public static EloOptions Default { get; } = new();

    /// <summary>
    /// Tham số đã kiểm chứng NGOÀI MẪU: quét lưới trên 70% trận cũ nhất, rồi đo trên 30% trận
    /// mới nhất mà phần đó không tham gia việc chọn. Xem api/calibration để chạy lại trên dữ
    /// liệu hiện tại.
    ///
    /// KẾT QUẢ ĐO (1782 trận, mốc chia 2026-02-13, 515 dự đoán kiểm định):
    ///
    ///   Thang quy đổi — Brier trên tập kiểm định
    ///     400 → 0.2350   500 → 0.2351   600 → 0.2358   700 → 0.2368   800 → 0.2377
    ///   Kiểm định ghép cặp 400 vs 600: chênh 0.0009, sai số chuẩn 0.0017, t = −0.52.
    ///   Nghĩa là KHÔNG phân biệt được 400 với 600 bằng dữ liệu này. Đáng chú ý: tập huấn
    ///   luyện lại chọn 700 — chính vì thế mới phải tách tập, nếu chọn và báo cáo trên cùng
    ///   một tập thì đã tưởng 700 là câu trả lời.
    ///   Giữ 600 vì trên nửa dữ liệu mới mô hình nghiêng về DÈ DẶT (nói 63.5% thì thực tế
    ///   72.2%), mà sai theo hướng dè dặt an toàn hơn hẳn sai theo hướng tự tin.
    ///
    ///   Yếu tố bản game — Brier trên tập kiểm định, thang 600
    ///     reg 0 → 0.2358   0.15 → 0.2360   0.30 → 0.2362
    ///   Ghép cặp 0 vs 0.30: chênh 0.0004, t = −0.51. Không có tác dụng đo được.
    ///   Thử cả biến thể mạnh hơn — vứt hẳn mọi trận trước 7.41 — và so trên CÙNG 310 dự
    ///   đoán: chỉ 7.41 cho Brier 0.2338, dùng cả lịch sử cho 0.2311. Tức là dữ liệu bản cũ
    ///   KHÔNG những không cần giảm sức nặng, mà bỏ đi còn hơi tệ hơn.
    ///
    ///   Vì sao: Elo vốn đã tự quên. Với K = 24 và hàng trăm ván mỗi đội, ảnh hưởng của một
    ///   trận từ hai năm trước đã bị ghi đè nhiều lần rồi. Một hệ số quên gắn thêm vào chỉ
    ///   lặp lại việc mà mô hình đang làm sẵn.
    ///
    /// Vì vậy PatchRegression = 0: cơ chế có sẵn, đã kiểm thử, và BẬT ĐƯỢC ngay khi dữ liệu
    /// nói khác — nhưng không bật theo cảm tính khi phép đo nói là không cần.
    /// </summary>
    public static class Calibrated
    {
        public const double ProbabilityScale = 600;
        public const double PatchRegression = 0;
    }
}
