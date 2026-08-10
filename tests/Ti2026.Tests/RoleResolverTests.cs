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
}
