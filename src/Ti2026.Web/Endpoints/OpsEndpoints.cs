using Microsoft.EntityFrameworkCore;
using Ti2026.Data;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// Quan sát hệ thống ở mức tối thiểu nhưng đủ: khi nguồn ngoài gãy, đây là chỗ nói rõ
/// đang lỗi gì thay vì phải đi đọc log.
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
                players = await db.Players.CountAsync(),
                matches = await db.Matches.CountAsync(),
                snapshots = await db.TeamStatSnapshots.CountAsync(),
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
    }
}
