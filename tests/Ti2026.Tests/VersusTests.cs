using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Analytics;
using Ti2026.Web.Endpoints;

namespace Ti2026.Tests;

/// <summary>
/// api/versus — hai đội ghép theo từng vị trí.
///
/// MỖI BÀI DÙNG MỘT CẶP ĐỘI KHÁC NHAU. Cả lớp dùng chung một DB qua IClassFixture, nên nếu hai
/// bài cùng ghi ván cho cùng một cặp thì bài chạy sau đọc phải dữ liệu của bài chạy trước — và
/// xUnit không bảo đảm thứ tự, nên hỏng kiểu đó sẽ lúc xanh lúc đỏ.
/// </summary>
public class VersusTests(Ti2026TestFactory factory) : IClassFixture<Ti2026TestFactory>
{
    private static long _nextId = 700_000_000;

    /// <summary>
    /// Net worth theo ghế, giảm dần. Khoảng cách phải RÕ để thứ hạng không phụ thuộc thứ tự
    /// chèn — RoleResolver đọc hạng chứ không đọc giá trị, nhưng một bài kiểm dựa vào hoà điểm
    /// là bài kiểm dựa vào chi tiết cài đặt của bộ sắp xếp.
    /// </summary>
    private static readonly int[] NetWorthBySeat = [30_000, 24_000, 20_000, 14_000, 9_000];

    /// <summary>
    /// Ghi một ván đủ 10 người.
    ///
    /// <paramref name="lanes"/> là nhãn lane của 5 ghế theo đúng thứ tự net worth giảm dần. Ghế 0
    /// giàu nhất. Đây là chỗ dựng ra ca quan trọng nhất của cả tính năng: hai ghế cùng nhãn `safe`
    /// nhưng một giàu nhất và một nghèo nhất.
    /// </summary>
    private static long AddMatch(
        Ti2026DbContext db, Team rad, Team dire, DateTime when,
        IReadOnlyList<int> radRoster, IReadOnlyList<int> direRoster,
        IReadOnlyList<int> lanes, IReadOnlyList<int> radHeroes, IReadOnlyList<int> direHeroes,
        bool radiantWin = true)
    {
        var id = _nextId++;

        db.Matches.Add(new Match
        {
            Id = id, SeriesId = id, StartTime = when, DurationSeconds = 2280,
            LeagueName = "Giải kiểm thử", LeagueId = 42,
            RadiantTeamId = rad.Id, DireTeamId = dire.Id,
            RadiantWin = radiantWin, RadiantScore = 30, DireScore = 20, IngestedAt = when,
        });

        for (var seat = 0; seat < 5; seat++)
        {
            db.MatchPlayers.Add(Row(true, radRoster[seat], radHeroes[seat], seat));
            db.MatchPlayers.Add(Row(false, direRoster[seat], direHeroes[seat], seat));
        }

        return id;

        MatchPlayer Row(bool radiant, int playerId, int heroId, int seat) => new()
        {
            MatchId = id, IsRadiant = radiant, PlayerId = playerId, HeroId = heroId,
            LaneRole = lanes[seat], NetWorth = NetWorthBySeat[seat],
            Kills = 5, Deaths = 4, Assists = 10,
            GoldPerMin = 500 - seat * 40, XpPerMin = 600 - seat * 40,
            LastHits = 250 - seat * 40, HeroDamage = 20_000, TowerDamage = 3_000,
            LaneEfficiencyPct = 80,
        };
    }

    private async Task<(Team A, Team B, List<int> Ra, List<int> Rb, Ti2026DbContext Db)> PairAsync(int skip)
    {
        var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Ti2026DbContext>();

        var teams = await db.Teams.OrderBy(t => t.Slug).Skip(skip).Take(2).ToListAsync();
        teams.Should().HaveCount(2, "cần hai đội chưa bài nào dùng");

        async Task<List<int>> RosterOf(Team t) => await db.RosterEntries
            .Where(r => r.TeamId == t.Id && r.ValidTo == null && r.Role != "COACH")
            .Select(r => r.PlayerId).Take(5).ToListAsync();

        var ra = await RosterOf(teams[0]);
        var rb = await RosterOf(teams[1]);
        ra.Should().HaveCount(5);
        rb.Should().HaveCount(5);

        return (teams[0], teams[1], ra, rb, db);
    }

