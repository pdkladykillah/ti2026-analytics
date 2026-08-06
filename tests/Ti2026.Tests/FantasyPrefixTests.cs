using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Prefix áp cho CẢ đội hình, nên phải cộng theo ĐIỂM chứ không lấy trung bình tỷ lệ: cùng một
/// prefix, hợp với người ghi 5000 điểm đáng hơn hẳn hợp với người ghi 800 điểm.
/// </summary>
public class FantasyPrefixTests
{
    private static readonly Dictionary<string, double> Bonus = new()
    {
        ["crimson"] = 6, ["cerulean"] = 11,
    };

    private static PrefixPlayer P(string nick, double pts, double crimson, double cerulean) =>
        new(nick, pts, new Dictionary<string, double> { ["crimson"] = crimson, ["cerulean"] = cerulean });

    [Fact]
    public void Loi_ky_vong_la_thuong_nhan_ty_le_nhan_diem()
    {
        var r = FantasyPrefix.Rank([P("A", 1000, crimson: 50, cerulean: 0)], Bonus);

        r.First(x => x.Key == "crimson").ExpectedPoints
            .Should().Be(30, "1000 × 6% × 50%");
        r.First(x => x.Key == "cerulean").ExpectedPoints.Should().Be(0);
    }

    /// <summary>
    /// Bài kiểm chính. Nếu lấy TRUNG BÌNH tỷ lệ thì cerulean thắng (tỷ lệ 50 so với 40); cộng
    /// theo điểm thì crimson thắng, vì nó hợp với người ghi nhiều điểm hơn hẳn.
    /// </summary>
    [Fact]
    public void Cong_theo_diem_chu_khong_lay_trung_binh_ty_le()
    {
        var lineup = new[]
        {
            P("ngôi sao", 5000, crimson: 40, cerulean: 0),
            P("người phụ", 500, crimson: 0, cerulean: 100),
        };

        var r = FantasyPrefix.Rank(lineup, Bonus);

        var crimson = r.First(x => x.Key == "crimson").ExpectedPoints;   // 5000 × 6% × 40% = 120
        var cerulean = r.First(x => x.Key == "cerulean").ExpectedPoints; // 500 × 11% × 100% = 55

        crimson.Should().Be(120);
        cerulean.Should().Be(55);
        r[0].Key.Should().Be("crimson", "prefix hợp với người ghi nhiều điểm mới đáng chọn");
    }

    /// <summary>
    /// Người CHƯA có dữ liệu hero pool bị bỏ qua, không tính tỷ lệ 0. Tính 0 thì một đội hình
    /// chưa có dữ liệu trông y hệt đội hình toàn người không bao giờ chơi nhóm hero đó.
    /// </summary>
    [Fact]
    public void Nguoi_chua_co_du_lieu_bi_bo_qua_chu_khong_tinh_ty_le_0()
    {
        var lineup = new[]
        {
            P("có dữ liệu", 1000, crimson: 50, cerulean: 0),
            new PrefixPlayer("chưa có", 1000, null),
        };

        var r = FantasyPrefix.Rank(lineup, Bonus);
        var crimson = r.First(x => x.Key == "crimson");

        crimson.ExpectedPoints.Should().Be(30, "chỉ cộng người có dữ liệu");
        crimson.PlayersCovered.Should().Be(1);
        crimson.PlayersTotal.Should().Be(2, "phải khai ra là đang thiếu một người");
    }

    [Fact]
    public void Doi_hinh_rong_thi_khong_chia_cho_khong()
    {
        var r = FantasyPrefix.Rank([], Bonus);
        r.Should().HaveCount(2);
        r.Should().AllSatisfy(x => x.ExpectedPercentOfTotal.Should().Be(0));
    }
}
