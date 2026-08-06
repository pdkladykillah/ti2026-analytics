using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Tests;

/// <summary>
/// Đi hết đường từ MatchPlayers tới câu nhận định trên trang.
///
/// Cần bài này vì ApiContractTests chạy trên DB chỉ có dữ liệu biên tập, không có ván nào — mọi
/// khẳng định theo từng cặp đấu ở đó đều rơi vào vòng lặp rỗng và không kiểm được gì. Chỗ dễ sai
/// nhất lại nằm đúng ở phần nối: xoay góc nhìn Radiant/Dire về đúng đội A/B của khoá cặp đấu.
/// Sai một nước ở đó thì số người còn lại bị gán nhầm bên, mà kết quả nhìn vẫn hợp lý.
/// </summary>
public class H2hLineupTests(Ti2026TestFactory factory) : IClassFixture<Ti2026TestFactory>
{
    private static long _nextId = 900_000_000;

    /// <param name="keptRad">Bao nhiêu người của đội hình HIỆN TẠI bên Radiant cho ra sân.</param>
    private static void AddMatch(
        Ti2026DbContext db, Team rad, Team dire, DateTime when, bool radiantWin,
        IReadOnlyList<int> radRoster, IReadOnlyList<int> direRoster, int keptRad, int keptDire)
    {
        var id = _nextId++;

        db.Matches.Add(new Match
        {
            Id = id,
            SeriesId = id,
            StartTime = when,
            DurationSeconds = 2100,
            LeagueName = "Giải kiểm thử",
            RadiantTeamId = rad.Id,
            DireTeamId = dire.Id,
            RadiantWin = radiantWin,
            RadiantScore = radiantWin ? 30 : 20,
            DireScore = radiantWin ? 20 : 30,
            IngestedAt = when,
        });

        // Chỗ nào không phải người của đội hình hiện tại thì để PlayerId null — đúng như ván thật
        // của một người chưa từng khớp về Player nào của ta.
        for (var i = 0; i < 5; i++)
        {
            db.MatchPlayers.Add(new MatchPlayer
            {
                MatchId = id, IsRadiant = true, HeroId = i + 1,
                PlayerId = i < keptRad ? radRoster[i] : null,
            });
            db.MatchPlayers.Add(new MatchPlayer
            {
                MatchId = id, IsRadiant = false, HeroId = i + 6,
                PlayerId = i < keptDire ? direRoster[i] : null,
            });
        }
    }

