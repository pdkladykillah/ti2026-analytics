using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

public class PatchIndexTests
{
    [Fact]
    public void Doc_duoc_chi_so_ban_game_dang_chuoi()
    {
        PatchIndex.Parse("60").Should().Be(60);
        PatchIndex.Parse(null).Should().BeNull();
        PatchIndex.Parse("").Should().BeNull();

        // OpenDota trả chỉ số nguyên; nếu một ngày nào đó nó đổi sang "7.41" thì phải trả
        // null chứ không được đoán bừa — Elo sai âm thầm còn tệ hơn không có dữ liệu patch.
        PatchIndex.Parse("7.41").Should().BeNull();
    }

    [Fact]
    public void Doi_chi_so_sang_nhan_hien_thi()
    {
        PatchIndex.Name(60).Should().Be("7.41");
        PatchIndex.Name(59).Should().Be("7.40");
        PatchIndex.Name(45).Should().Be("7.26");

        // Dưới mốc neo thì không suy ra được, phải nói thẳng là không biết tên
        PatchIndex.Name(30).Should().Be("bản #30");
    }
}

public class PatchRegressionTests
{
    private static RatedMatch M(int day, int winner, int loser, int? patch = null) =>
        new(new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc), winner, loser, patch);

    private static EloOptions Reg(double r) => EloOptions.Default with { PatchRegression = r };

    /// <summary>Mặc định phải là "không đổi hành vi" — bật yếu tố patch là một lựa chọn có ý thức.</summary>
    [Fact]
    public void Reg_bang_0_thi_ban_game_khong_anh_huong_gi()
    {
        var withPatch = Enumerable.Range(1, 10).Select(i => M(i, 1, 2, patch: 50 + i)).ToList();
        var withoutPatch = Enumerable.Range(1, 10).Select(i => M(i, 1, 2)).ToList();

        EloEngine.Compute(withPatch, [1, 2], Reg(0))[1].Elo
            .Should().Be(EloEngine.Compute(withoutPatch, [1, 2], Reg(0))[1].Elo);
    }

    [Fact]
    public void Len_ban_moi_thi_khoang_cach_co_lai_nhung_thu_hang_giu_nguyen()
    {
        // Đội 1 xây điểm ở bản 59, rồi game lên bản 60
        var matches = Enumerable.Range(1, 10).Select(i => M(i, 1, 2, patch: 59)).ToList();

        var before = EloEngine.Compute(matches, [1, 2], Reg(0.3));

        matches.Add(M(11, 1, 2, patch: 60));
        var after = EloEngine.Compute(matches, [1, 2], Reg(0.3));

        var gapBefore = before[1].Elo - before[2].Elo;
        var gapAfter = after[1].Elo - after[2].Elo;

        // Trận thứ 11 đội 1 vẫn thắng nên có cộng thêm, nhưng phần bị kéo về lớn hơn nhiều
        gapAfter.Should().BeLessThan(gapBefore,
            "sang bản mới thì hiểu biết cũ bớt giá trị, khoảng cách rating phải co lại");
        after[1].Elo.Should().BeGreaterThan(after[2].Elo, "co lại chứ không đảo thứ hạng");
    }

    /// <summary>
    /// Kéo về mốc 1500 theo cùng một hệ số cho mọi đội thì tổng điểm không đổi — vì Elo luôn
    /// giữ trung bình đúng bằng 1500. Nếu tính chất này vỡ nghĩa là rating đang bị bơm hoặc
    /// rút điểm từ hư không.
    /// </summary>
    [Fact]
    public void Keo_ve_trung_binh_khong_lam_thay_doi_tong_diem()
    {
        var matches = new[]
        {
            M(1, 1, 2, patch: 59), M(2, 1, 3, patch: 59), M(3, 2, 3, patch: 59),
            M(4, 1, 2, patch: 60), M(5, 3, 1, patch: 61),
        };

        var r = EloEngine.Compute(matches, [1, 2, 3], Reg(0.25));

        r.Values.Sum(x => x.Elo)
            .Should().BeApproximately(EloEngine.InitialRating * 3, 0.05);
    }

    [Fact]
    public void Nhay_hai_bac_patch_thi_keo_ve_hai_lan()
    {
        var buildUp = Enumerable.Range(1, 10).Select(i => M(i, 1, 2, patch: 58)).ToList();

        var oneStep = EloEngine.Compute([.. buildUp, M(11, 1, 2, patch: 59)], [1, 2], Reg(0.2));
        var twoSteps = EloEngine.Compute([.. buildUp, M(11, 1, 2, patch: 60)], [1, 2], Reg(0.2));

        (twoSteps[1].Elo - EloEngine.InitialRating)
            .Should().BeLessThan(oneStep[1].Elo - EloEngine.InitialRating,
                "bỏ qua hai bản thì phần quá khứ còn giữ lại phải ít hơn");
    }

    /// <summary>
    /// Dữ liệu vẫn có thể lệch thứ tự: một trận cũ được nạp bổ sung, hoặc OpenDota gán patch
    /// không khớp mốc thời gian. Kéo ngược về quá khứ không có nghĩa gì và sẽ làm rating nhảy
    /// loạn, nên phải bỏ qua.
    /// </summary>
    [Fact]
    public void Patch_tut_lui_thi_bo_qua_chu_khong_keo_nguoc()
    {
        var buildUp = Enumerable.Range(1, 10).Select(i => M(i, 1, 2, patch: 60)).ToList();

        var clean = EloEngine.Compute([.. buildUp, M(11, 1, 2, patch: 60)], [1, 2], Reg(0.3));
        var messy = EloEngine.Compute([.. buildUp, M(11, 1, 2, patch: 58)], [1, 2], Reg(0.3));

        messy[1].Elo.Should().Be(clean[1].Elo);
    }

    [Fact]
    public void Tran_khong_biet_patch_thi_giu_nguyen_moc_hien_hanh()
    {
        var buildUp = Enumerable.Range(1, 10).Select(i => M(i, 1, 2, patch: 59)).ToList();

        var withGap = EloEngine.Compute(
            [.. buildUp, M(11, 1, 2), M(12, 1, 2, patch: 60)], [1, 2], Reg(0.3));
        var withoutGap = EloEngine.Compute(
            [.. buildUp, M(11, 1, 2, patch: 59), M(12, 1, 2, patch: 60)], [1, 2], Reg(0.3));

        withGap[1].Elo.Should().BeApproximately(withoutGap[1].Elo, 0.05,
            "trận thiếu patch không được coi là một bậc patch mới");
    }

    /// <summary>
    /// Thứ tự bên trong backtest quan trọng: phải kéo về trung bình TRƯỚC khi dự đoán trận
    /// đầu tiên của bản mới. Kéo sau nghĩa là trận đó vẫn được dự đoán bằng hiểu biết của
    /// bản cũ với đủ sức nặng — đúng cái mà tham số này sinh ra để tránh.
    /// </summary>
    [Fact]
    public void Backtest_keo_ve_truoc_khi_du_doan_tran_dau_ban_moi()
    {
        var matches = Enumerable.Range(1, 10).Select(i => M(i, 1, 2, patch: 59)).ToList();
        matches.Add(M(11, 1, 2, patch: 60));

        var noReg = EloEngine.Backtest(matches, [1, 2], warmupGamesPerTeam: 2, options: Reg(0));
        var withReg = EloEngine.Backtest(matches, [1, 2], warmupGamesPerTeam: 2, options: Reg(0.3));

        withReg[^1].PredictedProbability.Should().BeLessThan(noReg[^1].PredictedProbability,
            "trận đầu của bản mới phải được dự đoán dè dặt hơn");
    }
}

