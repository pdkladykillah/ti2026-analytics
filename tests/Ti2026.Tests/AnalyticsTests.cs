using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

public class EloEngineTests
{
    private static RatedMatch M(int day, int winner, int loser) =>
        new(new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc), winner, loser);

    [Fact]
    public void Chua_da_tran_nao_thi_moi_doi_giu_diem_khoi_diem()
    {
        var r = EloEngine.Compute([], [1, 2, 3]);

        r.Should().HaveCount(3);
        r.Values.Should().OnlyContain(x => x.Elo == EloEngine.InitialRating && x.Games == 0);
    }

    [Fact]
    public void Thang_thi_len_diem_thua_thi_xuong_dung_bang_do()
    {
        var r = EloEngine.Compute([M(1, winner: 1, loser: 2)], [1, 2]);

        r[1].Elo.Should().BeGreaterThan(EloEngine.InitialRating);
        r[2].Elo.Should().BeLessThan(EloEngine.InitialRating);

        // Tổng điểm bảo toàn: bên thắng nhận đúng phần bên thua mất
        (r[1].Elo + r[2].Elo).Should().BeApproximately(EloEngine.InitialRating * 2, 0.05);
        r[1].Games.Should().Be(1);
    }

    /// <summary>
    /// Đây là lý do Elo tồn tại: winrate không biết đối thủ là ai. Đội thắng đội mạnh phải
    /// được nhiều điểm hơn đội thắng đội yếu, dù cả hai cùng winrate 100%.
    /// </summary>
    [Fact]
    public void Thang_doi_manh_duoc_nhieu_diem_hon_thang_doi_yeu()
    {
        // Đội 3 tự làm mình mạnh lên bằng cách thắng đội 4 nhiều lần
        var buildUp = Enumerable.Range(1, 10).Select(i => M(i, 3, 4)).ToList();

        // Đội 1 thắng đội mạnh (3); đội 2 thắng đội yếu (4)
        var withStrong = EloEngine.Compute([.. buildUp, M(20, 1, 3)], [1, 2, 3, 4]);
        var withWeak = EloEngine.Compute([.. buildUp, M(20, 2, 4)], [1, 2, 3, 4]);

        var gainVsStrong = withStrong[1].Elo - EloEngine.InitialRating;
        var gainVsWeak = withWeak[2].Elo - EloEngine.InitialRating;

        gainVsStrong.Should().BeGreaterThan(gainVsWeak,
            "cùng là một trận thắng, nhưng thắng đội mạnh mới là bằng chứng mạnh");
    }

    [Fact]
    public void Xac_suat_can_bang_khi_hai_ben_cung_diem()
    {
        EloEngine.ExpectedScore(1500, 1500).Should().BeApproximately(0.5, 0.0001);
        EloEngine.ExpectedScore(1600, 1500).Should().BeApproximately(0.64, 0.01);
        EloEngine.ExpectedScore(1700, 1500).Should().BeApproximately(0.76, 0.01);
    }

    /// <summary>
    /// Elo phụ thuộc đường đi: xử lý sai thứ tự cho ra con số khác hẳn và KHÔNG có gì báo lỗi.
    /// Engine phải tự sắp xếp thay vì tin vào thứ tự đầu vào.
    /// </summary>
    [Fact]
    public void Tu_sap_xep_theo_thoi_gian_du_dau_vao_lon_xon()
    {
        var ordered = new[] { M(1, 1, 2), M(2, 2, 1), M(3, 1, 2) };
        var shuffled = new[] { M(3, 1, 2), M(1, 1, 2), M(2, 2, 1) };

        EloEngine.Compute(ordered, [1, 2])[1].Elo
            .Should().Be(EloEngine.Compute(shuffled, [1, 2])[1].Elo);
    }

    [Fact]
    public void Bo_qua_tran_co_doi_khong_nam_trong_he()
    {
        var r = EloEngine.Compute([M(1, 1, 99)], [1, 2]);

        r[1].Elo.Should().Be(EloEngine.InitialRating);
        r[1].Games.Should().Be(0);
    }
}

public class DistributionStatsTests
{
    [Fact]
    public void Mo_ta_dung_phan_phoi()
    {
        var d = DistributionStats.Describe([10, 20, 30, 40, 50]);

        d.Count.Should().Be(5);
        d.Mean.Should().Be(30);
        d.Median.Should().Be(30);
        d.Min.Should().Be(10);
        d.Max.Should().Be(50);
        d.P25.Should().Be(20);
        d.P75.Should().Be(40);
    }

