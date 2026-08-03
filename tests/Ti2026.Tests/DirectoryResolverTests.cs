using FluentAssertions;
using Ti2026.Web;

namespace Ti2026.Tests;

public class DirectoryResolverTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"ti2026-resolve-{Guid.NewGuid():N}");

    [Fact]
    public void Tim_thay_ngay_khi_thu_muc_nam_canh_content_root()
    {
        // Hoàn cảnh Docker: Dockerfile COPY data ./data nên nằm ngay cạnh dll
        var contentRoot = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(contentRoot, "data"));

        var resolved = DirectoryResolver.Resolve(contentRoot, "data");

        resolved.Should().Be(Path.Combine(contentRoot, "data"));
    }

    [Fact]
    public void Di_nguoc_len_tim_khi_thu_muc_o_goc_repo()
    {
        // Hoàn cảnh dotnet run --project src/Ti2026.Web: content root là thư mục project,
        // còn data/ ở gốc repo. Đây chính là nhánh mà bug api/tiers 404 đã đi qua.
        var repoRoot = Path.Combine(_root, "repo");
        var contentRoot = Path.Combine(repoRoot, "src", "Ti2026.Web");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.Combine(repoRoot, "data"));

        var resolved = DirectoryResolver.Resolve(contentRoot, "data");

        resolved.Should().Be(Path.Combine(repoRoot, "data"));
    }

    [Fact]
    public void Duong_dan_tuyet_doi_thi_dung_nguyen()
    {
        var absolute = Path.Combine(_root, "somewhere", "data");

        DirectoryResolver.Resolve(Path.Combine(_root, "app"), absolute).Should().Be(absolute);
    }

    [Fact]
    public void Khong_tim_thay_thi_tra_phuong_an_noi_truc_tiep()
    {
        var contentRoot = Path.Combine(_root, "empty");
        Directory.CreateDirectory(contentRoot);

        var resolved = DirectoryResolver.Resolve(contentRoot, "khong-ton-tai");

        // Trả về phương án nối trực tiếp để lời gọi phía sau báo lỗi ở chỗ dễ hiểu,
        // thay vì trả null rồi nổ NullReference ở nơi khác
        resolved.Should().Be(Path.Combine(contentRoot, "khong-ton-tai"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
