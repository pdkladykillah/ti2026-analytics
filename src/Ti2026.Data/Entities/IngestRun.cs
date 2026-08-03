namespace Ti2026.Data.Entities;

public enum IngestStatus
{
    Running,
    Succeeded,
    Failed,
    Skipped,
}

/// <summary>
/// Lịch sử mỗi vòng ingest. Giữ nguyên văn lỗi vì đó là thứ duy nhất cần để chẩn đoán
/// khi nguồn đổi layout hoặc bị chặn.
/// </summary>
public class IngestRun
{
    public int Id { get; set; }

    /// <summary>"opendota" | "dltv" | "snapshot"</summary>
    public required string Source { get; set; }

    /// <summary>UTC — xem ghi chú kiểu dữ liệu trong Match.StartTime.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? FinishedAt { get; set; }
    public IngestStatus Status { get; set; }
    public int ItemsWritten { get; set; }
    public string? ErrorMessage { get; set; }
}
