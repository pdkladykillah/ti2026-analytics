using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Xác định vai trò một ván, và nói rõ kết luận đó chắc tới đâu.
///
/// Bài toán thật: người dùng chơi offlane nhưng farm ngang carry. Đo trên tài khoản của họ,
/// last hits theo lane thật là 299 (safe) / 345 (mid) / 282 (off) — gần như bằng nhau. Nên mọi
/// bài kiểm dưới đây xoay quanh một điều: KHÔNG được suy vai trò từ mức farm.
/// </summary>
public class RoleResolverTests
{
    // ---------- Nhãn thật từ replay ----------

    [Fact]
    public void Lane_mid_thi_luon_la_pos2_bat_ke_farm_the_nao()
    {
        foreach (var rank in new int?[] { 1, 2, 3, 4, 5 })
            RoleResolver.Resolve(laneRole: 2, teamFarmRank: rank).Code.Should().Be("pos2");
    }

    /// <summary>
    /// Safelane có CẢ pos1 lẫn pos5, offlane có cả pos3 lẫn pos4. Chỉ mình lane_role không ra
    /// được vị trí — phải kết hợp thứ hạng farm trong đội.
    /// </summary>
    [Fact]
    public void Cung_mot_lane_nhung_khac_muc_farm_thi_khac_vi_tri()
    {
        RoleResolver.Resolve(1, 1).Code.Should().Be("pos1");
        RoleResolver.Resolve(1, 5).Code.Should().Be("pos5");
        RoleResolver.Resolve(3, 2).Code.Should().Be("pos3");
        RoleResolver.Resolve(3, 4).Code.Should().Be("pos4");
    }

    [Fact]
    public void Nhan_tu_replay_thi_danh_dau_la_chinh_xac()
    {
        var r = RoleResolver.Resolve(3, 1);
        r.IsExact.Should().BeTrue();
        r.Source.Should().Be("replay");
    }

    // ---------- Chưa parse: chỉ dám nói core/support ----------

    /// <summary>
    /// Đây là bài kiểm quan trọng nhất. Chưa có nhãn thật thì TUYỆT ĐỐI không được đoán lane —
    /// đã đo trên 36 ván có nhãn: phân bố hạng XPM của mid/safe/off chồng nhau nặng, nên mọi
    /// phỏng đoán lane từ chỉ số đều là bịa.
    /// </summary>
    [Fact]
    public void Chua_parse_thi_KHONG_bao_gio_doan_ra_lane()
    {
        foreach (var rank in new int?[] { 1, 2, 3, 4, 5 })
        {
            var r = RoleResolver.Resolve(laneRole: null, teamFarmRank: rank);

            r.Code.Should().BeOneOf("core", "support");
            r.Code.Should().NotStartWith("pos");
            r.IsExact.Should().BeFalse();
            r.Source.Should().Be("doi-hinh");
        }
    }

    [Fact]
    public void Chua_parse_van_tach_duoc_core_voi_support()
    {
        RoleResolver.Resolve(null, 1).Code.Should().Be("core");
        RoleResolver.Resolve(null, 3).Code.Should().Be("core");
        RoleResolver.Resolve(null, 4).Code.Should().Be("support");
        RoleResolver.Resolve(null, 5).Code.Should().Be("support");
    }

    /// <summary>
    /// Không có bối cảnh đội thì CHỊU. Cám dỗ ở đây là quay lại đoán bằng mức farm — đúng cái
    /// sai đã loại bỏ.
    /// </summary>
    [Fact]
    public void Khong_co_boi_canh_doi_thi_noi_thang_la_chua_xac_dinh()
    {
        var r = RoleResolver.Resolve(null, null);

        r.Code.Should().Be("khong-biet");
        r.IsExact.Should().BeFalse();
    }

