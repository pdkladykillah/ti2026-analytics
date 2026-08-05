using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Luật đội hình TI2026: MỘT cặp core (carry + offlane) CÙNG một đội, MỘT cặp hỗ trợ (số 4 +
/// số 5) CÙNG một đội, một mid tự do.
///
/// "Cùng đội" là RÀNG BUỘC chứ không phải điểm thưởng. Bản trước lấy hai core điểm cao nhất
/// bất kể đội nào — kết quả không phải "chưa tối ưu" mà là KHÔNG THỂ ĐĂNG KÝ, và vì nó luôn
/// cho tổng cao hơn đáp án hợp lệ nên con số sai lại trông thuyết phục hơn con số đúng.
/// </summary>
public class FantasyLineupTests
{
    private static LineupCandidate P(int id, int pos, int team, double avg) =>
        new(id, $"P{id}", pos, team, $"Team{team}", avg, Matches: 10);

    /// <summary>
    /// Bài kiểm quyết định. Hai carry/offlane điểm cao nhất nằm ở HAI đội khác nhau; đáp án
    /// đúng phải bỏ qua cả hai để lấy một cặp cùng đội có tổng thấp hơn.
    /// </summary>
    [Fact]
    public void Cap_core_phai_cung_mot_doi_du_co_nguoi_le_diem_cao_hon()
    {
        var result = FantasyLineup.Build([
            P(1, FantasyLineup.Carry,   team: 1, avg: 100),  // carry hay nhất, đội 1
            P(2, FantasyLineup.Offlane, team: 2, avg: 100),  // offlane hay nhất, đội 2
            P(3, FantasyLineup.Carry,   team: 3, avg: 60),
            P(4, FantasyLineup.Offlane, team: 3, avg: 60),   // đội 3 có ĐỦ CẶP
        ]);

        var core = result.Picks.Where(x => x.Slot == "core").Select(x => x.Player).ToList();

        core.Should().HaveCount(2);
        core.Select(c => c.TeamId).Distinct().Should().ContainSingle("cặp core phải cùng một đội");
        core.Select(c => c.PlayerId).Should().BeEquivalentTo([3, 4]);
        result.Total.Should().Be(120, "không được lấy 100 + 100 = 200 vì cặp đó không hợp lệ");
    }

    [Fact]
    public void Cap_ho_tro_cung_phai_cung_mot_doi()
    {
        var result = FantasyLineup.Build([
            P(1, FantasyLineup.Support4, team: 1, avg: 90),
            P(2, FantasyLineup.Support5, team: 2, avg: 90),
            P(3, FantasyLineup.Support4, team: 3, avg: 50),
            P(4, FantasyLineup.Support5, team: 3, avg: 50),
        ]);

        var sup = result.Picks.Where(x => x.Slot == "support").Select(x => x.Player).ToList();
        sup.Select(c => c.TeamId).Distinct().Should().ContainSingle();
        result.Total.Should().Be(100);
    }

    /// <summary>Mid là suất tự do — không bị ràng buộc đội, nên phải lấy đúng người cao nhất.</summary>
    [Fact]
    public void Mid_khong_bi_rang_buoc_doi()
    {
        var result = FantasyLineup.Build([
            P(1, FantasyLineup.Carry,   team: 1, avg: 10),
            P(2, FantasyLineup.Offlane, team: 1, avg: 10),
            P(3, FantasyLineup.Mid,     team: 9, avg: 999),
        ]);

        var mid = result.Picks.Single(x => x.Slot == "mid").Player;
        mid.PlayerId.Should().Be(3, "mid được chọn tự do, không cần cùng đội với ai");
    }

    /// <summary>
    /// Cặp core và cặp hỗ trợ KHÔNG buộc phải khác đội nhau — lấy bốn người của cùng một đội
    /// là hợp lệ. Tự thêm ràng buộc không có trong luật cũng sai như bỏ sót ràng buộc có thật.
    /// </summary>
    [Fact]
    public void Hai_cap_duoc_phep_cung_mot_doi()
    {
        var result = FantasyLineup.Build([
            P(1, FantasyLineup.Carry,    team: 1, avg: 100),
            P(2, FantasyLineup.Offlane,  team: 1, avg: 100),
            P(3, FantasyLineup.Support4, team: 1, avg: 100),
            P(4, FantasyLineup.Support5, team: 1, avg: 100),
            P(5, FantasyLineup.Support4, team: 2, avg: 10),
            P(6, FantasyLineup.Support5, team: 2, avg: 10),
        ]);

        result.Picks.Should().HaveCount(4);
        result.Total.Should().Be(400);
    }

    /// <summary>
    /// Không đội nào ghép được cặp thì phải NÓI RA và bỏ trống suất, chứ không được lặng lẽ
    /// lấy hai người lẻ cho đủ mặt. Một đội hình không hợp lệ mà không có cảnh báo còn tệ hơn
    /// một đội hình thiếu người.
    /// </summary>
    [Fact]
    public void Khong_ghep_duoc_cap_thi_khai_ra_chu_khong_lay_bua()
    {
        var result = FantasyLineup.Build([
            P(1, FantasyLineup.Carry,   team: 1, avg: 100),
            P(2, FantasyLineup.Offlane, team: 2, avg: 100),
        ]);

        result.Picks.Where(x => x.Slot == "core").Should().BeEmpty();
        result.Total.Should().Be(0);
        result.Shortfall.Should().HaveCount(3, "thiếu cả cặp core, cặp hỗ trợ lẫn mid");
        result.Shortfall.Should().Contain(s => s.Contains("carry") && s.Contains("offlane"));
    }

    /// <summary>Người chưa có điểm bị loại hẳn: null nghĩa là chưa đo được, không phải 0 điểm.</summary>
    [Fact]
    public void Nguoi_chua_co_diem_khong_duoc_xep_vao_doi_hinh()
    {
        var result = FantasyLineup.Build([
            new(1, "A", FantasyLineup.Carry,   1, "T1", Average: null, Matches: 0),
            new(2, "B", FantasyLineup.Offlane, 1, "T1", Average: null, Matches: 0),
        ]);

        result.Picks.Should().BeEmpty();
    }

    /// <summary>
    /// Người không xác định được đội thì không thể ghép cặp — nhưng vẫn được quyền tranh suất
    /// mid, vì suất đó không cần đội.
    /// </summary>
    [Fact]
    public void Khong_biet_doi_thi_khong_ghep_cap_duoc_nhung_van_lam_mid_duoc()
    {
        var result = FantasyLineup.Build([
            new(1, "A", FantasyLineup.Carry,   TeamId: null, TeamName: null, Average: 100, Matches: 9),
            new(2, "B", FantasyLineup.Offlane, TeamId: null, TeamName: null, Average: 100, Matches: 9),
            new(3, "C", FantasyLineup.Mid,     TeamId: null, TeamName: null, Average: 50, Matches: 9),
        ]);

        result.Picks.Should().ContainSingle();
        result.Picks.Single().Slot.Should().Be("mid");
    }
}
