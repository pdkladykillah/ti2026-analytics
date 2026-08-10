using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Tests;

/// <summary>
/// Đường ghi xuống DB của phần bối cảnh ván: phân vị và đồng đội.
///
/// Payload dưới đây dựng theo response THẬT của ván 8937662260 — kể cả ô hero_healing_per_min
/// có raw 0 mà pct 0,93, vì đó chính là cái bẫy phải chặn.
/// </summary>
public class TrackedMatchDetailIngesterTests : IDisposable
{
    private const long Me = 230500070;

    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"ti2026-tracked-{Guid.NewGuid():N}.db");

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// Năm người phe Radiant: chủ trang, ba người cùng nhóm, một người ẩn danh. Phe Dire để đủ
    /// 10 người nhưng KHÔNG được xuất hiện trong bảng đồng đội.
    /// </summary>
    private const string Payload = """
    {
      "match_id": 8937662260,
      "radiant_win": true,
      "duration": 2748,
      "start_time": 1770000000,
      "players": [
        {
          "account_id": 230500070, "hero_id": 96, "player_slot": 2, "party_id": 0,
          "personaname": "Bi ba bi bo", "rank_tier": 64,
          "kills": 8, "deaths": 6, "assists": 14,
          "gold_per_min": 696, "xp_per_min": 741, "net_worth": 24100, "level": 27,
          "lane_role": 3,
          "benchmarks": {
            "gold_per_min":        { "raw": 696,   "pct": 0.9634146341463414 },
            "xp_per_min":          { "raw": 741,   "pct": 0.5905006418485238 },
            "last_hits_per_min":   { "raw": 8.879, "pct": 0.94801026957638 },
            "deaths_per_min":      { "raw": 0,     "pct": 0.02 },
            "hero_damage_per_min": { "raw": 875.8, "pct": 0.8883183568677792 },
            "hero_healing_per_min":{ "raw": 0,     "pct": 0.9326059050064185 },
            "tower_damage":        { "raw": 4407,  "pct": 0.7458279845956355 }
          }
        },
        {
          "account_id": 237103955, "hero_id": 87, "player_slot": 0, "party_id": 0,
          "personaname": "Ban mot", "rank_tier": 72,
          "gold_per_min": 500, "xp_per_min": 600, "net_worth": 19000
        },
        {
          "account_id": 180791235, "hero_id": 54, "player_slot": 1, "party_id": 0,
          "personaname": "Ban hai", "rank_tier": 55,
          "gold_per_min": 400, "xp_per_min": 500, "net_worth": 15000
        },
        {
          "account_id": 184370991, "hero_id": 114, "player_slot": 3, "party_id": 7,
          "personaname": "Nguoi la", "rank_tier": 34,
          "gold_per_min": 300, "xp_per_min": 400, "net_worth": 11000
        },
        {
          "account_id": null, "hero_id": 126, "player_slot": 4, "party_id": null,
          "gold_per_min": 250, "xp_per_min": 350, "net_worth": 9000
        },
        { "account_id": 317270679, "hero_id": 1,  "player_slot": 128, "gold_per_min": 600, "net_worth": 20000 },
        { "account_id": 326402696, "hero_id": 2,  "player_slot": 129, "gold_per_min": 500, "net_worth": 18000 },
        { "account_id": 132842063, "hero_id": 3,  "player_slot": 130, "gold_per_min": 400, "net_worth": 16000 },
        { "account_id": 168851674, "hero_id": 4,  "player_slot": 131, "gold_per_min": 300, "net_worth": 12000 },
        { "account_id": 336128173, "hero_id": 5,  "player_slot": 132, "gold_per_min": 200, "net_worth": 8000 }
      ]
    }
    """;

    private static TrackedMatchDetailIngester Ingester(Ti2026DbContext db, HttpMessageHandler h) =>
        new(db,
            new OpenDotaClient(new HttpClient(h) { BaseAddress = new Uri("https://x/api/") }),
            NullLogger<TrackedMatchDetailIngester>.Instance);

    private static async Task SeedAsync(Ti2026DbContext db)
    {
        db.TrackedPlayers.Add(new TrackedPlayer
        {
            Id = 1, AccountId = Me, DisplayName = "PDK", AddedAt = DateTime.UtcNow,
        });

        db.TrackedPlayerMatches.Add(new TrackedPlayerMatch
        {
            TrackedPlayerId = 1, MatchId = 8937662260,
            HeroId = 96, StartTime = DateTime.UtcNow.AddDays(-5),
            DurationSeconds = 2748, Won = true, IsRadiant = true,
        });

        await db.SaveChangesAsync();
    }

    // ---------- Phân vị ----------

    [Fact]
    public async Task Chep_phan_vi_va_bo_o_hoi_mau_rac()
    {
        using var db = NewDb();
        await SeedAsync(db);

        await Ingester(db, new StubHandler(Payload)).IngestAsync(default);

        var row = await db.TrackedPlayerMatches.SingleAsync();

        row.PctGpm.Should().Be(96);
        row.PctLastHits.Should().Be(95);
        row.PctHeroDamage.Should().Be(89);
        row.PctTowerDamage.Should().Be(75);

        row.PctDeaths.Should().Be(2, "không chết lần nào là thành tích thật, phải giữ");
        row.PctHeroHealing.Should().BeNull(
            "hồi 0 máu mà phân vị 93 là rác — quá nửa người chơi hero đó cũng hồi 0");

        row.PctKills.Should().BeNull("nguồn không trả ô này thì để trống, không quy về 0");
    }

    [Fact]
    public async Task Van_lay_ca_boi_canh_doi_va_nhan_vai_tro_that()
    {
        using var db = NewDb();
        await SeedAsync(db);

        await Ingester(db, new StubHandler(Payload)).IngestAsync(default);

        var row = await db.TrackedPlayerMatches.SingleAsync();

        row.NetWorth.Should().Be(24100);
        row.Level.Should().Be(27);
        row.TeamFarmRank.Should().Be(1, "24.100 là cao nhất trong 5 người cùng phe");
        row.LaneRole.Should().Be(3);
        row.DetailFetchedAt.Should().NotBeNull();
    }

    // ---------- Đồng đội ----------

    [Fact]
    public async Task Chi_luu_nguoi_CUNG_PHE_va_bo_nguoi_an_danh()
    {
        using var db = NewDb();
        await SeedAsync(db);

        await Ingester(db, new StubHandler(Payload)).IngestAsync(default);

        var mates = await db.TrackedMatchTeammates.ToListAsync();

        mates.Select(m => m.AccountId).Should().BeEquivalentTo(
            [237103955L, 180791235L, 184370991L],
            "4 người cùng phe trừ chính mình, trừ tiếp người ẩn danh");

        mates.Should().NotContain(m => m.AccountId == Me);
        mates.Should().NotContain(m => m.AccountId == 317270679, "đó là đối thủ");
    }

    /// <summary>
    /// party_id là thứ DUY NHẤT tách bạn bè khỏi người ghép trúng. party_size thì không: một ván
    /// 5 người vẫn có thể gồm hai nhóm 3 và 2.
    /// </summary>
    [Fact]
    public async Task Cung_nhom_suy_tu_party_id_chu_khong_phai_cung_phe()
    {
        using var db = NewDb();
        await SeedAsync(db);

        await Ingester(db, new StubHandler(Payload)).IngestAsync(default);

        var mates = await db.TrackedMatchTeammates.ToDictionaryAsync(m => m.AccountId);

        mates[237103955].SameParty.Should().BeTrue();
        mates[180791235].SameParty.Should().BeTrue();
        mates[184370991].SameParty.Should().BeFalse("party_id 7 khác party_id 0 của chủ trang");
    }

    [Fact]
    public async Task Luu_ten_va_hang_cua_dong_doi()
    {
        using var db = NewDb();
        await SeedAsync(db);

        await Ingester(db, new StubHandler(Payload)).IngestAsync(default);

        var mate = await db.TrackedMatchTeammates.SingleAsync(m => m.AccountId == 237103955);

        mate.PersonaName.Should().Be("Ban mot");
        mate.RankTier.Should().Be(72);
        mate.HeroId.Should().Be(87);
    }

    /// <summary>
    /// Bài kiểm quan trọng nhất của phần ghi. Một ván ĐƯỢC lấy lại nhiều lần — sau khi xin parse
    /// thì DetailFetchedAt bị đặt lại null có chủ ý. Nếu mỗi lần lấy lại đều thêm dòng mới thì
    /// số ván đã chơi cùng mỗi người sẽ tăng dần mà không có gì đổ vỡ: bảng vẫn có thứ hạng hợp
    /// lý, chỉ là mọi con số đều phóng đại.
    /// </summary>
    [Fact]
    public async Task Lay_lai_cung_mot_van_thi_KHONG_nhan_doi_dong_doi()
    {
        using var db = NewDb();
        await SeedAsync(db);

        await Ingester(db, new StubHandler(Payload)).IngestAsync(default);

        var row = await db.TrackedPlayerMatches.SingleAsync();
        row.DetailFetchedAt = null;
        await db.SaveChangesAsync();

        await Ingester(db, new StubHandler(Payload)).IngestAsync(default);

        (await db.TrackedMatchTeammates.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Van_da_lay_roi_thi_khong_goi_lai()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var handler = new StubHandler(Payload);
        await Ingester(db, handler).IngestAsync(default);
        var afterFirst = handler.Calls;

        await Ingester(db, handler).IngestAsync(default);

        handler.Calls.Should().Be(afterFirst, "ván đã có DetailFetchedAt thì không hỏi lại");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }
}
