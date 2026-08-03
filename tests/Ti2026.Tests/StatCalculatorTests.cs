using FluentAssertions;
using Ti2026.Ingest.Snapshots;

namespace Ti2026.Tests;

public class StatCalculatorTests
{
    private static MatchOutcome M(bool won, int kills, int deaths, int assists,
        bool fb, bool f10, int durationSeconds) =>
        new(new DateOnly(2026, 7, 1), won, kills, deaths, assists, fb, f10, durationSeconds);

    [Fact]
    public void Compute_tinh_dung_winrate_va_trung_binh()
    {
        var stats = StatCalculator.Compute([
            M(won: true,  kills: 30, deaths: 20, assists: 60, fb: true,  f10: true,  durationSeconds: 2400),
            M(won: false, kills: 20, deaths: 30, assists: 40, fb: false, f10: false, durationSeconds: 3000),
        ]);

        stats.Maps.Should().Be(2);
        stats.Wins.Should().Be(1);
        stats.Losses.Should().Be(1);
        stats.Winrate.Should().Be(50);
        stats.AvgKills.Should().Be(25);
        stats.AvgDeaths.Should().Be(25);
        stats.AvgAssists.Should().Be(50);
        stats.KillDiff.Should().Be(0);
        stats.TotalKills.Should().Be(50);          // kills + deaths = tổng máu cả hai bên
        stats.AvgDurationMinutes.Should().Be(45);  // (2400+3000)/2 = 2700s = 45 phút
    }

    [Fact]
    public void Compute_tinh_dung_ty_le_co_dieu_kien()
    {
        // 3 ván có first blood, thắng 2 → WinWhenFb = 66.67 (mẫu số là số ván CÓ fb)
        var stats = StatCalculator.Compute([
            M(true,  25, 20, 50, fb: true,  f10: true,  2400),
            M(true,  25, 20, 50, fb: true,  f10: false, 2400),
            M(false, 20, 25, 40, fb: true,  f10: false, 2400),
            M(false, 20, 25, 40, fb: false, f10: false, 2400),
        ]);

        stats.FirstBloodRate.Should().BeApproximately(75, 0.01);
        stats.WinWhenFbRate.Should().BeApproximately(66.67, 0.01);
        stats.F10Rate.Should().Be(25);
        stats.WinWhenF10Rate.Should().Be(100);
    }

    [Fact]
    public void Compute_khong_chia_cho_khong_khi_khong_co_van_nao()
    {
        var stats = StatCalculator.Compute([]);

        stats.Maps.Should().Be(0);
        stats.Winrate.Should().Be(0);
        stats.WinWhenFbRate.Should().Be(0);
        stats.AvgDurationMinutes.Should().Be(0);
    }

    [Fact]
    public void Compute_khong_tra_NaN_khi_khong_van_nao_co_first_blood()
    {
        var stats = StatCalculator.Compute([
            M(true, 25, 20, 50, fb: false, f10: false, 2400),
        ]);

        stats.FirstBloodRate.Should().Be(0);

        // WinWhenFb là tỷ lệ CÓ ĐIỀU KIỆN: mẫu số là số ván có first blood, không phải
        // tổng số ván. Chia cho 0 sinh NaN, mà NaN serialize sang JSON sẽ làm JSON.parse
        // ở browser chết -> trang trắng.
        stats.WinWhenFbRate.Should().Be(0);
        double.IsNaN(stats.WinWhenFbRate).Should().BeFalse();
        double.IsNaN(stats.WinWhenF10Rate).Should().BeFalse();
    }
}
