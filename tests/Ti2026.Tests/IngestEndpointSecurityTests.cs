using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ti2026.Tests;

/// <summary>
/// Factory có cấu hình token, để test được cả nhánh từ chối và nhánh chấp nhận.
/// KHÔNG bật IngestEnabled — không test nào được phép gọi ra mạng thật.
/// </summary>
public class TokenedFactory : WebApplicationFactory<Program>
{
    public const string Token = "token-cho-test-32-ky-tu-abcdefgh";

    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), $"ti2026-tok-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dataDir);
        builder.UseSetting("Ti2026:DataDirectory", _dataDir);
        builder.UseSetting("Ti2026:IngestToken", Token);
        builder.UseSetting("Ti2026:IngestEnabled", "false");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dataDir, recursive: true); } catch { }
    }
}

public class IngestEndpointSecurityTests(TokenedFactory factory)
    : IClassFixture<TokenedFactory>
{
    [Fact]
    public async Task Khong_co_token_thi_tu_choi()
    {
        var res = await factory.CreateClient().PostAsync("/api/ingest/run", null);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_sai_thi_tu_choi()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Ingest-Token", "sai-be-bet");

        var res = await client.PostAsync("/api/ingest/run", null);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_dai_bang_nhung_khac_noi_dung_thi_tu_choi()
    {
        var client = factory.CreateClient();
        var sameLength = new string('x', TokenedFactory.Token.Length);
        client.DefaultRequestHeaders.Add("X-Ingest-Token", sameLength);

        var res = await client.PostAsync("/api/ingest/run", null);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "FixedTimeEquals phải so cả nội dung, không chỉ độ dài");
    }
}

/// <summary>
/// Factory KHÔNG cấu hình token — endpoint phải tự vô hiệu hoá thay vì mở cửa.
/// </summary>
public class NoTokenFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), $"ti2026-notok-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dataDir);
        builder.UseSetting("Ti2026:DataDirectory", _dataDir);
        builder.UseSetting("Ti2026:IngestToken", "");
        builder.UseSetting("Ti2026:IngestEnabled", "false");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dataDir, recursive: true); } catch { }
    }
}

public class IngestEndpointWithoutTokenTests(NoTokenFactory factory)
    : IClassFixture<NoTokenFactory>
{
    /// <summary>
    /// Chưa cấu hình token thì phải ĐÓNG, không phải mở. Mặc định mở nghĩa là ai cũng ép được
    /// VPS spam OpenDota tới mức bị chặn IP — mất IP là mất luôn nguồn dữ liệu.
    /// </summary>
    [Fact]
    public async Task Chua_cau_hinh_token_thi_endpoint_bi_vo_hieu_hoa()
    {
        var res = await factory.CreateClient().PostAsync("/api/ingest/run", null);

        res.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
