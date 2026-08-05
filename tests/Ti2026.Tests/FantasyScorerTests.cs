using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Hai luật của fantasy TI2026 phải làm đúng, nếu không mọi con số phía sau đều lệch:
/// điểm một trận là tổng N ván CAO NHẤT trong series, và giá trị một người là TRUNG BÌNH
/// mỗi trận chứ không phải tổng.
/// </summary>
public class FantasyScorerTests
{
    private static FantasyConfig Config(params (string Key, double Per, double? Points)[] stats) =>
        new(stats.Select(s => new FantasyStat(s.Key, s.Key, s.Per, s.Points)).ToList(),
            CountBestGames: 2, Source: "test", UpdatedAt: null);

    private static FantasyConfig ConfigWithBase(string key, double per, double points, double bas) =>
        new([new FantasyStat(key, key, per, points, bas)],
            CountBestGames: 2, Source: "test", UpdatedAt: null);

    private static FantasyGame Game(long id, long? series, params (string, double?)[] values) =>
        new(id, series, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            values.ToDictionary(v => v.Item1, v => v.Item2));

    [Fact]
    public void Chia_cho_Per_roi_moi_nhan_he_so()
    {
        var cfg = Config(("creeps", 100, 0.5));
        var score = FantasyScorer.ScoreGame(Game(1, null, ("creeps", 300)), cfg);

        score.Points.Should().Be(1.5, "300 lính, 0.5 điểm mỗi 100 lính");
    }

    /// <summary>
    /// Luật TI2026 chấm điểm chết là "1950 − 195 × số lần chết" — hàm bậc nhất CÓ hằng số.
    /// Engine bản đầu chỉ có phép nhân, nên nếu lắp bảng hệ số vào mà không sửa thì điểm chết
    /// ra âm ở MỌI ván và không ai biết vì sao.
    /// </summary>
    [Fact]
    public void Tinh_dung_ham_bac_nhat_co_hang_so_cho_diem_chet()
    {
        var cfg = ConfigWithBase("deaths", per: 1, points: -195, bas: 1950);

        FantasyScorer.ScoreGame(Game(1, null, ("deaths", 0)), cfg).Points.Should().Be(1950,
            "không chết lần nào thì được trọn hằng số");
        FantasyScorer.ScoreGame(Game(2, null, ("deaths", 5)), cfg).Points.Should().Be(975,
            "1950 − 195 × 5");
        FantasyScorer.ScoreGame(Game(3, null, ("deaths", 12)), cfg).Points.Should().Be(-390,
            "điểm chết KHÔNG chặn ở 0, được phép âm — đúng luật");
    }

    /// <summary>Chưa đo được thì không cộng cả hằng số: chưa biết chết mấy lần ≠ chết 0 lần.</summary>
    [Fact]
    public void Chua_do_duoc_thi_khong_cong_ca_hang_so()
    {
        var cfg = ConfigWithBase("deaths", per: 1, points: -195, bas: 1950);
        var score = FantasyScorer.ScoreGame(Game(1, null, ("deaths", null)), cfg);

        score.Points.Should().Be(0);
        score.Parts.Single().Points.Should().BeNull();
    }

    [Fact]
    public void He_so_am_lam_giam_diem()
    {
        var cfg = Config(("deaths", 1, -0.3));
        FantasyScorer.ScoreGame(Game(1, null, ("deaths", 5)), cfg).Points.Should().Be(-1.5);
    }

    /// <summary>
    /// Chỉ số CHƯA ĐO ĐƯỢC bị loại khỏi phép tính và khai ra ở breakdown. Quy về 0 thì một
    /// người hỗ trợ chưa nạp dữ liệu cắm mắt sẽ trông như người không cắm mắt bao giờ.
    /// </summary>
    [Fact]
    public void Chi_so_null_bi_loai_chu_khong_tinh_thanh_0()
    {
        var cfg = Config(("kills", 1, 1.0), ("wards", 1, 0.5));
        var score = FantasyScorer.ScoreGame(Game(1, null, ("kills", 3), ("wards", null)), cfg);

        score.Points.Should().Be(3.0, "chỉ tính phần đo được");

        var wards = score.Parts.Single(p => p.Key == "wards");
        wards.Points.Should().BeNull("phải khai là chưa biết, không phải 0 điểm");
        wards.Raw.Should().BeNull();
    }

