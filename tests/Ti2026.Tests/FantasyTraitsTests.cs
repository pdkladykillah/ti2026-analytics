using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Trait emblem. Khác với tier, trait làm các ô ẢNH HƯỞNG LẪN NHAU — benevolent và vampiric
/// tác động sang ô kề bên — nên không tính từng ô độc lập được nữa, và thứ tự ô có ý nghĩa.
///
/// Ngữ nghĩa lấy đúng bản luật gốc, gồm cả hai chỗ dễ hiểu nhầm nhất: benevolent KHÔNG tự cộng
/// cho mình, và unique tự vô hiệu hoá khi có cái thứ hai trên cùng banner.
/// </summary>
public class FantasyTraitsTests
{
    private static readonly Dictionary<string, double?> Points = new()
    {
        ["a"] = 1000, ["b"] = 1000, ["c"] = 1000,
    };

    private static Emblem E(string stat, string tier, string trait) => new(stat, tier, trait);

    [Fact]
    public void Tier_cong_dung_phan_tram()
    {
        FantasyTraits.Total([E("a", "I", "none")], Points).Should().Be(1100);
        FantasyTraits.Total([E("a", "III", "none")], Points).Should().Be(1600);
        FantasyTraits.Total([E("a", "V", "none")], Points).Should().Be(2500);
    }

    /// <summary>Tier lạ hoặc bỏ trống về I — mức thấp nhất, không phải mức "không có tier".</summary>
    [Fact]
    public void Tier_la_thi_ve_muc_thap_nhat()
    {
        FantasyTraits.NormalizeTier(null).Should().Be("I");
        FantasyTraits.NormalizeTier("").Should().Be("I");
        FantasyTraits.NormalizeTier("VII").Should().Be("I");
        FantasyTraits.NormalizeTier("III (Rare)").Should().Be("III", "chỉ lấy từ đầu tiên");
        FantasyTraits.NormalizeTier("v").Should().Be("V");
    }

    /// <summary>
    /// Chỗ hiểu nhầm số một: benevolent cộng cho HÀNG XÓM, không cộng cho chính nó.
    /// </summary>
    [Fact]
    public void Benevolent_khong_tu_cong_cho_minh()
    {
        var f = FantasyTraits.TraitFactors([
            E("a", "I", "benevolent"), E("b", "II", "none"), E("c", "III", "none"),
        ]);

        f[0].Should().Be(1.0, "chính ô benevolent KHÔNG được cộng gì");
        f[1].Should().Be(1.2, "ô kề bên được +20%");
        f[2].Should().Be(1.0, "ô cách hai bậc không phải hàng xóm");
    }

    /// <summary>Vampiric vừa tự cộng vừa RÚT của hàng xóm — nên vị trí đặt nó rất quan trọng.</summary>
    [Fact]
    public void Vampiric_tu_cong_va_rut_cua_hang_xom()
    {
        var f = FantasyTraits.TraitFactors([
            E("a", "I", "none"), E("b", "II", "vampiric"), E("c", "III", "none"),
        ]);

        f[0].Should().Be(0.9);
        f[1].Should().Be(1.5);
        f[2].Should().Be(0.9);
    }

    /// <summary>Vampiric ở ĐẦU banner chỉ rút của một hàng xóm, nên tổng cao hơn khi ở giữa.</summary>
    [Fact]
    public void Vampiric_o_dau_banner_hai_it_hon_o_giua()
    {
        var oGiua = FantasyTraits.Total(
            [E("a", "I", "none"), E("b", "I", "vampiric"), E("c", "I", "none")], Points);

        var oDau = FantasyTraits.Total(
            [E("a", "I", "vampiric"), E("b", "I", "none"), E("c", "I", "none")], Points);

        oDau.Should().BeGreaterThan(oGiua, "ở đầu chỉ rút của một ô thay vì hai");
    }

