namespace Ti2026.Ingest.Analytics;

public readonly record struct TrendReading(
    string Direction, double Change, double Noise, double Ratio, int Points, string Text);

/// <summary>
/// Đọc một chuỗi số theo thời gian và NÓI RA nó đang đi lên hay đi xuống.
///
/// Vì sao cần. Một biểu đồ đường chỉ bày dữ liệu; nó bắt người xem tự nhìn dốc rồi tự kết
/// luận, và hai người nhìn cùng một đường có thể nói hai điều khác nhau. Nhiệm vụ của hệ thống
/// là trả lời, không phải đưa nguyên liệu rồi để người dùng tự nấu.
///
/// Cách làm: khớp đường thẳng bình phương tối thiểu, lấy TỔNG THAY ĐỔI trên toàn khoảng, rồi
/// so nó với chính độ nhiễu của chuỗi (độ lệch chuẩn phần dư). Đây là chỗ quan trọng nhất —
/// một chỉ số dao động mạnh cần dốc lớn hơn hẳn mới được gọi là "đang lên", còn một chỉ số vốn
/// êm thì thay đổi nhỏ đã có nghĩa. So dốc với một ngưỡng cố định sẽ sai ở cả hai đầu.
/// </summary>
public static class TrendVerdict
{
    /// <summary>
    /// Tổng thay đổi phải lớn hơn ngần này lần độ nhiễu mới dám kết luận có xu hướng.
    ///
    /// 1,0 là mức vừa đủ để không gọi tên nhiễu là xu hướng. Đặt thấp hơn thì mọi đội lúc nào
    /// cũng "đang lên" hoặc "đang xuống" và chữ đó mất nghĩa; đặt cao hơn thì bỏ sót đúng
    /// những chuyển biến chậm mà người ta cần biết trước giải.
    /// </summary>
    public const double NoiseMultiple = 1.0;

    /// <summary>Dưới ngần này mốc thì chưa đủ để nói gì — hai điểm luôn tạo ra một đường thẳng.</summary>
    public const int MinPoints = 4;

    /// <param name="notableChange">
    /// Mức thay đổi tối thiểu để ĐÁNG NÓI, tính theo đơn vị của chính chỉ số.
    ///
    /// Cần nó vì "tách được khỏi nhiễu" và "đáng quan tâm" là hai chuyện khác nhau. Một chuỗi
    /// gần như phẳng có độ nhiễu xấp xỉ 0, nên MỌI trôi dạt đều thành nhiều lần độ nhiễu —
    /// đã gặp thật: Elo đổi 0,36 điểm bị gọi là "đang xuống" vì nó gấp 3,79 lần nhiễu. Đúng về
    /// thống kê nhưng vô nghĩa với người đọc.
    ///
    /// Dùng chung ngưỡng với ChangeDetector để cả trang nói cùng một ngôn ngữ về "đáng kể".
    /// </param>
    public static TrendReading Read(
        IReadOnlyList<double> values, string label, double notableChange = 0)
    {
        if (values.Count < MinPoints)
            return new TrendReading("chưa đủ dữ liệu", 0, 0, 0, values.Count,
                $"Mới có {values.Count} mốc — cần ít nhất {MinPoints} mốc mới nói được xu hướng.");

        var n = values.Count;
        var meanX = (n - 1) / 2.0;
        var meanY = values.Average();

        double sxy = 0, sxx = 0;
        for (var i = 0; i < n; i++)
        {
            sxy += (i - meanX) * (values[i] - meanY);
            sxx += (i - meanX) * (i - meanX);
        }

        var slope = sxx == 0 ? 0 : sxy / sxx;
        var change = slope * (n - 1);

        // Độ nhiễu = độ lệch chuẩn của phần dư quanh chính đường xu hướng đó, KHÔNG phải độ
        // lệch chuẩn quanh trung bình. Dùng quanh trung bình thì một xu hướng mạnh và đều lại
        // tự làm mẫu số phồng lên và tự bác bỏ chính nó.
        double ss = 0;
        for (var i = 0; i < n; i++)
        {
            var fitted = meanY + slope * (i - meanX);
            ss += (values[i] - fitted) * (values[i] - fitted);
        }

        var noise = n > 2 ? Math.Sqrt(ss / (n - 2)) : 0;
        var ratio = noise <= 1e-9 ? (Math.Abs(change) > 1e-9 ? double.PositiveInfinity : 0)
                                  : Math.Abs(change) / noise;

        // PHẢI thoả cả hai: tách được khỏi nhiễu VÀ đủ lớn để đáng nói. Thiếu vế thứ hai thì
        // một chuỗi gần như phẳng biến mọi trôi dạt tí hon thành "xu hướng".
        var standsOut = ratio >= NoiseMultiple;
        var bigEnough = Math.Abs(change) >= notableChange;

        var direction = standsOut && bigEnough
            ? change > 0 ? "đang lên" : "đang xuống"
            : "đi ngang";

        var text = direction != "đi ngang"
            ? $"{label} {direction}: {change:+0.0;-0.0} qua {n} mốc, gấp {ratio:0.0} lần mức "
              + $"dao động thường thấy (±{noise:0.0})."
            : !bigEnough && standsOut
                ? $"{label} đi ngang: có nhích {change:+0.0;-0.0} và tách được khỏi nhiễu, nhưng "
                  + $"chưa tới mức {notableChange:0.#} nên chưa đáng gọi là xu hướng."
                : $"{label} đi ngang: thay đổi {change:+0.0;-0.0;0} nhưng chuỗi vốn dao động "
                  + $"±{noise:0.0}, nên chưa tách được khỏi nhiễu.";

        return new TrendReading(
            direction, Math.Round(change, 2), Math.Round(noise, 2), Math.Round(ratio, 2), n, text);
    }
}