    [Fact]
    public void Lane_rung_hoac_gia_tri_la_thi_khong_nhan_bua()
    {
        RoleResolver.Resolve(4, 2).Code.Should().Be("core", "lane 4 là rừng, không phải một vị trí");
        RoleResolver.Resolve(9, null).Code.Should().Be("khong-biet");
    }

    // ---------- Bảng chéo, KHÔNG phải độ chính xác ----------

    /// <summary>
    /// Bản đầu của bộ này có hàm Calibrate so "core/hỗ trợ suy ra" với một "nhãn thật" mà chính
    /// nó cũng định nghĩa bằng hạng farm — LẶP VÒNG. Nó báo 98,5% trên dữ liệu thật trong khi
    /// thực chất chỉ kiểm được đúng một ca. Bài kiểm này khoá lại điều đã học: chỉ trả bảng chéo
    /// để người đọc tự thấy, không gắn phần trăm cho thứ không đo được.
    /// </summary>
    [Fact]
    public void Chi_tra_bang_cheo_chu_khong_bia_ra_do_chinh_xac()
    {
        var t = RoleResolver.CrossTab([(1, 1), (1, 1), (2, 2), (3, 4), (1, 5)]);

        t[1][1].Should().Be(2, "hai ván hạng farm 1 ở safelane");
        t[2][2].Should().Be(1);
        t[4][3].Should().Be(1);
        t[5][1].Should().Be(1);

        typeof(RoleResolver).GetMethod("Calibrate")
            .Should().BeNull("phép hiệu chuẩn lặp vòng đã bị bỏ, đừng dựng lại");
    }

    [Fact]
    public void Van_thieu_nhan_hoac_thieu_hang_thi_khong_vao_bang()
    {
        RoleResolver.CrossTab([(null, 2), (1, null), (0, 3), (9, 9)]).Should().BeEmpty();
    }

    // ---------- Suy lane từ tiền nghiệm hero ----------
    //
    // Đường này CHỈ bật khi người đọc đã chấp nhận con số ước lượng: đo được 65,0% chính xác
    // so với mốc đoán bừa 46,4%, và sai số có cấu trúc chứ không ngẫu nhiên. Mọi kết quả từ
    // đây phải mang IsExact = false — đó là thứ giữ cho nó không lẫn vào nhãn thật.

    private static RoleResolver.HeroLanePrior Prior(int lane, double share = 0.8, int n = 50) =>
        new(lane, share, n);

    /// <summary>
    /// NHÃN THẬT LUÔN THẮNG. Nếu tiền nghiệm hero đè được lên nhãn replay thì cả hệ thống mất
    /// đúng thứ quý nhất nó có — 528 ván biết chắc — để đổi lấy một phỏng đoán 65%.
    /// </summary>
    [Fact]
    public void Tien_nghiem_khong_bao_gio_de_len_nhan_that()
    {
        // Hero này ở cấp pro gần như luôn đi mid, nhưng replay nói ván đó đi safelane.
        var v = RoleResolver.Resolve(laneRole: 1, teamFarmRank: 1, Prior(2));

        v.Code.Should().Be("pos1");
        v.IsExact.Should().BeTrue();
        v.Source.Should().Be("replay");
    }

    [Fact]
    public void Tien_nghiem_du_manh_thi_suy_ra_vi_tri_nhung_phai_khai_la_uoc_luong()
    {
        var v = RoleResolver.Resolve(laneRole: null, teamFarmRank: 2, Prior(2));

        v.Code.Should().Be("pos2");
        v.IsExact.Should().BeFalse("65% thì không được đứng chung hàng với nhãn replay");
        v.Source.Should().Be("hero");
        v.Label.Should().Contain("ước lượng");
    }

