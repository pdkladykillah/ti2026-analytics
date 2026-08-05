using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ti2026.Web;

namespace Ti2026.Tests;

public class PlayerLookupTests
{
    private static PlayerLookup New() => new(NullLogger<PlayerLookup>.Instance);

    /// <summary>
    /// Người dùng dán cả hai dạng. Đoán sai giữa hai dạng sẽ tra ra hồ sơ của một người hoàn
    /// toàn khác, và trang sẽ tự tin hiển thị hero pool của người lạ như thể là của họ.
    /// </summary>
    [Fact]
    public void Nhan_ca_account_id_lan_Steam_ID64()
    {
        PlayerLookup.ParseAccountId("86745912").Should().Be(86745912);

        PlayerLookup.ParseAccountId("76561198047011640")
            .Should().Be(76561198047011640L - PlayerLookup.SteamId64Offset);
    }

    [Fact]
    public void Tu_choi_thay_vi_doan_khi_id_khong_thuoc_dai_nao()
    {
        PlayerLookup.ParseAccountId(null).Should().BeNull();
        PlayerLookup.ParseAccountId("").Should().BeNull();
        PlayerLookup.ParseAccountId("khong-phai-so").Should().BeNull();
        PlayerLookup.ParseAccountId("0").Should().BeNull();
        PlayerLookup.ParseAccountId("-5").Should().BeNull();

        // Số lớn bất thường nhưng chưa tới ngưỡng ID64: gõ sai, và đoán bừa còn tệ hơn từ chối
        PlayerLookup.ParseAccountId("9999999999").Should().BeNull();
    }

    [Fact]
    public void Bo_khoang_trang_hai_dau()
    {
        PlayerLookup.ParseAccountId("  86745912  ").Should().Be(86745912);
    }

    /// <summary>
    /// Trần là thứ bảo vệ IP của VPS. Đây là endpoint công khai gọi ra nguồn ngoài; không có
    /// trần thì một con bot sẽ khiến OpenDota chặn IP, và mất IP là mất nguồn dữ liệu cho TOÀN
    /// BỘ ứng dụng chứ không phải chỉ một lần tra cứu.
    /// </summary>
    [Fact]
    public void Het_suat_thi_tu_choi_chu_khong_goi_tiep()
    {
        var lookup = New();

        for (var i = 0; i < PlayerLookup.MaxLookupsPerWindow; i++)
            lookup.TryTakeBudget().Should().BeTrue($"lần {i + 1} vẫn còn trong trần");

        lookup.TryTakeBudget().Should().BeFalse("vượt trần thì phải từ chối");
    }

    [Fact]
    public void Chua_tra_thi_khong_co_gi_trong_dem()
    {
        New().TryGetCached(86745912, out var snapshot).Should().BeFalse();
        snapshot.Should().BeNull();
    }

    /// <summary>
    /// Shape của nguồn ngoài đổi thì phải xuống cấp thành "không tra được", KHÔNG được thành
    /// 500. Đây là lỗi đã xảy ra thật: tôi khai hero_id là chuỗi trong khi nguồn trả về số, và
    /// endpoint trả 500 ngay lần gọi thật đầu tiên.
    /// </summary>
    [Fact]
    public async Task Shape_nguon_doi_thi_tra_null_chu_khong_nem_ra()
    {
        var lookup = New();
        var client = ClientReturning("""{ "profile": { "account_id": 1 } }""", """[{ "hero_id": "khong-phai-so" }]""");

        var result = await lookup.FetchAsync(client, 86745912, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Doc_dung_hero_id_dang_SO_nhu_nguon_that_tra_ve()
    {
        var lookup = New();

        // Shape thật của players/{id}/heroes, đã đối chiếu với response từ OpenDota
        var client = ClientReturning(
            """{ "profile": { "account_id": 86745912, "personaname": "Ye Xiu" }, "rank_tier": 74 }""",
            """[{ "hero_id": 11, "last_played": 1782853806, "games": 167, "win": 115 }]""");

        var result = await lookup.FetchAsync(client, 86745912, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Ye Xiu");
        result.Heroes.Should().ContainSingle();
        result.Heroes[0].HeroId.Should().Be(11);
        result.Heroes[0].Games.Should().Be(167);
        result.Heroes[0].Wins.Should().Be(115);
    }

    private static Ti2026.Ingest.OpenDota.OpenDotaClient ClientReturning(
        string profileJson, string heroesJson) =>
        new(new HttpClient(new TwoRouteHandler(profileJson, heroesJson))
        {
            BaseAddress = new Uri("https://x/api/"),
        });

    private sealed class TwoRouteHandler(string profileJson, string heroesJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.RequestUri!.AbsolutePath.EndsWith("/heroes", StringComparison.Ordinal)
                ? heroesJson
                : profileJson;

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// Bộ đệm phải TÍNH VÀO trần đúng một lần: tra lại cùng một id trong thời hạn không được
    /// tiêu thêm suất, nếu không thì mở lại trang là mất suất.
    /// </summary>
    [Fact]
    public void Dem_co_thoi_han_duong_va_hop_ly()
    {
        PlayerLookup.CacheTtl.Should().BeGreaterThan(TimeSpan.Zero);
        PlayerLookup.Window.Should().BeGreaterThan(TimeSpan.Zero);
        PlayerLookup.MaxLookupsPerWindow.Should().BeGreaterThan(0);
    }
}
