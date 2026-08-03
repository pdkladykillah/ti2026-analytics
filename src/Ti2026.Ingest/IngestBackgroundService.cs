using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ti2026.Ingest;

public sealed record IngestSchedule(TimeSpan Interval, TimeSpan InitialDelay);

public class IngestBackgroundService(
    IServiceScopeFactory scopeFactory,
    IngestSchedule schedule,
    ILogger<IngestBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Ingest sẽ chạy mỗi {Hours} giờ, vòng đầu sau {Delay} giây",
            schedule.Interval.TotalHours, schedule.InitialDelay.TotalSeconds);

        // Hoãn vòng đầu để restart liên tục không thành spam nguồn dữ liệu, và để app kịp
        // phục vụ request trước khi làm việc nặng.
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
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
