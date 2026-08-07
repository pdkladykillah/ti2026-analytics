using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Ingest.Analytics;
using Ti2026.Ingest.OpenDota;
using Ti2026.Ingest.Snapshots;

namespace Ti2026.Ingest;

public class IngestPipeline(
    IngestOrchestrator orchestrator,
    OpenDotaIngester openDota,
    MatchDetailIngester matchDetails,
    ProPubIngester proPub,
    SnapshotWriter snapshots,
    PredictionLedger ledger,
    Ti2026DbContext db,
    IngestSchedule schedule,
    IngestGate gate,
    ILogger<IngestPipeline> logger)
{
    /// <summary>
    /// Chấm dự đoán cũ rồi ghi dự đoán mới, đọc Elo từ snapshot vừa tính xong.
    /// Trả về tổng số dòng đã đụng tới, để orchestrator ghi vào lịch sử vòng chạy.
    /// </summary>
    private async Task<int> PredictionLedgerAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var resolved = await ledger.ResolveAsync(now, ct);

        var latest = await db.TeamStatSnapshots
            .Where(s => s.WindowDays == 180)
            .MaxAsync(s => (DateOnly?)s.CapturedOn, ct);

        if (latest is null) return resolved;

        var elo = await db.TeamStatSnapshots
            .Where(s => s.CapturedOn == latest && s.WindowDays == 180 && s.Elo != null)
            .ToDictionaryAsync(s => s.TeamId, s => s.Elo!.Value, ct);

        return resolved + await ledger.SnapshotAsync(elo, now, ct);
    }

    /// <summary>
    /// Tính lại những ngày snapshot ghi TRƯỚC khi có luật lọc đội hình.
    ///
    /// Vì sao cần: biểu đồ Elo theo ngày đọc thẳng các hàng lịch sử. Để nguyên thì đường Elo có
    /// một bậc nhảy ngay tại ngày đổi luật — bậc nhảy đó không phải chuyện xảy ra trên sân, mà
    /// là hai định nghĩa khác nhau vẽ chung một đường. Và TrendVerdict đọc chính chuỗi đó để
    /// nói đội "đang lên" hay "đang xuống", nên nó sẽ kết luận từ một hiện tượng do ta gây ra.
    ///
    /// Nhận biết hàng cũ bằng EloGames == null, nên bước này TỰ TẮT sau vòng đầu tiên và không
    /// cần ai nhớ gỡ nó đi.
    /// </summary>
    private async Task<int> RestateOldSnapshotsAsync(CancellationToken ct)
    {
        var stale = await db.TeamStatSnapshots
            .Where(s => s.EloGames == null)
            .Select(s => s.CapturedOn)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(ct);

        if (stale.Count == 0) return 0;

        logger.LogInformation(
            "Tính lại {Count} ngày snapshot ghi trước luật lọc đội hình: {Days}",
            stale.Count, string.Join(", ", stale));

        var written = 0;
        foreach (var day in stale)
            written += await snapshots.WriteAsync(day, ct);

        return written;
    }

    /// <summary>
    /// Chạy một vòng, CHỜ tới lượt nếu đang có vòng khác. Dành cho scheduler — nó không có ai
    /// ngồi đợi phản hồi nên chờ là hành vi đúng.
    /// </summary>
    public async Task RunAllAsync(CancellationToken ct)
    {
        using var _ = await gate.EnterAsync(ct);
        await RunInsideGateAsync(ct);
    }

    /// <summary>
    /// Chạy một vòng, hoặc trả false NGAY nếu đang có vòng khác. Dành cho lời gọi tay: giữ một
    /// HTTP request mở hàng phút để chờ tới lượt là cách chắc chắn làm người gọi tưởng hệ thống
    /// đã treo.
    /// </summary>
    public async Task<bool> TryRunAllAsync(CancellationToken ct)
    {
        using var slot = gate.TryEnter();
        if (slot is null)
        {
            logger.LogInformation("Bỏ qua lời gọi ingest: đang có một vòng chạy");
            return false;
        }

        await RunInsideGateAsync(ct);
        return true;
    }

    private async Task RunInsideGateAsync(CancellationToken ct)
    {
        logger.LogInformation("Bắt đầu vòng ingest");

        // SanityKind.None cho cả hai bước: chúng THÊM dữ liệu chứ không thay thế toàn bộ như
        // dltv. Gate Teams/Players áp cho dltv ở M3 — đó là nguồn có nguy cơ trả rỗng do đổi
        // layout. Áp gate ở đây sẽ đánh Failed cho một vòng hợp lệ chỉ vì hôm đó không có
        // trận mới nào.
        await orchestrator.RunSourceAsync(
            "opendota", openDota.IngestAsync, SanityKind.None, ct);

        // Nạp detail SAU khi có danh sách ván, TRƯỚC khi tính snapshot — để chỉ số của vòng
        // này đã bao gồm phần detail vừa nạp thêm.
        await orchestrator.RunSourceAsync(
            "match-detail",
            c => matchDetails.IngestAsync(schedule.MaxMatchDetailsPerRun, c),
            SanityKind.None, ct);

        // Job theo khung giờ, tự bỏ qua nếu chưa tới hạn — xem ProPubIngester.MinInterval.
        // Đặt ở đây chứ không thành BackgroundService riêng để nó dùng chung IngestGate: hai
        // vòng ghi SQLite cùng lúc là cách chắc chắn để gặp lỗi khoá sau 30 giây.
        await orchestrator.RunSourceAsync(
            ProPubIngester.Source, proPub.IngestAsync, SanityKind.None, ct);

        await orchestrator.RunSourceAsync(
            "snapshot",
            c => snapshots.WriteAsync(DateOnly.FromDateTime(DateTime.UtcNow), c),
            SanityKind.None, ct);

        await orchestrator.RunSourceAsync("snapshot-backfill", RestateOldSnapshotsAsync,
            SanityKind.None, ct);

        // Sổ theo dõi dự đoán. Đặt SAU snapshot vì nó đọc Elo vừa tính xong — ghi trước thì
        // sổ luôn chậm một vòng so với mô hình đang phục vụ trang.
        //
        // Chấm TRƯỚC rồi mới ghi: chấm trước thì những dự đoán cũ đã có kết quả được đóng lại,
        // nên bước ghi không coi chúng là "bản ghi chưa chấm gần nhất" và bỏ qua nhầm.
        await orchestrator.RunSourceAsync(
            "prediction-ledger", PredictionLedgerAsync, SanityKind.None, ct);

        logger.LogInformation("Kết thúc vòng ingest");
    }
}
