using System.Net;

namespace Ti2026.Ingest.Http;

/// <summary>
/// Tự giãn cách request để không bao giờ vượt giới hạn của nguồn.
///
/// Chủ động chậm còn hơn bị chặn IP: nếu OpenDota chặn IP của VPS thì cả app mất quyền truy
/// cập, không phải chỉ một vòng ingest thất bại. Vì vậy giới hạn ở phía mình chứ không đợi
/// bị 429 rồi mới xử lý — 429 chỉ là lưới cuối.
/// </summary>
public sealed class RateLimitedHandler(double requestsPerSecond, ApiCallMeter? meter = null)
    : DelegatingHandler
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minInterval =
        TimeSpan.FromSeconds(1.0 / Math.Max(requestsPerSecond, 0.01));

    private DateTimeOffset _lastSent = DateTimeOffset.MinValue;

    /// <summary>Khi nguồn không nói Retry-After thì tự chọn mức lùi này.</summary>
    public static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(30);

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

        // Đếm ở ĐÂY, ngay trước khi gửi — không đếm ở tầng client.
        //
        // Tầng client không thấy hết: một lời gọi bị 429 rồi thử lại vẫn là HAI lời gọi bị
        // tính tiền, còn tầng trên chỉ biết một. Đây là chỗ duy nhất mọi request bay ra đều
        // đi qua, nên cũng là chỗ duy nhất đếm được đúng.
        meter?.Record();

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // TUYỆT ĐỐI KHÔNG ngủ ở đây.
            //
            // Bản đầu tiên gọi Task.Delay(30s) tại chỗ này. Nhưng Task.Delay bên trong
            // SendAsync tính vào HttpClient.Timeout, mà timeout cũng là 30 giây — nên mọi 429
            // đều biến thành TaskCanceledException. Hậu quả thật, đã thấy trên production:
            // tầng trên mất khả năng phân biệt "bị giới hạn tốc độ" với "mạng chết", exception
            // vượt qua khối catch (chỉ bắt HttpRequestException), và cả mẻ 200 ván bị rollback
            // sau khi đã tải xong hơn một trăm ván.
            //
            // Cách đúng: đẩy mốc cho phép của request KẾ TIẾP ra xa, rồi trả 429 về NGAY để
            // tầng trên tự quyết định.
            var retryAfter = response.Headers.RetryAfter?.Delta ?? DefaultBackoff;
            await DeferNextRequestAsync(retryAfter, ct);
        }

        return response;
    }

    /// <summary>Lùi mốc gửi để request kế tiếp phải chờ đúng <paramref name="delay"/>.</summary>
    private async Task DeferNextRequestAsync(TimeSpan delay, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var next = DateTimeOffset.UtcNow + delay - _minInterval;
            if (next > _lastSent) _lastSent = next;
        }
        finally
        {
            _gate.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _gate.Dispose();
        base.Dispose(disposing);
    }
}
