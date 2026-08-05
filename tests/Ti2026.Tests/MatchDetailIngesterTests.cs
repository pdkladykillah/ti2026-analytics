using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Tests;

/// <summary>
/// Shape JSON dưới đây dựng theo response THẬT của matches/{id} (đã soi trên 4 trận 7.41):
/// picks_bans 24 lượt với team 0=Radiant, purchase_log có cả món thành phẩm lẫn linh kiện và
/// mốc thời gian ÂM cho đồ mua trước khai cuộc.
/// </summary>
public class MatchDetailIngesterTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"ti2026-detail-{Guid.NewGuid():N}.db");

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

    private const string Payload = """
    {
      "match_id": 900001,
      "radiant_win": true,
      "duration": 2400,
      "start_time": 1770000000,
      "patch": 60,
      "picks_bans": [
        { "is_pick": false, "hero_id": 74, "team": 0, "order": 0 },
        { "is_pick": false, "hero_id": 90, "team": 1, "order": 1 },
        { "is_pick": true,  "hero_id": 54, "team": 0, "order": 2 },
        { "is_pick": true,  "hero_id": 45, "team": 1, "order": 3 }
      ],
      "players": [
        {
          "account_id": 111, "hero_id": 54, "player_slot": 0, "isRadiant": true,
          "kills": 10, "deaths": 2, "assists": 5, "gold_per_min": 700, "xp_per_min": 800,
          "lane_role": 1, "lane": 1, "lane_efficiency_pct": 81,
          "last_hits": 500, "denies": 20, "net_worth": 31253,
          "hero_damage": 40000, "tower_damage": 9000, "obs_placed": 2,
          "purchase_log": [
            { "time": -89, "key": "gauntlets" },
            { "time": 6,   "key": "tango" },
            { "time": 141, "key": "tango" },
            { "time": 316, "key": "phase_boots" },
            { "time": 924, "key": "radiance" }
          ]
        },
        {
          "account_id": 222, "hero_id": 45, "player_slot": 128, "isRadiant": false,
          "kills": 1, "deaths": 9, "assists": 2, "gold_per_min": 300, "xp_per_min": 350,
          "lane_role": 0, "lane": 0,
          "purchase_log": [
            { "time": 400, "key": "phase_boots" }
          ]
        }
      ]
    }
    """;

    private static MatchDetailIngester Ingester(Ti2026DbContext db, HttpMessageHandler handler) =>
        new(db,
            new OpenDotaClient(new HttpClient(handler) { BaseAddress = new Uri("https://x/api/") }),
            NullLogger<MatchDetailIngester>.Instance);

    private static async Task SeedMatchAsync(Ti2026DbContext db, int schemaVersion = 0,
        DateTime? ingestedAt = null)
    {
        db.Matches.Add(new Match
        {
            Id = 900001,
            StartTime = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            RadiantTeamId = null,
            DireTeamId = null,
            RadiantWin = true,
            DetailsIngestedAt = ingestedAt,
            DetailSchemaVersion = schemaVersion,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Ghi_ban_draft_dung_luot_va_dung_ben()
    {
        using var db = NewDb();
        await SeedMatchAsync(db);

        await Ingester(db, new StubHandler(Payload)).IngestAsync(10, CancellationToken.None);

        db.ChangeTracker.Clear();
        var draft = await db.DraftEvents.OrderBy(d => d.Order).ToListAsync();

        draft.Should().HaveCount(4);
        draft[0].IsPick.Should().BeFalse();
        draft[0].HeroId.Should().Be(74);
        draft[0].IsRadiant.Should().BeTrue("team 0 của OpenDota là Radiant");
        draft[1].IsRadiant.Should().BeFalse("team 1 là Dire");
        draft[2].IsPick.Should().BeTrue();
    }

    /// <summary>
    /// Nạp lại là chuyện thường xuyên (nâng SchemaVersion, thử lại sau lỗi mạng). Nếu mỗi lượt
    /// nạp lại nhân đôi bàn draft thì mọi tỷ lệ cấm/chọn sẽ phình lên mà không có gì báo.
    /// </summary>
    [Fact]
    public async Task Nap_lai_khong_nhan_doi_draft_va_item()
    {
        using var db = NewDb();
        await SeedMatchAsync(db);

        await Ingester(db, new StubHandler(Payload)).IngestAsync(10, CancellationToken.None);

        // Hạ phiên bản schema để ingester coi ván này là còn thiếu và nạp lại
        db.ChangeTracker.Clear();
        var m = await db.Matches.FirstAsync();
        m.DetailSchemaVersion = 0;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Ingester(db, new StubHandler(Payload)).IngestAsync(10, CancellationToken.None);

        db.ChangeTracker.Clear();
        (await db.DraftEvents.CountAsync()).Should().Be(4);
        (await db.ItemPurchases.CountAsync()).Should().Be(6);
        (await db.MatchPlayers.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Luu_moc_mua_do_tho_ke_ca_thoi_gian_am_va_mua_trung_lap()
    {
        using var db = NewDb();
        await SeedMatchAsync(db);

        await Ingester(db, new StubHandler(Payload)).IngestAsync(10, CancellationToken.None);

        db.ChangeTracker.Clear();
        var buys = await db.ItemPurchases.Where(p => p.HeroId == 54).ToListAsync();

        buys.Should().HaveCount(5);
        buys.Should().Contain(p => p.ItemKey == "gauntlets" && p.TimeSeconds == -89,
            "đồ mua trước khai cuộc là dữ liệu thật, không phải rác");
        buys.Count(p => p.ItemKey == "tango").Should().Be(2,
            "lưu thô: gộp sẵn ở đây thì mất khả năng hỏi câu khác về sau");
        buys.Should().OnlyContain(p => p.IsRadiant);
    }

    /// <summary>
    /// Chốt của cả cơ chế nạp bù: ván đã nạp ở phiên bản cũ phải được coi là CÒN THIẾU, còn ván
    /// đã ở phiên bản hiện tại thì không được gọi lại — mỗi lần gọi lại là một request thật.
    /// </summary>
    [Fact]
    public async Task Phien_ban_schema_cu_thi_nap_lai_con_dung_phien_ban_thi_bo_qua()
    {
        using var db = NewDb();
        await SeedMatchAsync(db, schemaVersion: 1, ingestedAt: DateTime.UtcNow);

        var stale = new StubHandler(Payload);
        var done = await Ingester(db, stale).IngestAsync(10, CancellationToken.None);

        done.Should().Be(1, "phiên bản 1 cũ hơn phiên bản hiện tại nên phải nạp lại");
        stale.Calls.Should().Be(1);

        db.ChangeTracker.Clear();
        (await db.Matches.FirstAsync()).DetailSchemaVersion
            .Should().Be(MatchDetailIngester.SchemaVersion);

        var fresh = new StubHandler(Payload);
        var again = await Ingester(db, fresh).IngestAsync(10, CancellationToken.None);

        again.Should().Be(0);
        fresh.Calls.Should().Be(0, "gọi lại ván đã đủ dữ liệu là đốt hạn mức request vô ích");
    }

    [Fact]
    public async Task Ghi_chi_so_lane_va_coi_lane_role_0_la_khong_xac_dinh()
    {
        using var db = NewDb();
        await SeedMatchAsync(db);

        await Ingester(db, new StubHandler(Payload)).IngestAsync(10, CancellationToken.None);

        db.ChangeTracker.Clear();
        var carry = await db.MatchPlayers.FirstAsync(p => p.HeroId == 54);
        var loser = await db.MatchPlayers.FirstAsync(p => p.HeroId == 45);

        carry.LaneRole.Should().Be(1);
        carry.LaneEfficiencyPct.Should().Be(81);
        carry.LastHits.Should().Be(500);
        carry.NetWorth.Should().Be(31253);
        carry.ObserversPlaced.Should().Be(2);

        loser.LaneRole.Should().BeNull("lane_role = 0 nghĩa là không xác định, không phải vai trò 0");
        loser.LaneEfficiencyPct.Should().BeNull("thiếu trường thì để null, không quy về 0");
        loser.LastHits.Should().BeNull();
    }

    /// <summary>
    /// Thể thức không có draft (All Pick chẳng hạn) thì picks_bans rỗng hoặc thiếu. Ván đó vẫn
    /// phải nạp bình thường, chỉ là không có bàn draft — không được ném lỗi và chặn cả mẻ.
    /// </summary>
    [Fact]
    public async Task Van_khong_co_draft_van_nap_binh_thuong()
    {
        using var db = NewDb();
        await SeedMatchAsync(db);

        var noDraft = """
        {
          "match_id": 900001, "radiant_win": true, "duration": 2000,
          "start_time": 1770000000, "patch": 60,
          "players": [
            { "account_id": 111, "hero_id": 54, "player_slot": 0, "isRadiant": true,
              "kills": 3, "deaths": 3, "assists": 3, "gold_per_min": 400, "xp_per_min": 450 }
          ]
        }
        """;

        var done = await Ingester(db, new StubHandler(noDraft)).IngestAsync(10, CancellationToken.None);

        done.Should().Be(1);
        db.ChangeTracker.Clear();
        (await db.DraftEvents.CountAsync()).Should().Be(0);
        (await db.ItemPurchases.CountAsync()).Should().Be(0);
        (await db.MatchPlayers.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// 429 phải được coi là lỗi tạm thời, KHÔNG được ném ra khỏi hàm.
    ///
    /// Đây là lỗi đã xảy ra thật trên production: mức lùi 30 giây của RateLimitedHandler biến
    /// 429 thành TaskCanceledException, khối catch chỉ bắt HttpRequestException nên nó lọt qua,
    /// và IngestOrchestrator rollback cả mẻ — hơn một trăm ván đã tải xong bị xoá sạch.
    /// </summary>
    [Fact]
    public async Task Bi_429_thi_dung_dep_va_GIU_phan_da_nap()
    {
        using var db = NewDb();

        // 3 ván: hai ván đầu OK, từ ván thứ ba trở đi luôn 429
        for (var i = 0; i < 3; i++)
        {
            db.Matches.Add(new Match
            {
                Id = 900001 + i,
                StartTime = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-i),
                RadiantWin = true,
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ThrottleAfterHandler(okCalls: 2);
        var done = await Ingester(db, handler).IngestAsync(10, CancellationToken.None);

        done.Should().Be(2, "hai ván nạp được phải được giữ, không bị mất vì ván thứ ba lỗi");

        db.ChangeTracker.Clear();
        (await db.Matches.CountAsync(m => m.DetailSchemaVersion == MatchDetailIngester.SchemaVersion))
            .Should().Be(2);
        (await db.Matches.CountAsync(m => m.DetailSchemaVersion == 0))
            .Should().Be(1, "ván lỗi phải còn nguyên trong hàng chờ để vòng sau thử lại");
    }

    /// <summary>
    /// Nhiều lỗi liên tiếp thì dừng, không cố hết 200 ván. Cố thêm chỉ làm nguồn chặn IP —
    /// và mất IP là mất luôn nguồn dữ liệu, không phải chỉ một vòng ingest.
    /// </summary>
    [Fact]
    public async Task Dung_sau_so_lan_loi_lien_tiep_thay_vi_co_het_me()
    {
        using var db = NewDb();

        for (var i = 0; i < 20; i++)
        {
            db.Matches.Add(new Match
            {
                Id = 900001 + i,
                StartTime = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-i),
                RadiantWin = true,
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var handler = new ThrottleAfterHandler(okCalls: 0);
        await Ingester(db, handler).IngestAsync(20, CancellationToken.None);

        handler.Calls.Should().Be(MatchDetailIngester.MaxConsecutiveFailures,
            "phải dừng đúng sau ngưỡng lỗi liên tiếp, không gọi tiếp 17 lần nữa");
    }

    /// <summary>
    /// Bảng Heroes phải được nạp. Nó tồn tại từ đầu nhưng chưa bao giờ có ai ghi vào, và một
    /// bảng rỗng không làm gì đổ vỡ — nên nó nằm im cho tới lúc bảng ưu tiên cấm/chọn hiện ra
    /// 30 dòng "hero 80". Test này khoá lại điều đó.
    /// </summary>
    [Fact]
    public async Task Nap_bang_hero_kem_ten_va_anh()
    {
        using var db = NewDb();
        db.Teams.Add(new Team { Slug = "t", Name = "T", OpenDotaTeamId = 1, LogoUrl = "https://steamcdn/x.png" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var heroesJson = """
        [
          { "id": 1, "name": "npc_dota_hero_antimage", "localized_name": "Anti-Mage" },
          { "id": 80, "name": "npc_dota_hero_lone_druid", "localized_name": "Lone Druid" }
        ]
        """;

        var ingester = new OpenDotaIngester(
            db,
            new OpenDotaClient(new HttpClient(new RouteHandler(heroesJson))
            {
                BaseAddress = new Uri("https://x/api/"),
            }),
            new TeamResolver(db, NullLogger<TeamResolver>.Instance),
            NullLogger<OpenDotaIngester>.Instance);

        await ingester.IngestAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var lone = await db.Heroes.FirstOrDefaultAsync(h => h.Id == 80);

        lone.Should().NotBeNull();
        lone!.LocalizedName.Should().Be("Lone Druid");
        lone.ImageUrl.Should().Contain("lone_druid",
            "ảnh Steam CDN dùng phần đuôi sau tiền tố npc_dota_hero_");
        lone.ImageUrl.Should().NotContain("npc_dota_hero_");
    }

    /// <summary>Trả heroes cho đường /heroes, mảng rỗng cho mọi đường khác.</summary>
    private sealed class RouteHandler(string heroesJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.RequestUri!.AbsolutePath.EndsWith("/heroes", StringComparison.Ordinal)
                ? heroesJson
                : "[]";

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>Trả OK cho <c>okCalls</c> lần đầu, sau đó luôn 429.</summary>
    private sealed class ThrottleAfterHandler(int okCalls) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;

            if (Calls <= okCalls)
            {
                // match_id trong payload không quan trọng: ingester dùng id từ hàng chờ.
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(Payload, System.Text.Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests));
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* file tạm, xoá được thì tốt */ }
    }
}
