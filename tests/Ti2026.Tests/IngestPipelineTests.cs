using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest;
using Ti2026.Ingest.OpenDota;
using Ti2026.Ingest.Snapshots;

namespace Ti2026.Tests;

public class IngestPipelineTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"ti2026-pipe-{Guid.NewGuid():N}.db");

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    private static IngestOrchestrator Orchestrator(Ti2026DbContext db, int minTeams = 16) =>
        new(db, new SanityThresholds(minTeams, 60), NullLogger<IngestOrchestrator>.Instance);

    // ---------- Orchestrator ----------

    [Fact]
    public async Task Truot_sanity_gate_thi_danh_Failed_va_giu_nguyen_du_lieu_cu()
    {
        using var db = NewDb();
        db.Teams.Add(new Team { Slug = "old-team", Name = "Dữ liệu cũ tốt" });
        await db.SaveChangesAsync();

        // Ingester giả ghi thêm một đội rồi báo chỉ ghi được 0 — mô phỏng nguồn đổi layout
        var run = await Orchestrator(db).RunSourceAsync("dltv", async ct =>
        {
            db.Teams.Add(new Team { Slug = "rac", Name = "Rác từ vòng lỗi" });
            await db.SaveChangesAsync(ct);
            return 0;
        }, SanityKind.Teams, CancellationToken.None);

        run.Status.Should().Be(IngestStatus.Failed);
        run.ErrorMessage.Should().Contain("0").And.Contain("16");

        db.ChangeTracker.Clear();
        (await db.Teams.CountAsync()).Should().Be(1, "vòng lỗi phải bị rollback hoàn toàn");
        (await db.Teams.AnyAsync(t => t.Slug == "old-team")).Should().BeTrue();
        (await db.Teams.AnyAsync(t => t.Slug == "rac")).Should().BeFalse();
    }

    /// <summary>
    /// Ingester nào gọi ChangeTracker.Clear() — MatchDetailIngester làm thế để giữ bộ nhớ — sẽ
    /// gỡ luôn bản ghi IngestRun khỏi tracker. Khi đó gán trạng thái rồi SaveChanges không phát
    /// ra UPDATE nào, và vòng chạy nằm lại ở Running VĨNH VIỄN.
    ///
    /// Đã xảy ra thật trên production: match-detail kẹt "Running" trong khi các vòng sau đó
    /// đã xong. Một vòng đã chết trông y hệt một vòng đang chạy, nên trang trạng thái nói sai.
    /// </summary>
    [Fact]
    public async Task Trang_thai_van_duoc_ghi_du_ingester_xoa_change_tracker()
    {
        using var db = NewDb();

        var run = await Orchestrator(db).RunSourceAsync("opendota", async ct =>
        {
            db.Teams.Add(new Team { Slug = "x", Name = "X" });
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            return 1;
        }, SanityKind.None, CancellationToken.None);

        run.Status.Should().Be(IngestStatus.Succeeded);

        db.ChangeTracker.Clear();
        var stored = await db.IngestRuns.OrderByDescending(r => r.StartedAt).FirstAsync();

        stored.Status.Should().Be(IngestStatus.Succeeded,
            "trạng thái phải xuống được DB, không chỉ nằm trên đối tượng trong bộ nhớ");
        stored.FinishedAt.Should().NotBeNull("vòng đã xong thì phải có mốc kết thúc");
        stored.ItemsWritten.Should().Be(1);
    }

    [Fact]
    public async Task Ban_ghi_IngestRun_song_sot_qua_rollback()
    {
        using var db = NewDb();

        await Orchestrator(db).RunSourceAsync("dltv",
            _ => Task.FromResult(0), SanityKind.Teams, CancellationToken.None);

        db.ChangeTracker.Clear();
        var runs = await db.IngestRuns.ToListAsync();
        runs.Should().HaveCount(1,
            "ghi IngestRun trong transaction thì rollback sẽ xoá luôn thông tin lỗi");
        runs[0].Status.Should().Be(IngestStatus.Failed);
        runs[0].ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Dat_nguong_thi_danh_Succeeded()
    {
        using var db = NewDb();

        var run = await Orchestrator(db, minTeams: 1).RunSourceAsync("opendota",
            _ => Task.FromResult(5), SanityKind.Teams, CancellationToken.None);

        run.Status.Should().Be(IngestStatus.Succeeded);
        run.ItemsWritten.Should().Be(5);
        run.ErrorMessage.Should().BeNull();
        run.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Ingester_nem_exception_thi_ghi_Failed_chu_khong_lam_sap()
    {
        using var db = NewDb();

        var run = await Orchestrator(db, minTeams: 1).RunSourceAsync("dltv",
            _ => throw new HttpRequestException("dltv sập"),
            SanityKind.Teams, CancellationToken.None);

        run.Status.Should().Be(IngestStatus.Failed);
        run.ErrorMessage.Should().Contain("dltv sập");
    }

    // ---------- TeamResolver ----------

    [Fact]
    public async Task Resolver_khop_theo_ten_va_theo_tag()
    {
        using var db = NewDb();
        db.Teams.AddRange(
            new Team { Slug = "alpha-legends", Name = "Alpha Legends", ShortName = "ALP" },
            new Team { Slug = "gamma", Name = "Gamma Esports" });
        await db.SaveChangesAsync();

        var odTeams = await new OpenDotaClientTestsHelper().TeamsFromFixture();
        var resolved = await new TeamResolver(db, NullLogger<TeamResolver>.Instance)
            .ResolveAsync(odTeams, CancellationToken.None);

        resolved.Should().Be(2);
        (await db.Teams.FirstAsync(t => t.Slug == "alpha-legends")).OpenDotaTeamId.Should().Be(9000001);
        (await db.Teams.FirstAsync(t => t.Slug == "gamma")).OpenDotaTeamId.Should().Be(9000003);
    }

    [Fact]
    public async Task Resolver_khop_bo_qua_hoa_thuong_va_dau_gach()
    {
        using var db = NewDb();
        db.Teams.Add(new Team { Slug = "alpha", Name = "alpha-legends" });
        await db.SaveChangesAsync();

        var odTeams = await new OpenDotaClientTestsHelper().TeamsFromFixture();
        await new TeamResolver(db, NullLogger<TeamResolver>.Instance)
            .ResolveAsync(odTeams, CancellationToken.None);

        (await db.Teams.FirstAsync()).OpenDotaTeamId.Should().Be(9000001,
            "\"alpha-legends\" phải khớp \"Alpha Legends\"");
    }

    /// <summary>
    /// Hồi quy cho lỗi phát hiện trên production: đội "Aurora Gaming" của ta bị gán cho một
    /// đội khớp yếu theo tag, rồi từ chối chính ứng viên trùng tên đầy đủ vì "đến sau".
    /// Trùng tên đầy đủ phải luôn THẮNG khớp theo tag.
    /// </summary>
    [Fact]
    public async Task Resolver_uu_tien_trung_ten_day_du_hon_trung_tag()
    {
        using var db = NewDb();
        db.Teams.Add(new Team { Slug = "aurora", Name = "Aurora Gaming", ShortName = "AUR" });
        await db.SaveChangesAsync();

        // Ứng viên khớp-tag đứng TRƯỚC trong danh sách, đúng như thứ tự đã gây lỗi
        var odTeams = new List<OpenDotaTeam>
        {
            new() { TeamId = 111, Name = "Aurora Ascension", Tag = "AUR" },
            new() { TeamId = 222, Name = "Aurora Gaming", Tag = "AG" },
        };

        await new TeamResolver(db, NullLogger<TeamResolver>.Instance)
            .ResolveAsync(odTeams, CancellationToken.None);

        (await db.Teams.FirstAsync()).OpenDotaTeamId.Should().Be(222,
            "trùng tên đầy đủ mạnh hơn trùng tag, bất kể thứ tự trong danh sách");
    }

    [Fact]
    public async Task Resolver_bo_qua_tag_qua_ngan_de_tranh_khop_bua()
    {
        using var db = NewDb();
        db.Teams.Add(new Team { Slug = "betboom", Name = "BetBoom Team", ShortName = "BB" });
        await db.SaveChangesAsync();

        // "BB" khop ca BarBrothers lan BITCHBUILD — bang chung qua yeu de gan
        var odTeams = new List<OpenDotaTeam>
        {
            new() { TeamId = 301, Name = "BarBrothers", Tag = "BB" },
            new() { TeamId = 302, Name = "BITCHBUILD", Tag = "BB" },
        };

        var resolved = await new TeamResolver(db, NullLogger<TeamResolver>.Instance)
            .ResolveAsync(odTeams, CancellationToken.None);

        resolved.Should().Be(0);
        (await db.Teams.FirstAsync()).OpenDotaTeamId.Should().BeNull(
            "tag 2 ký tự không đủ phân biệt — gán bừa sẽ quy kết nhầm toàn bộ ván đấu");
    }

    [Fact]
    public async Task Resolver_khong_cho_hai_doi_tro_ve_cung_mot_id()
    {
        using var db = NewDb();
        db.Teams.AddRange(
            new Team { Slug = "a", Name = "Alpha Legends" },
            new Team { Slug = "b", Name = "Alpha Legends" });   // trùng tên, tình huống hiếm
        await db.SaveChangesAsync();

        var odTeams = new List<OpenDotaTeam> { new() { TeamId = 9000001, Name = "Alpha Legends" } };

        await new TeamResolver(db, NullLogger<TeamResolver>.Instance)
            .ResolveAsync(odTeams, CancellationToken.None);

        var ids = await db.Teams.Select(t => t.OpenDotaTeamId).ToListAsync();
        ids.Count(x => x == 9000001).Should().Be(1, "chỉ một đội được chiếm id đó");
    }

    [Fact]
    public async Task Resolver_tu_choi_khi_co_nhieu_ung_vien_dong_hang()
    {
        using var db = NewDb();
        db.Teams.Add(new Team { Slug = "x", Name = "Team X" });
        await db.SaveChangesAsync();

        var odTeams = new List<OpenDotaTeam>
        {
            new() { TeamId = 401, Name = "Team X" },
            new() { TeamId = 402, Name = "TEAM-X" },   // chuan hoa xong cung ra "TEAMX"
        };

        var resolved = await new TeamResolver(db, NullLogger<TeamResolver>.Instance)
            .ResolveAsync(odTeams, CancellationToken.None);

        resolved.Should().Be(0);
        (await db.Teams.FirstAsync()).OpenDotaTeamId.Should().BeNull();
    }

    [Fact]
    public async Task Resolver_KHONG_doan_khi_khong_khop()
    {
        using var db = NewDb();
        db.Teams.Add(new Team { Slug = "khong-lien-quan", Name = "Đội Không Có Trong OpenDota" });
        await db.SaveChangesAsync();

        var odTeams = await new OpenDotaClientTestsHelper().TeamsFromFixture();
        var resolved = await new TeamResolver(db, NullLogger<TeamResolver>.Instance)
            .ResolveAsync(odTeams, CancellationToken.None);

        resolved.Should().Be(0);
        (await db.Teams.FirstAsync()).OpenDotaTeamId.Should().BeNull(
            "đoán sai sẽ gán toàn bộ ván của một đội cho đội khác — sai lặng lẽ");
    }

    // ---------- SnapshotWriter ----------

    /// <summary>
    /// Gieo 5 người vào đội hình ĐANG hiệu lực của một đội.
    ///
    /// Bắt buộc từ khi form và Elo lọc theo đội hình: ván không có dòng MatchPlayer nào thì hệ
    /// coi là KHÔNG BIẾT ai ra sân, và không biết thì không dùng để kết luận. Thiếu bước này,
    /// mọi ván gieo trong test đều bị loại và snapshot ra rỗng.
    /// </summary>
    private static async Task<List<int>> SeedRosterAsync(Ti2026DbContext db, Team team)
    {
        var roles = new[] { "CORE", "MID", "OFFLANE", "SUPPORT", "FULL SUPPORT" };

        var players = roles.Select((_, i) =>
        {
            var nick = $"{team.Slug}-p{i}";
            return new Player { Nick = nick, NickKey = Player.MakeNickKey(nick) };
        }).ToList();

        db.Players.AddRange(players);
        await db.SaveChangesAsync();

        for (var i = 0; i < roles.Length; i++)
            db.RosterEntries.Add(new RosterEntry
            {
                TeamId = team.Id, PlayerId = players[i].Id, Role = roles[i],
                ValidFrom = new DateOnly(2026, 1, 1), ValidTo = null,
            });

        await db.SaveChangesAsync();
        return players.Select(p => p.Id).ToList();
    }

    private static void AddPlayerRows(
        Ti2026DbContext db, long matchId, IReadOnlyList<int> radiant, IReadOnlyList<int> dire)
    {
        for (var i = 0; i < 5; i++)
        {
            db.MatchPlayers.Add(new MatchPlayer
            { MatchId = matchId, IsRadiant = true, HeroId = i + 1, PlayerId = radiant[i] });
            db.MatchPlayers.Add(new MatchPlayer
            { MatchId = matchId, IsRadiant = false, HeroId = i + 6, PlayerId = dire[i] });
        }
    }

    private static async Task<Team> SeedTeamsWithMatches(Ti2026DbContext db)
    {
        var team = new Team { Slug = "test-team", Name = "Test Team" };
        var foe = new Team { Slug = "foe", Name = "Foe" };
        db.Teams.AddRange(team, foe);
        await db.SaveChangesAsync();

        var teamRoster = await SeedRosterAsync(db, team);
        var foeRoster = await SeedRosterAsync(db, foe);

        var now = DateTime.UtcNow;
        for (var i = 0; i < 4; i++)
        {
            db.Matches.Add(new Match
            {
                Id = 1000 + i,
                StartTime = now.AddDays(-i - 1),
                DurationSeconds = 2400,
                RadiantTeamId = team.Id,
                DireTeamId = foe.Id,
                RadiantWin = i % 2 == 0,
                RadiantScore = 25,
                DireScore = 20,
                IngestedAt = now,
            });

            AddPlayerRows(db, 1000 + i, teamRoster, foeRoster);
        }
        await db.SaveChangesAsync();
        return team;
    }

    [Fact]
    public async Task Snapshot_chay_hai_lan_cung_ngay_khong_nhan_doi()
    {
        using var db = NewDb();
        await SeedTeamsWithMatches(db);
        var writer = new SnapshotWriter(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var first = await writer.WriteAsync(today, CancellationToken.None);
        var count = await db.TeamStatSnapshots.CountAsync();
        var second = await writer.WriteAsync(today, CancellationToken.None);

        count.Should().BeGreaterThan(0);
        (await db.TeamStatSnapshots.CountAsync()).Should().Be(count);
        second.Should().Be(first);
    }

    /// <summary>
    /// Ván có đối thủ NGOÀI 16 đội được lưu với một phe null, và phải bị loại khỏi mọi chỉ số
    /// mức ĐỘI.
    ///
    /// Đây là luật "không có dữ liệu nào là phế vật, nhưng phải dùng đúng chỗ": những ván đó
    /// dùng được cho phân tích hero (tier list, cấm/chọn, mốc lên đồ) và cho chỉ số cá nhân,
    /// nhưng KHÔNG dùng cho Elo, winrate đội, đối đầu hay phân phối kèo — vì đối thủ ở đó
    /// không cùng một mặt bằng, và trộn vào sẽ thổi phồng thành tích của đội một cách vô hình.
    /// </summary>
    [Fact]
    public async Task Van_gap_doi_ngoai_16_doi_KHONG_duoc_tinh_vao_chi_so_doi()
    {
        using var db = NewDb();
        var team = await SeedTeamsWithMatches(db);   // 4 ván hợp lệ, thắng 2

        // Hai ván gặp đối thủ không thuộc 16 đội — thắng cả hai. Nếu bị đếm nhầm thì winrate
        // nhảy từ 50% lên 67% và đội trông khoẻ hơn hẳn thực tế.
        var now = DateTime.UtcNow;
        for (var i = 0; i < 2; i++)
        {
            db.Matches.Add(new Match
            {
                Id = 2000 + i,
                StartTime = now.AddDays(-i - 1),
                DurationSeconds = 2400,
                RadiantTeamId = team.Id,
                DireTeamId = null,          // đối thủ ngoài 16 đội
                RadiantWin = true,
                RadiantScore = 40,
                DireScore = 5,
                IngestedAt = now,
            });
        }
        await db.SaveChangesAsync();

        await new SnapshotWriter(db).WriteAsync(
            DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        var snap = await db.TeamStatSnapshots
            .FirstAsync(s => s.TeamId == team.Id && s.WindowDays == 30);

        snap.Maps.Should().Be(4, "chỉ đếm ván giữa hai đội đều thuộc 16 đội");
        snap.Wins.Should().Be(2);
        snap.Winrate.Should().Be(50, "hai ván thắng đậm kia không được kéo winrate lên");
        snap.AvgKills.Should().Be(25, "40 kill của ván ngoài phạm vi không được trộn vào");
    }

    [Fact]
    public async Task Snapshot_ghi_cho_ca_ba_cua_so()
    {
        using var db = NewDb();
        var team = await SeedTeamsWithMatches(db);

        await new SnapshotWriter(db).WriteAsync(
            DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        var windows = await db.TeamStatSnapshots
            .Where(s => s.TeamId == team.Id)
            .Select(s => s.WindowDays).OrderBy(w => w).ToListAsync();

        windows.Should().Equal([30, 90, 180]);
    }

    [Fact]
    public async Task Snapshot_tinh_dung_tu_goc_nhin_moi_doi()
    {
        using var db = NewDb();
        var team = await SeedTeamsWithMatches(db);   // 4 ván, team ở Radiant, thắng 2

        await new SnapshotWriter(db).WriteAsync(
            DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        var snap = await db.TeamStatSnapshots
            .FirstAsync(s => s.TeamId == team.Id && s.WindowDays == 30);

        snap.Maps.Should().Be(4);
        snap.Wins.Should().Be(2);
        snap.Winrate.Should().Be(50);
        snap.AvgKills.Should().Be(25);
        snap.AvgDeaths.Should().Be(20);
        snap.KillDiff.Should().Be(5);

        // Đối thủ nhìn từ phía họ: thắng 2, kills 20, deaths 25
        var foeSnap = await db.TeamStatSnapshots
            .FirstAsync(s => s.TeamId != team.Id && s.WindowDays == 30);
        foeSnap.AvgKills.Should().Be(20);
        foeSnap.KillDiff.Should().Be(-5);
    }

    /// <summary>
    /// Đây là hành vi giữ cho bảng không mất 5 cột sau khi ingest chạy: giá trị biên tập đã
    /// seed phải được giữ lại khi OpenDota không cung cấp được chỉ số đó.
    /// </summary>
    [Fact]
    public async Task Snapshot_KHONG_ghi_null_de_len_gia_tri_bien_tap()
    {
        using var db = NewDb();
        var team = await SeedTeamsWithMatches(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Giả lập hàng đã seed từ teams.json với đủ 12 chỉ số
        db.TeamStatSnapshots.Add(new TeamStatSnapshot
        {
            TeamId = team.Id, CapturedOn = today, WindowDays = 30, Source = "editorial",
            Maps = 100, Winrate = 60, AvgKills = 27, AvgDeaths = 24,
            AvgAssists = 61.5, FirstBloodRate = 46, F10Rate = 50,
            WinWhenFbRate = 62, WinWhenF10Rate = 78, AvgDurationMinutes = 44,
        });
        await db.SaveChangesAsync();

        await new SnapshotWriter(db).WriteAsync(today, CancellationToken.None);

        db.ChangeTracker.Clear();
        var snap = await db.TeamStatSnapshots
            .FirstAsync(s => s.TeamId == team.Id && s.WindowDays == 30);

        // Chỉ số tính được: cập nhật theo match thật
        snap.Maps.Should().Be(4);
        snap.Winrate.Should().Be(50);
        snap.AvgKills.Should().Be(25);

        // Chỉ số OpenDota không cung cấp: GIỮ giá trị biên tập, không thành null
        snap.AvgAssists.Should().Be(61.5);
        snap.FirstBloodRate.Should().Be(46);
        snap.WinWhenF10Rate.Should().Be(78);

        snap.Source.Should().Be("mixed", "phải ghi rõ là số trộn để không nhầm là số đo");
    }

    [Fact]
    public async Task Snapshot_bo_qua_doi_khong_co_van_nao_thay_vi_ghi_hang_rong()
    {
        using var db = NewDb();
        await SeedTeamsWithMatches(db);

        var lonely = new Team { Slug = "khong-thi-dau", Name = "Không thi đấu" };
        db.Teams.Add(lonely);
        await db.SaveChangesAsync();

        await new SnapshotWriter(db).WriteAsync(
            DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);

        (await db.TeamStatSnapshots.AnyAsync(s => s.TeamId == lonely.Id)).Should().BeFalse(
            "ghi hàng toàn 0 sẽ xoá mất giá trị biên tập đang phục vụ được");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!,
                     Path.GetFileNameWithoutExtension(_dbPath) + "*"))
            try { File.Delete(f); } catch { }
    }
}

/// <summary>Đọc fixture đội qua chính OpenDotaClient để test resolver dùng đúng shape thật.</summary>
internal class OpenDotaClientTestsHelper
{
    public async Task<List<OpenDotaTeam>> TeamsFromFixture()
    {
        var json = OpenDotaClientTests.Fixture("opendota-teams.json");
        return System.Text.Json.JsonSerializer.Deserialize<List<OpenDotaTeam>>(json)!;
    }
}
