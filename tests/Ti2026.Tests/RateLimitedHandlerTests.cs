using System.Net;
using FluentAssertions;
using Ti2026.Ingest.Http;

namespace Ti2026.Tests;

public class RateLimitedHandlerTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task Gian_cach_toi_thieu_giua_cac_request()
    {
        var inner = new CountingHandler();
        // 20 req/s -> giãn cách 50ms. Dùng 20 chứ không phải 1 để test chạy trong 100ms
        // thay vì 2 giây — bộ test chậm là bộ test không ai chạy.
        var handler = new RateLimitedHandler(requestsPerSecond: 20) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await client.GetAsync("https://example.test/a");
        await client.GetAsync("https://example.test/b");
        await client.GetAsync("https://example.test/c");
        sw.Stop();

        inner.Calls.Should().Be(3);
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(90,
            "3 request ở 20 req/s phải mất ít nhất 2 khoảng giãn cách");
    }

    [Fact]
    public async Task Gian_cach_ap_dung_ca_khi_goi_song_song()
    {
        var inner = new CountingHandler();
        var handler = new RateLimitedHandler(requestsPerSecond: 20) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(i => client.GetAsync($"https://example.test/{i}")));
        sw.Stop();

        inner.Calls.Should().Be(4);
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(140,
            "gọi song song vẫn phải bị nối đuôi, nếu không thì rate limit vô nghĩa");
    }

    /// <summary>
    /// Bản trước của test này assert "bị 429 thì phải chờ đúng Retry-After TRƯỚC KHI trả về" —
    /// tức mã hoá chính cái lỗi thành kỳ vọng, và test vẫn xanh trong lúc production gãy.
    ///
    /// Vì sao chờ tại chỗ là sai: Task.Delay bên trong SendAsync tính vào HttpClient.Timeout.
    /// Mức lùi mặc định 30 giây bằng đúng timeout 30 giây, nên MỌI 429 đều biến thành
    /// TaskCanceledException. Tầng trên chỉ bắt HttpRequestException nên exception lọt qua, và
    /// orchestrator rollback cả mẻ 200 ván sau khi đã tải xong hơn một trăm ván.
    /// </summary>
    [Fact]
    public async Task Bi_429_thi_tra_ve_ngay_khong_ngu_trong_request()
    {
        var handler = new RateLimitedHandler(requestsPerSecond: 1000)
        {
            InnerHandler = new TooManyRequestsHandler(TimeSpan.FromSeconds(30)),
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(1) };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = await client.GetAsync("https://example.test/x");
        sw.Stop();

        res.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "tầng trên phải NHÌN THẤY 429 để phân biệt bị giới hạn với mạng chết");
        sw.ElapsedMilliseconds.Should().BeLessThan(500,
            "mức lùi 30 giây không được tiêu vào timeout của chính request đó");
    }

    [Fact]
    public async Task Bi_429_thi_lui_request_KE_TIEP_dung_Retry_After()
    {
        var inner = new FlakyHandler(retryAfter: TimeSpan.FromMilliseconds(300));
        var handler = new RateLimitedHandler(requestsPerSecond: 1000) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        var first = await client.GetAsync("https://example.test/1");
        first.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var second = await client.GetAsync("https://example.test/2");
        sw.Stop();

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(250,
            "sau 429 thì request kế tiếp mới là chỗ phải chờ — chờ ở đó không tốn timeout của ai");
    }

    private sealed class TooManyRequestsHandler(TimeSpan retryAfter) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage r, CancellationToken ct)
        {
            var res = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            res.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter);
            return Task.FromResult(res);
        }
    }

    /// <summary>429 lần đầu, rồi OK — để đo xem mức lùi có rơi vào request kế tiếp không.</summary>
    private sealed class FlakyHandler(TimeSpan retryAfter) : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage r, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) > 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

            var res = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            res.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter);
            return Task.FromResult(res);
        }
    }
}
