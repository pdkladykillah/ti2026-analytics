using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Hero pool đặt cạnh meta. Cả bộ này xoay quanh một điều: mốc so phải là mức chung CỦA CHÍNH
/// HERO ĐÓ, không phải 50%.
/// </summary>
public class HeroMetaGapTests
{
    // ---------- Mốc so là hero đó, không phải 50% ----------

    /// <summary>
    /// Huskar thắng 53% trên toàn bộ người chơi bậc cao. Thắng 53% với Huskar là ĐÚNG BẰNG mọi
    /// người, không phải điểm mạnh — nhưng mốc 50% sẽ khen.
    /// </summary>
    [Fact]
    public void Hero_manh_san_ma_ban_chi_bang_muc_chung_thi_khong_dang_khen()
    {
        var pool = new[] { (1, "Huskar", 400, 212) };   // 53,0%
        var meta = new Dictionary<int, double> { [1] = 53.0 };

        var row = HeroMetaGap.Read(pool, meta).Single();

        row.Winrate.Should().BeApproximately(53, 0.1);
        row.Edge.Should().BeApproximately(0, 0.1);
        row.Notable.Should().BeFalse();
    }

    /// <summary>
    /// Broodmother thắng 44% ở mức chung. Thắng 50% với hero đó là hơn hẳn — nhưng mốc 50% sẽ
    /// coi đó là tầm thường, tức khen ngược đúng chiều so với ca trên.
    /// </summary>
    [Fact]
    public void Hero_yeu_san_ma_ban_thang_ngang_50_thi_la_diem_manh_that()
    {
        var pool = new[] { (2, "Broodmother", 400, 200) };   // 50,0%
        var meta = new Dictionary<int, double> { [2] = 44.0 };

        var row = HeroMetaGap.Read(pool, meta).Single();

        row.Edge.Should().BeApproximately(6, 0.1);
        row.Notable.Should().BeTrue();
    }

    // ---------- Tràn số âm thầm ----------

    /// <summary>
    /// Bài kiểm quan trọng nhất. Cách viết thẳng — bắt đầu từ (1−p)ⁿ rồi nhân dần — tràn xuống 0
    /// khi n vài trăm, và tổng bằng 0 thì luôn "đáng nói". Tức là ở đúng những hero chơi nhiều
    /// nhất, nơi có nhiều dữ liệu nhất, hàm sẽ tuyên bố mọi thứ đều có ý nghĩa.
    /// </summary>
    [Fact]
    public void Mau_rat_lon_ma_khong_lech_gi_thi_xac_suat_phai_gan_1_chu_khong_phai_0()
    {
        HeroMetaGap.TwoSidedTail(2000, 1000, 0.5).Should().BeGreaterThan(0.9);
        HeroMetaGap.TwoSidedTail(2000, 1000, 0.5).Should().BeLessThanOrEqualTo(1.0);

        var pool = new[] { (1, "Hero to", 2000, 1000) };
        var meta = new Dictionary<int, double> { [1] = 50.0 };

        HeroMetaGap.Read(pool, meta).Single().Notable.Should().BeFalse();
    }

    [Fact]
    public void Xac_suat_hai_phia_khop_voi_gia_tri_da_biet()
    {
        // 10 lần tung, 8 mặt ngửa, p = 0,5 → hai phía = 0,109375.
        HeroMetaGap.TwoSidedTail(10, 8, 0.5).Should().BeApproximately(0.109375, 1e-6);

        // Lệch hẳn thì phải rất nhỏ. Giá trị đúng là 1,507e−8 (đối chiếu bằng phép tính độc lập).
        HeroMetaGap.TwoSidedTail(200, 140, 0.5).Should().BeApproximately(1.507e-8, 1e-11);

        // Đúng bằng kỳ vọng thì gần 1.
        HeroMetaGap.TwoSidedTail(100, 50, 0.5).Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void Truong_hop_bien_khong_no()
    {
        HeroMetaGap.TwoSidedTail(0, 0, 0.5).Should().Be(1);
        HeroMetaGap.TwoSidedTail(10, 0, 0.5).Should().BeGreaterThan(0);
        HeroMetaGap.TwoSidedTail(10, 10, 1.0).Should().Be(1);
        HeroMetaGap.TwoSidedTail(10, 3, 0.0).Should().Be(0);
    }

    // ---------- So sánh bội và ngưỡng dữ liệu ----------

    [Fact]
    public void Pool_cang_rong_thi_nguong_cang_chat()
    {
        // 65/100 với mốc 50%. Hai phía chính xác = 0,0035: lọt ngưỡng 0,05 của một phép so,
        // nhưng không lọt ngưỡng 0,00125 khi xét cả pool 40 hero.
        (int, string, int, int) star = (1, "Hero chinh", 100, 65);

        var one = HeroMetaGap.Read([star], new Dictionary<int, double> { [1] = 50.0 });

        var many = new List<(int, string, int, int)> { star };
        var meta = new Dictionary<int, double> { [1] = 50.0 };
        for (var i = 2; i <= 40; i++)
        {
            many.Add((i, $"Hero {i}", 100, 50));
            meta[i] = 50.0;
        }

        one.Single().Notable.Should().BeTrue();
        HeroMetaGap.Read(many, meta).Single(h => h.HeroId == 1).Notable.Should().BeFalse(
            "cùng dữ liệu, nhưng đây là cực trị của 40 phép so");
    }

    [Fact]
    public void Qua_it_van_hoac_thieu_moc_meta_thi_khong_vao_bang()
    {
        var pool = new[]
        {
            (1, "It van", HeroMetaGap.MinGames - 1, 9),
            (2, "Khong co moc", 200, 120),
        };

        HeroMetaGap.Read(pool, new Dictionary<int, double> { [1] = 50.0 }).Should().BeEmpty();
    }

    [Fact]
    public void Chenh_qua_nho_thi_khong_dang_noi_du_mau_rat_lon()
    {
        var pool = new[] { (1, "Hero", 20000, 10300) };   // 51,5% so với 50%
        var meta = new Dictionary<int, double> { [1] = 50.0 };

        var row = HeroMetaGap.Read(pool, meta).Single();

        row.Edge.Should().BeApproximately(1.5, 0.1);
        row.Notable.Should().BeFalse("có ý nghĩa thống kê nhưng không có ý nghĩa với người đọc");
    }

    // ---------- Hero mạnh chưa đụng tới ----------

    [Fact]
    public void Neu_ra_hero_manh_ma_pool_chua_dung_toi()
    {
        var played = new Dictionary<int, int> { [1] = 300, [2] = 4 };
        var meta = new Dictionary<int, double> { [1] = 56.0, [2] = 55.0, [3] = 54.0, [4] = 40.0 };

        var picks = HeroMetaGap.Untouched(played, meta, take: 2);

        picks.Select(p => p.HeroId).Should().Equal([2, 3],
            "hero 1 đã chơi 300 ván nên không còn là hero chưa đụng tới");
        picks.Should().NotContain(p => p.HeroId == 4);
    }
}
