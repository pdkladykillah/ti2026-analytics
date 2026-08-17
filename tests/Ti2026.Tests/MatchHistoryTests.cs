using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Web.Endpoints;

namespace Ti2026.Tests;

/// <summary>
/// api/profile/matches — lịch sử đấu từng ván.
///
/// Bài quan trọng nhất ở đây là bộ lọc theo VỊ TRÍ: nó phải đi qua RoleResolver chứ không phải
/// `lane_role`. Hard support và carry cùng mang nhãn lane `safe`, nên lọc theo lane sẽ trộn hai
/// công việc ngược nhau vào một danh sách — và danh sách đó trông hoàn toàn hợp lý.
/// </summary>
public class MatchHistoryTests(Ti2026TestFactory factory) : IClassFixture<Ti2026TestFactory>
{
    private const long Me = 777_000_111;
    private static long _nextMatch = 880_000_000;

    /// <summary>Ghi một ván. lane + hạng net worth là hai thứ cùng quyết định vị trí thật.</summary>
    private static void AddMatch(
        Ti2026DbContext db, int playerId, int heroId, int? lane, int? farmRank,
        DateTime when, bool won = true, int? pctGpm = null, int? gpm = 500)
        => db.TrackedPlayerMatches.Add(new TrackedPlayerMatch
        {
            TrackedPlayerId = playerId, MatchId = _nextMatch++, HeroId = heroId,
            StartTime = when, DurationSeconds = 2400, Won = won,
            Kills = 6, Deaths = 3, Assists = 9,
            LaneRole = lane, TeamFarmRank = farmRank,
            GoldPerMin = gpm, XpPerMin = 600, PctGpm = pctGpm,
        });

    private async Task<(int PlayerId, Ti2026DbContext Db)> SeedAsync()
    {
        var client = factory.CreateClient();   // buộc app khởi động xong rồi mới ghi thêm
        _ = client;

        var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Ti2026DbContext>();

        var existing = await db.TrackedPlayers.FirstOrDefaultAsync(p => p.AccountId == Me);
        if (existing is not null) return (existing.Id, db);

        var p = new TrackedPlayer { AccountId = Me, DisplayName = "Kiểm thử", AddedAt = DateTime.UtcNow };
        db.TrackedPlayers.Add(p);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;

        // HAI VÁN CÙNG NHÃN LANE `safe`, khác hẳn vị trí: một giàu nhất đội, một nghèo nhất.
        AddMatch(db, p.Id, heroId: 1, lane: 1, farmRank: 1, now.AddDays(-1));   // carry  pos1
        AddMatch(db, p.Id, heroId: 2, lane: 1, farmRank: 5, now.AddDays(-2));   // hard support pos5
        AddMatch(db, p.Id, heroId: 1, lane: 2, farmRank: 2, now.AddDays(-3));   // mid    pos2
        AddMatch(db, p.Id, heroId: 3, lane: 3, farmRank: 4, now.AddDays(-4));   // pos4

        // Ván ngoài cửa sổ replay: không có nhãn lane, và sẽ không bao giờ có.
        AddMatch(db, p.Id, heroId: 1, lane: null, farmRank: null,
            now.AddDays(-(MatchHistoryEndpoints.ReplayWindowDays + 10)));

        await db.SaveChangesAsync();
        return (p.Id, db);
    }

