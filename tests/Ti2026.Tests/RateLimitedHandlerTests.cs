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

    [Fact]
    public async Task Ton_trong_Retry_After_khi_bi_429()
    {
        var handler = new RateLimitedHandler(requestsPerSecond: 1000)
        {
            InnerHandler = new TooManyRequestsHandler(TimeSpan.FromMilliseconds(150)),
        };
        using var client = new HttpClient(handler);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = await client.GetAsync("https://example.test/x");
        sw.Stop();

        res.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(140,
            "bị 429 thì phải chờ đúng Retry-After trước khi trả về");
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
}
