namespace Ti2026.Web;

/// <summary>
/// Các đường dẫn đã giải xong, tính MỘT LẦN lúc startup và đăng ký singleton.
///
/// Tồn tại vì bài học sau đây: ban đầu Program.cs giải đường dẫn dữ liệu biên tập bằng
/// DirectoryResolver, còn endpoint lại tự nối ContentRootPath + option một lần nữa. Hai
/// cách tính khác nhau cho cùng một thứ, nên api/tiers trả 404 khi chạy thật — trong khi
/// test vẫn xanh vì test ghi đè bằng đường dẫn tuyệt đối và không bao giờ chạm nhánh sai.
///
/// Một nguồn sự thật duy nhất cho mỗi đường dẫn.
/// </summary>
public sealed class Ti2026Paths
{
    public required string DataDirectory { get; init; }
    public required string EditorialDirectory { get; init; }
    public required string DatabasePath { get; init; }
    public required string MediaDirectory { get; init; }

    public static Ti2026Paths Create(string contentRoot, Ti2026Options options)
    {
        // DataDirectory là volume trong Docker nên KHÔNG đi ngược lên tìm — luôn nối với
        // content root và tạo nếu chưa có. Đi ngược lên ở đây sẽ vô tình dùng App_Data
        // của thư mục cha.
        var dataDir = Path.IsPathRooted(options.DataDirectory)
            ? options.DataDirectory
            : Path.Combine(contentRoot, options.DataDirectory);

        return new Ti2026Paths
        {
            DataDirectory = dataDir,
            DatabasePath = Path.Combine(dataDir, "ti2026.db"),
            MediaDirectory = Path.Combine(dataDir, "media"),
            // Ngược lại, data/ nằm ở gốc repo khi chạy dotnet run nên phải đi ngược lên tìm
            EditorialDirectory = DirectoryResolver.Resolve(contentRoot, options.EditorialDirectory),
        };
    }

    public string EditorialFile(string fileName) => Path.Combine(EditorialDirectory, fileName);
}
