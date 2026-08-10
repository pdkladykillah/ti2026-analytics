using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Đọc lịch sử pub của một người và nói ra điểm mạnh, điểm yếu.
///
/// Rủi ro lớn nhất y hệt bộ nhận định đội: nói bừa. Nhưng ở đây nó nặng hơn một bậc, vì một
/// người chơi 40 hero thì việc chọn ra "hero mạnh nhất" là lấy CỰC TRỊ của 40 phép so — chỉ
/// riêng may rủi đã đủ tạo ra vài hero thắng 75%. Phần lớn bài kiểm dưới đây kiểm chiều NGƯỢC
/// LẠI: khi nào thì IM.
/// </summary>
public class PlayerInsightsTests
{
    private static readonly DateTime T0 = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PlayerGame G(
        bool won, int heroId = 1, int lastHits = 250, int gpm = 500, int duration = 2400,
        int? party = 1, bool radiant = true, int dayOffset = 0) =>
        new(heroId, T0.AddDays(dayOffset), duration, won, 8, 5, 10, gpm, lastHits,
            party, 50, radiant);

    private static List<PlayerGame> Many(int n, bool won, params object[] _) =>
        Enumerable.Range(0, n).Select(i => G(won, dayOffset: -i)).ToList();

    private static HeroLine Hero(string name, int games, int wins, double gpm = 500,
        double? proGpm = null, int proGames = 0) =>
        new(1, name, games, wins, wins * 100.0 / games, gpm, 6.0, 3.0, proGpm, proGames,
            PlayerInsights.Notable(games, wins));

    // ---------- So sánh bội trên hero pool ----------

    /// <summary>
    /// Bài kiểm quan trọng nhất. Thắng 7/10 nghe rất ấn tượng, nhưng trong một pool 40 hero thì
    /// chuyện đó xảy ra thường xuyên chỉ do may rủi. Không hiệu chỉnh thì trang sẽ khen nhầm
    /// hàng loạt hero, và mọi lời khen mất giá.
    /// </summary>
    [Fact]
    public void Thang_7_tren_10_van_KHONG_du_de_goi_la_hero_manh()
    {
        PlayerInsights.Notable(games: 10, wins: 7).Should().BeFalse();
        PlayerInsights.Notable(games: 12, wins: 9).Should().BeFalse();
    }

    [Fact]
    public void Cach_biet_du_lon_va_du_van_thi_moi_dang_ke()
    {
        PlayerInsights.Notable(games: 40, wins: 32).Should().BeTrue();
        PlayerInsights.Notable(games: 60, wins: 15).Should().BeTrue("thua nhiều cũng là tín hiệu");
    }

    [Fact]
    public void Duoi_nguong_so_van_thi_khong_bao_gio_dang_ke()
    {
        PlayerInsights.Notable(games: PlayerInsights.MinGamesPerHero - 1, wins: 7)
            .Should().BeFalse();
    }

    [Fact]
    public void Pool_cang_rong_thi_nguong_cang_chat()
    {
        // Cùng một thành tích, xét trong pool lớn hơn thì phải khó được gọi là đáng kể hơn
        var hep = PlayerInsights.Notable(games: 20, wins: 16, poolSize: 1);
        var rong = PlayerInsights.Notable(games: 20, wins: 16, poolSize: 200);

        hep.Should().BeTrue();
        rong.Should().BeFalse();
    }

    // ---------- Vị trí suy từ mức farm ----------

    /// <summary>
    /// Vị trí PHẢI suy từ chỉ số đo được. lane_role chỉ có ở 6% số ván trên tài khoản thật, nên
    /// dựng phân tích vai trò lên nó là dựng trên 6% dữ liệu rồi trình bày như thể đủ.
    /// </summary>
    [Fact]
    public void Farm_cao_thi_ket_luan_la_core()
    {
        var games = Enumerable.Range(0, 40)
            .Select(i => G(i % 2 == 0, lastHits: 300, duration: 2400, dayOffset: -i)).ToList();

        var r = PlayerInsights.Read(games, [], 50);
        var farm = r.Single(x => x.Kind == "muc-farm");

        farm.Text.Should().Contain("core");
        farm.Text.Should().Contain("7.5 last hit mỗi phút");
    }

    [Fact]
    public void Farm_thap_thi_ket_luan_la_ho_tro()
    {
        var games = Enumerable.Range(0, 40)
            .Select(i => G(i % 2 == 0, lastHits: 60, duration: 2400, dayOffset: -i)).ToList();

        PlayerInsights.Read(games, [], 50)
            .Single(x => x.Kind == "muc-farm").Text.Should().Contain("hỗ trợ");
    }

