namespace Ti2026.Data.Entities;

/// <summary>
/// Ảnh đã tải về VPS thay vì hotlink. ETag để không tải lại thứ chưa đổi —
/// vừa bớt tốn bandwidth của nguồn, vừa hết cảnh ảnh chết khi họ đổi đường dẫn.
/// </summary>
public class MediaAsset
{
    public int Id { get; set; }
    public required string SourceUrl { get; set; }

    /// <summary>Tương đối với App_Data/media.</summary>
    public required string LocalPath { get; set; }

    /// <summary>Dùng làm URL phục vụ: media/{ContentHash}.</summary>
    public required string ContentHash { get; set; }

    public string? ETag { get; set; }
    public string? ContentType { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}