    private async Task<JsonDocument> GetAsync(string a, string b)
    {
        var res = await factory.CreateClient().GetAsync($"/api/versus?a={a}&b={b}");
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync());
    }

    private static JsonElement PositionOf(JsonDocument doc, string code) =>
        doc.RootElement.GetProperty("positions").EnumerateArray()
            .First(p => p.GetProperty("position").GetString() == code);

    /// <summary>
    /// HAI NGƯỜI CÙNG NHÃN LANE `safe` PHẢI TÁCH RA POS1 VÀ POS5.
    ///
    /// Đây là bài quan trọng nhất của tệp, và nó khoá luật số 1 của cả dự án: `lane_role` là LANE,
    /// không phải VỊ TRÍ. Hard support đứng safelane để giữ lane cho carry nên mang đúng nhãn
    /// `safe` như carry. Ai đó rút gọn phép nhóm thành `GroupBy(LaneRole)` thì bài này đỏ — mà nếu
    /// không có nó, hậu quả là carry và hard support bị gộp vào một ô, và mọi con số ở ô đó là
    /// trung bình của hai công việc ngược nhau.
    ///
    /// Đo được trên dữ liệu thật: đếm hero theo lane cho PARIVISION ra 68 hero "safelane".
    /// </summary>
    [Fact]
    public async Task Hai_nguoi_cung_nhan_lane_safe_phai_tach_thanh_pos1_va_pos5()
    {
        var (a, b, ra, rb, db) = await PairAsync(0);

        // Ghế 0 (giàu nhất) và ghế 4 (nghèo nhất) CÙNG mang nhãn safe.
        int[] lanes = [1, 2, 3, 3, 1];
        var t0 = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 6; i++)
            AddMatch(db, a, b, t0.AddDays(i), ra, rb, lanes,
                [10, 11, 12, 13, 14], [20, 21, 22, 23, 24]);

        await db.SaveChangesAsync();

        using var doc = await GetAsync(a.Slug, b.Slug);

        var pos1 = PositionOf(doc, "pos1").GetProperty("a");
        var pos5 = PositionOf(doc, "pos5").GetProperty("a");

        pos1.ValueKind.Should().NotBe(JsonValueKind.Null,
            "người giàu nhất mang nhãn safe là carry");
        pos5.ValueKind.Should().NotBe(JsonValueKind.Null,
            "người nghèo nhất mang nhãn safe là hard support — cùng nhãn lane, khác hẳn công việc");

        pos1.GetProperty("nick").GetString().Should().NotBe(pos5.GetProperty("nick").GetString(),
            "hai ghế khác nhau thì phải ra hai người khác nhau");

        // Và hero của họ không được trộn vào nhau.
        PositionOf(doc, "pos1").GetProperty("a").GetProperty("topHeroes")
            .EnumerateArray().Select(h => h.GetProperty("heroId").GetInt32())
            .Should().NotContain(14, "hero của hard support không được rơi vào ô carry");
    }

    /// <summary>
    /// Ngưỡng hero là ĐÚNG 3 ván: 2 ván chưa vào pool, 3 ván thì vào.
    ///
    /// Kiểm ở đúng biên vì đây là chỗ một dấu &gt; thay cho &gt;= sẽ đi qua mọi bài kiểm khác mà
    /// vẫn làm lệch mọi danh sách hero trên trang.
    /// </summary>
    [Fact]
    public async Task Hero_vao_pool_o_dung_nguong_ba_van()
    {
        var (a, b, ra, rb, db) = await PairAsync(2);

        int[] lanes = [1, 2, 3, 3, 1];
        var t0 = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        // Hero 31 cho pos1 của A: 3 ván. Hero 32: 2 ván. Đội B luôn hero 41 ở pos1.
        for (var i = 0; i < 3; i++)
            AddMatch(db, a, b, t0.AddDays(i), ra, rb, lanes,
                [31, 11, 12, 13, 14], [41, 21, 22, 23, 24]);

        for (var i = 0; i < 2; i++)
            AddMatch(db, a, b, t0.AddDays(10 + i), ra, rb, lanes,
                [32, 11, 12, 13, 14], [41, 21, 22, 23, 24]);

        await db.SaveChangesAsync();

        using var doc = await GetAsync(a.Slug, b.Slug);
        var heroes = PositionOf(doc, "pos1").GetProperty("heroes");

        var onlyA = heroes.GetProperty("onlyA").EnumerateArray()
            .Select(h => h.GetProperty("heroId").GetInt32()).ToList();

        onlyA.Should().Contain(31, "3 ván là đúng ngưỡng, phải vào pool");
        onlyA.Should().NotContain(32, "2 ván là dưới ngưỡng");

        VersusEndpoints.MinHeroGames.Should().Be(3, "bài kiểm này dựng quanh đúng con số đó");
    }

    /// <summary>
    /// Hai đội chưa gặp nhau lần nào: KHÔNG được ném, và phải nói rõ là chưa gặp.
    ///
    /// Ca này có thật trên dữ liệu sản xuất — 20 trong 120 cặp có thể có chưa từng gặp nhau.
    /// </summary>
    [Fact]
    public async Task Cap_doi_chua_gap_nhau_van_tra_ve_binh_thuong()
    {
        var (a, b, _, _, _) = await PairAsync(4);

        using var doc = await GetAsync(a.Slug, b.Slug);
        var h2h = doc.RootElement.GetProperty("head2head");

        h2h.GetProperty("games").GetInt32().Should().Be(0);
        h2h.GetProperty("enough").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("ready").GetBoolean().Should().BeTrue(
            "chưa gặp nhau không phải là lỗi, chỉ là một sự thật về hai đội");
    }

    /// <summary>
    /// Dưới 5 ván đối đầu thì `enough` phải là false — trung vị thật chỉ 8 ván, và 21 cặp có
    /// tối đa 2 ván. Hiện tỷ số dựng trên hai ván là hiện nhiễu dưới dạng kết luận.
    /// </summary>
    [Fact]
    public async Task Doi_dau_duoi_nguong_thi_danh_dau_chua_du_mau()
    {
        var (a, b, ra, rb, db) = await PairAsync(6);

        int[] lanes = [1, 2, 3, 3, 1];
        var t0 = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < VersusEndpoints.MinHeadToHead - 1; i++)
            AddMatch(db, a, b, t0.AddDays(i), ra, rb, lanes,
                [10, 11, 12, 13, 14], [20, 21, 22, 23, 24]);

        await db.SaveChangesAsync();

        using (var doc = await GetAsync(a.Slug, b.Slug))
        {
            var h2h = doc.RootElement.GetProperty("head2head");
            h2h.GetProperty("games").GetInt32().Should().Be(VersusEndpoints.MinHeadToHead - 1);
            h2h.GetProperty("enough").GetBoolean().Should().BeFalse();
        }

        // Thêm đúng một ván nữa là chạm ngưỡng.
        AddMatch(db, a, b, t0.AddDays(20), ra, rb, lanes,
            [10, 11, 12, 13, 14], [20, 21, 22, 23, 24]);
        await db.SaveChangesAsync();

        using (var doc = await GetAsync(a.Slug, b.Slug))
        {
            var h2h = doc.RootElement.GetProperty("head2head");
            h2h.GetProperty("games").GetInt32().Should().Be(VersusEndpoints.MinHeadToHead);
            h2h.GetProperty("enough").GetBoolean().Should().BeTrue();
        }
    }

    /// <summary>
    /// Dưới <see cref="IdolStyle.MinGames"/> ván ở một vị trí thì KHÔNG vẽ trục nào.
    ///
    /// Bảy trục dựng trên 6 ván là bảy con số trông chắc chắn hệt như bảy con số dựng trên 300
    /// ván. Giao diện phải nhận được cờ `enough` để nói ra thay vì vẽ.
    /// </summary>
    [Fact]
    public async Task Duoi_nguong_van_thi_khong_ve_truc_nao()
    {
        var (a, b, ra, rb, db) = await PairAsync(8);

        int[] lanes = [1, 2, 3, 3, 1];
        var t0 = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 6; i++)
            AddMatch(db, a, b, t0.AddDays(i), ra, rb, lanes,
                [10, 11, 12, 13, 14], [20, 21, 22, 23, 24]);

        await db.SaveChangesAsync();

        using var doc = await GetAsync(a.Slug, b.Slug);
        var seat = PositionOf(doc, "pos2").GetProperty("a");

        seat.GetProperty("games").GetInt32().Should().Be(6);
        seat.GetProperty("enough").GetBoolean().Should().BeFalse();
        seat.GetProperty("signature").GetArrayLength().Should().Be(0,
            "Signature tự bỏ trục khi chưa đủ ván — bài này khoá lại việc endpoint không tự nới ra");
    }

    /// <summary>
    /// HUẤN LUYỆN VIÊN KHÔNG ĐƯỢC CHIẾM CHỖ.
    ///
    /// RosterEntries chứa cả HLV, và điều đó không vô hại ở đây: Puppey của PARIVISION và Milan
    /// của Team Spirit đều từng là tuyển thủ thi đấu. Nếu dữ liệu có ván cũ của họ thì một trong
    /// năm chỗ sẽ trao cho người không còn ra sân, và ô đó trông y hệt một ô hợp lệ.
    /// </summary>
    [Fact]
    public async Task Huan_luyen_vien_khong_chiem_cho_cua_tuyen_thu()
    {
        var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Ti2026DbContext>();

        var teams = await db.Teams.OrderBy(t => t.Slug).Skip(10).Take(2).ToListAsync();
        var (a, b) = (teams[0], teams[1]);

        var coach = await db.RosterEntries
            .Where(r => r.TeamId == a.Id && r.ValidTo == null && r.Role == "COACH")
            .Select(r => r.PlayerId)
            .FirstOrDefaultAsync();

        coach.Should().NotBe(0, "seed biên tập phải có ít nhất một HLV để bài này có nghĩa");

        var players = await db.RosterEntries
            .Where(r => r.TeamId == a.Id && r.ValidTo == null && r.Role != "COACH")
            .Select(r => r.PlayerId).Take(4).ToListAsync();

        var rb = await db.RosterEntries
            .Where(r => r.TeamId == b.Id && r.ValidTo == null && r.Role != "COACH")
            .Select(r => r.PlayerId).Take(5).ToListAsync();

        // Cho HLV ngồi ghế giàu nhất với NHIỀU ván hơn mọi tuyển thủ — nếu bộ lọc hỏng thì ông
        // chắc chắn thắng phép ghép greedy và chiếm pos1.
        var ra = new List<int> { coach }.Concat(players).ToList();
        int[] lanes = [1, 2, 3, 3, 1];
        var t0 = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 8; i++)
            AddMatch(db, a, b, t0.AddDays(i), ra, rb, lanes,
                [10, 11, 12, 13, 14], [20, 21, 22, 23, 24]);

        await db.SaveChangesAsync();

        var coachNick = await db.Players.Where(p => p.Id == coach).Select(p => p.Nick).FirstAsync();

        using var doc = await GetAsync(a.Slug, b.Slug);

        foreach (var pos in doc.RootElement.GetProperty("positions").EnumerateArray())
        {
            var seat = pos.GetProperty("a");
            if (seat.ValueKind == JsonValueKind.Null) continue;

            seat.GetProperty("nick").GetString().Should().NotBe(coachNick,
                $"HLV không được giữ chỗ {pos.GetProperty("position").GetString()}");
        }
    }

    [Theory]
    [InlineData("/api/versus", 400)]                       // thiếu cả hai
    [InlineData("/api/versus?a=team-spirit", 400)]         // thiếu b
    [InlineData("/api/versus?a=x&b=x", 400)]               // hai đội trùng nhau
    [InlineData("/api/versus?a=khong-co-that&b=cung-khong", 404)]
    public async Task Tham_so_sai_tra_ve_dung_ma_loi(string url, int expected)
    {
        var res = await factory.CreateClient().GetAsync(url);
        ((int)res.StatusCode).Should().Be(expected);
    }
}
