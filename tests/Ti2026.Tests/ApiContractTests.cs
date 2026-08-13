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

    /// <summary>
    /// VÁN và TRẬN phải là hai con số riêng, và nhãn cửa sổ phải nói đúng điều đang làm.
    ///
    /// Trước đây endpoint chỉ trả một con số tên `n`, UI đọc nó rồi ghi "71 trận" — trong khi 71
    /// là số VÁN, còn số trận thật chỉ 32. Một Bo3 đếm thành ba trận thì mọi cặp đấu trông như
    /// đã gặp nhau gấp ba lần thực tế.
    ///
    /// Và nhãn `window` từng ghi "6 tháng gần nhất" dù truy vấn không có bộ lọc thời gian nào —
    /// cặp Falcons–Liquid trải từ 12/2023. Một nhãn sai về phạm vi dữ liệu làm sai mọi kết luận
    /// rút ra từ nó.
    /// </summary>
    [Fact]
    public async Task api_h2h_tach_rieng_so_van_va_so_tran_va_khai_dung_pham_vi()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/h2h"));

        doc.RootElement.GetProperty("window").GetString()
            .Should().NotContain("6 tháng", "truy vấn không hề lọc theo thời gian");

        foreach (var pair in doc.RootElement.GetProperty("pairs").EnumerateObject())
        {
            var games = pair.Value.GetProperty("games").GetInt32();
            var seriesCount = pair.Value.GetProperty("seriesCount").GetInt32();

            seriesCount.Should().BeLessThanOrEqualTo(games,
                "một trận gồm một hoặc nhiều ván, nên số trận không thể lớn hơn số ván");
            seriesCount.Should().BeGreaterThan(0);

            pair.Value.TryGetProperty("firstMet", out _).Should().BeTrue();
            pair.Value.TryGetProperty("lastMet", out _).Should().BeTrue();
        }
    }

    /// <summary>
    /// Mỗi ván phải khai được BÊN NÀO là Radiant và mỗi bên còn mấy người của đội hình TI2026.
    ///
    /// Thiếu vế thứ nhất thì cột tỷ số "12 – 8" không cho biết ai được 12 — bảng bày ra một con
    /// số không đọc được. Thiếu vế thứ hai thì trang lại gộp trận của đội hình cũ vào: Falcons–
    /// Liquid có 71 ván nhưng 51 ván trong đó Liquid chỉ còn 3/5 người của hôm nay.
    /// </summary>
    [Fact]
    public async Task api_h2h_khai_ro_ben_radiant_va_so_nguoi_con_lai_cua_doi_hinh_TI2026()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/h2h"));

        foreach (var pair in doc.RootElement.GetProperty("pairs").EnumerateObject())
        {
            var slugs = pair.Name.Split('|');

            foreach (var row in pair.Value.GetProperty("series").EnumerateArray())
            {
                row.GetArrayLength().Should().Be(8,
                    "UI đọc [ngày, giải, điểmRad, điểmDire, slugRad, keptRad, keptDire, slugThắng]");

                slugs.Should().Contain(row[4].GetString()!,
                    "bên Radiant phải là một trong hai đội của chính cặp đấu");
                slugs.Should().Contain(row[7].GetString()!);

                foreach (var i in new[] { 5, 6 })
                    row[i].GetInt32().Should().BeInRange(0, 5, "một đội chỉ có 5 người ra trận");
            }

            var verdict = pair.Value.GetProperty("verdict");
            verdict.GetProperty("basis").GetString()
                .Should().BeOneOf("dung-doi-hinh", "lech-1-nguoi", "khong-du");
            verdict.GetProperty("text").GetString().Should().NotBeNullOrWhiteSpace();

            // Nhận định chỉ được dựa trên tập con của chính cặp đấu này
            verdict.GetProperty("games").GetInt32()
                .Should().BeLessThanOrEqualTo(pair.Value.GetProperty("games").GetInt32());

            // Số ván dùng + số ván bỏ phải bằng đúng tổng — không được rơi mất ván nào
            (verdict.GetProperty("games").GetInt32() + verdict.GetProperty("ignored").GetInt32())
                .Should().Be(pair.Value.GetProperty("games").GetInt32());
        }
    }

    /// <summary>
    /// Bảng đấu lấy từ API chính chủ của Valve, KHÔNG nhập tay. Endpoint phải nói được nguồn,
    /// và phải phân biệt "chưa nạp" với "đã nạp nhưng Valve chưa xếp giờ" — hai chuyện đó nhìn
    /// trên trang giống hệt nhau nếu không khai ra.
    /// </summary>
    [Fact]
    public async Task api_schedule_khai_ro_nguon_va_trang_thai_san_sang()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/schedule"));

        var root = doc.RootElement;
        root.GetProperty("source").GetString().Should().Contain("Valve");
        root.TryGetProperty("ready", out var ready).Should().BeTrue();
        root.TryGetProperty("stages", out var stages).Should().BeTrue();

        if (!ready.GetBoolean())
        {
            stages.GetArrayLength().Should().Be(0);
            root.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();
            return;
        }

        foreach (var st in stages.EnumerateArray())
        foreach (var s in st.GetProperty("series").EnumerateArray())
        {
            s.GetProperty("status").GetString()
                .Should().BeOneOf("da-xong", "dang-dien-ra", "chua-xep-gio", "sap-toi", "cho-ket-qua");

            // Nút chưa biết đội nào vào thì team phải là null, KHÔNG phải một đội rỗng
            foreach (var k in new[] { "team1", "team2" })
            {
                var t = s.GetProperty(k);
                if (t.ValueKind != JsonValueKind.Null)
                    t.TryGetProperty("slug", out _).Should().BeTrue();
            }
        }
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

    /// <summary>
    /// Chưa có trận nào thì phải nói "chưa đủ dữ liệu", KHÔNG được trả 0% hay 50% như thể đã
    /// đo được. Một con số bịa nhìn y hệt một con số thật.
    /// </summary>
    [Fact]
    public async Task api_calibration_chua_co_tran_thi_noi_thang_la_chua_do_duoc()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/calibration"));

        doc.RootElement.GetProperty("evaluated").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("buckets").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// app.js đọc đúng những khoá này để dựng thẻ "Bản game và sức nặng của dữ liệu".
    /// Đổi tên khoá ở API mà quên sửa JS thì thẻ trắng, và không có gì báo.
    /// </summary>
    [Fact]
    public async Task api_patches_tra_dung_shape_ma_app_js_dang_doc()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/patches"));

        foreach (var field in new[] { "currentPatch", "patchRegression", "note", "patches" })
            doc.RootElement.TryGetProperty(field, out _).Should().BeTrue($"app.js cần '{field}'");

        doc.RootElement.GetProperty("patches").GetArrayLength().Should().Be(0,
            "chưa ingest trận nào nên chưa biết bản game — trả mảng rỗng chứ không phải lỗi");
        doc.RootElement.GetProperty("currentPatch").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task api_draft_va_api_lanes_chua_co_du_lieu_thi_noi_thang()
    {
        var client = factory.CreateClient();

        using var draft = JsonDocument.Parse(await client.GetStringAsync("/api/draft"));
        draft.RootElement.GetProperty("matchesWithDraft").GetInt32().Should().Be(0);
        draft.RootElement.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();

        using var lanes = JsonDocument.Parse(await client.GetStringAsync("/api/lanes"));
        lanes.RootElement.GetProperty("lanes").GetArrayLength().Should().Be(0);
        lanes.RootElement.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Endpoint duy nhất gọi ra nguồn ngoài theo yêu cầu người dùng. Id không hợp lệ phải bị
    /// chặn TRƯỚC khi phát sinh request thật — nếu không thì ai cũng ép VPS gọi OpenDota bằng
    /// rác, và bị chặn IP là mất nguồn dữ liệu cho cả ứng dụng.
    /// </summary>
    /// <summary>
    /// Tier list tính động và job pub của pro đều phải xuống cấp tử tế khi chưa có dữ liệu —
    /// đây là trạng thái của mọi lần deploy đầu tiên.
    /// </summary>
    [Fact]
    public async Task api_tierlist_va_pro_pub_chua_co_du_lieu_thi_noi_thang()
    {
        var client = factory.CreateClient();

        using var tl = JsonDocument.Parse(await client.GetStringAsync("/api/tierlist"));
        tl.RootElement.GetProperty("heroes").GetArrayLength().Should().Be(0);
        tl.RootElement.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();

        using var pp = JsonDocument.Parse(await client.GetStringAsync("/api/pro-pub"));
        pp.RootElement.GetProperty("heroes").GetArrayLength().Should().Be(0);
        pp.RootElement.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// api/me hứa KHÔNG lưu gì xuống DB, nên nó cũng không được trả URL avatar: cache thì phá
    /// lời hứa, mà hotlink thì không hiện được. Test này khoá lời hứa đó lại.
    /// </summary>
    /// <summary>
    /// Bản trước của test này assert ready == false — nó khoá TRẠNG THÁI TẠM THỜI "chưa điền hệ
    /// số" chứ không phải một tính chất bền vững, nên vừa điền xong bảng hệ số là nó đỏ.
    ///
    /// Giờ nó kiểm ba điều đúng mãi: bảng hệ số đọc được, giới hạn của NGUỒN luôn được khai
    /// (điền hệ số cũng không cứu được Lotus), và chưa có ván nào thì xuống cấp tử tế.
    /// </summary>
    [Fact]
    public async Task api_fantasy_doc_duoc_he_so_va_xuong_cap_tu_te_khi_chua_co_van()
    {
        var client = factory.CreateClient();

        using var cfg = JsonDocument.Parse(await client.GetStringAsync("/api/fantasy/config"));
        cfg.RootElement.GetProperty("ready").GetBoolean().Should().BeTrue(
            "data/fantasy.json đã có đủ hệ số");
        cfg.RootElement.GetProperty("missingCoefficients").GetArrayLength().Should().Be(0);
        cfg.RootElement.GetProperty("stats").GetArrayLength().Should().BeGreaterThan(10);

        // Giới hạn của NGUỒN phải luôn được khai, kể cả khi hệ số đã đủ
        cfg.RootElement.GetProperty("unavailable").GetArrayLength().Should().BeGreaterThan(0);

        // DB của test chưa có ván nào — phải nói rõ, không được trả bảng rỗng không lời giải thích
        using var pl = JsonDocument.Parse(await client.GetStringAsync("/api/fantasy/players"));
        pl.RootElement.GetProperty("players").GetArrayLength().Should().Be(0);
        pl.RootElement.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();

        using var op = JsonDocument.Parse(await client.GetStringAsync("/api/fantasy/optimize"));
        op.RootElement.GetProperty("roster").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task api_me_khong_tra_ve_avatar()
    {
        var res = await factory.CreateClient().GetAsync("/api/me?id=86745912");

        // Không có mạng trong test nên tra cứu sẽ trượt; điều cần khoá là shape, không phải dữ liệu
        var body = await res.Content.ReadAsStringAsync();
        body.Should().NotContain("\"avatar\"");
    }

    [Fact]
    public async Task api_me_tu_choi_id_khong_hop_le_truoc_khi_goi_ra_ngoai()
    {
        var client = factory.CreateClient();

        foreach (var bad in new[] { "", "abc", "0", "-1", "9999999999" })
        {
            var res = await client.GetAsync($"/api/me?id={bad}");
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"id '{bad}' không hợp lệ");
        }

        (await client.GetAsync("/api/me")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "thiếu id cũng phải bị từ chối");
    }
}
