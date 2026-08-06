using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Lọc lịch sử đối đầu theo ĐỘI HÌNH, không theo ngày.
///
/// Bài toán thật: Falcons–Liquid có 71 ván, nhưng 51 ván trong đó Liquid chỉ còn 3/5 người của
/// hôm nay. Gộp cả 71 ván rồi bảo "đối đầu 71 trận" là đem thành tích của một đội khác mang cùng
/// tên ra để đoán trận sắp tới.
/// </summary>
public class LineupContinuityTests
{
    private static H2hGame G(string date, int keptA, int keptB, string? winner = "a") =>
        new(date, "giải", winner, keptA, keptB);

    [Fact]
    public void Du_van_dung_doi_hinh_thi_KHONG_dung_toi_van_doi_hinh_cu()
    {
        // 8 ván đúng đội hình + 40 ván đội hình cũ. Đã đủ ván của chính cặp đấu hôm nay thì ván
        // của đội hình cũ không thêm thông tin, chỉ thêm nhiễu.
        var games = Enumerable.Range(0, 8).Select(i => G($"2026-0{i + 1}-01", 5, 5))
            .Concat(Enumerable.Range(0, 40).Select(i => G("2024-01-01", 5, 3)))
            .ToList();

        var v = LineupContinuity.Read(games, "A", "B", "a");

        v.Basis.Should().Be("dung-doi-hinh");
        v.Games.Should().Be(8);
        v.Ignored.Should().Be(40);
        v.Text.Should().Contain("Đã bỏ 40 ván");
    }

    [Fact]
    public void Thieu_van_dung_doi_hinh_thi_noi_sang_muc_lech_1_nguoi()
    {
        var games = Enumerable.Range(0, 2).Select(_ => G("2026-05-01", 5, 5))
            .Concat(Enumerable.Range(0, 6).Select(_ => G("2026-03-01", 5, 4)))
            .ToList();

        var v = LineupContinuity.Read(games, "A", "B", "a");

        v.Basis.Should().Be("lech-1-nguoi");
        v.Games.Should().Be(8);          // mức nới CHỨA cả ván đúng đội hình
        v.Text.Should().Contain("Chỉ có 2 ván đúng đội hình");
    }

    /// <summary>
    /// Nới bậc nhưng không nhặt thêm ván lệch nào thì nó VẪN là đúng đội hình. Đọc nhãn từ nhánh
    /// đã chọn thay vì từ dữ liệu sẽ gọi tên sai ở đúng ca này.
    /// </summary>
    [Fact]
    public void Noi_bac_ma_khong_co_van_lech_nao_thi_van_la_dung_doi_hinh()
    {
        var v = LineupContinuity.Read([G("2026-05-01", 5, 5), G("2026-05-02", 5, 5)], "A", "B", "a");

        v.Basis.Should().Be("dung-doi-hinh");
        v.Games.Should().Be(2);
    }

    [Fact]
    public void Duoi_hai_nguoi_con_lai_thi_bi_loai_han()
    {
        var v = LineupContinuity.Read(
            [G("2024-01-01", 5, 2), G("2024-01-02", 1, 5), G("2023-01-01", 0, 0)], "A", "B", "a");

        v.Basis.Should().Be("khong-du");
        v.Games.Should().Be(0);
        v.Ignored.Should().Be(3);
        v.Text.Should().Contain("chưa từng gặp nhau với đội hình TI2026");
    }

    /// <summary>
    /// Điểm quan trọng nhất: 12–8 KHÔNG phải là bằng chứng ai trên cơ ai. Không có vế này thì
    /// trang vẫn bắt người đọc tự biết cỡ mẫu bao nhiêu là đủ.
    /// </summary>
    [Fact]
    public void Cach_biet_nho_o_co_mau_nho_thi_KHONG_duoc_ket_luan()
    {
        var games = Enumerable.Range(0, 12).Select(_ => G("2026-05-01", 5, 5, "a"))
            .Concat(Enumerable.Range(0, 8).Select(_ => G("2026-05-01", 5, 5, "b")))
            .ToList();

        var v = LineupContinuity.Read(games, "A", "B", "a");

        v.WinsA.Should().Be(12);
        v.WinsB.Should().Be(8);
        v.Decisive.Should().BeFalse();
        v.Text.Should().Contain("CHƯA vượt được may rủi");
    }

    [Fact]
    public void Cach_biet_lon_thi_moi_duoc_ket_luan()
    {
        var games = Enumerable.Range(0, 18).Select(_ => G("2026-05-01", 5, 5, "a"))
            .Concat(Enumerable.Range(0, 2).Select(_ => G("2026-05-01", 5, 5, "b")))
            .ToList();

        var v = LineupContinuity.Read(games, "A", "B", "a");

        v.Decisive.Should().BeTrue();
        v.Text.Should().Contain("vượt mức giải thích được bằng may rủi");
    }

    [Fact]
    public void Chua_gap_nhau_bao_gio_thi_noi_that_chu_khong_bao_la_doi_hinh_cu()
    {
        var v = LineupContinuity.Read([], "A", "B", "a");

        v.Basis.Should().Be("khong-du");
        v.Text.Should().Contain("chưa từng gặp nhau trong dữ liệu");
        v.Text.Should().NotContain("đội hình TI2026");
    }

    /// <summary>
    /// Trọng số phải ĐƠN ĐIỆU giảm và chạm 0 từ mức 2 người: giữ ván mà quá nửa đội đã khác là
    /// giữ thành tích của người khác.
    /// </summary>
    [Fact]
    public void Trong_so_giam_dan_va_ve_0_tu_muc_hai_nguoi()
    {
        LineupContinuity.Weight(5).Should().Be(1.0);
        LineupContinuity.Weight(4).Should().BeLessThan(LineupContinuity.Weight(5));
        LineupContinuity.Weight(3).Should().BeLessThan(LineupContinuity.Weight(4));
        LineupContinuity.Weight(2).Should().Be(0);
        LineupContinuity.Weight(0).Should().Be(0);

        // Hai bên lệch nhẹ thì cộng dồn thành lệch nặng — nặng hơn một bên đủ, một bên lệch.
        LineupContinuity.GameWeight(4, 4)
            .Should().BeLessThan(LineupContinuity.GameWeight(5, 4));
        LineupContinuity.GameWeight(5, 2).Should().Be(0);
    }

    /// <summary>Ai thắng đọc theo slug, không theo bên Radiant — bên Radiant đổi từng ván.</summary>
    [Fact]
    public void Dem_thang_thua_theo_doi_chu_khong_theo_ben_radiant()
    {
        var v = LineupContinuity.Read(
            [G("2026-05-01", 5, 5, "b"), G("2026-05-02", 5, 5, "b"), G("2026-05-03", 5, 5, "a")],
            "Falcons", "Liquid", "a");

        v.WinsA.Should().Be(1);
        v.WinsB.Should().Be(2);
        v.Text.Should().Contain("Liquid thắng 2–1");
    }
}