    [Fact]
    public void Qua_it_van_thi_khong_ket_luan_ve_vi_tri()
    {
        var games = Enumerable.Range(0, 10).Select(i => G(true, dayOffset: -i)).ToList();
        PlayerInsights.Read(games, [], 50).Should().NotContain(x => x.Kind == "muc-farm");
    }

    // ---------- Bên sân ----------

    /// <summary>Lệch bên sân ở cỡ mẫu nhỏ vẫn hoàn toàn có thể là ngẫu nhiên.</summary>
    [Fact]
    public void Lech_ben_san_o_co_mau_nho_thi_KHONG_ket_luan()
    {
        var nho = Enumerable.Range(0, 40)
            .Select(i => G(i < 12, radiant: i < 20, dayOffset: -i)).ToList();

        PlayerInsights.Read(nho, [], 50).Should().NotContain(x => x.Kind == "ben-san");
    }

    [Fact]
    public void Lech_ben_san_ro_va_du_van_thi_bao()
    {
        // Radiant 200 ván thắng 120 (60%), Dire 200 ván thắng 80 (40%)
        var games = new List<PlayerGame>();
        for (var i = 0; i < 200; i++) games.Add(G(i < 120, radiant: true, dayOffset: -i));
        for (var i = 0; i < 200; i++) games.Add(G(i < 80, radiant: false, dayOffset: -i));

        var r = PlayerInsights.Read(games, [], 50).Single(x => x.Kind == "ben-san");
        r.Text.Should().Contain("Radiant");
        r.Text.Should().Contain("vượt mức giải thích được bằng may rủi");
    }

    // ---------- Phong độ so với cả đời ----------

    [Fact]
    public void Chenh_lech_nho_so_voi_ca_doi_thi_bao_la_on_dinh()
    {
        var games = Enumerable.Range(0, 300).Select(i => G(i < 151, dayOffset: -i)).ToList();

        var r = PlayerInsights.Read(games, [], 50.0).Single(x => x.Kind == "tong-quan");
        r.Tone.Should().Be("flat");
        r.Text.Should().Contain("ổn định");
    }

    [Fact]
    public void Tut_ro_so_voi_ca_doi_thi_bao_la_that()
    {
        var games = Enumerable.Range(0, 400).Select(i => G(i < 160, dayOffset: -i)).ToList();

        var r = PlayerInsights.Read(games, [], 55.0).Single(x => x.Kind == "tong-quan");
        r.Tone.Should().Be("bad");
        r.Text.Should().Contain("chênh lệch thật");
    }

    // ---------- Hero mạnh / yếu ----------

    [Fact]
    public void Chi_neu_hero_da_vuot_hieu_chinh()
    {
        var games = Many(60, true);
        var heroes = new List<HeroLine>
        {
            Hero("May rủi", 10, 7),        // ấn tượng nhưng chưa vượt ngưỡng
            Hero("Thật sự mạnh", 40, 32),
            Hero("Thật sự yếu", 60, 15),
        };

        var r = PlayerInsights.Read(games, heroes, 50);

        r.Should().Contain(x => x.Kind == "hero-manh" && x.Text.Contains("Thật sự mạnh"));
        r.Should().Contain(x => x.Kind == "hero-yeu" && x.Text.Contains("Thật sự yếu"));
        r.Should().NotContain(x => x.Text.Contains("May rủi"));
    }

    /// <summary>Mốc pro chỉ nêu khi có đủ ván pro — vài ván pro không phải một mốc.</summary>
    [Fact]
    public void Moc_pro_can_du_van_pro_moi_duoc_neu()
    {
        var games = Many(60, true);

        var it = new List<HeroLine> { Hero("A", 40, 20, gpm: 400, proGpm: 700, proGames: 3) };
        PlayerInsights.Read(games, it, 50).Should().NotContain(x => x.Kind == "so-voi-pro");

        var du = new List<HeroLine> { Hero("A", 40, 20, gpm: 400, proGpm: 700, proGames: 25) };
        PlayerInsights.Read(games, du, 50)
            .Single(x => x.Kind == "so-voi-pro").Text.Should().Contain("thấp hơn 300");
    }

    [Fact]
    public void Chenh_GPM_khong_dang_ke_thi_khong_neu()
    {
        var games = Many(60, true);
        var heroes = new List<HeroLine> { Hero("A", 40, 20, gpm: 690, proGpm: 700, proGames: 25) };

        PlayerInsights.Read(games, heroes, 50).Should().NotContain(x => x.Kind == "so-voi-pro");
    }

    [Fact]
    public void Khong_co_van_nao_thi_khong_noi_gi()
    {
        PlayerInsights.Read([], [], 50).Should().BeEmpty();
    }
}
