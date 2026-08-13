using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Tests;

/// <summary>
/// Bắt đội của ta ra trận dưới một team_id OpenDota chưa khai.
///
/// Đây là loại hỏng KHÔNG làm gì đổ vỡ: TeamResolver chỉ phân giải đội có OpenDotaTeamId là
/// null, nên đã gán một lần thì không bao giờ kiểm lại; khi một roster đăng ký lại dưới bản ghi
/// mới thì ingest ngừng nhận ván của đội đó và MỌI VÒNG VẪN BÁO "Succeeded".
///
/// Xảy ra thật với 4/16 đội TI2026. Tệ nhất là PariVision: mất nguyên giải EWC 2026 mà họ VÔ
/// ĐỊCH, và chỉ lộ ra vì có người tình cờ hỏi "EWC xong rồi, kiểm tra thử đã cập nhật đủ chưa".
/// </summary>
public class StaleTeamIdTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"ti2026-stale-{Guid.NewGuid():N}.db");

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    private static readonly DateTime Now = new(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc);
    private long _nextId = 700_000_000;

    private static List<long> SeedTeam(Ti2026DbContext db, string slug, int? openDotaId)
    {
        var team = new Team { Slug = slug, Name = slug, OpenDotaTeamId = openDotaId };
        db.Teams.Add(team);
        db.SaveChanges();

        var accounts = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            var nick = $"{slug}-{i}";
            var p = new Player
            {
                Nick = nick, NickKey = Player.MakeNickKey(nick),
                OpenDotaAccountId = team.Id * 1000L + i,
            };
            db.Players.Add(p);
            db.SaveChanges();

            db.RosterEntries.Add(new RosterEntry
            {
                TeamId = team.Id, PlayerId = p.Id, Role = "CORE",
                ValidFrom = new DateOnly(2026, 1, 1), ValidTo = null,
            });
            accounts.Add(p.OpenDotaAccountId!.Value);
        }

        db.SaveChanges();
        return accounts;
    }

    /// <param name="knownSide">Đội nhận diện được ở bên Radiant; null = cũng không nhận ra.</param>
    private void AddMatch(
        Ti2026DbContext db, int? knownSide, IReadOnlyList<long> unknownSideAccounts,
        int daysAgo, int howManyOfThem)
    {
        var id = _nextId++;
        db.Matches.Add(new Match
        {
            Id = id, StartTime = Now.AddDays(-daysAgo), DurationSeconds = 2100,
            RadiantTeamId = knownSide, DireTeamId = null,
            RadiantWin = true, RadiantScore = 20, DireScore = 10, IngestedAt = Now,
        });

        for (var i = 0; i < 5; i++)
            db.MatchPlayers.Add(new MatchPlayer
            {
                MatchId = id, IsRadiant = false, HeroId = i + 1,
                // Người thứ i là của đội ta hay là người lạ
                AccountId = i < howManyOfThem ? unknownSideAccounts[i] : 999_000 + i,
            });
    }

    [Fact]
    public async Task Nhan_ra_doi_ta_o_ben_khong_dinh_danh_duoc_khi_du_5_nguoi()
    {
        using var db = NewDb();
        var foe = SeedTeam(db, "doi-khac", 111);
        var lost = SeedTeam(db, "doi-bi-mat-dau", 222);
        await db.SaveChangesAsync();

        var foeId = await db.Teams.Where(t => t.Slug == "doi-khac").Select(t => t.Id).FirstAsync();

        for (var i = 0; i < 3; i++)
            AddMatch(db, foeId, lost, daysAgo: 10 + i, howManyOfThem: 5);

        await db.SaveChangesAsync();

        var found = await new StaleTeamIdDetector(db, NullLogger<StaleTeamIdDetector>.Instance)
            .FindAsync(Now, default);

        found.Should().HaveCount(3);
        found.Should().OnlyContain(x => x.Slug == "doi-bi-mat-dau" && x.RosterMatched == 5);
    }

    /// <summary>
    /// Một người lạ trong đội hình vẫn phải báo: thay một người thường là đánh thay, và bỏ qua
    /// những ca đó thì đúng ván đáng ngờ nhất lại lọt lưới.
    /// </summary>
    [Fact]
    public async Task Bon_tren_nam_van_bao_nhung_ba_thi_khong()
    {
        using var db = NewDb();
        var foe = SeedTeam(db, "doi-khac", 111);
        var lost = SeedTeam(db, "doi-bi-mat-dau", 222);
        await db.SaveChangesAsync();
        var foeId = await db.Teams.Where(t => t.Slug == "doi-khac").Select(t => t.Id).FirstAsync();

        AddMatch(db, foeId, lost, daysAgo: 5, howManyOfThem: 4);
        AddMatch(db, foeId, lost, daysAgo: 6, howManyOfThem: 3);
        await db.SaveChangesAsync();

        var found = await new StaleTeamIdDetector(db, NullLogger<StaleTeamIdDetector>.Instance)
            .FindAsync(Now, default);

        found.Should().HaveCount(1);
        found[0].RosterMatched.Should().Be(4);
    }

    [Fact]
    public async Task Ván_da_dinh_danh_duoc_ca_hai_ben_thi_khong_bao_gi()
    {
        using var db = NewDb();
        var a = SeedTeam(db, "doi-a", 111);
        var b = SeedTeam(db, "doi-b", 222);
        await db.SaveChangesAsync();

        var ids = await db.Teams.OrderBy(t => t.Slug).Select(t => t.Id).ToListAsync();
        var id = _nextId++;
        db.Matches.Add(new Match
        {
            Id = id, StartTime = Now.AddDays(-3), DurationSeconds = 2100,
            RadiantTeamId = ids[0], DireTeamId = ids[1],
            RadiantWin = true, RadiantScore = 20, DireScore = 10, IngestedAt = Now,
        });
        for (var i = 0; i < 5; i++)
        {
            db.MatchPlayers.Add(new MatchPlayer
            { MatchId = id, IsRadiant = true, HeroId = i + 1, AccountId = a[i] });
            db.MatchPlayers.Add(new MatchPlayer
            { MatchId = id, IsRadiant = false, HeroId = i + 6, AccountId = b[i] });
        }
        await db.SaveChangesAsync();

        var found = await new StaleTeamIdDetector(db, NullLogger<StaleTeamIdDetector>.Instance)
            .FindAsync(Now, default);

        found.Should().BeEmpty();
    }

    [Fact]
    public async Task Chuyen_qua_lau_thi_khong_bao_nua()
    {
        using var db = NewDb();
        var foe = SeedTeam(db, "doi-khac", 111);
        var lost = SeedTeam(db, "doi-bi-mat-dau", 222);
        await db.SaveChangesAsync();
        var foeId = await db.Teams.Where(t => t.Slug == "doi-khac").Select(t => t.Id).FirstAsync();

        AddMatch(db, foeId, lost, daysAgo: StaleTeamIdDetector.LookbackDays + 30, howManyOfThem: 5);
        await db.SaveChangesAsync();

        (await new StaleTeamIdDetector(db, NullLogger<StaleTeamIdDetector>.Instance)
            .FindAsync(Now, default)).Should().BeEmpty();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* file bị giữ, bỏ qua */ }
        GC.SuppressFinalize(this);
    }
}
