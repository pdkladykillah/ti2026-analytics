using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Phân giải team_id của OpenDota cho 16 đội trong DB, bằng cách đối chiếu tên và tag.
///
/// Duyệt theo đội CỦA TA rồi tìm ứng viên tốt nhất, KHÔNG duyệt theo danh sách OpenDota.
/// Bản đầu tiên làm ngược lại và dính lỗi thật phát hiện trên dữ liệu production: đội
/// "Aurora Gaming" của ta bị gán cho một đội khác khớp yếu theo tag, rồi từ chối chính
/// ứng viên trùng tên đầy đủ vì "đến sau". Với tag 2-3 ký tự (BB, OG, LGD) chuyện này
/// xảy ra thường xuyên, và hậu quả là gán toàn bộ ván của một đội cho đội khác.
///
/// Nguyên tắc: KHÔNG ĐOÁN. Không có ứng viên nào đủ mạnh, hoặc có nhiều ứng viên đồng
/// hạng, thì để OpenDotaTeamId = null và ghi log để người vận hành thêm alias bằng tay.
/// </summary>
public class TeamResolver(Ti2026DbContext db, ILogger<TeamResolver> logger)
{
    /// <summary>Điểm càng cao thì khớp càng chắc chắn.</summary>
    private const int ScoreExactName = 3;   // trùng tên đầy đủ
    private const int ScoreAlias = 2;   // trùng một alias đã biết
    private const int ScoreTag = 1;   // chỉ trùng tag/tên viết tắt

    public async Task<int> ResolveAsync(
        IReadOnlyList<OpenDotaTeam> openDotaTeams, CancellationToken ct)
    {
        var teams = await db.Teams.Include(t => t.Aliases).ToListAsync(ct);

        // Id đã bị đội khác chiếm — không cho hai đội của ta trỏ về cùng một id OpenDota
        var taken = teams.Where(t => t.OpenDotaTeamId.HasValue)
                         .Select(t => t.OpenDotaTeamId!.Value)
                         .ToHashSet();

        var resolved = 0;

        foreach (var team in teams.Where(t => t.OpenDotaTeamId is null))
        {
            var scored = openDotaTeams
                .Where(od => !taken.Contains(od.TeamId))
                .Select(od => (Team: od, Score: Score(team, od)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ToList();

            if (scored.Count == 0)
                continue;

            var best = scored[0];
            var tiedAtBest = scored.Count(x => x.Score == best.Score);

            if (tiedAtBest > 1)
            {
                logger.LogWarning(
                    "Đội {Slug} có {Count} ứng viên đồng hạng (điểm {Score}): {Names}. " +
                    "Không gán để tránh quy kết nhầm ván đấu — thêm alias để phân biệt.",
                    team.Slug, tiedAtBest, best.Score,
                    string.Join(", ", scored.Where(x => x.Score == best.Score)
                        .Select(x => $"{x.Team.Name} (#{x.Team.TeamId})")));
                continue;
            }

            team.OpenDotaTeamId = best.Team.TeamId;
            taken.Add(best.Team.TeamId);
            EnsureAlias(team, best.Team.Name);
            EnsureAlias(team, best.Team.Tag);
            resolved++;

            logger.LogInformation(
                "Phân giải {Slug} -> OpenDota #{Id} ({Name}), điểm khớp {Score}",
                team.Slug, best.Team.TeamId, best.Team.Name, best.Score);
        }

        await db.SaveChangesAsync(ct);

        var unresolved = teams.Where(t => t.OpenDotaTeamId is null).Select(t => t.Slug).ToList();
        if (unresolved.Count > 0)
        {
            logger.LogWarning(
                "Chưa phân giải được OpenDotaTeamId cho {Count} đội: {Slugs}. " +
                "Ingest bỏ qua các đội này thay vì đoán.",
                unresolved.Count, string.Join(", ", unresolved));
        }

        return resolved;
    }

    /// <summary>
    /// Chấm điểm mức độ khớp giữa một đội của ta và một đội OpenDota.
    /// Trả 0 nghĩa là không khớp.
    /// </summary>
    private static int Score(Team team, OpenDotaTeam od)
    {
        var odName = Normalize(od.Name);
        var odTag = Normalize(od.Tag);
        if (odName.Length == 0 && odTag.Length == 0) return 0;

        if (odName.Length > 0 && odName == Normalize(team.Name)) return ScoreExactName;

        if (odName.Length > 0 &&
            team.Aliases.Any(a => Normalize(a.Alias) == odName)) return ScoreAlias;

        // Tag là bằng chứng YẾU NHẤT: "BB" khớp cả BetBoom, BarBrothers lẫn BITCHBUILD.
        // Chỉ chấp nhận khi tag đủ dài để có ý nghĩa phân biệt.
        var shortName = Normalize(team.ShortName);
        if (odTag.Length >= 3 && shortName.Length >= 3 && odTag == shortName) return ScoreTag;

        return 0;
    }

    /// <summary>
    /// Chuẩn hoá để so khớp: chỉ giữ chữ và số, chuyển hoa.
    /// "Team Falcons" / "TEAM FALCONS" / "team-falcons" đều về một khoá.
    /// </summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return new string(value.Where(char.IsLetterOrDigit)
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
