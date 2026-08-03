namespace Ti2026.Web;

public static class DirectoryResolver
{
    /// <summary>
    /// Giải đường dẫn thư mục cấu hình được, chịu được ba hoàn cảnh chạy khác nhau:
    ///
    /// 1. Docker  — Dockerfile COPY data ./data nên nằm ngay cạnh dll, tìm thấy ngay.
    /// 2. dotnet run --project src/Ti2026.Web — ContentRootPath là thư mục project,
    ///    còn data/ ở gốc repo, nên phải đi ngược lên tìm.
    /// 3. Test — content root do WebApplicationFactory quyết định, cũng cần đi ngược lên.
    ///
    /// Trả về đường dẫn tuyệt đối đầu tiên tồn tại; nếu không tìm được thì trả về phương án
    /// nối trực tiếp với contentRoot để lời gọi phía sau báo lỗi ở đúng chỗ dễ hiểu.
    /// </summary>
    public static string Resolve(string contentRoot, string configured, int maxLevelsUp = 6)
    {
        if (Path.IsPathRooted(configured))
            return configured;

        var direct = Path.Combine(contentRoot, configured);
        if (Directory.Exists(direct)) return direct;

        var dir = new DirectoryInfo(contentRoot);
        for (var i = 0; i < maxLevelsUp && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, configured);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return direct;
    }
}