public class CalibrationTests
{
    private static BacktestRecord R(int day, double p, bool correct) =>
        new(new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc), p, correct);

    /// <summary>day đếm từ 1; cộng dồn để test dài hơn 31 ngày vẫn hợp lệ.</summary>
    private static RatedMatch M(int day, int winner, int loser) =>
        new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(day - 1), winner, loser);

    [Fact]
    public void Brier_0_khi_hoan_hao_va_025_khi_doan_bua()
    {
        Calibration.Brier([R(1, 100, true), R(2, 100, true)]).Should().BeApproximately(0, 1e-9);
        Calibration.Brier([R(1, 50, true), R(2, 50, false)]).Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void Khong_co_du_doan_nao_thi_bao_cao_rong_chu_khong_bia_so()
    {
        var report = Calibration.Summarize([]);

        report.Evaluated.Should().Be(0);
        report.Buckets.Should().BeEmpty();
        double.IsNaN(report.Brier).Should().BeTrue("không có mẫu thì Brier không tồn tại, không phải 0");
    }

    /// <summary>
    /// Chia theo THỜI GIAN chứ không ngẫu nhiên: chia ngẫu nhiên sẽ để trận tương lai lọt vào
    /// tập huấn luyện, đúng thứ mà ngoài đời không bao giờ có.
    /// </summary>
    [Fact]
    public void Chia_tap_theo_thoi_gian_dung_ty_le()
    {
        var matches = Enumerable.Range(1, 10).Select(i => M(i, 1, 2)).ToList();

        var cutoff = Calibration.SplitCutoff(matches, 0.7);

        cutoff.Should().Be(new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc));
        matches.Count(m => m.StartTime < cutoff!.Value).Should().Be(7);
    }

    [Fact]
    public void Chia_tap_luon_chua_it_nhat_mot_tran_moi_ben()
    {
        var two = new[] { M(1, 1, 2), M(2, 1, 2) };

        var cutoff = Calibration.SplitCutoff(two, 0.99);

        two.Count(m => m.StartTime < cutoff!.Value).Should().Be(1);
        two.Count(m => m.StartTime >= cutoff!.Value).Should().Be(1);
    }

    [Fact]
    public void Khong_co_tran_nao_thi_khong_co_moc_chia()
    {
        Calibration.SplitCutoff([], 0.7).Should().BeNull();
    }

    /// <summary>
    /// Điểm mấu chốt của kỷ luật ngoài mẫu: lưới tham số chỉ được chấm trên phần huấn luyện.
    /// Nếu nó lỡ chấm cả phần kiểm định thì con số báo cáo sau đó là con số đã bị ngắm vào.
    /// </summary>
    [Fact]
    public void Luoi_tham_so_chi_cham_diem_tren_tap_huan_luyen()
    {
        var matches = Enumerable.Range(1, 40)
            .Select(i => M(i, i % 3 == 0 ? 2 : 1, i % 3 == 0 ? 1 : 2)).ToList();
        var cutoff = Calibration.SplitCutoff(matches, 0.7);

        var trainOnly = Calibration.Grid(matches, [1, 2], [600], [0], cutoff);
        var everything = Calibration.Grid(matches, [1, 2], [600], [0], trainUntil: null);

        trainOnly[0].Samples.Should().BeLessThan(everything[0].Samples);
        trainOnly[0].Samples.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Luoi_phu_het_moi_to_hop()
    {
        var matches = Enumerable.Range(1, 30).Select(i => M(i, 1, 2)).ToList();

        var grid = Calibration.Grid(matches, [1, 2], [400, 600], [0, 0.1, 0.2], trainUntil: null);

        grid.Should().HaveCount(6);
    }

    [Fact]
    public void Chon_diem_co_Brier_thap_nhat_va_bo_qua_diem_khong_co_mau()
    {
        var grid = new List<GridPoint>
        {
            new(400, 0, double.NaN, 0),
            new(600, 0, 0.24, 100),
            new(700, 0.1, 0.22, 100),
            new(800, 0, 0.26, 100),
        };

        Calibration.Best(grid)!.Value.ProbabilityScale.Should().Be(700);
        Calibration.Best([]).Should().BeNull();
    }

    [Fact]
    public void Bucket_gom_theo_bac_10_va_dem_dung_ty_le_thuc_te()
    {
        var report = Calibration.Summarize([
            R(1, 62, true), R(2, 64, true), R(3, 66, false), R(4, 68, true),
            R(5, 55, false),
        ]);

        var b60 = report.Buckets.Single(b => b.Range == "60–70%");
        b60.Samples.Should().Be(4);
        b60.Actual.Should().Be(75);
        report.Buckets.Single(b => b.Range == "50–60%").Samples.Should().Be(1);
    }
}
