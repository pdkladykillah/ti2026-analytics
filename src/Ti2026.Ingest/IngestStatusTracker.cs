namespace Ti2026.Ingest;

/// <summary>
/// Trạng thái sống của scheduler, để trang có thể nói ĐANG chạy gì và LÚC NÀO chạy tiếp.
///
/// Vì sao không suy từ IngestRun: "lần chạy cuối + chu kỳ" chỉ là ước lượng, và nó sai ngay khi
/// một vòng chạy lâu hơn chu kỳ hoặc container vừa khởi động lại. Mốc kế tiếp là thứ chỉ
/// BackgroundService biết chắc, nên để nó tự khai ra.
///
/// Singleton, ghi từ một luồng và đọc từ nhiều luồng — dùng trường volatile thay vì khoá vì đây
/// chỉ là hai mốc thời gian, đọc lệch một nhịp không gây hại gì.
/// </summary>
public sealed class IngestStatusTracker
{
    private volatile object? _nextRunAt;
    private volatile object? _lastStartedAt;

    /// <summary>UTC. null khi scheduler tắt hoặc chưa tới vòng đầu.</summary>
    public DateTime? NextRunAt => (DateTime?)_nextRunAt;

    public DateTime? LastStartedAt => (DateTime?)_lastStartedAt;

    /// <summary>Scheduler có bật không. Tắt thì trang phải nói thế, không để người đọc tự đoán.</summary>
    public bool Enabled { get; set; }

    public TimeSpan Interval { get; set; }

    public void MarkRunStarted(DateTime utcNow)
    {
        _lastStartedAt = utcNow;
        _nextRunAt = null;
    }

    public void MarkNextRun(DateTime utcNext) => _nextRunAt = utcNext;
}