    [Fact]
    public void Fractal_chi_an_khi_moi_tier_deu_khac_nhau()
    {
        var khacNhau = FantasyTraits.TraitFactors([
            E("a", "I", "fractal"), E("b", "II", "none"), E("c", "III", "none"),
        ]);
        khacNhau[0].Should().Be(1.6);

        var trungTier = FantasyTraits.TraitFactors([
            E("a", "I", "fractal"), E("b", "I", "none"), E("c", "III", "none"),
        ]);
        trungTier[0].Should().Be(1.0, "có hai tier trùng nhau thì fractal không ăn");
    }

    /// <summary>Chỗ hiểu nhầm số hai: hai emblem unique TỰ VÔ HIỆU HOÁ lẫn nhau.</summary>
    [Fact]
    public void Hai_emblem_unique_tu_vo_hieu_hoa_lan_nhau()
    {
        var motCai = FantasyTraits.TraitFactors([
            E("a", "I", "unique"), E("b", "II", "none"), E("c", "III", "none"),
        ]);
        motCai[0].Should().Be(1.3);

        var haiCai = FantasyTraits.TraitFactors([
            E("a", "I", "unique"), E("b", "II", "unique"), E("c", "III", "none"),
        ]);
        haiCai[0].Should().Be(1.0);
        haiCai[1].Should().Be(1.0);
    }

    /// <summary>Friendly cần TỪ BA cái trở lên — hai cái thì không ai được gì.</summary>
    [Fact]
    public void Friendly_can_du_ba_cai_moi_an()
    {
        var haiCai = FantasyTraits.TraitFactors([
            E("a", "I", "friendly"), E("b", "II", "friendly"), E("c", "III", "none"),
        ]);
        haiCai.Should().AllSatisfy(x => x.Should().Be(1.0));

        var baCai = FantasyTraits.TraitFactors([
            E("a", "I", "friendly"), E("b", "II", "friendly"), E("c", "III", "friendly"),
        ]);
        baCai.Should().AllSatisfy(x => x.Should().Be(1.5));
    }

    /// <summary>Trait nhân SAU tier, đúng theo cách diễn đạt của bảng luật.</summary>
    [Fact]
    public void Trait_nhan_sau_tier()
    {
        var score = FantasyTraits.Score([E("a", "V", "vampiric")], Points).Single();

        score.TierBonusPercent.Should().Be(150);
        score.TraitFactor.Should().Be(1.5);
        score.Factor.Should().Be(3.75, "2,5 × 1,5");
        score.Points.Should().Be(3750);
    }

    /// <summary>
    /// Hai benevolent cùng kề một ô thì ô đó ăn +20% HAI LẦN — hệ số nhân dồn, không lấy cái
    /// lớn nhất. Đây là hệ quả trực tiếp của cách bản luật gốc cài đặt.
    /// </summary>
    [Fact]
    public void Hai_benevolent_ke_cung_mot_o_thi_cong_don()
    {
        var f = FantasyTraits.TraitFactors([
            E("a", "I", "benevolent"), E("b", "II", "none"), E("c", "III", "benevolent"),
        ]);

        f[1].Should().BeApproximately(1.44, 1e-9, "1,2 × 1,2");
    }

    /// <summary>
    /// TIER QUAN TRỌNG HƠN TRAIT, và đây là con số chứng minh.
    ///
    /// Khoảng của tier là 1,10 → 2,50, tức rộng 2,27 lần. Khoảng của trait chỉ là 1,0 → 1,6.
    /// Nên "trait tốt nhất mà tier thấp" gần như luôn kém "trait tầm tầm mà tier cao" — đúng
    /// câu hỏi mà người chơi phải quyết mỗi lần quay ra một bộ không như ý.
    /// </summary>
    [Fact]
    public void Tier_cao_voi_trait_thuong_an_trait_tot_nhat_voi_tier_thap()
    {
        // fractal ×1,6 — trait mạnh nhất cho chính ô đó — nhưng ở tier I
        var traitTotTierThap = FantasyTraits.Total(
            [E("a", "I", "fractal"), E("b", "II", "none"), E("c", "III", "none")], Points);

        // không trait gì cả, nhưng tier V
        var khongTraitTierCao = FantasyTraits.Total(
            [E("a", "V", "none"), E("b", "II", "none"), E("c", "III", "none")], Points);

        khongTraitTierCao.Should().BeGreaterThan(traitTotTierThap,
            "tier V trơn (×2,5) ăn tier I + fractal (×1,76)");
    }

