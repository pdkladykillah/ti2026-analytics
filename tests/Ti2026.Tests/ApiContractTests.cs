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

    /// <summary>
    /// Bản trước của test này assert stats == null, tức mã hoá chính cái lỗi "seed bỏ qua khối
    /// stats" thành kỳ vọng — trang render gần như trắng mà test vẫn xanh. Giờ nó kiểm điều
    /// đúng: đủ 12 khoá, đúng kiểu số, đúng đơn vị.
    /// </summary>
    [Fact]
    public async Task api_teams_tra_du_12_chi_so_ngay_khi_moi_seed()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/teams"));

        var teams = doc.RootElement.GetProperty("teams");
        var withStats = teams.EnumerateArray()
            .Where(t => t.GetProperty("stats").ValueKind != JsonValueKind.Null)
            .ToList();

        withStats.Should().HaveCount(16,
            "cả 16 đội trong teams.json đều có khối stats, phải nạp hết vào snapshot");

        // index.html:284 dựng bảng từ đúng 12 khoá này; thiếu một khoá là cột đó in "—"
        var stats = withStats[0].GetProperty("stats");
        foreach (var key in new[] { "maps", "winrate", "kills", "deaths", "assists",
                                    "firstBlood", "f10", "winWhenFb", "winWhenF10",
                                    "duration", "totalKills", "killDiff" })
        {
            stats.TryGetProperty(key, out var v).Should().BeTrue($"index.html cần stats.{key}");
            v.ValueKind.Should().Be(JsonValueKind.Number,
                $"index.html:284 gọi toFixed()/phép toán trên stats.{key} nên phải là số");
        }
    }

    [Fact]
    public async Task api_teams_giu_dung_don_vi_cua_JSON_cu()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/teams"));

        var falcons = doc.RootElement.GetProperty("teams").EnumerateArray()
            .First(t => t.GetProperty("slug").GetString() == "team-falcons")
            .GetProperty("stats");

        // Đối chiếu trực tiếp với data/teams.json: winrate 60, kills 27.38, killDiff 1.15,
        // duration 44 (PHÚT), maps 159
        falcons.GetProperty("maps").GetInt32().Should().Be(159);
        falcons.GetProperty("winrate").GetDouble().Should().Be(60);
        falcons.GetProperty("kills").GetDouble().Should().BeApproximately(27.38, 0.01);
        falcons.GetProperty("killDiff").GetDouble().Should().BeApproximately(1.15, 0.01);
        falcons.GetProperty("duration").GetDouble().Should().Be(44);
        falcons.GetProperty("winWhenF10").GetDouble().Should().Be(78);
    }

    /// <summary>
    /// index.html:251 tính tier từ winrate: >=60 -> S, >=50 -> A, còn lại B. Nếu winrate trả
    /// về dạng phân số 0..1 thay vì phần trăm thì mọi đội đều thành tier B mà không có lỗi nào.
    /// </summary>
    [Fact]
    public async Task winrate_la_phan_tram_chu_khong_phai_phan_so()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/teams"));

        var winrates = doc.RootElement.GetProperty("teams").EnumerateArray()
            .Where(t => t.GetProperty("stats").ValueKind != JsonValueKind.Null)
            .Select(t => t.GetProperty("stats").GetProperty("winrate").GetDouble())
            .ToList();

        winrates.Should().OnlyContain(w => w >= 1 && w <= 100);
        winrates.Should().Contain(w => w >= 50, "phải có đội đạt tier S hoặc A");
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
        doc.RootElement.GetProperty("players").GetInt32().Should().BeGreaterThan(0);
        doc.RootElement.GetProperty("matches").GetInt32().Should().Be(0,
            "chưa có vòng ingest OpenDota nào — đó là việc của M2");
        doc.RootElement.GetProperty("snapshots").GetInt32().Should().Be(16,
            "seed phải nạp snapshot cho cả 16 đội, nếu không trang render trắng");
        doc.RootElement.GetProperty("recentRuns").GetArrayLength().Should().Be(0);
    }
}
