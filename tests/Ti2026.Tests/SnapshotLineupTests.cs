using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Snapshots;

namespace Ti2026.Tests;

/// <summary>
/// Form và Elo chỉ được tính trên ĐỘI HÌNH HIỆN TẠI.
///
/// Hai luật khác nhau, và sự khác nhau đó là điểm chính:
///
/// • Form là chỉ số MÔ TẢ chính đội đó chơi thế nào, nên chỉ đòi hỏi ĐỘI ĐÓ đủ 5 người.
/// • Elo là số TƯƠNG ĐỐI, cập nhật rating của X bằng rating hiện tại của Y — Y lúc ấy không
///   phải Y là sai phép tính — nên đòi hỏi CẢ HAI bên.
/// </summary>
public class SnapshotLineupTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"ti2026-snap-{Guid.NewGuid():N}.db");

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    private static readonly DateOnly Today = new(2026, 8, 6);
    private long _nextId = 800_000_000;

    private static Team NewTeam(Ti2026DbContext db, string slug)
    {
        var t = new Team { Slug = slug, Name = slug.ToUpperInvariant() };
        db.Teams.Add(t);
        return t;
    }

    /// <summary>Tạo 5 người và gắn vào đội hình ĐANG hiệu lực của đội.</summary>
    private static List<Player> Roster(Ti2026DbContext db, Team team)
    {
        var roles = new[] { "CORE", "MID", "OFFLANE", "SUPPORT", "FULL SUPPORT" };
        var players = roles.Select((r, i) =>
        {
            var nick = $"{team.Slug}-{i}";
            return new Player { Nick = nick, NickKey = Player.MakeNickKey(nick) };
        }).ToList();

        db.Players.AddRange(players);
        db.SaveChanges();

        for (var i = 0; i < 5; i++)
            db.RosterEntries.Add(new RosterEntry
            {
                TeamId = team.Id, PlayerId = players[i].Id, Role = roles[i],
                ValidFrom = new DateOnly(2026, 1, 1), ValidTo = null,
            });

        return players;
    }

    private void AddGame(
        Ti2026DbContext db, Team rad, Team dire, List<Player> radRoster, List<Player> direRoster,
        int keptRad, int keptDire, bool radiantWin, int daysAgo)
    {
        var id = _nextId++;
        var when = Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

        db.Matches.Add(new Match
        {
            Id = id, SeriesId = id, StartTime = when, DurationSeconds = 2100,
            LeagueName = "Giải kiểm thử",
            RadiantTeamId = rad.Id, DireTeamId = dire.Id,
            RadiantWin = radiantWin, RadiantScore = 25, DireScore = 20, IngestedAt = when,
        });

        for (var i = 0; i < 5; i++)
        {
            db.MatchPlayers.Add(new MatchPlayer
            {
                MatchId = id, IsRadiant = true, HeroId = i + 1,
                PlayerId = i < keptRad ? radRoster[i].Id : null,
            });
            db.MatchPlayers.Add(new MatchPlayer
            {
                MatchId = id, IsRadiant = false, HeroId = i + 6,
                PlayerId = i < keptDire ? direRoster[i].Id : null,
            });
        }
    }

    private async Task<Dictionary<string, TeamStatSnapshot>> RunAsync(Ti2026DbContext db)
    {
        await new SnapshotWriter(db).WriteAsync(Today, CancellationToken.None);

        return await db.TeamStatSnapshots
            .Where(s => s.CapturedOn == Today && s.WindowDays == 180)
            .Include(s => s.Team)
            .ToDictionaryAsync(s => s.Team!.Slug);
    }

    /// <summary>
    /// Form chỉ đếm ván mà CHÍNH đội đó đủ 5 người — thắng bằng đội hình cũ không được cộng vào
    /// winrate của đội hình hôm nay.
    /// </summary>
    [Fact]
    public async Task Form_bo_van_ma_chinh_doi_do_khong_du_doi_hinh()
    {
        using var db = NewDb();
        var a = NewTeam(db, "alpha");
        var b = NewTeam(db, "beta");
        await db.SaveChangesAsync();
        var ra = Roster(db, a);
        var rb = Roster(db, b);
        await db.SaveChangesAsync();

        // 4 ván a đủ đội hình và THUA hết
        for (var i = 0; i < 4; i++)
            AddGame(db, a, b, ra, rb, keptRad: 5, keptDire: 5, radiantWin: false, daysAgo: 10 + i);

        // 6 ván a chỉ còn 3 người và THẮNG hết — đây là đội hình cũ, không được tính
        for (var i = 0; i < 6; i++)
            AddGame(db, a, b, ra, rb, keptRad: 3, keptDire: 5, radiantWin: true, daysAgo: 30 + i);

        await db.SaveChangesAsync();
        var snaps = await RunAsync(db);

        snaps["alpha"].Maps.Should().Be(4, "chỉ 4 ván là của đội hình hiện tại");
        snaps["alpha"].Winrate.Should().Be(0, "đội hình hôm nay thua cả 4; 6 ván thắng là đội khác");

        // Không lọc thì b đã ăn đủ 10 ván; b luôn đủ đội hình nên b giữ cả 10
        snaps["beta"].Maps.Should().Be(10);
    }

    /// <summary>
    /// Elo đòi hỏi CẢ HAI bên. Đây là chỗ khác biệt so với form, và cũng là chỗ dễ làm sai
    /// nhất: lấy một bên là đủ thì rating của X bị chấm bằng sức mạnh của một đội Y không hề đá.
    /// </summary>
    [Fact]
    public async Task Elo_chi_tinh_van_ma_CA_HAI_ben_deu_du_doi_hinh()
    {
        using var db = NewDb();
        var a = NewTeam(db, "alpha");
        var b = NewTeam(db, "beta");
        await db.SaveChangesAsync();
        var ra = Roster(db, a);
        var rb = Roster(db, b);
        await db.SaveChangesAsync();

        // 12 ván cả hai bên đủ đội hình -> vượt ngưỡng, có Elo
        for (var i = 0; i < 12; i++)
            AddGame(db, a, b, ra, rb, keptRad: 5, keptDire: 5, radiantWin: true, daysAgo: 5 + i);

        // 40 ván nữa nhưng b thiếu người -> a đủ, vẫn KHÔNG được tính vào Elo
        for (var i = 0; i < 40; i++)
            AddGame(db, a, b, ra, rb, keptRad: 5, keptDire: 3, radiantWin: true, daysAgo: 60 + i);

        await db.SaveChangesAsync();
        var snaps = await RunAsync(db);

        snaps["alpha"].EloGames.Should().Be(12, "40 ván kia có bên beta là đội hình khác");
        snaps["alpha"].Elo.Should().NotBeNull();

        // Form của a thì ĐƯỢC tính cả 52 ván — a luôn đủ đội hình. Đây chính là chỗ hai luật
        // khác nhau, và nếu ai đó gộp chúng lại thì bài kiểm này đổ.
        snaps["alpha"].Maps.Should().Be(52);
    }

    [Fact]
    public async Task Chua_du_van_thi_KHONG_cong_bo_Elo_thay_vi_de_1500()
    {
        using var db = NewDb();
        var a = NewTeam(db, "alpha");
        var b = NewTeam(db, "beta");
        await db.SaveChangesAsync();
        var ra = Roster(db, a);
        var rb = Roster(db, b);
        await db.SaveChangesAsync();

        // Đúng dưới ngưỡng một ván
        for (var i = 0; i < SnapshotWriter.MinGamesForRating - 1; i++)
            AddGame(db, a, b, ra, rb, keptRad: 5, keptDire: 5, radiantWin: true, daysAgo: 5 + i);

        await db.SaveChangesAsync();
        var snaps = await RunAsync(db);

        snaps["alpha"].EloGames.Should().Be(SnapshotWriter.MinGamesForRating - 1);
        snaps["alpha"].Elo.Should().BeNull(
            "1500 nằm giữa bảng nên hiện số sẽ làm đội chưa biết trông như đội trung bình");
    }

    /// <summary>
    /// Ván CHƯA nạp match detail thì không có dòng MatchPlayer nào, tức là KHÔNG BIẾT ai ra sân —
    /// và không biết thì không dùng để kết luận.
    ///
    /// Đây là lựa chọn có chủ ý, không phải tác dụng phụ. Cái giá phải trả: một ván vừa đá xong
    /// chưa được tính vào form cho tới khi detail về. Chấp nhận được vì detail nạp trong cùng
    /// một vòng pipeline, và nếu tồn đọng thì api/ingest/status có pendingDetails để thấy.
    /// Chiều ngược lại — đoán bừa là đội hình hiện tại — mới là thứ làm hỏng số liệu ngầm.
    /// </summary>
    [Fact]
    public async Task Van_chua_co_match_detail_thi_khong_duoc_doan_bua_la_doi_hinh_hien_tai()
    {
        using var db = NewDb();
        var a = NewTeam(db, "alpha");
        var b = NewTeam(db, "beta");
        await db.SaveChangesAsync();
        var ra = Roster(db, a);
        var rb = Roster(db, b);
        await db.SaveChangesAsync();

        for (var i = 0; i < 3; i++)
            AddGame(db, a, b, ra, rb, keptRad: 5, keptDire: 5, radiantWin: true, daysAgo: 5 + i);

        // Một ván chỉ có ở mức đội, chưa có MatchPlayer nào
        var bare = _nextId++;
        db.Matches.Add(new Match
        {
            Id = bare, StartTime = Today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc),
            DurationSeconds = 2100, RadiantTeamId = a.Id, DireTeamId = b.Id,
            RadiantWin = false, RadiantScore = 5, DireScore = 40,
            IngestedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        var snaps = await RunAsync(db);

        snaps["alpha"].Maps.Should().Be(3, "ván chưa có detail không được tính");
        snaps["alpha"].Winrate.Should().Be(100, "trận thua kia chưa biết ai đá nên chưa tính");
    }

    /// <summary>
    /// Hàng snapshot của một ngày phải mang Elo CỦA ngày đó. Thiếu chặn này thì mỗi lần nạp lại,
    /// mọi hàng lịch sử đều nhận Elo hôm nay và biểu đồ phong độ thành đường phẳng của hiện tại.
    /// </summary>
    [Fact]
    public async Task Chup_lai_ngay_cu_thi_khong_duoc_dung_van_dien_ra_sau_ngay_do()
    {
        using var db = NewDb();
        var a = NewTeam(db, "alpha");
        var b = NewTeam(db, "beta");
        await db.SaveChangesAsync();
        var ra = Roster(db, a);
        var rb = Roster(db, b);
        await db.SaveChangesAsync();

        for (var i = 0; i < 12; i++)
            AddGame(db, a, b, ra, rb, keptRad: 5, keptDire: 5, radiantWin: true, daysAgo: 2 + i);

        await db.SaveChangesAsync();

        var older = Today.AddDays(-8);
        await new SnapshotWriter(db).WriteAsync(older, CancellationToken.None);

        var snap = await db.TeamStatSnapshots
            .Include(s => s.Team)
            .FirstAsync(s => s.CapturedOn == older && s.WindowDays == 180 && s.Team!.Slug == "alpha");

        // Tính tới 08/06 - 8 ngày thì mới có 6 ván diễn ra (daysAgo 8..13)
        snap.EloGames.Should().Be(6);
        snap.EloGames.Should().BeLessThan(12, "không được nhìn thấy ván của tương lai");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* file bị giữ, bỏ qua */ }
        GC.SuppressFinalize(this);
    }
}
