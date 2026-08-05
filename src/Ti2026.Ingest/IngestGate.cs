namespace Ti2026.Ingest;

/// <summary>
/// Đảm bảo chỉ MỘT vòng ingest chạy tại một thời điểm trong cả tiến trình.
///
/// VÌ SAO CẦN: SQLite chỉ có một người ghi. Hai vòng ingest chồng nhau thì vòng sau nằm chờ
/// khoá cho tới khi hết CommandTimeout 30 giây rồi ném lỗi — và đã xảy ra thật: một lời gọi
/// POST api/ingest/run bị curl bỏ ngang vẫn tiếp tục chạy phía server, rồi lời gọi tiếp theo
/// chết ngay ở "INSERT INTO IngestRuns" sau đúng 30 giây. Thông báo lỗi lúc đó nói về DbCommand
/// và không hề nhắc gì tới nguyên nhân thật.
///
/// Ngoài chuyện khoá, chạy chồng còn đốt hạn mức request của OpenDota để làm đúng một việc hai
/// lần — và hạn mức đó là tài nguyên có thể mất hẳn nếu bị chặn IP.
/// </summary>
public sealed class IngestGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsRunning => _gate.CurrentCount == 0;

    /// <summary>
    /// Thử vào cổng mà KHÔNG chờ. Trả null khi đang có vòng khác chạy — người gọi phải xử lý
    /// trường hợp đó tử tế thay vì nằm chờ, vì "chờ" ở đây nghĩa là giữ một HTTP request mở
    /// hàng phút.
    /// </summary>
    public IDisposable? TryEnter() => _gate.Wait(0) ? new Releaser(_gate) : null;

    /// <summary>Dành cho scheduler: chờ tới lượt, vì nó không có ai ngồi đợi phản hồi.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        return new Releaser(_gate);
    }

    public void Dispose() => _gate.Dispose();

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            gate.Release();
        }
    }
}
