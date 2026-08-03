using FluentAssertions;
using Ti2026.Ingest.Snapshots;

namespace Ti2026.Tests;

public class StatCalculatorTests
{
    private static MatchOutcome M(bool won, int kills, int deaths, int? assists,
        bool? fb, bool? f10, int durationSeconds) =>
        new(new DateOnly(2026, 7, 1), won, kills, deaths, assists, fb, f10, durationSeconds);

    /// <summary>Ván chỉ có dữ liệu mà OpenDota teams/{id}/matches thực sự trả về.</summary>
    private static MatchOutcome Basic(bool won, int kills, int deaths, int durationSeconds) =>
        new(new DateOnly(2026, 7, 1), won, kills, deaths,
            Assists: null, HadFirstBlood: null, ReachedTenFirst: null, durationSeconds);

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
        stats.AvgDurationMinutes.Should().Be(0);
        stats.WinWhenFbRate.Should().BeNull("không có ván nào thì không có gì để đo");
    }

    [Fact]
    public void Da_do_va_khong_co_first_blood_thi_ty_le_bang_0_chu_khong_null()
    {
        var stats = StatCalculator.Compute([
            M(true, 25, 20, 50, fb: false, f10: false, 2400),
        ]);

        stats.FirstBloodRate.Should().Be(0, "đã đo và không ván nào có first blood");

        // WinWhenFb là tỷ lệ CÓ ĐIỀU KIỆN: mẫu số là số ván CÓ first blood. Chia cho 0 sinh
        // NaN, mà NaN serialize sang JSON làm JSON.parse ở browser chết -> trang trắng.
        stats.WinWhenFbRate.Should().BeNull("không có ván nào có fb thì không có gì để tính");
        stats.WinWhenF10Rate.Should().BeNull();
    }

    /// <summary>
    /// Đây là bẫy nguy hiểm nhất của M2. OpenDota teams/{id}/matches KHÔNG trả về assists,
    /// first blood, hay mốc 10 kill. Nếu quy "không biết" thành "bằng không" thì trang sẽ
    /// hiển thị "first blood 0%" cho cả 16 đội, trông y hệt số thật.
    /// </summary>
    [Fact]
    public void Chua_biet_thi_phai_tra_null_chu_KHONG_duoc_tra_0()
    {
        var stats = StatCalculator.Compute([
            Basic(won: true,  kills: 30, deaths: 20, durationSeconds: 2400),
            Basic(won: false, kills: 20, deaths: 30, durationSeconds: 3000),
        ]);

        // Những chỉ số tính được vẫn phải đúng
        stats.Maps.Should().Be(2);
        stats.Winrate.Should().Be(50);
        stats.AvgKills.Should().Be(25);
        stats.KillDiff.Should().Be(0);
        stats.TotalKills.Should().Be(50);
        stats.AvgDurationMinutes.Should().Be(45);

        // Những chỉ số nguồn không cung cấp PHẢI là null
        stats.AvgAssists.Should().BeNull();
        stats.FirstBloodRate.Should().BeNull();
        stats.F10Rate.Should().BeNull();
        stats.WinWhenFbRate.Should().BeNull();
        stats.WinWhenF10Rate.Should().BeNull();
    }

    [Fact]
    public void Tron_van_biet_va_khong_biet_thi_chi_tinh_tren_van_biet()
    {
        var stats = StatCalculator.Compute([
            M(true,  25, 20, 50, fb: true,  f10: true,  2400),
            M(false, 20, 25, 40, fb: false, f10: false, 2400),
            Basic(true, 25, 20, 2400),   // không biết fb/f10
            Basic(true, 25, 20, 2400),   // không biết fb/f10
        ]);

        // Mẫu số là 2 ván BIẾT, không phải 4 ván. Nếu tính trên cả 4 thì ra 25% — sai.
        stats.FirstBloodRate.Should().Be(50);
        stats.WinWhenFbRate.Should().Be(100);
        stats.Maps.Should().Be(4, "Maps vẫn đếm đủ vì thắng/thua thì biết hết");
        stats.Winrate.Should().Be(75);
    }
}
