using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ti2026.Ingest;

/// <param name="MaxMatchDetailsPerRun">
/// Trần số ván nạp detail mỗi vòng. Mỗi ván tốn một request, nên nạp bù toàn bộ lịch sử ở
/// 1 req/giây mất nhiều chục phút. Chia nhỏ để mỗi vòng vẫn commit được và không giữ
/// transaction mở quá lâu.
/// </param>
public sealed record IngestSchedule(
    TimeSpan Interval,
    TimeSpan InitialDelay,
    int MaxMatchDetailsPerRun);

public class IngestBackgroundService(
    IServiceScopeFactory scopeFactory,
    IngestSchedule schedule,
    IngestStatusTracker status,
    ILogger<IngestBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Ingest sẽ chạy mỗi {Hours} giờ, vòng đầu sau {Delay} giây",
            schedule.Interval.TotalHours, schedule.InitialDelay.TotalSeconds);

        // Hoãn vòng đầu để restart liên tục không thành spam nguồn dữ liệu, và để app kịp
        // phục vụ request trước khi làm việc nặng.
        status.Enabled = true;
        status.Interval = schedule.Interval;
        status.MarkNextRun(DateTime.UtcNow + schedule.InitialDelay);

        try
        {
            await Task.Delay(schedule.InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(schedule.Interval);
        do
        {
            // try/catch NGOÀI CÙNG là BẮT BUỘC, không được bỏ: exception thoát khỏi
            // ExecuteAsync sẽ hạ cả host — sập luôn web, không chỉ ingest. Một lần dltv đổi
            // layout không được phép làm trang không truy cập được.
            try
            {
                status.MarkRunStarted(DateTime.UtcNow);

                using var scope = scopeFactory.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<IngestPipeline>();
                await pipeline.RunAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Vòng ingest lỗi không mong đợi — sẽ thử lại ở vòng sau");
            }
            finally
            {
                // Đặt trong finally: vòng lỗi vẫn phải công bố mốc kế tiếp, nếu không trang sẽ
                // hiện "không biết bao giờ chạy lại" đúng lúc người ta cần biết nhất.
                status.MarkNextRun(DateTime.UtcNow + schedule.Interval);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
