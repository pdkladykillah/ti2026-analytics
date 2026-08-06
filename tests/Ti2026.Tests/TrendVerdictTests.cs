using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Đọc chuỗi thời gian và NÓI RA nó đang đi lên hay đi xuống.
///
/// Điểm cốt lõi: so tổng thay đổi với chính ĐỘ NHIỄU của chuỗi, không phải với một ngưỡng cố
/// định. Một chỉ số dao động mạnh cần dốc lớn hơn hẳn mới đáng gọi là xu hướng; một chỉ số vốn
/// êm thì thay đổi nhỏ đã có nghĩa. Ngưỡng cố định sai ở cả hai đầu.
/// </summary>
public class TrendVerdictTests
{
    [Fact]
    public void Chuoi_tang_deu_thi_ket_luan_dang_len()
    {
        var r = TrendVerdict.Read([10, 11, 12, 13, 14, 15], "Elo");

        r.Direction.Should().Be("đang lên");
        r.Change.Should().Be(5);
        r.Text.Should().Contain("đang lên");
    }

    [Fact]
    public void Chuoi_giam_deu_thi_ket_luan_dang_xuong()
    {
        TrendVerdict.Read([15, 14, 13, 12, 11, 10], "Elo").Direction.Should().Be("đang xuống");
    }

    /// <summary>
    /// Bài kiểm quan trọng nhất: CÙNG một độ dốc, nhưng chuỗi nhiễu mạnh thì không được gọi là
    /// xu hướng. Đây là chỗ mà một ngưỡng cố định sẽ kết luận sai.
    /// </summary>
    [Fact]
    public void Cung_do_doc_nhung_nhieu_manh_thi_KHONG_goi_la_xu_huong()
    {
        // Cả hai đều tăng tổng cộng 5 qua 6 mốc
        var em = TrendVerdict.Read([10, 11, 12, 13, 14, 15], "Elo");
        var nhieu = TrendVerdict.Read([10, 25, 2, 22, 1, 15], "Elo");

        em.Direction.Should().Be("đang lên");
        nhieu.Direction.Should().Be("đi ngang", "dao động của chính chuỗi nuốt mất độ dốc");
        nhieu.Noise.Should().BeGreaterThan(em.Noise);
    }

    /// <summary>
    /// "Tách được khỏi nhiễu" KHÔNG đủ để gọi là xu hướng — còn phải đủ lớn để đáng nói.
    ///
    /// Đây là lỗi đã xảy ra thật trên máy đang chạy: một đội có Elo đổi 0,36 điểm bị kết luận
    /// "đang xuống" vì chuỗi gần như phẳng nên 0,36 gấp 3,79 lần độ nhiễu. Đúng về thống kê,
    /// vô nghĩa với người đọc.
    /// </summary>
    [Fact]
    public void Tach_khoi_nhieu_nhung_qua_nho_thi_van_la_di_ngang()
    {
        // Tăng đều 0,4 qua 5 mốc — nhiễu gần bằng 0 nên tỷ lệ rất cao
        var values = new double[] { 1500.0, 1500.1, 1500.2, 1500.3, 1500.4 };

        TrendVerdict.Read(values, "Elo").Direction
            .Should().Be("đang lên", "không đặt ngưỡng thì nó vẫn là xu hướng");

        var withFloor = TrendVerdict.Read(values, "Elo", notableChange: 40);
        withFloor.Direction.Should().Be("đi ngang");
        withFloor.Text.Should().Contain("chưa tới mức");
    }

    [Fact]
    public void Du_lon_va_tach_khoi_nhieu_thi_moi_la_xu_huong()
    {
        TrendVerdict.Read([1500, 1520, 1540, 1560, 1580], "Elo", notableChange: 40)
            .Direction.Should().Be("đang lên", "đổi 80 điểm, vượt xa ngưỡng 40");
    }

    [Fact]
    public void Chuoi_phang_thi_di_ngang()
    {
        TrendVerdict.Read([12, 12, 12, 12, 12], "Winrate").Direction.Should().Be("đi ngang");
    }

    /// <summary>
    /// Quá ít mốc thì NÓI THẲNG là chưa đủ, không đoán bừa. Hai điểm luôn tạo ra một đường
    /// thẳng hoàn hảo, và một kết luận rút từ đó chỉ là hình dạng của phép toán.
    /// </summary>
    [Fact]
    public void Qua_it_moc_thi_noi_thang_la_chua_du()
    {
        var r = TrendVerdict.Read([10, 20], "Elo");

        r.Direction.Should().Be("chưa đủ dữ liệu");
        r.Text.Should().Contain("cần ít nhất");
    }

    [Fact]
    public void Chuoi_rong_khong_lam_do_vo()
    {
        TrendVerdict.Read([], "Elo").Direction.Should().Be("chưa đủ dữ liệu");
    }

    /// <summary>
    /// Độ nhiễu đo quanh chính ĐƯỜNG XU HƯỚNG, không phải quanh trung bình. Nếu đo quanh trung
    /// bình thì một xu hướng mạnh và đều sẽ tự làm mẫu số phồng lên rồi tự bác bỏ chính nó.
    /// </summary>
    [Fact]
    public void Xu_huong_manh_va_deu_khong_tu_bac_bo_chinh_no()
    {
        var r = TrendVerdict.Read([0, 100, 200, 300, 400, 500], "Elo");

        r.Direction.Should().Be("đang lên");
        r.Noise.Should().BeApproximately(0, 0.01, "khớp hoàn hảo thì phần dư bằng 0");
    }
}
