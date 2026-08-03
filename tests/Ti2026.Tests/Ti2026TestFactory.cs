using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ti2026.Tests;

/// <summary>
/// Chạy app thật (kể cả migrate + seed lúc startup) nhưng ghi DB vào thư mục tạm riêng
/// cho mỗi lần chạy, để test không đụng vào App_Data của máy dev và không phụ thuộc thứ
/// tự chạy.
///
/// CỐ Ý không ghi đè Ti2026:EditorialDirectory. Trước đây có ghi đè bằng đường dẫn tuyệt
/// đối, và chính vì vậy test không bao giờ chạm vào logic giải đường dẫn — api/tiers trả
/// 404 khi chạy thật mà test vẫn xanh. Để mặc định "data" thì test đi đúng đường mà
/// production đi.
/// </summary>
public class Ti2026TestFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), $"ti2026-web-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dataDir);
        builder.UseSetting("Ti2026:DataDirectory", _dataDir);
        builder.UseSetting("Ti2026:IngestToken", "");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* file bị giữ, bỏ qua */ }
    }
}
