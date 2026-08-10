using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Tách lịch sử theo vai trò, và theo năm để thấy vai trò dịch chuyển.
///
/// Rủi ro ở đây: phần "dịch chuyển theo năm" chỉ dựng được trên ván có NHÃN THẬT, mà nhãn thật
/// thì rất thưa. Một biểu đồ đầy đặn dựng từ phần suy luận sẽ trông thuyết phục hơn hẳn và hoàn
/// toàn bịa.
/// </summary>
public class RoleBreakdownTests
{
    private static RoleGame G(int year, bool won, int? lane, int? rank) =>
        new(new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc), won, lane, rank);

    // ---------- Tách theo vai trò ----------

    [Fact]
    public void Van_co_nhan_that_thi_tach_ra_vi_tri_va_danh_dau_la_chinh_xac()
    {
        var games = Enumerable.Range(0, 40)
            .Select(i => G(2026, i % 2 == 0, lane: 2, rank: 1)).ToList();

        var mid = RoleBreakdown.Slices(games).Single();

        mid.Code.Should().Be("pos2");
        mid.Games.Should().Be(40);
        mid.Winrate.Should().Be(50);
        mid.Exact.Should().BeTrue();
    }

    [Fact]
    public void Van_chua_parse_thi_chi_ra_core_ho_tro_va_danh_dau_la_suy_luan()
    {
        var games = Enumerable.Range(0, 40)
            .Select(i => G(2026, true, lane: null, rank: i % 2 == 0 ? 1 : 5)).ToList();

        var slices = RoleBreakdown.Slices(games);

        slices.Select(s => s.Code).Should().BeEquivalentTo(["core", "support"]);
        slices.Should().OnlyContain(s => !s.Exact);
        slices.Should().NotContain(s => s.Code.StartsWith("pos"));
    }

    [Fact]
    public void Vai_tro_qua_it_van_thi_khong_dung_rieng_thanh_muc()
    {
        var games = Enumerable.Range(0, 40).Select(i => G(2026, true, 2, 1)).ToList();
        games.AddRange(Enumerable.Range(0, 3).Select(i => G(2026, true, 1, 1)));

        RoleBreakdown.Slices(games).Should().ContainSingle().Which.Code.Should().Be("pos2");
    }

    [Fact]
    public void Van_khong_xac_dinh_duoc_thi_khong_vao_bang()
    {
        var games = Enumerable.Range(0, 40).Select(i => G(2026, true, null, null)).ToList();
        RoleBreakdown.Slices(games).Should().BeEmpty();
    }

    // ---------- Dịch chuyển theo năm ----------

    /// <summary>
    /// Chỉ đếm ván có nhãn thật. Ván suy luận được KHÔNG phân biệt mid với offlane, nên đưa vào
    /// đây sẽ tạo ra tỷ trọng lane từ dữ liệu vốn không chứa thông tin về lane.
    /// </summary>
    [Fact]
    public void Ty_trong_lane_theo_nam_chi_dem_van_co_nhan_that()
    {
        var games = new List<RoleGame>();
        for (var i = 0; i < 12; i++) games.Add(G(2018, true, lane: 2, rank: 1));
        for (var i = 0; i < 200; i++) games.Add(G(2018, true, lane: null, rank: 1));

        var era = RoleBreakdown.Eras(games).Single();

        era.Games.Should().Be(212, "tổng số ván của năm vẫn phải nêu để biết tỷ lệ dựng trên bao nhiêu");
        era.Labelled.Should().Be(12);
        era.Mid.Should().Be(12);
    }

    [Fact]
    public void Nam_khong_co_nhan_nao_van_hien_ra_voi_labelled_bang_khong()
    {
        var games = Enumerable.Range(0, 5).Select(i => G(2015, true, null, 2)).ToList();

        var era = RoleBreakdown.Eras(games).Single();

        era.Labelled.Should().Be(0);
        era.Mid.Should().Be(0);
    }

    /// <summary>
    /// Đây là bài kiểm quan trọng nhất của phần này. So năm 2015 có 4 ván có nhãn với năm 2026
    /// có 300 ván rồi tuyên bố "bạn đã đổi vai trò" là kết luận rút từ 4 điểm dữ liệu.
    /// </summary>
    [Fact]
    public void Nam_qua_mong_thi_khong_duoc_dung_lam_moc_so_sanh()
    {
        var games = new List<RoleGame>();
        for (var i = 0; i < 4; i++) games.Add(G(2015, true, 2, 1));
        for (var i = 0; i < 60; i++) games.Add(G(2026, true, 3, 2));

        RoleBreakdown.Shift(RoleBreakdown.Eras(games)).Should().BeNull();
    }

    [Fact]
    public void Hai_nam_du_day_thi_lay_nam_dau_va_nam_cuoi()
    {
        var games = new List<RoleGame>();
        for (var i = 0; i < 30; i++) games.Add(G(2016, true, 2, 1));
        for (var i = 0; i < 30; i++) games.Add(G(2020, true, 2, 1));
        for (var i = 0; i < 30; i++) games.Add(G(2026, true, 3, 2));

        var shift = RoleBreakdown.Shift(RoleBreakdown.Eras(games));

        shift.Should().NotBeNull();
        shift!.Value.First.Year.Should().Be(2016);
        shift.Value.First.Mid.Should().Be(30);
        shift.Value.Last.Year.Should().Be(2026);
        shift.Value.Last.Mid.Should().Be(0);
        shift.Value.Last.Off.Should().Be(30);
    }
}
