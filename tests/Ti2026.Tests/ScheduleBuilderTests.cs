using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Ghép LỊCH biên tập với KẾT QUẢ đo được.
///
/// Lịch phải nhập tay vì không nguồn nào có trận sắp diễn ra, nhưng tỷ số thì tuyệt đối không
/// nhập tay — nhập tay là tạo ra hai nguồn sự thật cho cùng một con số, và chúng sẽ lệch nhau
/// đúng lúc giải đang diễn ra. Nên toàn bộ rủi ro dồn vào bước ghép này.
/// </summary>
public class ScheduleBuilderTests
{
    private static readonly DateTime T0 = new(2026, 9, 5, 7, 0, 0, DateTimeKind.Utc);

    private static Fixture Fix(string format = "Bo3", int hourOffset = 0) =>
        new(T0.AddHours(hourOffset), "Vòng bảng", "alpha", "beta", format, "Đội Alpha", "Đội Beta");

    private static PlayedGame Game(int minutes, bool alphaWon, long series = 500, long id = 0) =>
        new(id == 0 ? 1000 + minutes : id, series, T0.AddMinutes(minutes), "alpha", "beta", alphaWon);

    /// <summary>Ván mà alpha ở phe Dire — thắng/thua phải đọc theo ĐỘI, không theo vị trí trong bản ghi.</summary>
    private static PlayedGame Flipped(int minutes, bool alphaWon, long series = 500) =>
        new(2000 + minutes, series, T0.AddMinutes(minutes), "beta", "alpha", !alphaWon);

    private static readonly DateTime Now = T0.AddDays(1);

    [Fact]
    public void Chua_toi_gio_thi_dem_nguoc_chu_khong_bao_thieu_du_lieu()
    {
        var r = ScheduleBuilder.Build(Fix(), [], T0.AddHours(-5));

        r.Status.Should().Be("sap-toi");
        r.Text.Should().Contain("Còn 5 giờ");
    }

    [Fact]
    public void Qua_gio_ma_khong_co_van_nao_thi_noi_that_la_chua_nap_duoc()
    {
        var r = ScheduleBuilder.Build(Fix(), [], Now);

        r.Status.Should().Be("cho-ket-qua");
        r.Text.Should().Contain("chưa có ván nào");
    }

    [Fact]
    public void Bo3_dat_hai_van_thang_thi_ket_thuc()
    {
        var r = ScheduleBuilder.Build(Fix("Bo3"),
            [Game(10, true), Game(60, false), Game(120, true)], Now);

        r.Status.Should().Be("da-xong");
        r.WinsA.Should().Be(2);
        r.WinsB.Should().Be(1);
        r.Text.Should().Contain("Đội Alpha thắng 2–1", "câu kết quả phải đọc tên đội, không phải slug");
    }

    [Fact]
    public void Bo3_moi_da_mot_van_thi_la_dang_dien_ra()
    {
        var r = ScheduleBuilder.Build(Fix("Bo3"), [Game(10, true)], T0.AddHours(1));

        r.Status.Should().Be("dang-dien-ra");
        r.Text.Should().Contain("1–0 sau 1 ván");
    }

    /// <summary>
    /// Bo2 KHÔNG có mốc thắng: hoà 1–1 là kết quả hợp lệ. Gán mốc 2 cho nó thì mọi loạt hoà sẽ
    /// mãi mãi hiện là "đang diễn ra".
    /// </summary>
    [Fact]
    public void Bo2_hoa_1_1_van_la_da_xong()
    {
        ScheduleBuilder.TargetWins("Bo2").Should().BeNull();

        var r = ScheduleBuilder.Build(Fix("Bo2"), [Game(10, true), Game(60, false)], Now);

        r.Status.Should().Be("da-xong");
        r.Text.Should().Contain("Hoà 1–1");
    }

    /// <summary>Thắng/thua đọc theo ĐỘI. Bên Radiant đổi từng ván nên đọc theo vị trí là sai.</summary>
    [Fact]
    public void Doc_thang_thua_theo_doi_chu_khong_theo_ben()
    {
        var r = ScheduleBuilder.Build(Fix("Bo3"),
            [Game(10, alphaWon: true), Flipped(60, alphaWon: true)], Now);

        r.WinsA.Should().Be(2);
        r.WinsB.Should().Be(0);
        r.Status.Should().Be("da-xong");
    }

    /// <summary>
    /// Hai loạt của CÙNG hai đội trong một ngày (vòng bảng rồi playoff) không được trộn thành
    /// một tỷ số. Chốt theo SeriesId của ván sớm nhất trong cửa sổ.
    /// </summary>
    [Fact]
    public void Hai_loat_cung_cap_doi_trong_mot_ngay_khong_bi_tron_lam_mot()
    {
        var games = new List<PlayedGame>
        {
            Game(10, true, series: 500), Game(50, true, series: 500),
            Game(300, false, series: 700), Game(340, false, series: 700),
        };

        var som = ScheduleBuilder.Build(Fix("Bo3"), games, Now);
        som.GamesPlayed.Should().Be(2, "chỉ lấy loạt 500");
        som.WinsA.Should().Be(2);

        var muon = ScheduleBuilder.Build(Fix("Bo3", hourOffset: 5), games, Now);
        muon.GamesPlayed.Should().Be(2, "chỉ lấy loạt 700");
        muon.WinsB.Should().Be(2);
    }

    [Fact]
    public void Van_ngoai_cua_so_thoi_gian_thi_khong_tinh_vao_cap_dau_nay()
    {
        var xa = new PlayedGame(9999, 900, T0.AddHours(30), "alpha", "beta", true);
        ScheduleBuilder.Build(Fix("Bo3"), [xa], Now).GamesPlayed.Should().Be(0);
    }

    [Fact]
    public void Van_cua_cap_doi_khac_thi_khong_lot_vao()
    {
        var khac = new PlayedGame(8888, 800, T0.AddMinutes(20), "alpha", "gamma", true);
        ScheduleBuilder.Build(Fix("Bo3"), [khac], Now).GamesPlayed.Should().Be(0);
    }

    // ---------- Ván không khớp lịch ----------

    /// <summary>
    /// Khi lịch chưa nhập, đây là TOÀN BỘ nội dung của trang. Bỏ đi thì tab trống trơn trong
    /// lúc dữ liệu đang nằm sẵn trong DB.
    /// </summary>
    [Fact]
    public void Van_khong_khop_lich_nao_thi_van_phai_tra_ve()
    {
        var games = new List<PlayedGame> { Game(10, true), Game(60, false) };

        ScheduleBuilder.Unscheduled([], games).Should().HaveCount(2);
    }

    [Fact]
    public void Van_da_khop_lich_thi_khong_lap_lai_o_phan_ngoai_lich()
    {
        var games = new List<PlayedGame>
        {
            Game(10, true), Game(60, false),
            new(7777, 900, T0.AddDays(3), "alpha", "beta", true),
        };

        var loose = ScheduleBuilder.Unscheduled([Fix("Bo3")], games);

        loose.Should().ContainSingle();
        loose[0].MatchId.Should().Be(7777);
    }
}
