using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Tests;

/// <summary>
/// Bộ nạp idol, chạy trên SQLite thật để bắt được cả những lỗi chỉ xuất hiện khi có ràng buộc
/// duy nhất và khoá ngoại — hai thứ mà một stub trong bộ nhớ sẽ bỏ qua.
/// </summary>
public class IdolIngesterTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"ti2026-idol-{Guid.NewGuid():N}.db");

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    /// <summary>
    /// Trả đúng một chi tiết ván cho mọi match id, và ĐẾM số lời gọi mạng — số đếm đó là cách duy
    /// nhất phân biệt "đã rút tồn đọng" với "đã bỏ qua vì cửa còn đóng".
    /// </summary>
    private sealed class DetailHandler(int lobbyType = 1, long leagueId = 17000) : HttpMessageHandler
    {
        // Interlocked, không phải ++. Bộ nạp gọi qua Parallel.ForEachAsync với 8 luồng, nên
        // một phép tăng thường sẽ MẤT LƯỢT: đã thấy thật, bài kiểm đòi 3 lời gọi mà đếm ra 1.
        // Lỗi nằm ở dụng cụ đo chứ không ở mã được đo — và đó là loại hỏng chỉ thỉnh thoảng
        // mới xuất hiện, tức loại tệ nhất để bỏ qua.
        private int _matchCalls;
        private int _listCalls;

        public int MatchCalls => Volatile.Read(ref _matchCalls);
        public int ListCalls => Volatile.Read(ref _listCalls);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            string json;

            if (path.Contains("/matches/"))
            {
                Interlocked.Increment(ref _matchCalls);
                json = Detail(long.Parse(path[(path.LastIndexOf('/') + 1)..]), lobbyType, leagueId);
            }
            else if (path.EndsWith("/matches"))
            {
                Interlocked.Increment(ref _listCalls);
                json = "[]";
            }
            else
            {
                json = "{}";
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }

        /// <summary>
        /// Mười người, đủ để MakeAnchor không loại ván. Ba người đầu phe Radiant là Topson,
        /// Malr1ne và ATF — đúng hình dạng dữ liệu thật, nơi Malr1ne và ATF cùng đội Falcons nên
        /// ván thi đấu của họ là cùng một ván.
        /// </summary>
        private static string Detail(long matchId, int lobbyType, long leagueId)
        {
            long[] seats = [94054712, 898455820, 183719386, 904, 905, 302214028, 907, 908, 909, 910];

            var players = Enumerable.Range(0, 10).Select(i => $$"""
                {
                  "account_id": {{seats[i]}},
                  "player_slot": {{(i < 5 ? i : 128 + i - 5)}},
                  "hero_id": {{10 + i}},
                  "kills": 5, "deaths": 4, "assists": 11,
                  "gold_per_min": 500, "xp_per_min": 600,
                  "last_hits": 200, "denies": 10, "net_worth": 18000, "level": 25,
                  "hero_damage": 25000, "tower_damage": 3000, "hero_healing": 0,
                  "lane_role": 2, "lane": 2, "lane_efficiency_pct": 82.4,
                  "teamfight_participation": 0.7,
                  "damage_taken": { "npc_a": 12000, "npc_b": 4000 },
                  "kills_log": [{ "time": 421 }]
                }
                """);

            return $$"""
                {
                  "match_id": {{matchId}},
                  "radiant_win": true,
                  "duration": 2400,
                  "start_time": 1750000000,
                  "lobby_type": {{lobbyType}},
                  "leagueid": {{leagueId}},
                  "game_mode": 2,
                  "patch": 57,
                  "radiant_name": "Tundra Esports",
                  "dire_name": "Team Spirit",
                  "radiant_gold_adv": [{{string.Join(",", Enumerable.Range(0, 31).Select(m => m * 400))}}],
                  "players": [{{string.Join(",", players)}}]
                }
                """;
        }
    }

    private static IdolIngester Make(Ti2026DbContext db, HttpMessageHandler handler) =>
        new(db,
            new OpenDotaClient(new HttpClient(handler) { BaseAddress = new Uri("https://x/api/") }),
            NullLogger<IdolIngester>.Instance);

    /// <summary>
    /// CỬA 12 GIỜ KHÔNG ĐƯỢC CHẶN VIỆC RÚT CẠN TỒN ĐỌNG.
    ///
    /// Đây là một lỗi đã có thật trong bản đầu: cửa bọc cả hai bước, mà vòng đầu tiên cần khoảng
    /// 1.200 chi tiết ván trong khi trần mỗi vòng là 400. Hậu quả là sau lần chạy đầu còn 800 ván
    /// nằm chờ suốt 12 giờ, trong khi vòng ingest vẫn báo Succeeded — trang chạy trên một phần ba
    /// dữ liệu mà không có gì báo là đang thiếu.
    /// </summary>
    [Fact]
    public async Task Cua_thoi_gian_khong_chan_viec_rut_can_ton_dong()
    {
        await using var db = NewDb();

        // Cả bốn đều vừa quét xong: bước quét PHẢI bị bỏ qua hoàn toàn ở vòng này.
        foreach (var s in IdolIngester.Seed)
            db.IdolPlayers.Add(new IdolPlayer
            {
                AccountId = s.AccountId,
                Name = s.Name,
                NameKey = IdolPlayer.MakeNameKey(s.Name),
                MatchesFetchedAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        var idol = await db.IdolPlayers.FirstAsync();
        for (var i = 0; i < 3; i++)
            db.IdolMatches.Add(new IdolMatch
            {
                IdolPlayerId = idol.Id,
                MatchId = 8_000_000 + i,
                HeroId = 10,
                StartTime = DateTime.UtcNow.AddDays(-i),
                Won = true,
                IsRadiant = true,
                LobbyType = 1,
            });
        await db.SaveChangesAsync();

        var handler = new DetailHandler();
        await Make(db, handler).IngestAsync(CancellationToken.None);

        handler.ListCalls.Should().Be(0, "cửa còn đóng nên KHÔNG được quét lại danh sách ván");
        handler.MatchCalls.Should().Be(3, "nhưng ba ván tồn đọng thì vẫn phải được lấy");

        db.ChangeTracker.Clear();
        (await db.IdolMatches.CountAsync(m => m.DetailFetchedAt == null)).Should().Be(0);
    }

    /// <summary>
    /// MỌI người đều phải được ghi mốc quét, không chỉ người đầu tiên.
    ///
    /// Đây là một lỗi thật của bản đầu, và là loại lỗi không làm gì đổ vỡ. Cả bốn hàng IdolPlayer
    /// nạp về trong một truy vấn nên cùng được EF theo dõi; gọi ChangeTracker.Clear() ở cuối mỗi
    /// lượt sẽ tách rời cả bốn, và từ lượt thứ hai trở đi thì MatchesFetchedAt gán vào thực thể đã
    /// rời ngữ cảnh nên SaveChanges không ghi gì. Ván mới vẫn được thêm bình thường vì chúng là
    /// thực thể mới — nên bề ngoài chạy đúng, chỉ có mốc của ba người sau là vĩnh viễn null và cửa
    /// gác không bao giờ đóng với họ: quét lại 6 lời gọi thừa mỗi vòng, mãi mãi.
    /// </summary>
    [Fact]
    public async Task Ghi_moc_quet_cho_tat_ca_khong_chi_nguoi_dau_tien()
    {
        await using var db = NewDb();
        await Make(db, new DetailHandler()).IngestAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var rows = await db.IdolPlayers.ToListAsync();

        rows.Should().HaveCount(IdolIngester.Seed.Length);
        rows.Should().OnlyContain(x => x.MatchesFetchedAt != null,
            "người nào thiếu mốc thì sẽ bị quét lại mỗi vòng mà không ai thấy");
    }

    /// <summary>
    /// Cửa gác theo TỪNG người: chỉ người quá hạn bị quét lại.
    ///
    /// Bản đầu hỏi "có ai tới hạn không" rồi quét cả nhóm, nên thêm một người mới vào hạt giống là
    /// kéo theo ba người vừa quét mười phút trước — 6 lời gọi thừa cho đúng không thông tin nào.
    /// </summary>
    [Fact]
    public async Task Chi_quet_lai_nguoi_qua_han()
    {
        await using var db = NewDb();

        foreach (var s in IdolIngester.Seed)
            db.IdolPlayers.Add(new IdolPlayer
            {
                AccountId = s.AccountId,
                Name = s.Name,
                NameKey = IdolPlayer.MakeNameKey(s.Name),

                // Một người quá hạn, ba người vừa quét.
                MatchesFetchedAt = s.Sort == 1
                    ? DateTime.UtcNow - IdolIngester.MinInterval - TimeSpan.FromHours(1)
                    : DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        var handler = new DetailHandler();
        await Make(db, handler).IngestAsync(CancellationToken.None);

        handler.ListCalls.Should().Be(1, "chỉ đúng một người quá hạn");
    }

    /// <summary>
    /// Cùng một ván chỉ được neo MỘT lần, dù hai tuyển thủ cùng có mặt trong đó.
    ///
    /// Malr1ne và ATF cùng đội Falcons nên phần lớn ván thi đấu của họ LÀ CÙNG MỘT VÁN. Không có
    /// chốt này thì mỗi ván chung bỏ phiếu gấp đôi vào mốc "người bình thường", và cái lệch đó
    /// nghiêng đúng về phía những trận có hai người — tức không phải nhiễu ngẫu nhiên mà là lệch
    /// có hệ thống.
    /// </summary>
    [Fact]
    public async Task Van_chung_cua_hai_dong_doi_chi_neo_mot_lan()
    {
        await using var db = NewDb();

        foreach (var (id, name) in new[] { (898455820L, "Malr1ne"), (183719386L, "ATF") })
            db.IdolPlayers.Add(new IdolPlayer
            {
                AccountId = id, Name = name, NameKey = IdolPlayer.MakeNameKey(name),
                MatchesFetchedAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        // Cùng MỘT match id, hai hàng — đúng hình dạng dữ liệu thật của hai người cùng đội.
        foreach (var idol in await db.IdolPlayers.ToListAsync())
            db.IdolMatches.Add(new IdolMatch
            {
                IdolPlayerId = idol.Id,
                MatchId = 8_111_111,
                HeroId = 10,
                StartTime = DateTime.UtcNow,
                Won = true,
                IsRadiant = true,
                LobbyType = 1,
            });
        await db.SaveChangesAsync();

        await Make(db, new DetailHandler()).IngestAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        (await db.IdolMatches.CountAsync()).Should().Be(2, "mỗi người giữ góc nhìn riêng của mình");
        (await db.StyleAnchors.CountAsync()).Should().Be(1, "nhưng ván thì chỉ neo một lần");
    }

    /// <summary>
    /// Hạt giống ghi đè tên và ghi chú cũ trong DB.
    ///
    /// Vì sao đáng khoá: nếu hạt giống chỉ tạo mới mà không cập nhật thì sửa một dòng trong mã sẽ
    /// không có tác dụng gì trên máy chủ đã chạy, và cách duy nhất để đổi là xoá bảng — một thao
    /// tác không ai muốn làm trên dữ liệu thật, nên thực tế là nội dung sẽ đóng băng vĩnh viễn.
    /// </summary>
    [Fact]
    public async Task Hat_giong_cap_nhat_lai_ten_va_ghi_chu_cu()
    {
        await using var db = NewDb();

        var seed = IdolIngester.Seed[0];
        db.IdolPlayers.Add(new IdolPlayer
        {
            AccountId = seed.AccountId,
            Name = "ten cu",
            NameKey = IdolPlayer.MakeNameKey("ten cu"),
            Note = "ghi chu cu",
            MatchesFetchedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await Make(db, new DetailHandler()).IngestAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var row = await db.IdolPlayers.SingleAsync(x => x.AccountId == seed.AccountId);
        row.Name.Should().Be(seed.Name);
        row.NameKey.Should().Be(IdolPlayer.MakeNameKey(seed.Name));
        row.Note.Should().Be(seed.Note);

        (await db.IdolPlayers.CountAsync()).Should()
            .Be(IdolIngester.Seed.Length, "khoá theo account_id nên không tạo thêm người trùng");
    }

    /// <summary>
    /// Tên phòng chờ pub KHÔNG được trở thành tên đội.
    ///
    /// Đã xảy ra thật trên dữ liệu sản xuất: Topson hiện lên với đội "Sniper monkeys" — tên một
    /// nhóm pub — nằm ngay cạnh Team Falcons và Team Spirit như thể ngang hàng. Nguyên nhân là bản
    /// đầu chỉ kiểm "trường tên có rỗng không", dựa trên một ghi chú tự khẳng định rằng ván xếp
    /// hạng luôn để trống hai trường đó. Phòng chờ pub đặt được tên, nên leagueid mới là thứ phân
    /// biệt giải đấu với một nhóm bạn tự gọi mình là gì.
    /// </summary>
    [Fact]
    public async Task Ten_phong_cho_pub_khong_tro_thanh_ten_doi()
    {
        await using var db = NewDb();

        db.IdolPlayers.Add(new IdolPlayer
        {
            AccountId = 94054712, Name = "Topson", NameKey = "TOPSON",
            MatchesFetchedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var idol = await db.IdolPlayers.FirstAsync();
        db.IdolMatches.Add(new IdolMatch
        {
            IdolPlayerId = idol.Id, MatchId = 8_333_333, HeroId = 10,
            StartTime = DateTime.UtcNow, Won = true, IsRadiant = true, LobbyType = 7,
        });
        await db.SaveChangesAsync();

        // Ván xếp hạng: leagueid 0 nhưng VẪN có tên phe — đúng hình dạng đã gặp thật.
        await Make(db, new DetailHandler(lobbyType: 7, leagueId: 0)).IngestAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        (await db.IdolPlayers.SingleAsync(x => x.AccountId == 94054712)).TeamName.Should()
            .BeNull("leagueid bằng 0 nên tên phe chỉ là tên phòng chờ, không phải tên đội");

        // Và ván xếp hạng vẫn phải neo — vào hồ pub của idol, không vào hồ pro.
        (await db.StyleAnchors.SingleAsync()).Pool.Should().Be(IdolIngester.IdolPubPool);
    }

    /// <summary>
    /// VÁN CŨ HƠN KHÔNG ĐƯỢC GHI ĐÈ TÊN ĐỘI ĐANG LƯU.
    ///
    /// Đây là lỗi người dùng bắt được trên trang thật. Vòng chi tiết lấy tối đa 400 ván mỗi lượt,
    /// nên "ván mới nhất trong lô" không phải "ván mới nhất của người đó" — và bản trước ghi đè vô
    /// điều kiện, tức tên đội bị quyết định bởi lô nào tình cờ chạy sau cùng.
    ///
    /// Bằng chứng nó không vô hại: Satanic, No[o]ne- và Dukalis cùng phe Radiant trong ĐÚNG MỘT
    /// ván (8904419709, radiant_name = "PVISION") mà ra ba kết quả — hai người ra PVISION, Satanic
    /// ra "Team Falcons". Whitemon thì đứng lại ở "Tundra Esports" trong khi ván giải mới nhất
    /// (8930664368) ghi anh ở phe Dire của "1w".
    /// </summary>
    [Fact]
    public async Task Van_cu_hon_khong_ghi_de_ten_doi_moi_hon()
    {
        await using var db = NewDb();

        var moiHon = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);

        db.IdolPlayers.Add(new IdolPlayer
        {
            AccountId = 94054712, Name = "Topson", NameKey = "TOPSON",
            MatchesFetchedAt = DateTime.UtcNow,
            TeamName = "Đội mới nhất", TeamNameAt = moiHon,
        });
        await db.SaveChangesAsync();

        var idol = await db.IdolPlayers.FirstAsync();

        // Ván giải THẬT, tên phe hợp lệ — chỉ có mỗi tội cũ hơn ván đã sinh ra tên đang lưu.
        db.IdolMatches.Add(new IdolMatch
        {
            IdolPlayerId = idol.Id, MatchId = 8_444_444, HeroId = 10,
            StartTime = moiHon.AddMonths(-1), Won = true, IsRadiant = true, LobbyType = 1,
        });
        await db.SaveChangesAsync();

        await Make(db, new DetailHandler()).IngestAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var after = await db.IdolPlayers.SingleAsync(x => x.AccountId == 94054712);

        after.TeamName.Should().Be("Đội mới nhất",
            "một ván cũ hơn không nói được gì về đội hiện tại — nó chỉ nói người này TỪNG ở đâu");
        after.TeamNameAt.Should().Be(moiHon);
    }

    /// <summary>
    /// Và chiều ngược lại phải chạy: ván mới hơn thì đổi tên, kèm mốc giờ mới.
    /// Thiếu nửa này thì "không ghi đè" có thể được cài đúng bằng cách không bao giờ ghi gì cả.
    /// </summary>
    [Fact]
    public async Task Van_moi_hon_thi_doi_ten_doi_va_ghi_lai_moc()
    {
        await using var db = NewDb();

        var cuHon = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        db.IdolPlayers.Add(new IdolPlayer
        {
            AccountId = 94054712, Name = "Topson", NameKey = "TOPSON",
            MatchesFetchedAt = DateTime.UtcNow,
            TeamName = "Đội cũ", TeamNameAt = cuHon,
        });
        await db.SaveChangesAsync();

        var idol = await db.IdolPlayers.FirstAsync();
        var vanMoi = cuHon.AddMonths(2);

        db.IdolMatches.Add(new IdolMatch
        {
            IdolPlayerId = idol.Id, MatchId = 8_555_555, HeroId = 10,
            StartTime = vanMoi, Won = true, IsRadiant = true, LobbyType = 1,
        });
        await db.SaveChangesAsync();

        await Make(db, new DetailHandler()).IngestAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var after = await db.IdolPlayers.SingleAsync(x => x.AccountId == 94054712);

        // Topson ngồi ghế đầu phe Radiant trong DetailHandler.
        after.TeamName.Should().Be("Tundra Esports");
        after.TeamNameAt.Should().Be(vanMoi);
    }

    /// <summary>
    /// Chênh lệch vàng phải ĐẢO DẤU cho phe Dire.
    ///
    /// radiant_gold_adv luôn là Radiant trừ Dire. Quên đảo thì gần nửa số ván đọc ngược và trung
    /// vị trung hoà về 0 — kết quả trông hoàn toàn hợp lý và không có gì đổ vỡ để báo.
    /// </summary>
    [Fact]
    public async Task Chenh_lech_vang_dao_dau_khi_o_phe_dire()
    {
        await using var db = NewDb();

        db.IdolPlayers.Add(new IdolPlayer
        {
            AccountId = 94054712, Name = "Topson", NameKey = "TOPSON",
            MatchesFetchedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var idol = await db.IdolPlayers.FirstAsync();
        db.IdolMatches.Add(new IdolMatch
        {
            IdolPlayerId = idol.Id, MatchId = 8_222_222, HeroId = 10,
            StartTime = DateTime.UtcNow, Won = true, IsRadiant = true, LobbyType = 1,
        });
        await db.SaveChangesAsync();

        await Make(db, new DetailHandler()).IngestAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var row = await db.IdolMatches.SingleAsync();

        // Người cần tìm ở player_slot 0, tức Radiant, và mảng vàng tăng dần 400 mỗi phút.
        row.IsRadiant.Should().BeTrue();
        row.GoldAdv10.Should().Be(4000);
        row.GoldAdv20.Should().Be(8000);
        row.GoldAdv30.Should().Be(12000);

        // Và đọc luôn những trường chỉ có ở ván đã parse, để chặn việc lặng lẽ mất chúng.
        row.LaneEfficiency.Should().Be(82);
        row.TeamfightParticipation.Should().BeApproximately(0.7, 1e-9);
        row.DamageTaken.Should().Be(16000);
        row.FirstKillSecond.Should().Be(421);
        row.TeamNetWorth.Should().Be(5 * 18000);
        row.LeagueId.Should().Be(17000);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
    }
}
