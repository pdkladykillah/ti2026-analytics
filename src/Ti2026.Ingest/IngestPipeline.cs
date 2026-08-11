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
    LeagueBackfillIngester leagueBackfill,
    TiScheduleIngester tiSchedule,
    ProPubIngester proPub,
    IdolIngester idols,
    SnapshotWriter snapshots,
    PredictionLedger ledger,
    StaleTeamIdDetector staleTeamIds,
    BracketTeamGapDetector bracketGaps,
    TrackedPlayerIngester trackedPlayers,
    TrackedMatchDetailIngester trackedDetails,
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

        // TRƯỚC khi chấm và ghi: dọn nốt các dòng do mô hình cũ ghi ra. Chạy sau đó thì đúng
        // những dòng vừa ghi lại bị dọn, và sổ không bao giờ có gì.
        var purged = await ledger.PurgeSupersededOnceAsync(now, ct);

        var resolved = purged + await ledger.ResolveAsync(now, ct);

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

        // Nạp nốt ván của giải cấp cao mà cả hai bên đều ngoài 16 đội. Phải nằm SAU ingest
        // theo đội (nó đọc chính dữ liệu đó để biết giải nào là cấp cao) và TRƯỚC nạp detail
        // (để những ván vừa thêm có detail ngay trong cùng vòng).
        await orchestrator.RunSourceAsync(
            "league-backfill", leagueBackfill.IngestAsync, SanityKind.None, ct);

        // Bảng đấu TI từ API chính chủ của Valve. Độc lập với mọi bước khác — hỏng nguồn này
        // thì chỉ tab lịch cũ đi, không ảnh hưởng phân tích.
        await orchestrator.RunSourceAsync(
            "ti-schedule", tiSchedule.IngestAsync, SanityKind.None, ct);

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

        // Cùng khuôn: tự bỏ qua nếu chưa tới hạn, và có trần số ván mỗi vòng nên lần đầu sẽ chạy
        // vài vòng mới đủ. Lambda chứ không phải method group vì IngestAsync có tham số tuỳ chọn.
        await orchestrator.RunSourceAsync(
            IdolIngester.Source, c => idols.IngestAsync(c), SanityKind.None, ct);

        await orchestrator.RunSourceAsync(
            "snapshot",
            c => snapshots.WriteAsync(DateOnly.FromDateTime(DateTime.UtcNow), c),
            SanityKind.None, ct);

        await orchestrator.RunSourceAsync("snapshot-backfill", RestateOldSnapshotsAsync,
            SanityKind.None, ct);

        // Đặt CUỐI, sau khi đã nạp xong: nó soi chính dữ liệu vừa nạp để tìm đội của ta đang
        // ra sân dưới một team_id chưa khai. Đây là loại hỏng không làm gì đổ vỡ — ingest vẫn
        // báo Succeeded trong lúc mất trắng ván của một đội — nên phải có ai đó đi tìm nó.
        await orchestrator.RunSourceAsync(
            "stale-team-id",
            async c => (await staleTeamIds.FindAsync(DateTime.UtcNow, c)).Count,
            SanityKind.None, ct);

        // Dò id lạ NGAY TRONG BẢNG ĐẤU. Bộ dò ở trên chỉ soi ván đã đá nên chỉ báo sau khi
        // đã mất dữ liệu; bảng đấu nêu tên đủ 16 đội TRƯỚC trận đầu tiên, và đó là cơ hội duy
        // nhất phát hiện trước khi mất. L1GA TEAM đăng ký TI2026 dưới bản ghi HULIGANI mà bộ
        // dò cũ không thể thấy, vì họ chưa đá ván nào dưới id đó.
        await orchestrator.RunSourceAsync(
            "bracket-team-gap",
            async c => (await bracketGaps.FindAsync(c)).Count,
            SanityKind.None, ct);

        // Lịch sử thi đấu của người dùng được theo dõi. Hoàn toàn độc lập với phần phân tích
        // giải — hỏng ở đây không được ảnh hưởng gì tới 16 đội.
        await orchestrator.RunSourceAsync(
            "tracked-players", trackedPlayers.IngestAsync, SanityKind.None, ct);

        // Bối cảnh cả đội + xin parse để có VAI TRÒ THẬT. Phải nằm sau bước trên: nó chỉ xử lý
        // những ván bước trên vừa ghi vào.
        await orchestrator.RunSourceAsync(
            "tracked-match-detail", trackedDetails.IngestAsync, SanityKind.None, ct);

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
