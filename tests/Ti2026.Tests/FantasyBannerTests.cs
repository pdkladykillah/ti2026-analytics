using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Luật banner: mỗi người chỉ ăn điểm ở những chỉ số nằm trên emblem của mình — BA ô ở vòng
/// bảng, mỗi ô một màu cố định theo vị trí. Không phải cộng cả 18 chỉ số.
/// </summary>
public class FantasyBannerTests
{
    private static FantasyStat S(string key, string color) =>
        new(key, key, Per: 1, Points: 1, Base: 0, Color: color);

    private static readonly List<FantasyStat> Stats =
    [
        S("kills", "red"), S("gpm", "red"), S("creeps", "red"),
        S("wards", "blue"), S("stacks", "blue"), S("runes", "blue"),
        S("teamfight", "green"), S("stuns", "green"), S("roshan", "green"),
    ];

    /// <summary>
    /// Bài kiểm quan trọng nhất. Banner hỗ trợ là dương–lá–dương, nên dù người này có chỉ số
    /// ĐỎ cao nhất bảng thì điểm đó cũng KHÔNG được tính — không có ô đỏ nào để đặt.
    /// </summary>
    [Fact]
    public void Chi_so_khong_dung_mau_thi_khong_duoc_tinh_du_cao_den_may()
    {
        var result = FantasyBanner.Build(
            ["blue", "green", "blue"],
            new Dictionary<string, double?>
            {
                ["kills"] = 99999,   // đỏ — không có ô nào nhận
                ["wards"] = 100,
                ["stacks"] = 90,
                ["runes"] = 10,
                ["teamfight"] = 50,
            },
            Stats);

        result.Picks.Should().NotContain(p => p.StatKey == "kills");
        result.BasePoints.Should().Be(240, "100 + 90 (hai ô xanh dương tốt nhất) + 50 (ô xanh lá)");
    }

    /// <summary>Hai ô cùng màu phải lấy HAI chỉ số khác nhau, không nhân đôi chỉ số tốt nhất.</summary>
    [Fact]
    public void Hai_o_cung_mau_lay_hai_chi_so_khac_nhau()
    {
        var result = FantasyBanner.Build(
            ["blue", "green", "blue"],
            new Dictionary<string, double?>
            {
                ["wards"] = 100, ["stacks"] = 90, ["runes"] = 80, ["teamfight"] = 50,
            },
            Stats);

        var blue = result.Picks.Where(p => p.Color == "blue").ToList();
        blue.Should().HaveCount(2);
        blue.Select(p => p.StatKey).Should().OnlyHaveUniqueItems();
        blue.Select(p => p.StatKey).Should().BeEquivalentTo(["wards", "stacks"]);
    }

    /// <summary>Ô giữ đúng thứ tự màu của banner, vì trait phụ thuộc vào ô KỀ NHAU.</summary>
    [Fact]
    public void Giu_dung_thu_tu_o_theo_banner()
    {
        var result = FantasyBanner.Build(
            ["red", "blue", "green"],
            new Dictionary<string, double?>
            {
                ["kills"] = 10, ["wards"] = 20, ["teamfight"] = 30,
            },
            Stats);

        result.Picks.Select(p => p.Color).Should().Equal("red", "blue", "green");
        result.Picks.Select(p => p.Slot).Should().Equal(0, 1, 2);
    }

    /// <summary>
    /// Chưa đo được chỉ số nào của một màu thì ô đó để TRỐNG và khai ra, chứ không tính 0.
    /// Tính 0 sẽ biến "chưa nạp dữ liệu" thành "ô này không đáng điểm nào" mà tổng vẫn hợp lệ.
    /// </summary>
    [Fact]
    public void Chua_do_duoc_mau_nao_thi_de_trong_chu_khong_tinh_0()
    {
        var result = FantasyBanner.Build(
            ["blue", "green", "blue"],
            new Dictionary<string, double?>
            {
                ["wards"] = 100,
                ["stacks"] = null,   // chưa đo được
                ["runes"] = null,
                ["teamfight"] = null,
            },
            Stats);

        result.Picks.Should().ContainSingle();
        result.EmptySlots.Should().BeEquivalentTo(["blue", "green"]);
        result.BasePoints.Should().Be(100, "chỉ cộng ô đã đo được");
    }

    /// <summary>
    /// Tier là thứ QUAY TRÚNG chứ không phải thứ chọn được, nên không được gộp sẵn vào một con
    /// số duy nhất — làm thế là biến may mắn thành năng lực. Trả cả sàn lẫn trần.
    /// </summary>
    [Fact]
    public void Tra_ca_san_tier_I_lan_tran_tier_V()
    {
        var result = FantasyBanner.Build(
            ["red"],
            new Dictionary<string, double?> { ["kills"] = 1000 },
            Stats);

        result.BasePoints.Should().Be(1000);
        result.TierIPoints.Should().Be(1100, "tier I là +10%");
        result.TierVPoints.Should().Be(2500, "tier V là +150%");
    }

    [Fact]
    public void Vi_tri_ve_dung_nhom_banner()
    {
        FantasyBanner.GroupOf(1).Should().Be("core");
        FantasyBanner.GroupOf(3).Should().Be("core");
        FantasyBanner.GroupOf(2).Should().Be("mid");
        FantasyBanner.GroupOf(4).Should().Be("support");
        FantasyBanner.GroupOf(5).Should().Be("support");
        FantasyBanner.GroupOf(null).Should().BeNull();
    }

    /// <summary>Banner vòng play-off có năm ô — cùng luật, chỉ nhiều ô hơn.</summary>
    [Fact]
    public void Banner_playoff_nam_o_van_chay_dung_luat()
    {
        var result = FantasyBanner.Build(
            ["blue", "green", "blue", "green", "blue"],
            new Dictionary<string, double?>
            {
                ["wards"] = 100, ["stacks"] = 90, ["runes"] = 80,
                ["teamfight"] = 50, ["stuns"] = 40,
            },
            Stats);

        result.Picks.Where(p => p.Color == "blue").Should().HaveCount(3);
        result.Picks.Where(p => p.Color == "green").Should().HaveCount(2);
        result.BasePoints.Should().Be(360);
    }
}
