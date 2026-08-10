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
            PlayerInsights.NotableSet([(games, wins)])[0]);

    /// <summary>Chấm một hero khi nó nằm giữa <paramref name="filler"/> hero chơi đúng mức 50%.</summary>
    private static bool InPool(int games, int wins, int filler)
    {
        var pool = new List<(int, int)> { (games, wins) };
        for (var i = 0; i < filler; i++) pool.Add((100, 50));

        return PlayerInsights.NotableSet(pool)[0];
    }

    // ---------- So sánh bội trên hero pool ----------

    /// <summary>
    /// Thắng 7/10 nghe rất ấn tượng, nhưng trong một pool hàng chục hero thì chuyện đó xảy ra
    /// thường xuyên chỉ do may rủi. Không hiệu chỉnh thì trang khen nhầm hàng loạt hero.
    /// </summary>
    [Fact]
    public void Thang_7_tren_10_van_KHONG_du_de_goi_la_hero_manh()
    {
        InPool(10, 7, filler: 40).Should().BeFalse();
        InPool(12, 9, filler: 40).Should().BeFalse();
    }

    [Fact]
    public void Cach_biet_du_lon_va_du_van_thi_moi_dang_ke()
    {
        InPool(40, 32, filler: 40).Should().BeTrue();
        InPool(60, 15, filler: 40).Should().BeTrue("thua nhiều cũng là tín hiệu");
    }

    [Fact]
    public void Duoi_nguong_so_van_thi_khong_bao_gio_dang_ke()
    {
        InPool(PlayerInsights.MinGamesPerHero - 1, 7, filler: 5).Should().BeFalse();
    }

    [Fact]
    public void Pool_cang_rong_thi_nguong_cang_chat()
    {
        InPool(20, 16, filler: 0).Should().BeTrue();
        InPool(20, 16, filler: 400).Should().BeFalse();
    }

    /// <summary>
    /// Dữ liệu THẬT của người dùng, rút gọn: 125 hero, hero lệch nhất là Razor 55 thắng/84 ván
    /// (p = 0,006) và Invoker 9/34 (p = 0,009). Nghe thì ấn tượng, nhưng trong 125 phép so hoàn
    /// toàn ngẫu nhiên thì p nhỏ nhất trung bình đã vào khoảng 1/126 ≈ 0,008 — tức những con số
    /// này là chuyện BÌNH THƯỜNG với cực trị của 125 phép so.
    ///
    /// Nên kết luận đúng là KHÔNG hero nào nổi bật, và bài kiểm này khoá lại điều đó. Cám dỗ ở
    /// đây là nới ngưỡng cho tới khi có thứ gì sáng lên — đúng thứ phải không làm.
    /// </summary>
    [Fact]
    public void Pool_that_125_hero_thi_khong_hero_nao_dang_ke_va_the_la_dung()
    {
        var pool = new List<(int, int)> { (84, 55), (34, 9) };
        for (var i = 0; i < 123; i++) pool.Add((40, 20));

        PlayerInsights.NotableSet(pool).Should().OnlyContain(x => x == false);
    }

    /// <summary>
    /// Nhưng khi tín hiệu ĐỦ MẠNH thì vẫn phải bắt được — nếu không thì phép kiểm chỉ là một
    /// cách im lặng cho sang.
    /// </summary>
    [Fact]
    public void Tin_hieu_du_manh_thi_van_bat_duoc_trong_pool_lon()
    {
        var pool = new List<(int, int)> { (300, 200) };
        for (var i = 0; i < 124; i++) pool.Add((40, 20));

        var flags = PlayerInsights.NotableSet(pool);

        flags[0].Should().BeTrue("200/300 là 66,7% — quá xa 50% để giải thích bằng may rủi");
        flags.Skip(1).Should().OnlyContain(x => x == false);
    }

    /// <summary>
    /// Chỉ "tách được khỏi nhiễu" là chưa đủ. Ở 4.000 ván, 52% qua được mọi phép kiểm nhưng
    /// không đáng để trang gọi là hero mạnh.
    /// </summary>
    [Fact]
    public void Mau_rat_lon_ma_chenh_nho_thi_van_khong_gan_nhan()
    {
        InPool(4000, 2080, filler: 40).Should().BeFalse();
    }

    // ---------- Dịch chuyển trên toàn bộ các mặt ----------

    private static SkillComponent C(string key, int median, int? recent) =>
        new(key, key, "nhóm", 500, median, median - 20, median + 20, recent, false);

    /// <summary>
    /// Đo trên dữ liệu thật: cả 10 mặt đều cao hơn ở 50 ván gần nhất, nhưng mặt lệch nhiều nhất
    /// cũng chỉ +13 điểm phân vị — dưới ngưỡng nên KHÔNG mặt nào sinh ra nhận định. Trang sẽ im
    /// lặng trước một tín hiệu rất rõ, chỉ vì mỗi mảnh nhỏ hơn ngưỡng dành cho một mảnh.
    /// </summary>
    [Fact]
    public void Moi_mat_deu_nhich_len_it_mot_thi_van_phai_noi()
    {
        var games = Enumerable.Range(0, 40).Select(i => G(i % 2 == 0, dayOffset: -i)).ToList();

        var comps = new List<SkillComponent>
        {
            C("a", 73, 80), C("b", 77, 84), C("c", 66, 74), C("d", 61, 66), C("e", 67, 69),
            C("f", 52, 65), C("g", 55, 62), C("h", 40, 42), C("i", 53, 61), C("j", 80, 82),
        };

        var r = PlayerInsights.Read(games, [], 50, comps);

        r.Should().NotContain(x => x.Kind == "tien-bo", "không mặt nào lệch đủ 15 điểm");

        var broad = r.Single(x => x.Kind == "dich-chuyen-chung");
        broad.Text.Should().Contain("10/10");
        broad.Tone.Should().Be("good");
    }

    /// <summary>
    /// KHÔNG được nêu xác suất. Mười mặt tương quan không phải mười lần tung đồng xu — kiếm vàng
    /// và ăn lính gần như là một. Con số "1/1024" sẽ mạnh hơn nhiều lần so với dữ liệu đỡ nổi.
    /// </summary>
    [Fact]
    public void Khong_duoc_bien_10_tren_10_thanh_mot_xac_suat()
    {
        var games = Enumerable.Range(0, 40).Select(i => G(true, dayOffset: -i)).ToList();
        var comps = Enumerable.Range(0, 10).Select(i => C($"c{i}", 50, 60)).ToList();

        var text = PlayerInsights.Read(games, [], 50, comps)
            .Single(x => x.Kind == "dich-chuyen-chung").Text;

        text.Should().NotContain("1024");
        text.Should().Contain("tương quan");
        text.Should().Contain("không phải mười tín hiệu độc lập");
    }

    [Fact]
    public void Da_so_mong_manh_thi_khong_ket_luan()
    {
        var games = Enumerable.Range(0, 40).Select(i => G(true, dayOffset: -i)).ToList();

        var comps = new List<SkillComponent>
        {
            C("a", 50, 60), C("b", 50, 60), C("c", 50, 60), C("d", 50, 60), C("e", 50, 60),
            C("f", 50, 40), C("g", 50, 40), C("h", 50, 40), C("i", 50, 60), C("j", 50, 60),
        };

        PlayerInsights.Read(games, [], 50, comps)
            .Should().NotContain(x => x.Kind == "dich-chuyen-chung");
    }

    [Fact]
    public void Chua_du_mat_co_cua_so_gan_day_thi_khong_ket_luan()
    {
        var games = Enumerable.Range(0, 40).Select(i => G(true, dayOffset: -i)).ToList();
        var comps = Enumerable.Range(0, 5).Select(i => C($"c{i}", 50, 70)).ToList();

        PlayerInsights.Read(games, [], 50, comps)
            .Should().NotContain(x => x.Kind == "dich-chuyen-chung");
    }

    // ---------- Vai trò KHÔNG được suy từ mức farm ----------

    /// <summary>
    /// Bản đầu của bộ này có mục "hồ sơ lối chơi": đọc last hit mỗi phút rồi tuyên bố người dùng
    /// là core hay hỗ trợ. Người dùng phản bác — họ chơi offlane và mid nhưng farm ngang carry —
    /// và số đo trên chính tài khoản đó xác nhận: last hit theo lane THẬT là 299 (safe) / 345
    /// (mid) / 282 (off). Ba lane gần như bằng nhau, nên luật cũ xếp nhầm người chơi offlane
    /// giỏi thành carry MỘT CÁCH CÓ HỆ THỐNG — sai lệch đều một chiều, loại khó thấy nhất.
    ///
    /// Bài kiểm này khoá lại điều đã học: dù farm cao tới đâu cũng không được sinh ra kết luận
    /// vai trò nào.
    /// </summary>
    [Fact]
    public void Farm_cao_ngat_van_KHONG_duoc_sinh_ra_ket_luan_vai_tro()
    {
        var games = Enumerable.Range(0, 40)
            .Select(i => G(i % 2 == 0, lastHits: 300, duration: 2400, dayOffset: -i)).ToList();

        var r = PlayerInsights.Read(games, [], 50);

        r.Should().NotContain(x => x.Kind == "muc-farm");
        r.Should().NotContain(x => x.Text.Contains("last hit mỗi phút"),
            "mức farm không nói được vai trò, nên không được dùng làm căn cứ cho câu nào");
    }

    /// <summary>Vai trò chỉ đến từ RoleResolver, và phải khai rõ đâu là nhãn thật đâu là suy luận.</summary>
    [Fact]
    public void Vai_tro_lay_tu_nhan_that_thi_moi_duoc_neu_vi_tri()
    {
        var games = Enumerable.Range(0, 40).Select(i => G(i % 2 == 0, dayOffset: -i)).ToList();

        var roles = new List<RoleSlice>
        {
            new("pos2", "Mid (pos 2)", 120, 70, 58.3, true),
            new("pos3", "Offlane (pos 3)", 80, 40, 50.0, true),
        };

        var text = PlayerInsights.Read(games, [], 50, roles: roles)
            .Single(x => x.Kind == "vai-tro").Text;

        text.Should().Contain("Mid (pos 2)");
        text.Should().Contain("nhãn vị trí thật từ replay");
    }

    [Fact]
    public void Chua_parse_thi_chi_noi_core_ho_tro_va_khai_ro_la_suy_luan()
    {
        var games = Enumerable.Range(0, 40).Select(i => G(i % 2 == 0, dayOffset: -i)).ToList();

        var roles = new List<RoleSlice>
        {
            new("core", "Core (suy từ đội hình)", 900, 470, 52.2, false),
            new("support", "Hỗ trợ (suy từ đội hình)", 300, 150, 50.0, false),
        };

        var text = PlayerInsights.Read(games, [], 50, roles: roles)
            .Single(x => x.Kind == "core-ho-tro").Text;

        text.Should().Contain("KHÔNG");
        text.Should().Contain("tách được mid với offlane");
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
