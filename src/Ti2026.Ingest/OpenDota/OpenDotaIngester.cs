using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Nạp ván đấu từ OpenDota cho 16 đội. Ghi vào bảng Match; việc tính chỉ số là của
/// SnapshotWriter.
/// </summary>
public class OpenDotaIngester(
    Ti2026DbContext db,
    OpenDotaClient client,
    TeamResolver resolver,
    ILogger<OpenDotaIngester> logger)
{
    public async Task<int> IngestAsync(CancellationToken ct)
    {
        // Gọi /teams khi còn đội chưa phân giải HOẶC còn đội chưa có logo dùng được.
        // Response ~250 KB nên không tải lại mỗi 6 giờ khi mọi thứ đã đủ.
        //
        // Logo: dltv.org chặn hotlink theo referrer nên ảnh của họ KHÔNG hiện trên trình
        // duyệt (chỉ ra chữ cái thay thế), và robots.txt của họ cấm /uploads/ nên cũng
        // không được phép cache về. OpenDota trả logo_url trỏ Steam CDN, tải trực tiếp
        // được — dùng nguồn đó.
        var needTeams = await db.Teams.AnyAsync(
            t => t.OpenDotaTeamId == null || t.LogoUrl == null || !t.LogoUrl.Contains("steam"), ct);

        if (needTeams)
        {
            var odTeams = await client.GetTeamsAsync(ct);
            logger.LogInformation("OpenDota trả về {Count} đội", odTeams.Count);
            await resolver.ResolveAsync(odTeams, ct);
            await UpdateLogosAsync(odTeams, ct);
        }

        var teams = await db.Teams
            .Where(t => t.OpenDotaTeamId != null)
            .ToListAsync(ct);

        if (teams.Count == 0)
        {
            logger.LogWarning("Không có đội nào phân giải được OpenDotaTeamId — bỏ qua vòng này");
            return 0;
        }

        // Bản đồ ngược để ánh xạ opposing_team_id về đội của ta
        var byOpenDotaId = teams.ToDictionary(t => t.OpenDotaTeamId!.Value, t => t.Id);

        var written = 0;

        foreach (var team in teams)
        {
            var matches = await client.GetTeamMatchesAsync(team.OpenDotaTeamId!.Value, ct);
            written += await UpsertMatchesAsync(team, matches, byOpenDotaId, ct);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Nạp xong {Written} ván từ {Teams} đội", written, teams.Count);
        return written;
    }

    /// <summary>
    /// Thay logo dltv (bị chặn hotlink) bằng logo Steam CDN của OpenDota.
    /// Chỉ ghi đè khi OpenDota thực sự có logo — không xoá logo cũ để lấy chỗ trống.
    /// </summary>
    private async Task UpdateLogosAsync(List<OpenDotaTeam> odTeams, CancellationToken ct)
    {
        var byId = odTeams.Where(t => !string.IsNullOrWhiteSpace(t.LogoUrl))
                          .ToDictionary(t => t.TeamId, t => t.LogoUrl!);

        var updated = 0;
        foreach (var team in await db.Teams.Where(t => t.OpenDotaTeamId != null).ToListAsync(ct))
        {
            if (!byId.TryGetValue(team.OpenDotaTeamId!.Value, out var logo)) continue;
            if (team.LogoUrl == logo) continue;

            team.LogoUrl = logo;
            updated++;
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Cập nhật logo Steam CDN cho {Count} đội", updated);
        }
    }

    private async Task<int> UpsertMatchesAsync(
        Team team,
        List<OpenDotaTeamMatch> matches,
        Dictionary<int, int> byOpenDotaId,
        CancellationToken ct)
    {
        var written = 0;

        foreach (var m in matches)
        {
            // Không biết đội được hỏi ở phe nào thì không thể quy kết kết quả cho ai — bỏ qua
            // thay vì đoán. Đoán ở đây làm sai winrate của cả hai đội.
            if (m.Radiant is null) continue;

            // Không ánh xạ được đối thủ về một trong 16 đội: bỏ qua. Ván với đội ngoài giải
            // không thuộc phạm vi phân tích, và ghi nửa vời (một phe null) sẽ bị SnapshotWriter
            // lọc ra nhưng vẫn phình bảng Match.
            if (m.OpposingTeamId is null
                || !byOpenDotaId.TryGetValue(m.OpposingTeamId.Value, out var opponentTeamId))
                continue;

            var isRadiant = m.Radiant.Value;
            var radiantTeamId = isRadiant ? team.Id : opponentTeamId;
            var direTeamId = isRadiant ? opponentTeamId : team.Id;

            var entity = await db.Matches.FirstOrDefaultAsync(x => x.Id == m.MatchId, ct)
                         ?? db.Matches.Local.FirstOrDefault(x => x.Id == m.MatchId);

            if (entity is null)
            {
                entity = new Match { Id = m.MatchId };
                db.Matches.Add(entity);
                written++;
            }

            entity.StartTime = DateTimeOffset.FromUnixTimeSeconds(m.StartTime).UtcDateTime;
            entity.DurationSeconds = m.Duration;
            entity.LeagueId = m.LeagueId;
            entity.LeagueName = m.LeagueName;
            entity.RadiantTeamId = radiantTeamId;
            entity.DireTeamId = direTeamId;
            entity.RadiantWin = m.RadiantWin;
            entity.RadiantScore = m.RadiantScore;
            entity.DireScore = m.DireScore;
            entity.IngestedAt = DateTime.UtcNow;

            // RadiantHadFirstBlood / RadiantReachedTenFirst / SeriesId để nguyên null:
            // endpoint này không cung cấp chúng. Xem ghi chú trong OpenDotaTeamMatch.
        }

        return written;
    }
}
