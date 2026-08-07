using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Sổ theo dõi dự đoán: mô hình nói TRƯỚC, rồi đối chiếu kết quả.
///
/// Đây là bài kiểm duy nhất không tự bào chữa được. Hiệu chuẩn hồi tố luôn đẹp hơn thực tế vì
/// tham số đã chọn khi đã nhìn thấy chính những trận đó; còn sổ này chỉ đếm những gì nói trước.
/// Và nó CHỈ dựng được nếu bắt đầu ghi từ trước — giải xong rồi thì Elo đã đổi theo chính các
/// trận ấy, không còn cách nào biết "lúc đó mô hình nói gì".
/// </summary>
public class PredictionLedgerTests
{
    private static Ti2026DbContext NewDb() =>
        new(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source=file:led{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options);

    private static async Task<(Ti2026DbContext Db, PredictionLedger Ledger, int A, int B)> SetupAsync()
    {
        var db = NewDb();
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        var a = new Team { Name = "Alpha", Slug = "alpha" };
        var b = new Team { Name = "Beta", Slug = "beta" };
        db.Teams.AddRange(a, b);
        await db.SaveChangesAsync();

        return (db, new PredictionLedger(db, NullLogger<PredictionLedger>.Instance), a.Id, b.Id);
    }

    private static DateTime T(int hour) => new(2026, 8, 1, hour, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Dọn sổ chạy ĐÚNG MỘT LẦN và chỉ đụng vào dòng CHƯA CHẤM.
    ///
    /// Không có chốt một lần thì mỗi vòng ingest lại xoá sạch những dòng vừa ghi ở chính vòng
    /// đó, và sổ vĩnh viễn rỗng — hỏng im lặng, vì "0 dòng" trông y hệt "chưa tới lúc ghi".
    /// </summary>
    [Fact]
    public async Task Don_so_chi_chay_mot_lan_va_khong_dung_vao_dong_da_cham()
    {
        var (db, ledger, a, b) = await SetupAsync();

        await ledger.SnapshotAsync(new Dictionary<int, double> { [a] = 1600, [b] = 1400 }, T(1), default);
        await db.SaveChangesAsync();

        // Một dòng đã chấm: là lịch sử, không được xoá
        var done = await db.Predictions.SingleAsync();
        done.ResolvedMatchId = 12345;
        done.TeamAWon = true;
        await db.SaveChangesAsync();

        await ledger.SnapshotAsync(new Dictionary<int, double> { [a] = 1700, [b] = 1300 }, T(2), default);
        await db.SaveChangesAsync();
        (await db.Predictions.CountAsync()).Should().Be(2);

        (await ledger.PurgeSupersededOnceAsync(T(3), default)).Should().Be(1);

        var left = await db.Predictions.ToListAsync();
        left.Should().ContainSingle().Which.ResolvedMatchId.Should().Be(12345);

        // Lần hai KHÔNG được xoá gì nữa
        await ledger.SnapshotAsync(new Dictionary<int, double> { [a] = 1800, [b] = 1200 }, T(4), default);
        await db.SaveChangesAsync();

        (await ledger.PurgeSupersededOnceAsync(T(5), default)).Should().Be(0);
        (await db.Predictions.CountAsync(p => p.ResolvedMatchId == null)).Should().Be(1,
            "dòng vừa ghi ở vòng sau phải còn nguyên");
    }

    [Fact]
    public async Task Ghi_du_doan_cho_moi_cap_doi()
    {
        var (db, ledger, a, b) = await SetupAsync();

        var added = await ledger.SnapshotAsync(
            new Dictionary<int, double> { [a] = 1600, [b] = 1400 }, T(1), default);
        await db.SaveChangesAsync();

        added.Should().Be(1);
        var p = await db.Predictions.SingleAsync();
        p.ProbabilityA.Should().BeGreaterThan(50, "Alpha Elo cao hơn nên phải được ưu tiên");
        p.ResolvedMatchId.Should().BeNull();
    }

    /// <summary>
    /// Elo không đổi thì mô hình chưa nói gì mới — không ghi thêm dòng. Không có luật này thì
    /// mỗi 6 giờ sổ lại phình thêm một bản sao y hệt của cùng một dự đoán.
    /// </summary>
    [Fact]
    public async Task Elo_khong_doi_thi_khong_ghi_them_dong()
    {
        var (db, ledger, a, b) = await SetupAsync();
        var elo = new Dictionary<int, double> { [a] = 1600, [b] = 1400 };

        await ledger.SnapshotAsync(elo, T(1), default);
        await db.SaveChangesAsync();

        var again = await ledger.SnapshotAsync(elo, T(7), default);
        await db.SaveChangesAsync();

        again.Should().Be(0);
        (await db.Predictions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Elo_doi_thi_ghi_dong_moi()
    {
        var (db, ledger, a, b) = await SetupAsync();

        await ledger.SnapshotAsync(new Dictionary<int, double> { [a] = 1600, [b] = 1400 }, T(1), default);
        await db.SaveChangesAsync();
        await ledger.SnapshotAsync(new Dictionary<int, double> { [a] = 1650, [b] = 1400 }, T(7), default);
        await db.SaveChangesAsync();

        (await db.Predictions.CountAsync()).Should().Be(2);
    }

    /// <summary>Chỉ chấm bằng trận diễn ra SAU lúc ghi — trận trước đó thì mô hình đã biết rồi.</summary>
    [Fact]
    public async Task Khong_chấm_bang_tran_dien_ra_TRUOC_luc_ghi()
    {
        var (db, ledger, a, b) = await SetupAsync();

        db.Matches.Add(new Match
        {
            Id = 111, StartTime = T(0), RadiantTeamId = a, DireTeamId = b, RadiantWin = true,
        });
        await db.SaveChangesAsync();

        await ledger.SnapshotAsync(new Dictionary<int, double> { [a] = 1600, [b] = 1400 }, T(1), default);
        await db.SaveChangesAsync();

        (await ledger.ResolveAsync(T(9), default)).Should().Be(0);
        (await db.Predictions.SingleAsync()).ResolvedMatchId.Should().BeNull();
    }

    [Fact]
    public async Task Cham_dung_ben_thang_ke_ca_khi_doi_A_o_phe_Dire()
    {
        var (db, ledger, a, b) = await SetupAsync();

        await ledger.SnapshotAsync(new Dictionary<int, double> { [a] = 1600, [b] = 1400 }, T(1), default);
        await db.SaveChangesAsync();

        // Alpha là Dire và Dire thắng
        db.Matches.Add(new Match
        {
            Id = 222, StartTime = T(2), RadiantTeamId = b, DireTeamId = a, RadiantWin = false,
        });
        await db.SaveChangesAsync();

        (await ledger.ResolveAsync(T(3), default)).Should().Be(1);
        await db.SaveChangesAsync();

        (await db.Predictions.SingleAsync()).TeamAWon
            .Should().BeTrue("Alpha ở phe Dire và Dire thắng");
    }

    /// <summary>
    /// Ba ván của một Bo3 chỉ chấm MỘT dự đoán. Chấm cả ba thì mẫu trông lớn gấp ba và đường
    /// hiệu chuẩn trông chắc hơn thực tế, trong khi ba ván đó không độc lập với nhau.
    /// </summary>
    [Fact]
    public async Task Mot_du_doan_chi_cham_bang_van_dau_tien_cua_series()
    {
        var (db, ledger, a, b) = await SetupAsync();

        await ledger.SnapshotAsync(new Dictionary<int, double> { [a] = 1600, [b] = 1400 }, T(1), default);
        await db.SaveChangesAsync();

        for (var i = 0; i < 3; i++)
        {
            db.Matches.Add(new Match
            {
                Id = 300 + i, StartTime = T(2).AddMinutes(i * 40),
                RadiantTeamId = a, DireTeamId = b, RadiantWin = i != 0,
            });
        }
        await db.SaveChangesAsync();

        (await ledger.ResolveAsync(T(6), default)).Should().Be(1);
        await db.SaveChangesAsync();

        var p = await db.Predictions.SingleAsync();
        p.ResolvedMatchId.Should().Be(300, "ván đầu tiên sau lúc ghi");
        p.TeamAWon.Should().BeFalse("ván 300 Radiant thua, mà Alpha là Radiant");
    }

    /// <summary>
    /// Một ván chỉ được chấm cho MỘT dự đoán. Hai bản ghi cùng cặp ở hai thời điểm mà cùng ăn
    /// một ván thì mẫu bị đếm đôi và Brier sai theo.
    /// </summary>
    [Fact]
    public async Task Mot_van_khong_duoc_cham_cho_hai_du_doan()
    {
        var (db, ledger, a, b) = await SetupAsync();

        await ledger.SnapshotAsync(new Dictionary<int, double> { [a] = 1600, [b] = 1400 }, T(1), default);
        await db.SaveChangesAsync();
        await ledger.SnapshotAsync(new Dictionary<int, double> { [a] = 1700, [b] = 1400 }, T(2), default);
        await db.SaveChangesAsync();

        (await db.Predictions.CountAsync()).Should().Be(2);

        db.Matches.Add(new Match
        {
            Id = 444, StartTime = T(3), RadiantTeamId = a, DireTeamId = b, RadiantWin = true,
        });
        await db.SaveChangesAsync();

        (await ledger.ResolveAsync(T(4), default)).Should().Be(1, "chỉ một dự đoán được chấm");
        await db.SaveChangesAsync();

        (await db.Predictions.CountAsync(p => p.ResolvedMatchId == 444)).Should().Be(1);
    }

    /// <summary>Chạy lại không được chấm lại — nếu không mỗi vòng nạp sẽ đếm thêm một lần.</summary>
    [Fact]
    public async Task Cham_lai_lan_hai_khong_doi_gi()
    {
        var (db, ledger, a, b) = await SetupAsync();

        await ledger.SnapshotAsync(new Dictionary<int, double> { [a] = 1600, [b] = 1400 }, T(1), default);
        await db.SaveChangesAsync();

        db.Matches.Add(new Match
        {
            Id = 555, StartTime = T(2), RadiantTeamId = a, DireTeamId = b, RadiantWin = true,
        });
        await db.SaveChangesAsync();

        await ledger.ResolveAsync(T(3), default);
        await db.SaveChangesAsync();

        (await ledger.ResolveAsync(T(4), default)).Should().Be(0);
    }
}
