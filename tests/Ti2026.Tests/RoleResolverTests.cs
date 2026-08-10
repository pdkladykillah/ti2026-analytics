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

    // ---------- Hiệu chuẩn ----------

    /// <summary>
    /// Một cách phân loại không kèm độ chính xác thì không phân biệt được với phỏng đoán, nên
    /// phải đo được và phải đo bằng chính ván có nhãn thật.
    /// </summary>
    [Fact]
    public void Do_duoc_do_chinh_xac_cua_buoc_suy_luan()
    {
        var labelled = new (int?, int?)[]
        {
            (1, 1),   // pos1, suy ra core -> đúng
            (1, 5),   // pos5, suy ra support -> đúng
            (3, 4),   // pos4, suy ra support -> đúng
            (2, 2),   // pos2, suy ra core -> đúng
            (3, 1),   // pos3, suy ra core -> đúng
        };

        var (n, ok) = RoleResolver.Calibrate(labelled);
        n.Should().Be(5);
        ok.Should().Be(5);
    }

    [Fact]
    public void Van_thieu_nhan_hoac_thieu_hang_thi_khong_tinh_vao_hieu_chuan()
    {
        var (n, _) = RoleResolver.Calibrate([(null, 2), (1, null), (0, 3)]);
        n.Should().Be(0);
    }
}
