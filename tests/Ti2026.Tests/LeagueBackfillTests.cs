using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Tests;

/// <summary>
/// Nạp nốt ván của giải cấp cao mà cả hai bên đều ngoài 16 đội.
///
/// Ingest chính chạy theo từng đội nên ván không có đội nào của ta thì vĩnh viễn không vào DB —
/// đúng với phân tích ĐỘI, nhưng tier list đo meta HERO và ở đó chúng vẫn là bằng chứng hợp lệ.
/// Đo thật ở bản game 60: OpenDota có 2.127 ván pro, ta chỉ có 948.
///
/// Điểm phải giữ đúng: chỉ nạp giải ở ĐÚNG MẶT BẰNG của TI, và biết điều đó bằng cách đếm số
/// đội TI2026 góp mặt chứ không bằng danh sách viết cứng — danh sách viết cứng lạc hậu ngay khi
/// có giải mới, kể cả chính TI2026.
/// </summary>
public class LeagueBackfillTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"ti2026-lb-{Guid.NewGuid():N}.db");

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    /// <summary>Trả JSON cố định cho leagues/{id}/matches, và đếm xem giải nào bị hỏi.</summary>
    private sealed class FakeHandler(Dictionary<long, string> byLeague) : HttpMessageHandler
    {
        public List<long> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var id = long.Parse(path.Split('/')[^2]);
            Asked.Add(id);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    byLeague.GetValueOrDefault(id, "[]"), Encoding.UTF8, "application/json"),
            });
        }
    }

    private static OpenDotaClient Client(FakeHandler h) =>
        new(new HttpClient(h) { BaseAddress = new Uri("https://example.invalid/api/") });

    private static DateTime Recent(int daysAgo) => DateTime.UtcNow.AddDays(-daysAgo);

    /// <summary>Gieo một giải với <paramref name="teamCount"/> đội của ta đã ra sân ở đó.</summary>
    private static void SeedLeague(Ti2026DbContext db, long leagueId, string name, int teamCount)
    {
        var teams = new List<Team>();
        for (var i = 0; i < teamCount; i++)
        {
            var t = new Team { Slug = $"l{leagueId}-t{i}", Name = $"L{leagueId} T{i}" };
            db.Teams.Add(t);
            teams.Add(t);
        }
        db.SaveChanges();

        for (var i = 0; i + 1 < teams.Count; i += 2)
            db.Matches.Add(new Match
            {
                Id = leagueId * 1000 + i,
                StartTime = Recent(10),
                DurationSeconds = 2100,
                LeagueId = leagueId,
                LeagueName = name,
                RadiantTeamId = teams[i].Id,
                DireTeamId = teams[i + 1].Id,
                RadiantWin = true, RadiantScore = 20, DireScore = 10,
                IngestedAt = DateTime.UtcNow,
            });

        db.SaveChanges();
    }

    private static string LeagueJson(long leagueId, IEnumerable<long> matchIds) =>
        "[" + string.Join(",", matchIds.Select(id =>
            $$"""
            {"match_id":{{id}},"radiant_win":true,"radiant_score":21,"dire_score":9,
             "duration":2000,"start_time":{{DateTimeOffset.UtcNow.AddDays(-9).ToUnixTimeSeconds()}},
             "leagueid":{{leagueId}},"series_id":77,"radiant_team_id":111,"dire_team_id":222}
            """)) + "]";

    [Fact]
    public async Task Chi_hoi_giai_du_so_doi_TI_va_bo_qua_giai_cap_thap()
    {
        using var db = NewDb();
        SeedLeague(db, 100, "Giải lớn", LeagueBackfillIngester.MinTiTeams);
        SeedLeague(db, 200, "Vòng loại nhỏ", LeagueBackfillIngester.MinTiTeams - 2);

        var h = new FakeHandler(new Dictionary<long, string>
        {
            [100] = LeagueJson(100, [900001, 900002]),
        });

        var added = await new LeagueBackfillIngester(
            db, Client(h), NullLogger<LeagueBackfillIngester>.Instance).IngestAsync(default);

        h.Asked.Should().Equal([100], "giải cấp thấp không được hỏi tới");
        added.Should().Be(2);

        var fresh = await db.Matches.Where(m => m.Id >= 900000).ToListAsync();
        fresh.Should().HaveCount(2);
        fresh.Should().OnlyContain(m => m.RadiantTeamId == null && m.DireTeamId == null,
            "hai bên đều ngoài 16 đội — đó chính là những ván đang thiếu");
        fresh.Should().OnlyContain(m => m.LeagueName == "Giải lớn");
    }

    /// <summary>
    /// Ván đã có KHÔNG được đụng vào. Ingest theo đội là nguồn chuẩn cho chúng và xử lý được
    /// nhiều trường hợp hơn; để hai nguồn cùng ghi một hàng là tự tạo ra tranh chấp.
    /// </summary>
    [Fact]
    public async Task Khong_ghi_de_van_da_co()
    {
        using var db = NewDb();
        SeedLeague(db, 100, "Giải lớn", LeagueBackfillIngester.MinTiTeams);

        var existing = await db.Matches.FirstAsync();
        var keptTeam = existing.RadiantTeamId;
        var keptScore = existing.RadiantScore;

        var h = new FakeHandler(new Dictionary<long, string>
        {
            [100] = LeagueJson(100, [existing.Id, 900003]),
        });

        var added = await new LeagueBackfillIngester(
            db, Client(h), NullLogger<LeagueBackfillIngester>.Instance).IngestAsync(default);

        added.Should().Be(1, "chỉ ván chưa có mới được thêm");

        db.ChangeTracker.Clear();
        var after = await db.Matches.FirstAsync(m => m.Id == existing.Id);
        after.RadiantTeamId.Should().Be(keptTeam);
        after.RadiantScore.Should().Be(keptScore);
    }

    /// <summary>Ván mới phải ánh xạ được về đội ta khi team_id đã khai.</summary>
    [Fact]
    public async Task Anh_xa_ve_doi_ta_khi_team_id_da_biet()
    {
        using var db = NewDb();
        SeedLeague(db, 100, "Giải lớn", LeagueBackfillIngester.MinTiTeams);

        var mine = await db.Teams.FirstAsync();
        db.TeamOpenDotaIds.Add(new TeamOpenDotaId
        { TeamId = mine.Id, OpenDotaTeamId = 111, Source = "test", AddedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var h = new FakeHandler(new Dictionary<long, string> { [100] = LeagueJson(100, [900004]) });

        await new LeagueBackfillIngester(
            db, Client(h), NullLogger<LeagueBackfillIngester>.Instance).IngestAsync(default);

        var m = await db.Matches.FirstAsync(x => x.Id == 900004);
        m.RadiantTeamId.Should().Be(mine.Id, "team_id 111 đã khai là của đội này");
        m.DireTeamId.Should().BeNull("222 chưa khai cho ai");
    }

    [Fact]
    public async Task Giai_qua_cu_thi_khong_xet_toi()
    {
        using var db = NewDb();
        SeedLeague(db, 100, "Giải lớn", LeagueBackfillIngester.MinTiTeams);

        foreach (var m in await db.Matches.ToListAsync())
            m.StartTime = Recent(LeagueBackfillIngester.RecentDays + 30);
        await db.SaveChangesAsync();

        var h = new FakeHandler(new Dictionary<long, string> { [100] = LeagueJson(100, [900005]) });

        var added = await new LeagueBackfillIngester(
            db, Client(h), NullLogger<LeagueBackfillIngester>.Instance).IngestAsync(default);

        h.Asked.Should().BeEmpty();
        added.Should().Be(0);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* file bị giữ, bỏ qua */ }
        GC.SuppressFinalize(this);
    }
}
