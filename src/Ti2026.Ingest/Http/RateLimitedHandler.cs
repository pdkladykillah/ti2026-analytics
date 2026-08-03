using System.Net;

namespace Ti2026.Ingest.Http;

/// <summary>
/// Tự giãn cách request để không bao giờ vượt giới hạn của nguồn.
///
/// Chủ động chậm còn hơn bị chặn IP: nếu OpenDota chặn IP của VPS thì cả app mất quyền truy
/// cập, không phải chỉ một vòng ingest thất bại. Vì vậy giới hạn ở phía mình chứ không đợi
/// bị 429 rồi mới xử lý — 429 chỉ là lưới cuối.
/// </summary>
public sealed class RateLimitedHandler(double requestsPerSecond) : DelegatingHandler
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minInterval =
        TimeSpan.FromSeconds(1.0 / Math.Max(requestsPerSecond, 0.01));

    private DateTimeOffset _lastSent = DateTimeOffset.MinValue;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // Giữ cổng trong lúc chờ để các lời gọi song song bị nối đuôi thật sự.
        // Nếu chỉ tính thời điểm rồi nhả cổng ngay, mọi request đồng thời sẽ thấy cùng một
        // _lastSent và bay đi cùng lúc — rate limit thành vô nghĩa.
        await _gate.WaitAsync(ct);
        try
        {
            var wait = _lastSent + _minInterval - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastSent = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
            await Task.Delay(retryAfter, ct);
        }

        return response;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _gate.Dispose();
        base.Dispose(disposing);
    }
}
