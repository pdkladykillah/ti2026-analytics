using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Seeding;

namespace Ti2026.Tests;

public class EditorialSeederTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"ti2026-seed-{Guid.NewGuid():N}.db");

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    [Fact]
    public async Task Seed_nap_16_doi_va_doi_hinh_tu_file_that()
    {
        using var db = NewDb();
        var seeder = new EditorialSeeder(db, SeedModelsTests.DataDir());

        var result = await seeder.SeedAsync(CancellationToken.None);

        result.Skipped.Should().BeFalse();
        result.TeamsWritten.Should().Be(16);
        (await db.Teams.CountAsync()).Should().Be(16);
        (await db.RosterEntries.CountAsync()).Should().BeGreaterThan(0);
        (await db.Teams.AnyAsync(t => t.Slug == "team-falcons")).Should().BeTrue();
    }

    [Fact]
    public async Task Seed_giu_dung_ten_vung_va_logo()
    {
        using var db = NewDb();
        await new EditorialSeeder(db, SeedModelsTests.DataDir()).SeedAsync(CancellationToken.None);

        var falcons = await db.Teams.FirstAsync(t => t.Slug == "team-falcons");
        falcons.Name.Should().Be("Team Falcons");
        falcons.ShortName.Should().Be("Falcons");
        falcons.Region.Should().Be("Đa quốc gia");
        falcons.LogoUrl.Should().StartWith("https://dltv.org/");
    }

    [Fact]
    public async Task Seed_lan_hai_bo_qua_vi_hash_file_khong_doi()
    {
        using var db = NewDb();
        var seeder = new EditorialSeeder(db, SeedModelsTests.DataDir());

        await seeder.SeedAsync(CancellationToken.None);
        var teamsAfterFirst = await db.Teams.CountAsync();
        var rostersAfterFirst = await db.RosterEntries.CountAsync();

        var second = await seeder.SeedAsync(CancellationToken.None);

        second.Skipped.Should().BeTrue("hash file không đổi thì không nạp lại");
        (await db.Teams.CountAsync()).Should().Be(teamsAfterFirst);
        (await db.RosterEntries.CountAsync()).Should().Be(rostersAfterFirst,
            "seed lại không được nhân đôi đội hình");
    }

    [Fact]
    public async Task Seed_ghi_alias_de_anh_xa_ten_giua_cac_nguon()
    {
        using var db = NewDb();
        await new EditorialSeeder(db, SeedModelsTests.DataDir()).SeedAsync(CancellationToken.None);

        // teams.json _note ghi: TEAM VISION = PARIVISION, HULIGANI = L1GA TEAM
        var teams = await db.Teams.Include(t => t.Aliases).ToListAsync();
        teams.Should().OnlyContain(t => t.Aliases.Count > 0,
            "mỗi đội phải có ít nhất alias theo tên đầy đủ để ingest ánh xạ được");

        var aliases = await db.TeamAliases.CountAsync();
        aliases.Should().BeGreaterThanOrEqualTo(16);
    }

    [Fact]
    public async Task Seed_dat_moi_roster_o_trang_thai_dang_hieu_luc()
    {
        using var db = NewDb();
        await new EditorialSeeder(db, SeedModelsTests.DataDir()).SeedAsync(CancellationToken.None);

        var entries = await db.RosterEntries.ToListAsync();
        entries.Should().OnlyContain(r => r.ValidTo == null,
            "seed lần đầu thì mọi bản ghi đều đang hiệu lực");
        entries.Should().OnlyContain(r => r.Role.Length > 0);
    }

    public void Dispose()
    {
        // Windows: EF Core pool connection nên file .db bị giữ và File.Delete sẽ ném IOException
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!,
                     Path.GetFileNameWithoutExtension(_dbPath) + "*"))
            try { File.Delete(f); } catch { /* file bị giữ, bỏ qua */ }
    }
}
