using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Hiệu chỉnh khi kiểm nhiều giả thuyết cùng lúc — nơi hai loại sai lầm đối nghịch nhau, và
/// tránh cái này là rơi vào cái kia.
/// </summary>
public class MultipleTestsTests
{
    // ---------- Benjamini–Hochberg ----------

    [Fact]
    public void Khong_co_gi_dang_ke_thi_khong_danh_dau_gi()
    {
        var p = Enumerable.Repeat(0.6, 50).ToList();
        MultipleTests.BenjaminiHochberg(p).Should().OnlyContain(x => x == false);
    }

    [Fact]
    public void Mot_p_cuc_nho_giua_bay_nhieu_thi_van_bat_duoc()
    {
        var p = new List<double> { 1e-9 };
        p.AddRange(Enumerable.Repeat(0.7, 99));

        var keep = MultipleTests.BenjaminiHochberg(p);

        keep[0].Should().BeTrue();
        keep.Skip(1).Should().OnlyContain(x => x == false);
    }

    /// <summary>
    /// Điểm dễ cài sai nhất: phải tìm hạng LỚN NHẤT thoả điều kiện, không phải dừng ở hạng đầu
    /// tiên trượt. Ở đây p₍₁₎ = 0,04 trượt ngưỡng 1·0,05/4 = 0,0125, nhưng p₍₃₎ = 0,03 lại thoả
    /// 3·0,05/4 = 0,0375 — nên cả ba phải được nhận.
    /// </summary>
    [Fact]
    public void Lay_hang_lon_nhat_thoa_dieu_kien_chu_khong_dung_o_hang_dau_tien_truot()
    {
        var keep = MultipleTests.BenjaminiHochberg([0.01, 0.02, 0.03, 0.9], 0.05);

        keep.Should().Equal([true, true, true, false]);
    }

    /// <summary>
    /// BH KHÔNG nới lỏng cho tín hiệu đơn độc — đây là hiểu nhầm đã suýt đưa vào mã. Ngưỡng của
    /// hạng 1 trong BH là đúng q/m, bằng y hệt Bonferroni. Nên một p = 0,002 nằm giữa 124 phép
    /// so vô vị thì cả hai phép cùng bác.
    /// </summary>
    [Fact]
    public void BH_khong_noi_long_cho_tin_hieu_don_doc()
    {
        var p = new List<double> { 0.002 };
        p.AddRange(Enumerable.Repeat(0.8, 124));

        MultipleTests.BenjaminiHochberg(p)[0].Should().BeFalse();
        (0.002 < 0.05 / 125).Should().BeFalse("Bonferroni cũng bác — hai phép cùng kết luận");
    }

    /// <summary>
    /// BH chỉ mạnh hơn khi có NHIỀU tín hiệu cùng lúc: hạng sau được ngưỡng rộng dần. Ở đây 20
    /// phép so đều có p = 0,003 — Bonferroni (ngưỡng 0,0004) bác sạch, BH nhận cả 20.
    /// </summary>
    [Fact]
    public void BH_manh_hon_khi_co_nhieu_tin_hieu_cung_luc()
    {
        var p = Enumerable.Repeat(0.003, 20).ToList();
        p.AddRange(Enumerable.Repeat(0.8, 105));

        var keep = MultipleTests.BenjaminiHochberg(p);

        keep.Take(20).Should().OnlyContain(x => x);
        keep.Skip(20).Should().OnlyContain(x => x == false);
        p.Count(x => x < 0.05 / 125).Should().Be(0, "Bonferroni thì bác sạch");
    }

    [Fact]
    public void Giu_dung_thu_tu_dau_vao_du_p_lon_xon()
    {
        var keep = MultipleTests.BenjaminiHochberg([0.9, 1e-8, 0.5, 1e-9]);

        keep.Should().Equal([false, true, false, true]);
    }

    [Fact]
    public void Dau_vao_rong_hoac_hong_thi_khong_no()
    {
        MultipleTests.BenjaminiHochberg([]).Should().BeEmpty();
        MultipleTests.BenjaminiHochberg([double.NaN, 1e-9]).Should().Equal([false, true]);
        MultipleTests.BenjaminiHochberg([-5, 2]).Should().HaveCount(2);
    }

    // ---------- Nhị thức ----------

    [Fact]
    public void Nhi_thuc_hai_phia_khop_voi_gia_tri_da_biet()
    {
        MultipleTests.BinomialTwoSided(10, 8, 0.5).Should().BeApproximately(0.109375, 1e-6);
        MultipleTests.BinomialTwoSided(200, 140, 0.5).Should().BeApproximately(1.507e-8, 1e-11);
        MultipleTests.BinomialTwoSided(100, 50, 0.5).Should().BeGreaterThan(0.9);
    }

    /// <summary>
    /// Tràn số âm thầm: cách viết thẳng cho ra 0 ở n lớn, và 0 thì luôn "đáng kể" — tức ở đúng
    /// những hero có nhiều dữ liệu nhất, hàm sẽ tuyên bố mọi thứ đều có ý nghĩa.
    /// </summary>
    [Fact]
    public void Mau_rat_lon_ma_khong_lech_gi_thi_phai_gan_1()
    {
        MultipleTests.BinomialTwoSided(2000, 1000, 0.5).Should().BeGreaterThan(0.9);
        MultipleTests.BinomialTwoSided(5000, 2500, 0.5).Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void Truong_hop_bien_khong_no()
    {
        MultipleTests.BinomialTwoSided(0, 0, 0.5).Should().Be(1);
        MultipleTests.BinomialTwoSided(10, 99, 0.5).Should().Be(1);
        MultipleTests.BinomialTwoSided(10, 10, 1.0).Should().Be(1);
        MultipleTests.BinomialTwoSided(10, 3, 0.0).Should().Be(0);
    }
}
