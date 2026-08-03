using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Seeding;

namespace Ti2026.Tests;

/// <summary>
/// Test hồi quy cho 7 lỗi tìm ra khi review đối kháng M1. Mỗi test dưới đây ĐỎ trên phiên bản
/// EditorialSeeder trước khi sửa. Dùng file JSON tự dựng trong thư mục tạm để mô phỏng đúng
/// quy trình sửa tay của người dùng.
/// </summary>
public class EditorialSeederRegressionTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"ti2026-reg-{Guid.NewGuid():N}.db");
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"ti2026-reg-data-{Guid.NewGuid():N}");

    public EditorialSeederRegressionTests() => Directory.CreateDirectory(_dir);

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    private const string TeamsTemplate = """
        {
          "teams": [
            {"name":"Alpha","short":"ALP","slug":"alpha","region":"EU",
             "logo":"https://x/a.pngLOGOSUFFIX",
             "stats":{"maps":100,"winrate":60,"kills":27.5,"deaths":24.5,"assists":60.0,
                      "firstBlood":50,"f10":55,"winWhenFb":62,"winWhenF10":78,
                      "duration":42,"totalKills":52.0,"killDiff":3.0}},
            {"name":"Beta","short":"BET","slug":"beta","region":"CN"}
          ]
        }
        """;

    private void WriteTeams(string extraLogoSuffix = "") => File.WriteAllText(
        Path.Combine(_dir, "teams.json"),
        TeamsTemplate.Replace("LOGOSUFFIX", extraLogoSuffix));

    private void WriteRosters(string json) =>
        File.WriteAllText(Path.Combine(_dir, "rosters.json"), json);

    private EditorialSeeder Seeder(Ti2026DbContext db) => new(db, _dir);

    // ---------- #1 ----------

    [Fact]
    public async Task Sua_rosters_json_ma_teams_json_khong_doi_thi_VAN_phai_nap_lai()
    {
        WriteTeams();
        WriteRosters("""{"rosters":{"alpha":[{"nick":"AAA","role":"CORE"}]}}""");

        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);
        (await db.Players.CountAsync()).Should().Be(1);

        // Quy trình thật của người dùng: chỉ sửa rosters.json rồi redeploy
        WriteRosters("""{"rosters":{"alpha":[{"nick":"AAA","role":"CORE"},{"nick":"BBB","role":"MID"}]}}""");

        var result = await Seeder(db).SeedAsync(CancellationToken.None);

        result.Skipped.Should().BeFalse("hash phải theo từng file, không gate chung teams.json");
        (await db.Players.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Khong_file_nao_doi_thi_bo_qua()
    {
        WriteTeams();
        WriteRosters("""{"rosters":{"alpha":[{"nick":"AAA","role":"CORE"}]}}""");

        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);

        (await Seeder(db).SeedAsync(CancellationToken.None)).Skipped.Should().BeTrue();
    }

    // ---------- #2 ----------

    [Fact]
    public async Task Player_chuyen_doi_khong_duoc_hien_o_ca_hai_doi()
    {
        WriteTeams();
        WriteRosters("""{"rosters":{"alpha":[{"nick":"MOVER","role":"CORE"}],"beta":[]}}""");

        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);

        // MOVER rời alpha sang beta
        WriteRosters("""{"rosters":{"alpha":[],"beta":[{"nick":"MOVER","role":"CORE"}]}}""");
        var result = await Seeder(db).SeedAsync(CancellationToken.None);

        result.RostersClosed.Should().Be(1);

        var openTeams = await db.RosterEntries
            .Where(r => r.ValidTo == null)
            .Include(r => r.Team)
            .Select(r => r.Team!.Slug)
            .ToListAsync();

        openTeams.Should().Equal(["beta"], "bản ghi ở alpha phải được đóng, không để mở vĩnh viễn");
        (await db.RosterEntries.CountAsync()).Should().Be(2, "vẫn giữ lịch sử: 1 đã đóng + 1 đang mở");
    }

    [Fact]
    public async Task Player_nghi_han_thi_duoc_dong_chu_khong_nam_mai_trong_doi_cu()
    {
        WriteTeams();
        WriteRosters("""{"rosters":{"alpha":[{"nick":"RETIRE","role":"CORE"}]}}""");

        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);

        WriteRosters("""{"rosters":{"alpha":[]}}""");
        await Seeder(db).SeedAsync(CancellationToken.None);

        (await db.RosterEntries.CountAsync(r => r.ValidTo == null)).Should().Be(0);
    }

    // ---------- #3 ----------

    [Fact]
    public async Task Liet_ke_trung_mot_nguoi_trong_cung_doi_chi_tao_mot_hang()
    {
        WriteTeams();
        // Hand-edit / merge để lọt hai lần cùng một người, kèm một người mới để kích hoạt
        // SaveChanges giữa vòng lặp — chính điều làm bug cũ trở nên bất định
        WriteRosters("""
        {"rosters":{"alpha":[
          {"nick":"DUP","role":"CORE"},
          {"nick":"NEW","role":"MID"},
          {"nick":"DUP","role":"CORE"}
        ]}}
        """);

        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);

        (await db.RosterEntries.CountAsync(r => r.ValidTo == null)).Should().Be(2);
        (await db.Players.CountAsync()).Should().Be(2);
    }

    // ---------- #4 ----------

    [Fact]
    public async Task Sua_hoa_thuong_cua_nick_khong_tao_player_thu_hai()
    {
        WriteTeams();
        WriteRosters("""{"rosters":{"alpha":[{"nick":"ATF","role":"CORE"}]}}""");

        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);

        // Editor sửa lại cách viết hoa — vẫn là cùng một người
        WriteRosters("""{"rosters":{"alpha":[{"nick":"Atf","role":"CORE"}]}}""");
        await Seeder(db).SeedAsync(CancellationToken.None);

        (await db.Players.CountAsync()).Should().Be(1,
            "SQLite so sánh chuỗi theo BINARY nên phải tra bằng NickKey đã chuẩn hoá");
        (await db.RosterEntries.CountAsync(r => r.ValidTo == null)).Should().Be(1);
        (await db.Players.FirstAsync()).Nick.Should().Be("Atf", "hiển thị theo bản mới nhất");
    }

    [Fact]
    public async Task Khoang_trang_dau_cuoi_khong_tao_player_thu_hai()
    {
        WriteTeams();
        WriteRosters("""{"rosters":{"alpha":[{"nick":"  Yatoro  ","role":"CORE"}]}}""");

        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);

        (await db.Players.FirstAsync()).Nick.Should().Be("Yatoro");
    }

    // ---------- #5 ----------

    [Fact]
    public async Task Seed_nap_luon_12_chi_so_de_trang_khong_render_trang()
    {
        WriteTeams();
        WriteRosters("""{"rosters":{"alpha":[]}}""");

        using var db = NewDb();
        var result = await Seeder(db).SeedAsync(CancellationToken.None);

        result.SnapshotsWritten.Should().Be(1, "chỉ alpha có khối stats");

        var snap = await db.TeamStatSnapshots.SingleAsync();
        snap.WindowDays.Should().Be(180, "teams.json là cửa sổ 6 tháng");
        snap.Maps.Should().Be(100);
        snap.Winrate.Should().Be(60);
        snap.Wins.Should().Be(60);
        snap.Losses.Should().Be(40);
        snap.AvgKills.Should().Be(27.5);
        snap.AvgDurationMinutes.Should().Be(42, "duration trong JSON là PHÚT");
        snap.KillDiff.Should().Be(3.0);
        snap.WinWhenF10Rate.Should().Be(78);
    }

    [Fact]
    public async Task Seed_lai_khong_nhan_doi_snapshot_trong_cung_ngay()
    {
        WriteTeams();
        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);

        WriteTeams("?v=2");   // đổi logo -> hash teams.json đổi -> chạy lại
        await Seeder(db).SeedAsync(CancellationToken.None);

        (await db.TeamStatSnapshots.CountAsync()).Should().Be(1);
    }

    // ---------- #6 ----------

    [Fact]
    public async Task JSON_go_sai_khong_de_lai_trang_thai_nua_voi()
    {
        WriteTeams();
        WriteRosters("""{"rosters":{"alpha":[{"nick":"AAA","role":"CORE"}]}}""");

        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);
        var teamsBefore = await db.Teams.CountAsync();
        var playersBefore = await db.Players.CountAsync();

        // teams.json bị gõ sai cú pháp
        File.WriteAllText(Path.Combine(_dir, "teams.json"), "{ \"teams\": [ , ] }");

        var act = () => Seeder(db).SeedAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        db.ChangeTracker.Clear();
        (await db.Teams.CountAsync()).Should().Be(teamsBefore, "transaction phải rollback");
        (await db.Players.CountAsync()).Should().Be(playersBefore);
    }

    // ---------- openDotaTeamId: chỉ định tay ----------

    [Fact]
    public async Task Chi_dinh_tay_openDotaTeamId_duoc_ghi_vao_DB()
    {
        File.WriteAllText(Path.Combine(_dir, "teams.json"), """
        {"teams":[{"name":"Alpha","slug":"alpha","openDotaTeamId":7119388}]}
        """);

        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);

        (await db.Teams.FirstAsync()).OpenDotaTeamId.Should().Be(7119388);
    }

    [Fact]
    public async Task De_trong_thi_KHONG_xoa_gia_tri_resolver_da_gan()
    {
        WriteTeams();
        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);

        // Resolver gán tự động
        var team = await db.Teams.FirstAsync(t => t.Slug == "alpha");
        team.OpenDotaTeamId = 555;
        await db.SaveChangesAsync();

        WriteTeams("?v=2");   // teams.json đổi nhưng vẫn KHÔNG có openDotaTeamId
        await Seeder(db).SeedAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        (await db.Teams.FirstAsync(t => t.Slug == "alpha")).OpenDotaTeamId.Should().Be(555,
            "để trống nghĩa là \"không có ý kiến\", không phải \"hãy xoá đi\"");
    }

    /// <summary>
    /// Unique index không cho hai đội cùng trỏ một id. Nếu resolver đã gán nhầm id đó cho
    /// đội khác thì chỉ định tay phải gỡ ra được, nếu không app sẽ nổ DbUpdateException
    /// ngay lúc startup và không lên nổi.
    /// </summary>
    [Fact]
    public async Task Chi_dinh_tay_go_duoc_id_khoi_doi_dang_giu_nham()
    {
        WriteTeams();
        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);

        // Resolver gán nhầm 7119388 cho beta
        var beta = await db.Teams.FirstAsync(t => t.Slug == "beta");
        beta.OpenDotaTeamId = 7119388;
        await db.SaveChangesAsync();

        // Người biên tập chỉ định id đó cho alpha
        File.WriteAllText(Path.Combine(_dir, "teams.json"), """
        {"teams":[
          {"name":"Alpha","slug":"alpha","openDotaTeamId":7119388},
          {"name":"Beta","slug":"beta"}
        ]}
        """);

        var act = () => Seeder(db).SeedAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        db.ChangeTracker.Clear();
        (await db.Teams.FirstAsync(t => t.Slug == "alpha")).OpenDotaTeamId.Should().Be(7119388);
        (await db.Teams.FirstAsync(t => t.Slug == "beta")).OpenDotaTeamId.Should().BeNull();
    }

    // ---------- #7 ----------

    [Fact]
    public async Task Doi_vai_tro_trong_cung_ngay_khong_tao_hang_dai_bang_khong()
    {
        WriteTeams();
        WriteRosters("""{"rosters":{"alpha":[{"nick":"AAA","role":"CORE"}]}}""");

        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);

        WriteRosters("""{"rosters":{"alpha":[{"nick":"AAA","role":"MID"}]}}""");
        await Seeder(db).SeedAsync(CancellationToken.None);

        var entries = await db.RosterEntries.ToListAsync();
        entries.Should().HaveCount(1, "sửa trong cùng ngày thì cập nhật tại chỗ");
        entries[0].Role.Should().Be("MID");
        entries[0].ValidTo.Should().BeNull();
    }

    [Fact]
    public async Task Doi_vai_tro_khac_ngay_tao_hai_khoang_ROI_NHAU_theo_quy_uoc_nua_mo()
    {
        WriteTeams();
        WriteRosters("""{"rosters":{"alpha":[{"nick":"AAA","role":"CORE"}]}}""");

        using var db = NewDb();
        await Seeder(db).SeedAsync(CancellationToken.None);

        // Giả lập bản ghi được tạo từ hôm qua
        var old = await db.RosterEntries.SingleAsync();
        old.ValidFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5);
        await db.SaveChangesAsync();

        WriteRosters("""{"rosters":{"alpha":[{"nick":"AAA","role":"MID"}]}}""");
        await Seeder(db).SeedAsync(CancellationToken.None);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var entries = await db.RosterEntries.OrderBy(r => r.ValidFrom).ToListAsync();

        entries.Should().HaveCount(2);
        entries[0].ValidTo.Should().Be(today);
        entries[1].ValidFrom.Should().Be(today);

        // Quy ước nửa mở [ValidFrom, ValidTo): đúng ngày hôm nay chỉ MỘT hàng hiệu lực
        var activeToday = entries.Count(r =>
            r.ValidFrom <= today && (r.ValidTo == null || r.ValidTo > today));
        activeToday.Should().Be(1, "hai khoảng phải rời nhau, không cùng nhận ngày đổi");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { }
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!,
                     Path.GetFileNameWithoutExtension(_dbPath) + "*"))
            try { File.Delete(f); } catch { }
    }
}
