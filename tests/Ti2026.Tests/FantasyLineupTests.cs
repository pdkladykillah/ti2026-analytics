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

    /// <summary>
    /// Bài kiểm quan trọng nhất của phần danh hiệu: prefix áp cho CẢ NĂM người, nên nó phải
    /// được chọn CÙNG LÚC với đội hình chứ không phải sau.
    ///
    /// Ở đây cặp hỗ trợ của đội 2 kém hơn đội 3 về điểm gốc, nhưng hero pool của họ ăn khớp
    /// với prefix mà ba người kia đang dùng. Chọn tách rời sẽ lấy đội 3 rồi mới đi tìm prefix —
    /// và bỏ mất tổng cao hơn.
    /// </summary>
    [Fact]
    public void Prefix_duoc_chon_CUNG_LUC_voi_doi_hinh_nen_doi_duoc_ca_lua_chon()
    {
        var red = new Dictionary<string, double> { ["crimson"] = 100, ["cerulean"] = 0 };
        var none = new Dictionary<string, double> { ["crimson"] = 0, ["cerulean"] = 0 };

        LineupCandidate C(int id, int pos, int team, double avg, Dictionary<string, double> pct) =>
            new(id, $"P{id}", pos, team, $"T{team}", avg, 10, pct);

        var roster = new[]
        {
            C(1, FantasyLineup.Carry,    1, 1000, red),
            C(2, FantasyLineup.Offlane,  1, 1000, red),
            C(3, FantasyLineup.Mid,      1, 1000, red),

            // Đội 2: kém 100 điểm nhưng toàn hero đỏ
            C(4, FantasyLineup.Support4, 2, 700, red),
            C(5, FantasyLineup.Support5, 2, 700, red),

            // Đội 3: hơn 100 điểm nhưng không hợp prefix nào
            C(6, FantasyLineup.Support4, 3, 750, none),
            C(7, FantasyLineup.Support5, 3, 750, none),
        };

        // Crimson +50%: ba người kia đã ăn 1500 điểm thưởng dù chọn cặp nào, nhưng cặp đội 2
        // ăn thêm 700 nữa — thừa sức bù 100 điểm gốc chênh lệch.
        var bonuses = new Dictionary<string, double> { ["crimson"] = 50, ["cerulean"] = 11 };

        var withTitles = FantasyLineup.Build(roster, bonuses);
        var supports = withTitles.Picks.Where(p => p.Slot == "support").Select(p => p.Player.PlayerId);

        supports.Should().BeEquivalentTo([4, 5], "cặp hợp prefix cho tổng cao hơn dù điểm gốc thấp hơn");
        withTitles.Prefix!.Value.Key.Should().Be("crimson");

        // Không xét danh hiệu thì đúng là chọn ngược lại — đây là bản trước làm sai
        var withoutTitles = FantasyLineup.Build(roster);
        withoutTitles.Picks.Where(p => p.Slot == "support").Select(p => p.Player.PlayerId)
            .Should().BeEquivalentTo([6, 7], "bỏ qua danh hiệu thì chỉ so điểm gốc");
    }

    /// <summary>
    /// Suffix phụ thuộc ĐỘI: đội hay thua thì "the Underdog" ăn nhiều hơn. Nên nó cũng phải
    /// tham gia phép chọn chứ không phải tính sau.
    /// </summary>
    [Fact]
    public void Suffix_cung_tham_gia_phep_chon()
    {
        LineupCandidate C(int id, int pos, int team, double avg, double loseRate) =>
            new(id, $"P{id}", pos, team, $"T{team}", avg, 10, null,
                new Dictionary<string, double> { ["underdog"] = loseRate });

        var roster = new[]
        {
            C(1, FantasyLineup.Carry, 1, 1000, 0), C(2, FantasyLineup.Offlane, 1, 1000, 0),
            C(3, FantasyLineup.Mid, 1, 1000, 0),
            C(4, FantasyLineup.Support4, 2, 700, 100), C(5, FantasyLineup.Support5, 2, 700, 100),
            C(6, FantasyLineup.Support4, 3, 750, 0), C(7, FantasyLineup.Support5, 3, 750, 0),
        };

        var r = FantasyLineup.Build(roster, null,
            new Dictionary<string, double> { ["underdog"] = 40 });

        r.Picks.Where(p => p.Slot == "support").Select(p => p.Player.PlayerId)
            .Should().BeEquivalentTo([4, 5]);
        r.Suffix!.Value.Key.Should().Be("underdog");
        r.Suffix.Value.ExpectedPoints.Should().Be(560, "1400 điểm × 40% × 100% khả năng thua");
    }

    /// <summary>
    /// Người chưa có dữ liệu danh hiệu bị BỎ QUA khi tính thưởng, không bị tính tỷ lệ 0 — và
    /// vẫn được xếp vào đội hình bình thường.
    /// </summary>
    [Fact]
    public void Chua_co_du_lieu_danh_hieu_thi_van_duoc_xep_doi_hinh()
    {
        var roster = new[]
        {
            new LineupCandidate(1, "A", FantasyLineup.Carry, 1, "T1", 1000, 10),
            new LineupCandidate(2, "B", FantasyLineup.Offlane, 1, "T1", 1000, 10),
            new LineupCandidate(3, "C", FantasyLineup.Mid, 1, "T1", 1000, 10),
            new LineupCandidate(4, "D", FantasyLineup.Support4, 2, "T2", 900, 10),
            new LineupCandidate(5, "E", FantasyLineup.Support5, 2, "T2", 900, 10),
        };

        var r = FantasyLineup.Build(roster, new Dictionary<string, double> { ["crimson"] = 6 });

        r.Picks.Should().HaveCount(5);
        r.Prefix.Should().BeNull("chưa ai có dữ liệu hero pool thì không xếp hạng prefix");
        r.Total.Should().Be(r.BasePoints);
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
