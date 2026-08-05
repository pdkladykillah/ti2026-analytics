using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// Quan sát hệ thống ở mức tối thiểu nhưng đủ: khi nguồn ngoài gãy, đây là chỗ nói rõ đang
/// lỗi gì thay vì phải đi đọc log container.
/// </summary>
public static class OpsEndpoints
{
    public static void MapOpsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (Ti2026DbContext db) =>
        {
            var runs = await db.IngestRuns
                .OrderByDescending(r => r.StartedAt)
                .Take(6)
                .ToListAsync();

            return Results.Ok(new
            {
                status = "ok",
                teams = await db.Teams.CountAsync(),
                teamsResolved = await db.Teams.CountAsync(t => t.OpenDotaTeamId != null),
                players = await db.Players.CountAsync(),
                matches = await db.Matches.CountAsync(),
                snapshots = await db.TeamStatSnapshots.CountAsync(),

                // Còn bao nhiêu ván cần gọi matches/{id}. Khác 0 là chuyện bình thường sau khi
                // nâng MatchDetailIngester.SchemaVersion — nhưng nếu con số này đứng yên qua
                // nhiều vòng thì việc nạp bù đã tắc, và không có chỗ nào khác nhìn ra điều đó.
                pendingDetails = await db.Matches.CountAsync(
                    m => m.DetailsIngestedAt == null
                         || m.DetailSchemaVersion < MatchDetailIngester.SchemaVersion),
                detailSchemaVersion = MatchDetailIngester.SchemaVersion,

                draftEvents = await db.DraftEvents.CountAsync(),
                itemPurchases = await db.ItemPurchases.CountAsync(),
                recentRuns = runs.Select(r => new
                {
                    source = r.Source,
                    status = r.Status.ToString(),
                    startedAt = r.StartedAt,
                    finishedAt = r.FinishedAt,
                    itemsWritten = r.ItemsWritten,
                    errorMessage = r.ErrorMessage,
                }),
            });
        });

        // ---------- Trạng thái scheduler ----------
        app.MapGet("/api/ingest/status", async (
            Ti2026DbContext db, IngestGate gate, IngestStatusTracker status) =>
        {
            var raw = await db.IngestRuns
                .OrderByDescending(r => r.StartedAt)
                .Take(24)
                .Select(r => new
                {
                    r.Source, r.Status, r.StartedAt, r.FinishedAt, r.ItemsWritten, r.ErrorMessage,
                })
                .ToListAsync();

            // CHỈ vòng mới nhất mới có thể đang thật sự chạy, và chỉ khi cổng đang bị giữ.
            // Mọi dòng "Running" khác là tàn dư của một vòng bị gián đoạn — hiện chúng là "đang
            // chạy" thì trang nói dối, mà sửa thẳng vào DB thì là viết lại lịch sử. Diễn giải
            // lúc đọc là cách duy nhất vừa trung thực vừa tự khỏi khi có bản ghi mới.
            var newest = raw.FirstOrDefault();

            var runs = raw.Select(r =>
            {
                var stale = r.Status == IngestStatus.Running
                            && !(gate.IsRunning && ReferenceEquals(r, newest));

                return new
                {
                    source = r.Source,
                    status = stale ? "Interrupted" : r.Status.ToString(),
                    startedAt = r.StartedAt,
                    finishedAt = r.FinishedAt,
                    itemsWritten = r.ItemsWritten,

                    // Cắt ngắn: thông báo lỗi đầy đủ là stack trace vài nghìn ký tự, không hợp
                    // để hiện trên trang — nhưng câu đầu thường đã nói đủ nguyên nhân.
                    error = stale
                        ? "Vòng bị gián đoạn, không ghi được kết quả cuối"
                        : r.ErrorMessage == null
                            ? null
                            : (r.ErrorMessage.Length > 220 ? r.ErrorMessage[..220] + "…" : r.ErrorMessage),
                };
            }).ToList();

            var pending = await db.Matches.CountAsync(
                m => m.DetailsIngestedAt == null
                     || m.DetailSchemaVersion < MatchDetailIngester.SchemaVersion);

            var total = await db.Matches.CountAsync();

            return Results.Ok(new
            {
                running = gate.IsRunning,
                enabled = status.Enabled,
                intervalHours = status.Interval == TimeSpan.Zero ? (double?)null : status.Interval.TotalHours,
                lastStartedAt = status.LastStartedAt,
                nextRunAt = status.NextRunAt,
                serverTime = DateTime.UtcNow,

                backfill = new
                {
                    pending,
                    total,
                    donePercent = total == 0 ? 100 : Math.Round((total - pending) * 100.0 / total, 1),
                    schemaVersion = MatchDetailIngester.SchemaVersion,
                    perRun = 200,
                },

                runs,
                note = status.Enabled
                    ? null
                    : "Scheduler đang TẮT — dữ liệu chỉ cập nhật khi chạy tay qua api/ingest/run.",
            });
        });

        app.MapPost("/api/ingest/run", async (
            HttpContext ctx,
            IOptions<Ti2026Options> opt,
            IngestPipeline pipeline,
            CancellationToken ct) =>
        {
            var expected = opt.Value.IngestToken;

            // Chưa cấu hình token thì TỪ CHỐI, không mở cửa. Để hở endpoint này nghĩa là bất
            // kỳ ai cũng ép VPS spam OpenDota tới mức bị chặn IP — và mất IP thì mất luôn
            // nguồn dữ liệu, không phải chỉ một vòng ingest.
            if (string.IsNullOrWhiteSpace(expected))
                return Results.Problem(
                    "Chưa cấu hình Ti2026__IngestToken nên endpoint này bị vô hiệu hoá.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            var provided = ctx.Request.Headers["X-Ingest-Token"].ToString();

            // FixedTimeEquals thay vì == để không rò rỉ thông tin qua thời gian so sánh.
            // Nó trả false ngay khi độ dài khác nhau, nên token rỗng cũng trượt đúng.
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
                return Results.Unauthorized();

            // Không chờ tới lượt: SQLite chỉ có một người ghi, nên hai vòng chồng nhau khiến
            // vòng sau nằm chờ khoá tới hết CommandTimeout 30 giây rồi chết ở "INSERT INTO
            // IngestRuns" — thông báo lỗi lúc đó không hề nhắc gì tới nguyên nhân thật.
            // Đã xảy ra thật khi một lời gọi bị curl bỏ ngang vẫn tiếp tục chạy phía server.
            if (!await pipeline.TryRunAllAsync(ct))
                return Results.Conflict(new
                {
                    error = "Đang có một vòng ingest chạy. Chờ vòng đó xong rồi gọi lại — chạy "
                          + "chồng sẽ khoá SQLite và đốt hạn mức request của OpenDota để làm "
                          + "đúng một việc hai lần.",
                });

            return Results.Accepted();
        });
    }
}