    [Fact]
    public void He_so_chua_dien_thi_khong_tinh_thanh_phan_do()
    {
        var cfg = Config(("kills", 1, 1.0), ("towerKills", 1, null));
        cfg.Ready.Should().BeFalse();
        cfg.MissingCoefficients.Should().Contain("towerKills");

        var score = FantasyScorer.ScoreGame(Game(1, null, ("kills", 2), ("towerKills", 4)), cfg);
        score.Points.Should().Be(2.0, "hệ số chưa biết thì không được đoán là 1");
    }

    /// <summary>
    /// Luật số 1. Cộng hết mọi ván thì người THUA Bo3 (3 ván) lại được cộng nhiều hơn người
    /// THẮNG 2-0 (2 ván), chỉ vì series dài hơn.
    /// </summary>
    [Fact]
    public void Diem_mot_tran_la_tong_hai_van_cao_nhat_khong_phai_tat_ca()
    {
        var cfg = Config(("kills", 1, 1.0));

        var games = new[]
        {
            FantasyScorer.ScoreGame(Game(1, 900, ("kills", 10)), cfg),
            FantasyScorer.ScoreGame(Game(2, 900, ("kills", 8)), cfg),
            FantasyScorer.ScoreGame(Game(3, 900, ("kills", 1)), cfg),
        };

        var matches = FantasyScorer.MatchScores(games, countBestGames: 2);

        matches.Should().ContainSingle("ba ván cùng một series là MỘT trận");
        matches[0].Should().Be(18, "10 + 8, bỏ ván 1 điểm");
    }

    /// <summary>
    /// Ván không có SeriesId là ván đơn thật, không phải dữ liệu thiếu. Gom hết chúng vào một
    /// nhóm sẽ tạo ra một "series" khổng lồ giả tạo và chỉ giữ lại hai ván tốt nhất của cả đời.
    /// </summary>
    [Fact]
    public void Van_khong_co_series_duoc_tinh_la_tran_rieng()
    {
        var cfg = Config(("kills", 1, 1.0));

        var games = new[]
        {
            FantasyScorer.ScoreGame(Game(1, null, ("kills", 10)), cfg),
            FantasyScorer.ScoreGame(Game(2, null, ("kills", 8)), cfg),
            FantasyScorer.ScoreGame(Game(3, 0, ("kills", 6)), cfg),
        };

        FantasyScorer.MatchScores(games, 2).Should().HaveCount(3);
    }

    /// <summary>
    /// Luật số 2. Cộng dồn thì ai thi đấu nhiều giải hơn luôn đứng đầu, bất kể chơi hay dở —
    /// mà fantasy thì chọn người, không chọn người bận rộn.
    /// </summary>
    [Fact]
    public void Gia_tri_mot_nguoi_la_TRUNG_BINH_moi_tran_khong_phai_tong()
    {
        var cfg = Config(("kills", 1, 1.0));

        var chamChi = new[]
        {
            FantasyScorer.ScoreGame(Game(1, 1, ("kills", 5)), cfg),
            FantasyScorer.ScoreGame(Game(2, 2, ("kills", 5)), cfg),
            FantasyScorer.ScoreGame(Game(3, 3, ("kills", 5)), cfg),
        };

        var xuatSac = new[] { FantasyScorer.ScoreGame(Game(4, 4, ("kills", 12)), cfg) };

        FantasyScorer.AverageMatchScore(chamChi, 2).Should().Be(5);
        FantasyScorer.AverageMatchScore(xuatSac, 2).Should().Be(12,
            "một người chơi ít nhưng hay hơn phải xếp trên người chơi nhiều mà trung bình thấp");
    }

    [Fact]
    public void Khong_co_tran_nao_thi_tra_null_chu_khong_phai_0()
    {
        FantasyScorer.AverageMatchScore([], 2).Should().BeNull();
    }

    [Fact]
    public void Bang_he_so_day_du_thi_san_sang()
    {
        Config(("kills", 1, 0.3), ("deaths", 1, -0.3)).Ready.Should().BeTrue();
        Config().Ready.Should().BeFalse("bảng rỗng không phải bảng đã điền");
    }
}
