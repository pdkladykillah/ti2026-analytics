using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest;

public enum SanityKind { None, Teams, Players }

/// <summary>
/// Chạy từng nguồn trong transaction riêng, áp sanity gate, ghi IngestRun.
/// Một nguồn fail KHÔNG ảnh hưởng nguồn khác.
/// </summary>
public class IngestOrchestrator(
    Ti2026DbContext db,
    SanityThresholds thresholds,
    ILogger<IngestOrchestrator> logger)
{
    public async Task<IngestRun> RunSourceAsync(
        string source,
        Func<CancellationToken, Task<int>> ingest,
        SanityKind sanityKind,
        CancellationToken ct)
    {
        var run = new IngestRun
        {
            Source = source,
            StartedAt = DateTime.UtcNow,
            Status = IngestStatus.Running,
        };
        db.IngestRuns.Add(run);
        await db.SaveChangesAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var written = await ingest(ct);

            var check = sanityKind switch
            {
                SanityKind.Teams => SanityGate.CheckTeams(written, thresholds),
                SanityKind.Players => SanityGate.CheckPlayers(written, thresholds),
                _ => SanityCheck.Ok(),
            };

            if (!check.Passed)
            {
                await tx.RollbackAsync(ct);
                await FinishAsync(run, IngestStatus.Failed, 0, check.Reason, ct);
                logger.LogWarning("Ingest {Source} trượt sanity gate: {Reason}", source, check.Reason);
                return run;
            }

            await tx.CommitAsync(ct);
            await FinishAsync(run, IngestStatus.Succeeded, written, null, ct);
            logger.LogInformation("Ingest {Source} xong, ghi {Written} bản ghi", source, written);
            return run;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            await FinishAsync(run, IngestStatus.Failed, 0, ex.ToString(), ct);
            logger.LogError(ex, "Ingest {Source} lỗi", source);
            return run;
        }
    }

    /// <summary>
    /// Ghi kết quả SAU khi transaction đã rollback/commit.
    ///
    /// IngestRun được SaveChanges trước khi mở transaction, và cập nhật sau khi đóng, có chủ ý:
    /// nếu ghi bên trong transaction thì rollback sẽ xoá luôn bản ghi lỗi — mất đúng cái thông
    /// tin duy nhất cần để chẩn đoán.
    /// </summary>
    private async Task FinishAsync(
        IngestRun run, IngestStatus status, int written, string? error, CancellationToken ct)
    {
        run.Status = status;
        run.ItemsWritten = written;
        run.ErrorMessage = Truncate(error, 4000);
        run.FinishedAt = DateTime.UtcNow;

        // Update() thay vì dựa vào change tracker, và đây KHÔNG phải thừa.
        //
        // MatchDetailIngester gọi db.ChangeTracker.Clear() sau mỗi ván để giữ bộ nhớ — việc đó
        // gỡ luôn `run` khỏi tracker. Khi ấy gán thuộc tính rồi SaveChanges sẽ không phát ra
        // UPDATE nào, và bản ghi nằm lại ở trạng thái Running VĨNH VIỄN.
        //
        // Hậu quả không phải chuyện nhỏ: một vòng đã chết trông y hệt một vòng đang chạy, nên
        // mọi thứ đọc IngestRun — trang trạng thái, cảnh báo, chẩn đoán — đều nói sai. Và nó
        // im lặng: không lỗi, không log, chỉ là một dòng không bao giờ kết thúc.
        db.IngestRuns.Update(run);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Stack trace dài không thêm thông tin nhưng làm phình DB và api/health.</summary>
    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max] + "… (đã cắt)";
}