    /// <summary>
    /// Xếp hạng lựa chọn THAY THẾ cho một ô, giữ nguyên các ô khác.
    ///
    /// Tier và trait là thứ quay trúng, nên "bộ tốt nhất" không phải lựa chọn có thật. Cái cần
    /// là: không ra được thứ tốt nhất thì thứ nào thay được, và thay thì mất bao nhiêu.
    /// </summary>
    [Fact]
    public void Xep_hang_lua_chon_thay_the_cho_mot_o()
    {
        // Lấy một bộ ở GIỮA thang làm hiện tại, để có cả lựa chọn tốt hơn và kém hơn.
        // Lấy tier I + none thì không có gì kém hơn được, và test sẽ không kiểm được gì.
        var current = new[] { E("a", "IV", "fractal"), E("b", "II", "none"), E("c", "III", "none") };
        var alts = FantasyTraits.Alternatives(current, 0, Points);

        alts.Should().HaveCount(30, "5 tier × 6 trait");
        alts[0].Total.Should().BeGreaterThan(alts[^1].Total, "phải xếp giảm dần");

        // Đúng một dòng là thứ đang có, và nó có delta bằng 0
        var mine = alts.Single(x => x.IsCurrent);
        mine.Tier.Should().Be("IV");
        mine.Trait.Should().Be("fractal");
        mine.Delta.Should().Be(0);

        // Có cả tốt hơn và kém hơn. Vế "kém hơn" mới là thông tin cần khi bản quay chỉ còn
        // lựa chọn tệ: biết mất bao nhiêu để quyết định có nên quay lại hay không.
        alts.Should().Contain(x => x.Delta > 0);
        alts.Should().Contain(x => x.Delta < 0);
    }

    /// <summary>
    /// Xếp hạng phải tính theo TỔNG BANNER, không phải điểm riêng ô đó — vì benevolent chỉ
    /// cộng cho hàng xóm. Tính riêng ô thì benevolent trông như vô dụng.
    /// </summary>
    [Fact]
    public void Benevolent_chi_co_nghia_khi_tinh_theo_tong_banner()
    {
        var current = new[] { E("a", "I", "none"), E("b", "II", "none"), E("c", "III", "none") };
        var alts = FantasyTraits.Alternatives(current, 0, Points);

        var benevolent = alts.First(x => x.Trait == "benevolent" && x.Tier == "I");
        var none = alts.First(x => x.Trait == "none" && x.Tier == "I");

        benevolent.Total.Should().BeGreaterThan(none.Total,
            "nó không cộng cho chính ô đó nhưng vẫn kéo tổng banner lên qua ô kề bên");
    }

    [Fact]
    public void O_khong_ton_tai_thi_tra_rong_chu_khong_no()
    {
        FantasyTraits.Alternatives([E("a", "I", "none")], 5, Points).Should().BeEmpty();
        FantasyTraits.Alternatives([E("a", "I", "none")], -1, Points).Should().BeEmpty();
    }

    /// <summary>Chỉ số chưa đo được thì ô vẫn hiện ra, chỉ là 0 điểm — không được giấu đi.</summary>
    [Fact]
    public void Chi_so_chua_do_duoc_van_hien_o_nhung_0_diem()
    {
        var score = FantasyTraits.Score(
            [E("chuaCo", "V", "none")],
            new Dictionary<string, double?> { ["chuaCo"] = null }).Single();

        score.Points.Should().Be(0);
        score.StatKey.Should().Be("chuaCo");
    }
}
