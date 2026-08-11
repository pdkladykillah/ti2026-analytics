using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// "Hero nào khắc chế bạn". Hai cái bẫy: mốc so sai, và so sánh bội trên 127 hero.
/// </summary>
public class NemesisHeroesTests
{
    private static List<FacedHero> Pool(int n, int games, double winPct)
        => Enumerable.Range(1, n)
            .Select(i => new FacedHero(i, $"Hero {i}", games, (int)Math.Round(games * winPct / 100)))
            .ToList();

    /// <summary>
    /// Mốc so là tỷ lệ thắng NỀN CỦA CHÍNH NGƯỜI ĐÓ, không phải 50%. Người thắng 55% mà gặp hero
    /// X chỉ thắng 52% thì hero đó vẫn đang khắc họ — dùng mốc 50% sẽ khen ngược.
    /// </summary>
    [Fact]
    public void Moc_so_la_ty_le_thang_nen_chu_khong_phai_50()
    {
        var faced = new List<FacedHero> { new(1, "X", 400, 208) };   // 52,0%

        var r = NemesisHeroes.Read(faced, baseWinrate: 55.0).Single();

        r.Winrate.Should().Be(52);
        r.Edge.Should().Be(-3, "52 trừ 55, chứ không phải 52 trừ 50");
    }

    [Fact]
    public void Hero_khac_that_thi_bat_duoc()
    {
        var faced = new List<FacedHero> { new(1, "Khac tinh", 500, 200) };  // 40% so với nền 50%

        var r = NemesisHeroes.Read(faced, 50.0).Single();

        r.Edge.Should().Be(-10);
        r.Notable.Should().BeTrue();
    }

    /// <summary>
    /// 127 hero là 127 phép so. Chấm bằng ngưỡng của một phép so duy nhất thì riêng may rủi đã
    /// đủ tạo ra vài "khắc tinh" — đúng lỗi đã mắc ở phần khắc tinh của các đội.
    /// </summary>
    [Fact]
    public void Pool_127_hero_thi_nguong_phai_chat_hon()
    {
        // Một hero lệch vừa phải, 126 hero còn lại đúng mức nền.
        var faced = new List<FacedHero> { new(999, "Hoi lech", 200, 84) };  // 42%
        faced.AddRange(Pool(126, 200, 50));

        var one = NemesisHeroes.Read([faced[0]], 50.0).Single();
        var many = NemesisHeroes.Read(faced, 50.0).Single(x => x.HeroId == 999);

        one.Notable.Should().BeTrue();
        many.Notable.Should().BeFalse("cùng dữ liệu, nhưng là cực trị của 127 phép so");
    }

    [Fact]
    public void It_van_doi_dau_thi_khong_vao_bang()
    {
        var faced = new List<FacedHero>
        {
            new(1, "It gap", NemesisHeroes.MinGames - 1, 5),
            new(2, "Hay gap", 300, 150),
        };

        NemesisHeroes.Read(faced, 50.0).Should().ContainSingle()
            .Which.HeroId.Should().Be(2);
    }

    [Fact]
    public void Chenh_qua_nho_thi_khong_danh_dau_du_mau_rat_lon()
    {
        var faced = new List<FacedHero> { new(1, "X", 20000, 9700) };  // 48,5% so với 50%

        var r = NemesisHeroes.Read(faced, 50.0).Single();

        r.Edge.Should().Be(-1.5);
        r.Notable.Should().BeFalse();
    }

    [Fact]
    public void Sap_hero_khac_nhat_len_dau()
    {
        var faced = new List<FacedHero>
        {
            new(1, "De", 300, 180),   // +10
            new(2, "Kho", 300, 120),  // -10
            new(3, "Thuong", 300, 150),
        };

        NemesisHeroes.Read(faced, 50.0).Select(x => x.HeroId).Should().Equal([2, 3, 1]);
    }

    [Fact]
    public void Rong_thi_khong_no()
    {
        NemesisHeroes.Read([], 50.0).Should().BeEmpty();
        NemesisHeroes.Read([new FacedHero(1, "X", 100, 50)], 0).Should().HaveCount(1);
    }
}
