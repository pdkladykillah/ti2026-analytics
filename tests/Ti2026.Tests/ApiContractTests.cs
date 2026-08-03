using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace Ti2026.Tests;

/// <summary>
/// Test bảo vệ lời hứa quan trọng nhất của cả thiết kế: API giữ đúng shape JSON cũ nên
/// frontend chỉ đổi URL. Mỗi assert dưới đây gắn với một dòng cụ thể trong index.html —
/// nếu ai đó đổi tên field ở API, test này đỏ trước khi người dùng thấy trang trắng.
/// </summary>
public class ApiContractTests(Ti2026TestFactory factory) : IClassFixture<Ti2026TestFactory>
{
    [Fact]
    public async Task api_teams_tra_dung_shape_ma_index_html_dang_parse()
    {
        var res = await factory.CreateClient().GetAsync("/api/teams");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());

        // index.html:255 -> d.teams.map(x => ({...x, tier: x.stats ? tier(x.stats.winrate) : null}))
        doc.RootElement.TryGetProperty("teams", out var teams).Should().BeTrue();
        teams.GetArrayLength().Should().Be(16);

        var first = teams[0];
        foreach (var field in new[] { "name", "short", "slug", "region", "logo" })
            first.TryGetProperty(field, out _).Should().BeTrue($"index.html cần field '{field}'");
    }

    [Fact]
    public async Task api_teams_khi_chua_co_snapshot_tra_stats_null_chu_khong_bo_field()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/teams"));

        var first = doc.RootElement.GetProperty("teams")[0];

        // index.html:255 kiểm x.stats truthy rồi mới đọc. Field phải TỒN TẠI và là null,
        // vì index.html:286 gọi t.stats?.[k] — thiếu field thì cũng undefined, nhưng
        // giữ field null làm hợp đồng rõ ràng hơn và khớp teams.json cũ.
        first.TryGetProperty("stats", out var stats).Should().BeTrue();
        stats.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task api_meta_co_updatedAt_va_bat_co_seed_khi_chua_ingest()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/meta"));

        doc.RootElement.TryGetProperty("updatedAt", out _).Should().BeTrue(
            "index.html:266 gọi new Date(m.updatedAt)");

        doc.RootElement.GetProperty("seed").GetBoolean().Should().BeTrue(
            "chưa có vòng ingest nào thành công thì phải bật cờ seed để index.html:267 hiện nhãn (seed)");

        doc.RootElement.GetProperty("teamsTotal").GetInt32().Should().Be(16);
    }

    [Fact]
    public async Task api_rosters_tra_object_khoa_theo_slug()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/rosters"));

        // index.html:258 -> x.rosters || {}
        doc.RootElement.TryGetProperty("rosters", out var rosters).Should().BeTrue();
        rosters.TryGetProperty("team-falcons", out var falcons).Should().BeTrue();
        falcons.GetArrayLength().Should().BeGreaterThan(0);

        // index.html:301-305 đọc p.nick, p.real, p.role, p.photo
        var member = falcons[0];
        foreach (var field in new[] { "nick", "real", "role", "photo" })
            member.TryGetProperty(field, out _).Should().BeTrue($"index.html cần '{field}'");
    }

    [Fact]
    public async Task api_rosters_chi_tra_vai_tro_ma_index_html_map_san()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/rosters"));

        // index.html:295-296 map đúng 6 vai trò này; giá trị lạ sẽ rơi vào POS[undefined]
        // và hiện ô trống trên UI đội hình.
        var valid = new[] { "CORE", "MID", "OFFLANE", "SUPPORT", "FULL SUPPORT", "COACH" };

        var roles = doc.RootElement.GetProperty("rosters").EnumerateObject()
            .SelectMany(t => t.Value.EnumerateArray())
            .Select(m => m.GetProperty("role").GetString()!)
            .Distinct()
            .ToList();

        roles.Should().NotBeEmpty();
        roles.Should().BeSubsetOf(valid);
    }

    [Fact]
    public async Task api_h2h_co_order_dung_10_chi_so_va_pairs()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/h2h"));

        // index.html:259 -> x.pairs || {}
        doc.RootElement.TryGetProperty("pairs", out _).Should().BeTrue();

        var order = doc.RootElement.GetProperty("order").EnumerateArray()
            .Select(x => x.GetString()).ToList();

        order.Should().Equal([
            "winrate", "kills", "deaths", "killDiff", "totalKills",
            "firstBlood", "f10", "winWhenFb", "winWhenF10", "duration"
        ], "phải khớp đúng field order của h2h.json hiện tại");
    }

    [Fact]
    public async Task api_tiers_va_api_players_phuc_vu_duoc_du_lieu_bien_tap()
    {
        var client = factory.CreateClient();

        using var tiers = JsonDocument.Parse(await client.GetStringAsync("/api/tiers"));
        tiers.RootElement.TryGetProperty("tierOrder", out _).Should().BeTrue();

        using var players = JsonDocument.Parse(await client.GetStringAsync("/api/players"));
        // index.html:265 -> pl.players, pl.h, pl.i
        players.RootElement.TryGetProperty("players", out var arr).Should().BeTrue();
        arr.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task api_health_bao_so_luong_va_lich_su_ingest()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/health"));

        doc.RootElement.GetProperty("teams").GetInt32().Should().Be(16);
        doc.RootElement.GetProperty("matches").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("snapshots").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("recentRuns").GetArrayLength().Should().Be(0);
    }
}
