using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Tests;

/// <summary>
/// api/profile/board — bảng điểm mười người, nạp theo yêu cầu.
///
/// Bài quan trọng nhất ở đây là bài CHẶN: endpoint chỉ được nạp ván nằm trong lịch sử của người
/// được theo dõi. Không có chốt đó thì nó là một proxy OpenDota miễn phí, và chi phí của trang
/// phụ thuộc vào lưu lượng chứ vào dữ liệu — đúng cái bẫy mà ProPubIngester đã tránh bằng cách
/// chạy theo lịch thay vì theo lượt tải trang. Hậu quả không phải một trang chậm mà là VPS bị
/// chặn IP, mất luôn nguồn dữ liệu.
/// </summary>
public class MatchBoardTests(Ti2026TestFactory factory) : IClassFixture<Ti2026TestFactory>
{
    private const long Acc = 778_000_222;
    private const long Mine = 870_000_001;
    private const long NotMine = 870_999_999;

    private async Task SeedAsync()
    {
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Ti2026DbContext>();

        if (await db.TrackedPlayers.AnyAsync(p => p.AccountId == Acc)) return;

        var p = new TrackedPlayer { AccountId = Acc, DisplayName = "Bảng điểm", AddedAt = DateTime.UtcNow };
        db.TrackedPlayers.Add(p);
        await db.SaveChangesAsync();

        db.TrackedPlayerMatches.Add(new TrackedPlayerMatch
        {
            TrackedPlayerId = p.Id, MatchId = Mine, HeroId = 5,
            StartTime = DateTime.UtcNow.AddHours(-3), DurationSeconds = 2400, Won = true,
            Kills = 7, Deaths = 2, Assists = 11,
        });

        // Bảng điểm đã có sẵn — bài kiểm không được gọi ra mạng thật.
        for (var slot = 0; slot < 10; slot++)
            db.TrackedMatchBoards.Add(new TrackedMatchBoard
            {
                MatchId = Mine, PlayerSlot = slot < 5 ? slot : 123 + slot,
                IsRadiant = slot < 5,
                AccountId = slot == 0 ? Acc : 900 + slot,
                PersonaName = slot == 0 ? "Bảng điểm" : "Người " + slot,
                HeroId = 10 + slot,
                Kills = slot, Deaths = 1, Assists = 2,
                NetWorth = 20_000 - slot * 1_000, GoldPerMin = 500 - slot * 20,
                XpPerMin = 600, LastHits = 200, Denies = 5, Level = 25,
                FetchedAt = DateTime.UtcNow,
            });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// VÁN KHÔNG THUỘC AI ĐANG ĐƯỢC THEO DÕI THÌ TỪ CHỐI, KHÔNG GỌI RA MẠNG.
    ///
    /// Đây là bài chặn cái bẫy "chi phí phụ thuộc lưu lượng". Nếu ai đó bỏ chốt này thì bất kỳ
    /// người lạ nào cũng ép được VPS gọi OpenDota liên tục — và mất IP là mất hẳn nguồn dữ liệu,
    /// không phải chỉ một trang lỗi.
    /// </summary>
    [Fact]
    public async Task Van_ngoai_lich_su_bi_tu_choi()
    {
        await SeedAsync();

        var res = await factory.CreateClient().GetAsync($"/api/profile/board?match={NotMine}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/api/profile/board?match=0", HttpStatusCode.BadRequest)]
    [InlineData("/api/profile/board?match=-5", HttpStatusCode.BadRequest)]
    public async Task Tham_so_sai_bi_tu_choi(string url, HttpStatusCode expected)
    {
        var res = await factory.CreateClient().GetAsync(url);
        res.StatusCode.Should().Be(expected);
    }

    /// <summary>
    /// Bảng điểm đã lưu thì trả về NGAY, và cờ `cached` phải nói đúng — đó là cách duy nhất biết
    /// một lần mở có tốn lời gọi hay không.
    /// </summary>
    [Fact]
    public async Task Bang_diem_da_luu_thi_khong_goi_lai_nguon()
    {
        await SeedAsync();

        var res = await factory.CreateClient().GetAsync($"/api/profile/board?match={Mine}");
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("ready").GetBoolean().Should().BeTrue();
        root.GetProperty("cached").GetBoolean().Should().BeTrue();

        var sides = root.GetProperty("sides").EnumerateArray().ToList();
        sides.Should().HaveCount(2, "một ván có đúng hai phe");
        sides.Should().OnlyContain(s => s.GetProperty("players").GetArrayLength() == 5);
    }

    /// <summary>
    /// Phải chỉ ra ĐÚNG MỘT dòng là của người đang xem. Trong mười dòng gần giống nhau thì đó là
    /// thứ mắt phải tìm thấy đầu tiên, và giao diện không tự suy ra được.
    /// </summary>
    [Fact]
    public async Task Chi_ra_dung_dong_cua_nguoi_dang_xem()
    {
        await SeedAsync();

        var res = await factory.CreateClient().GetAsync($"/api/profile/board?match={Mine}");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());

        doc.RootElement.GetProperty("meAccountId").GetInt64().Should().Be(Acc);

        var mine = doc.RootElement.GetProperty("sides").EnumerateArray()
            .SelectMany(s => s.GetProperty("players").EnumerateArray())
            .Count(p => p.GetProperty("accountId").ValueKind != JsonValueKind.Null
                     && p.GetProperty("accountId").GetInt64() == Acc);

        mine.Should().Be(1);
    }

    /// <summary>
    /// Mỗi người có ĐÚNG SÁU ô đồ, kể cả ô trống — mảng ngắn hơn sáu thì giao diện phải tự đoán
    /// còn thiếu bao nhiêu ô, và mỗi chỗ đọc sẽ đoán một kiểu.
    /// </summary>
    [Fact]
    public async Task Moi_nguoi_luon_co_dung_sau_o_do()
    {
        await SeedAsync();

        var res = await factory.CreateClient().GetAsync($"/api/profile/board?match={Mine}");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());

        doc.RootElement.GetProperty("sides").EnumerateArray()
            .SelectMany(s => s.GetProperty("players").EnumerateArray())
            .Should().OnlyContain(p => p.GetProperty("items").GetArrayLength() == 6);
    }
}