    private async Task<JsonDocument> GetAsync(string query = "")
    {
        var res = await factory.CreateClient()
            .GetAsync($"/api/profile/matches?player={Me}{query}");
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync());
    }

    private static List<JsonElement> Matches(JsonDocument d) =>
        d.RootElement.GetProperty("matches").EnumerateArray().ToList();

    /// <summary>
    /// LỌC THEO VỊ TRÍ PHẢI TÁCH ĐƯỢC POS1 KHỎI POS5 DÙ CHUNG NHÃN LANE.
    ///
    /// Đây là bài khoá luật số 1 của dự án ở màn này. Nếu ai đó đổi bộ lọc sang `lane_role` thì
    /// nó đỏ — mà không có nó, lọc "carry" sẽ trả về cả những ván đi hard support, và mọi con số
    /// đọc từ danh sách đó là trung bình của hai công việc ngược nhau.
    /// </summary>
    [Fact]
    public async Task Loc_theo_vi_tri_tach_duoc_carry_khoi_hard_support()
    {
        await SeedAsync();

        using var carry = await GetAsync("&position=pos1");
        using var sup = await GetAsync("&position=pos5");

        Matches(carry).Should().HaveCount(1);
        Matches(carry)[0].GetProperty("heroId").GetInt32().Should().Be(1);

        Matches(sup).Should().HaveCount(1);
        Matches(sup)[0].GetProperty("heroId").GetInt32().Should().Be(2,
            "cùng nhãn lane `safe` nhưng nghèo nhất đội thì là hard support, không phải carry");
    }

    /// <summary>Bộ lọc hero trả về đúng hero đó, và đếm đúng số ván.</summary>
    [Fact]
    public async Task Loc_theo_hero_tra_ve_dung_hero()
    {
        await SeedAsync();

        using var d = await GetAsync("&hero=1");
        var rows = Matches(d);

        rows.Should().HaveCountGreaterThan(1);
        rows.Should().OnlyContain(m => m.GetProperty("heroId").GetInt32() == 1);
        d.RootElement.GetProperty("matched").GetInt32().Should().Be(rows.Count);
    }

    /// <summary>
    /// Bộ lọc mang theo SỐ VÁN của từng lựa chọn. Danh sách hero không kèm số thì người dùng
    /// phải bấm thử từng cái mới biết cái nào có dữ liệu.
    /// </summary>
    [Fact]
    public async Task Bo_loc_mang_theo_so_van()
    {
        await SeedAsync();

        using var d = await GetAsync();
        var f = d.RootElement.GetProperty("filters");

        f.GetProperty("heroes").EnumerateArray().Should().OnlyContain(
            h => h.GetProperty("games").GetInt32() > 0);

        // Chỉ vị trí CHÍNH XÁC mới lên bộ lọc — "core"/"support" suy từ hạng net worth thì không,
        // vì lọc theo chúng sẽ gộp pos1 với pos3 vào một ô.
        f.GetProperty("positions").EnumerateArray()
            .Select(p => p.GetProperty("code").GetString())
            .Should().OnlyContain(c => c!.StartsWith("pos"));
    }

    /// <summary>
    /// Ván ngoài cửa sổ 60 ngày phải được ĐÁNH DẤU, không im lặng để trống.
    ///
    /// Replay của Valve hết hạn sau ~60 ngày nên vị trí không bao giờ lấy lại được. Đo trên tài
    /// khoản thật: trong 60 ngày gần nhất nhãn lane phủ 100%, ngoài đó chỉ còn 9,9%. Một ô trống
    /// không kèm lời giải thích sẽ bị đọc thành dữ liệu hỏng.
    /// </summary>
    [Fact]
    public async Task Van_qua_han_replay_duoc_danh_dau()
    {
        await SeedAsync();

        using var d = await GetAsync();
        var expired = Matches(d).Where(m => m.GetProperty("replayExpired").GetBoolean()).ToList();

        expired.Should().NotBeEmpty("bộ dữ liệu mẫu cố ý có một ván ngoài cửa sổ");
        expired.Should().OnlyContain(m => m.GetProperty("position").ValueKind == JsonValueKind.Null);

        d.RootElement.GetProperty("replayWindowDays").GetInt32()
            .Should().Be(MatchHistoryEndpoints.ReplayWindowDays);
    }

    /// <summary>
    /// Phân vị trả về NGUYÊN BẢN, không lọc lại lần hai.
    ///
    /// Luật "phân vị của giá trị 0 là rác" đã chặn ở chỗ GHI (TrackedMatchDetailIngester.Pct).
    /// Thêm một lớp lọc ở endpoint là hai nơi cùng quyết định một luật — và hai nơi thì sẽ lệch.
    /// </summary>
    [Fact]
    public async Task Phan_vi_tra_ve_nguyen_ban()
    {
        var (playerId, db) = await SeedAsync();

        AddMatch(db, playerId, heroId: 9, lane: 2, farmRank: 1,
            DateTime.UtcNow.AddHours(-1), pctGpm: 82);
        await db.SaveChangesAsync();

        using var d = await GetAsync("&hero=9");
        var pct = Matches(d)[0].GetProperty("pct");

        pct.GetProperty("gpm").GetInt32().Should().Be(82);
        pct.GetProperty("deathsLowerIsBetter").GetBoolean().Should().BeTrue(
            "phân vị số chết đảo chiều, và cờ này là cách nói ra thay vì tự đảo ở tầng dữ liệu");
    }

    [Fact]
    public async Task Tai_khoan_khong_theo_doi_tra_404()
    {
        var res = await factory.CreateClient().GetAsync("/api/profile/matches?player=1");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
