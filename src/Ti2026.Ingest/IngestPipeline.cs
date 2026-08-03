using Microsoft.Extensions.Logging;
using Ti2026.Ingest.OpenDota;
using Ti2026.Ingest.Snapshots;

namespace Ti2026.Ingest;

public class IngestPipeline(
    IngestOrchestrator orchestrator,
    OpenDotaIngester openDota,
    SnapshotWriter snapshots,
    ILogger<IngestPipeline> logger)
{
    public async Task RunAllAsync(CancellationToken ct)
    {
        logger.LogInformation("Bắt đầu vòng ingest");

        // SanityKind.None cho cả hai bước: chúng THÊM dữ liệu chứ không thay thế toàn bộ như
        // dltv. Gate Teams/Players áp cho dltv ở M3 — đó là nguồn có nguy cơ trả rỗng do đổi
        // layout. Áp gate ở đây sẽ đánh Failed cho một vòng hợp lệ chỉ vì hôm đó không có
        // trận mới nào.
        await orchestrator.RunSourceAsync(
            "opendota", openDota.IngestAsync, SanityKind.None, ct);

        await orchestrator.RunSourceAsync(
            "snapshot",
            c => snapshots.WriteAsync(DateOnly.FromDateTime(DateTime.UtcNow), c),
            SanityKind.None, ct);

        logger.LogInformation("Kết thúc vòng ingest");
    }
}