    /// <summary>
    /// Hai toạ độ, chỉ một cái là phỏng đoán. Lane suy từ hero, còn core/hỗ trợ vẫn là hạng
    /// net worth THẬT của chính ván đó — nên hero đi safelane cộng hạng 5 phải ra pos5, không
    /// phải pos1.
    /// </summary>
    [Fact]
    public void Hang_net_worth_that_van_quyet_dinh_nua_con_lai()
    {
        RoleResolver.Resolve(null, 1, Prior(1)).Code.Should().Be("pos1");
        RoleResolver.Resolve(null, 5, Prior(1)).Code.Should().Be("pos5");
        RoleResolver.Resolve(null, 2, Prior(3)).Code.Should().Be("pos3");
        RoleResolver.Resolve(null, 4, Prior(3)).Code.Should().Be("pos4");
    }

    /// <summary>
    /// Hero quá ít mẫu thì tiền nghiệm là nhiễu, phải rơi về core/hỗ trợ. Một hero có 3 ván pro
    /// mà "100% đi mid" thì con số 100% đó không mang thông tin nào.
    /// </summary>
    [Fact]
    public void Hero_qua_it_mau_thi_khong_dam_suy()
    {
        var v = RoleResolver.Resolve(null, 2, Prior(2, share: 1.0, n: RoleResolver.MinPriorSamples - 1));

        v.Code.Should().Be("core");
        v.Source.Should().Be("doi-hinh");
    }

    /// <summary>
    /// Không có hạng net worth thì KHÔNG suy gì cả, kể cả khi tiền nghiệm rất mạnh: thiếu một
    /// nửa toạ độ thì nửa kia cũng vô dụng, và đoán cả hai chính là cái sai đã loại từ đầu.
    /// </summary>
    [Fact]
    public void Thieu_hang_net_worth_thi_khong_suy_du_tien_nghiem_manh()
    {
        RoleResolver.Resolve(null, null, Prior(2, share: 1.0, n: 500)).Code.Should().Be("khong-biet");
    }

    [Fact]
    public void Khong_truyen_tien_nghiem_thi_giu_nguyen_hanh_vi_cu()
    {
        RoleResolver.Resolve(null, 2, null).Code.Should().Be("core");
        RoleResolver.Resolve(null, 5, null).Code.Should().Be("support");
    }

    /// <summary>
    /// NHÃN THẬT VÀ NHÃN ƯỚC LƯỢNG PHẢI Ở HAI DÒNG RIÊNG.
    ///
    /// Đây là một lỗi đã lọt lên trang thật: cả hai đường đều trả mã "pos2", nên chúng rơi
    /// chung một ô và cờ Exact bị ván cuối cùng ghi đè — 249 ván biết chắc biến mất vào 1.853
    /// ván phỏng đoán. Người đọc mất hẳn khả năng phân biệt thứ đo được với thứ đoán được,
    /// mà đó chính là điều kiện duy nhất để việc gộp nhãn chấp nhận được.
    /// </summary>
    [Fact]
    public void Nhan_that_va_nhan_uoc_luong_khong_gop_chung_o()
    {
        var priors = new Dictionary<int, RoleResolver.HeroLanePrior> { [7] = new(2, 0.9, 60) };

        var games = new List<RoleGame>();
        for (var i = 0; i < 30; i++)                       // nhãn thật, đi mid
            games.Add(new RoleGame(DateTime.UtcNow, true, 2, 2, 7));
        for (var i = 0; i < 40; i++)                       // chưa nhãn, hero tiền nghiệm mid
            games.Add(new RoleGame(DateTime.UtcNow, false, null, 2, 7));

        var slices = RoleBreakdown.Slices(games, priors);

        var real = slices.Single(s => s.Exact);
        var guess = slices.Single(s => !s.Exact);

        real.Games.Should().Be(30);
        guess.Games.Should().Be(40);

        // Và tỷ lệ thắng phải tách được: 100% ở nhãn thật, 0% ở ước lượng. Gộp chung thì cả
        // hai thành 43% và không con số nào còn đúng.
        real.Winrate.Should().Be(100);
        guess.Winrate.Should().Be(0);
    }
}