    [Fact]
    public async Task Van_cua_doi_hinh_cu_bi_loai_khoi_nhan_dinh_nhung_van_hien_trong_bang()
    {
        var client = factory.CreateClient();   // buộc app khởi động + seed xong rồi mới ghi thêm

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Ti2026DbContext>();

        var teams = await db.Teams.OrderBy(t => t.Slug).Take(2).ToListAsync();
        teams.Should().HaveCount(2);
        var (a, b) = (teams[0], teams[1]);

        async Task<List<int>> RosterOf(Team t) => await db.RosterEntries
            .Where(r => r.TeamId == t.Id && r.ValidTo == null && r.Role != "COACH")
            .Select(r => r.PlayerId).ToListAsync();

        var ra = await RosterOf(a);
        var rb = await RosterOf(b);
        ra.Should().HaveCount(5, "seed phải có đủ đội hình 5 người");
        rb.Should().HaveCount(5);

        var t0 = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        // 2 ván ĐÚNG đội hình cả hai bên; a thắng cả hai
        AddMatch(db, a, b, t0, radiantWin: true, ra, rb, 5, 5);
        AddMatch(db, b, a, t0.AddDays(1), radiantWin: false, rb, ra, 5, 5);

        // 3 ván mà bên b chỉ còn 2 người — dưới ngưỡng, phải bị loại hẳn
        for (var i = 0; i < 3; i++)
            AddMatch(db, a, b, t0.AddDays(-100 - i), radiantWin: false, ra, rb, 5, 2);

        await db.SaveChangesAsync();

        var key = string.CompareOrdinal(a.Slug, b.Slug) <= 0 ? $"{a.Slug}|{b.Slug}" : $"{b.Slug}|{a.Slug}";
        var slugA = key.Split('|')[0];

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/h2h"));
        var pair = doc.RootElement.GetProperty("pairs").GetProperty(key);

        pair.GetProperty("games").GetInt32().Should().Be(5, "bảng vẫn phải bày đủ 5 ván");
        pair.GetProperty("series").GetArrayLength().Should().Be(5);

        var verdict = pair.GetProperty("verdict");
        verdict.GetProperty("basis").GetString().Should().Be("dung-doi-hinh");
        verdict.GetProperty("games").GetInt32().Should().Be(2, "3 ván kia là đội hình khác");
        verdict.GetProperty("ignored").GetInt32().Should().Be(3);

        // a thắng cả hai ván đúng đội hình, dù một ván ở bên Radiant và một ván ở bên Dire —
        // đây chính là chỗ mà xoay nhầm góc nhìn sẽ cho ra 1–1.
        var winsA = verdict.GetProperty(slugA == a.Slug ? "winsA" : "winsB").GetInt32();
        var winsB = verdict.GetProperty(slugA == a.Slug ? "winsB" : "winsA").GetInt32();
        winsA.Should().Be(2);
        winsB.Should().Be(0);

        // Cỡ mẫu 2 ván thì không được phép kết luận ai trên cơ ai
        verdict.GetProperty("decisive").GetBoolean().Should().BeFalse();

        // Mỗi ván khai đúng số người còn lại theo bên Radiant/Dire của CHÍNH ván đó
        foreach (var row in pair.GetProperty("series").EnumerateArray())
        {
            var radSlug = row[4].GetString();
            var keptOfB = radSlug == b.Slug ? row[5].GetInt32() : row[6].GetInt32();
            keptOfB.Should().BeOneOf(5, 2);
        }

        pair.GetProperty("lineup").GetProperty(a.Slug).GetProperty("games").GetInt32()
            .Should().Be(5, "a ra sân đủ 5 người ở cả 5 ván");
        pair.GetProperty("lineup").GetProperty(b.Slug).GetProperty("games").GetInt32()
            .Should().Be(2, "b chỉ đủ 5 người ở 2 ván");

        // --- Trang dự đoán phải nói CÙNG một con số ---
        //
        // Hai endpoint trả lời cùng một câu hỏi. Lọc một chỗ mà quên chỗ kia thì trang H2H nói
        // 2 ván còn trang dự đoán nói 5 ván, và người đọc không có cách nào biết bên nào đúng.
        using var pred = JsonDocument.Parse(
            await client.GetStringAsync($"/api/predict?a={a.Slug}&b={b.Slug}"));

        var ph = pred.RootElement.GetProperty("headToHead");
        ph.GetProperty("played").GetInt32().Should().Be(5, "tổng thô vẫn giữ nguyên");

        var pl = ph.GetProperty("lineup");
        pl.GetProperty("games").GetInt32().Should().Be(2);
        pl.GetProperty("ignored").GetInt32().Should().Be(3);

        // a là teamA ở đây bất kể thứ tự alphabet — đúng chỗ dễ xoay nhầm góc nhìn nhất
        pl.GetProperty("winsA").GetInt32().Should().Be(2);
        pl.GetProperty("winsB").GetInt32().Should().Be(0);

        // Tỷ số phải xoay về góc nhìn A–B, không phải Radiant–Dire
        foreach (var m in ph.GetProperty("recent").EnumerateArray())
        {
            var parts = m.GetProperty("score").GetString()!.Split('-');
            var aWon = m.GetProperty("aWon").GetBoolean();

            (int.Parse(parts[0]) > int.Parse(parts[1])).Should().Be(aWon,
                "bên ghi nhiều điểm hơn trong chuỗi tỷ số phải đúng là bên thắng");
        }
    }
}
