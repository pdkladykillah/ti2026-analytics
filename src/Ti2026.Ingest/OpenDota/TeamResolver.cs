using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Phân giải team_id của OpenDota cho 16 đội trong DB, bằng cách đối chiếu tên và tag qua
/// bảng TeamAlias.
///
/// Đây chính là việc mà TeamAlias tồn tại để làm: OpenDota gọi PARIVISION thì dltv gọi
/// TEAM VISION, HULIGANI là L1GA TEAM. Không có bước phân giải này thì ingest sẽ tạo đội
/// trùng hoặc bỏ sót ván.
///
/// Nguyên tắc: KHÔNG ĐOÁN. Không khớp chắc chắn thì để OpenDotaTeamId = null và ghi log, để
/// người vận hành thêm alias bằng tay. Đoán sai sẽ gán toàn bộ ván của một đội cho đội khác —
/// sai lặng lẽ và rất khó phát hiện về sau.
/// </summary>
public class TeamResolver(Ti2026DbContext db, ILogger<TeamResolver> logger)
{
    public async Task<int> ResolveAsync(IReadOnlyList<OpenDotaTeam> openDotaTeams, CancellationToken ct)
    {
        var teams = await db.Teams.Include(t => t.Aliases).ToListAsync(ct);

        // Chỉ số tra cứu: khoá chuẩn hoá -> đội của ta. Gồm tên, tên viết tắt và mọi alias.
        var index = new Dictionary<string, Team>();
        foreach (var team in teams)
        {
            Index(index, team.Name, team);
            Index(index, team.ShortName, team);
            foreach (var alias in team.Aliases) Index(index, alias.Alias, team);
        }

        var resolved = 0;

        foreach (var od in openDotaTeams)
        {
            var match = Lookup(index, od.Name) ?? Lookup(index, od.Tag);
            if (match is null) continue;

            if (match.OpenDotaTeamId == od.TeamId)
            {
                EnsureAlias(match, od.Name);
                EnsureAlias(match, od.Tag);
                continue;
            }

            if (match.OpenDotaTeamId is not null)
            {
                // Hai đội OpenDota khác nhau cùng khớp về một đội của ta: không ghi đè,
                // vì không có cách nào biết cái nào đúng.
                logger.LogWarning(
                    "Đội {Slug} đã gán OpenDotaTeamId={Existing}, bỏ qua ứng viên {Candidate} ({Name})",
                    match.Slug, match.OpenDotaTeamId, od.TeamId, od.Name);
                continue;
            }

            match.OpenDotaTeamId = od.TeamId;
            EnsureAlias(match, od.Name);
            EnsureAlias(match, od.Tag);
            resolved++;

            logger.LogInformation("Phân giải {Slug} -> OpenDota team_id {Id} ({Name})",
                match.Slug, od.TeamId, od.Name);
        }

        await db.SaveChangesAsync(ct);

        var unresolved = teams.Where(t => t.OpenDotaTeamId is null).Select(t => t.Slug).ToList();
        if (unresolved.Count > 0)
        {
            logger.LogWarning(
                "Chưa phân giải được OpenDotaTeamId cho {Count} đội: {Slugs}. " +
                "Ingest sẽ bỏ qua các đội này thay vì đoán — thêm alias vào teams.json để khớp.",
                unresolved.Count, string.Join(", ", unresolved));
        }

        return resolved;
    }

    private static void Index(Dictionary<string, Team> index, string? name, Team team)
    {
        var key = Normalize(name);
        if (key.Length == 0) return;
        index.TryAdd(key, team);
    }

    private static Team? Lookup(Dictionary<string, Team> index, string? name)
    {
        var key = Normalize(name);
        return key.Length == 0 ? null : index.GetValueOrDefault(key);
    }

    /// <summary>
    /// Chuẩn hoá để so khớp: bỏ khoảng trắng, bỏ dấu chấm/gạch, chuyển hoa.
    /// "Team Falcons" / "TEAM FALCONS" / "team-falcons" đều về một khoá.
    /// </summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return new string(value
            .Where(c => char.IsLetterOrDigit(c))
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static void EnsureAlias(Team team, string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return;

        const string source = "opendota";
        if (team.Aliases.Any(a => a.Source == source
                                  && a.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)))
            return;

        team.Aliases.Add(new TeamAlias { Alias = alias.Trim(), Source = source });
    }
}