    /// <summary>
    /// Hai đội cùng trung bình nhưng độ phân tán khác nhau thì xác suất vượt mốc khác hẳn.
    /// Đây chính là lý do không dùng trung bình cho kèo over/under.
    /// </summary>
    [Fact]
    public void Cung_trung_binh_nhung_phan_tan_khac_thi_xac_suat_khac()
    {
        var chat = Enumerable.Repeat(52.0, 20).ToList();
        var loan = Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? 30.0 : 74.0).ToList();

        chat.Average().Should().BeApproximately(loan.Average(), 0.01);

        DistributionStats.ProbabilityOver(chat, 60).Should().Be(0);
        DistributionStats.ProbabilityOver(loan, 60).Should().Be(50);
    }

    [Fact]
    public void Mau_qua_nho_thi_tra_null_chu_khong_cho_con_so_gia_tin_cay()
    {
        DistributionStats.ProbabilityOver([50, 60, 70], 55).Should().BeNull();
        DistributionStats.ProbabilityOver([50, 60, 70], 55, minSample: 3).Should().BeApproximately(66.7, 0.1);
    }

    [Fact]
    public void Rong_thi_khong_no()
    {
        DistributionStats.Describe([]).Count.Should().Be(0);
        DistributionStats.ProbabilityOver([], 10).Should().BeNull();
    }
}

public class ChangeDetectorTests
{
    private static IReadOnlyList<MetricPair> Pair(string key, double? before, double? after) =>
        ChangeDetector.Metrics(
            k => k == key ? before : null,
            k => k == key ? after : null);

    [Fact]
    public void Bien_dong_nho_hon_nguong_thi_IM_LANG()
    {
        // winrate ngưỡng 5 điểm; nhích 3 điểm là dao động bình thường
        ChangeDetector.Detect("a", "Đội A", Pair("winrate", 50, 53)).Should().BeEmpty();
    }

    [Fact]
    public void Vuot_nguong_thi_neu_ra_kem_cau_ke()
    {
        var changes = ChangeDetector.Detect("a", "Đội A", Pair("winrate", 60, 48));

        changes.Should().HaveCount(1);
        var c = changes[0];
        c.Improved.Should().BeFalse();
        c.Delta.Should().Be(-12);
        c.Magnitude.Should().BeApproximately(2.4, 0.01);
        c.Narrative.Should().Contain("Đội A").And.Contain("giảm").And.Contain("bất lợi");
    }

    /// <summary>
    /// Deaths và duration là chỉ số THẤP HƠN TỐT HƠN. Coi mọi mức tăng là tiến bộ sẽ kể
    /// ngược câu chuyện.
    /// </summary>
    [Fact]
    public void Hieu_dung_chi_so_thap_hon_thi_tot_hon()
    {
        ChangeDetector.Detect("a", "A", Pair("deaths", 22, 26))[0].Improved.Should().BeFalse();
        ChangeDetector.Detect("a", "A", Pair("deaths", 26, 22))[0].Improved.Should().BeTrue();
        ChangeDetector.Detect("a", "A", Pair("duration", 38, 46))[0].Improved.Should().BeFalse();
    }

    [Fact]
    public void Thieu_mot_dau_thi_khong_suy_dien()
    {
        ChangeDetector.Detect("a", "A", Pair("winrate", null, 60)).Should().BeEmpty(
            "đội mới có dữ liệu hôm nay không phải là đội vừa tăng vọt từ 0");
        ChangeDetector.Detect("a", "A", Pair("winrate", 60, null)).Should().BeEmpty();
    }

    [Fact]
    public void Magnitude_so_sanh_duoc_giua_cac_chi_so_khac_don_vi()
    {
        // winrate +10 (ngưỡng 5) = 2.0 lần; elo +80 (ngưỡng 40) = 2.0 lần
        var wr = ChangeDetector.Detect("a", "A", Pair("winrate", 50, 60))[0];
        var elo = ChangeDetector.Detect("a", "A", Pair("elo", 1500, 1580))[0];

        wr.Magnitude.Should().Be(elo.Magnitude);
    }
}
