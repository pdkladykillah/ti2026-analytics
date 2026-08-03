using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ti2026.Tests;

/// <summary>
/// Factory trỏ vào một thư mục biên tập chứa teams.json gõ sai cú pháp — mô phỏng đúng tai nạn
/// sẽ xảy ra với quy trình sửa file bằng tay.
/// </summary>
public class BrokenEditorialFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), $"ti2026-bad-db-{Guid.NewGuid():N}");
    private readonly string _editorialDir =
        Path.Combine(Path.GetTempPath(), $"ti2026-bad-ed-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_editorialDir);
        File.WriteAllText(Path.Combine(_editorialDir, "teams.json"), "{ \"teams\": [ , ] }");

        builder.UseSetting("Ti2026:DataDirectory", _dataDir);
        builder.UseSetting("Ti2026:EditorialDirectory", _editorialDir);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var d in new[] { _dataDir, _editorialDir })
            try { Directory.Delete(d, recursive: true); } catch { }
    }
}

public class BadEditorialFileTests(BrokenEditorialFactory factory)
    : IClassFixture<BrokenEditorialFactory>
{
    /// <summary>
    /// Nguyên tắc của spec: "dữ liệu hơi lỗi thời tốt hơn dữ liệu rỗng". Một dấu phẩy thừa
    /// trong teams.json không được phép làm host chết — nếu chết thì site không phục vụ gì cả
    /// và mọi lần restart sau đều lặp lại cú crash, dù DB vẫn giữ nguyên dữ liệu tốt.
    /// </summary>
    [Fact]
    public async Task JSON_bien_tap_go_sai_thi_app_van_len_va_van_phuc_vu()
    {
        var client = factory.CreateClient();

        var health = await client.GetAsync("/api/health");
        health.StatusCode.Should().Be(HttpStatusCode.OK,
            "app phải khởi động được dù seed thất bại");

        var teams = await client.GetAsync("/api/teams");
        teams.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Seed_that_bai_thi_DB_rong_chu_khong_nua_voi()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/health"));

        doc.RootElement.GetProperty("teams").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("snapshots").GetInt32().Should().Be(0);
    }
}
