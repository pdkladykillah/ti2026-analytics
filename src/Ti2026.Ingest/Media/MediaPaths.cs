namespace Ti2026.Ingest.Media;

/// <summary>
/// Nơi chứa ảnh đã tải về. Truyền vào từ Ti2026Paths lúc startup thay vì để tầng Ingest tự tính:
/// hai chỗ tự tính cùng một đường dẫn là đúng cái lỗi đã làm api/tiers trả 404 khi chạy thật.
/// </summary>
public sealed record MediaPaths(string MediaDirectory);
