using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;

namespace Ti2026.Ingest.OpenDota;

/// <summary>Một team_id xuất hiện trong bảng đấu TI mà hệ thống chưa gắn được với đội nào.</summary>
public readonly record struct BracketGap(
    int ValveTeamId, string? RemoteName, int? LikelyTeamId, string LikelySlug, int RosterMatched);

/// <summary>
/// Bắt đội của ta thi đấu dưới một team_id chưa khai, NGAY TRONG BẢNG ĐẤU — trước khi mất ván nào.
///
/// VÌ SAO CẦN THÊM MỘT BỘ DÒ NỮA. <see cref="StaleTeamIdDetector"/> dò trên ván ĐÃ ĐÁ, nên nó
/// chỉ báo sau khi đã mất dữ liệu. Bảng đấu thì nêu tên đủ 16 đội TRƯỚC khi trận đầu tiên diễn
/// ra — đó là cơ hội duy nhất phát hiện trước khi mất.
///
/// Chuyện đã xảy ra thật, và bộ dò cũ KHÔNG bắt được: L1GA TEAM đăng ký TI2026 dưới bản ghi
/// HULIGANI (10149530), một id thứ ba mà ta chưa biết. Họ chưa đá ván nào dưới id đó nên không
/// có gì cho bộ dò cũ soi, và nếu không ai để ý thì toàn bộ giải đấu của L1GA sẽ biến mất khỏi
/// hệ thống trong khi mọi vòng ingest vẫn báo "Succeeded".
///
/// CÁCH DÒ. Với mỗi team_id lạ trong bảng đấu, hỏi OpenDota roster của nó rồi so ACCOUNT_ID với
/// đội hình đang hiệu lực của 16 đội. Không so theo TÊN — tên là thứ đã đổi, và cũng chính là
/// thứ đã lừa được bộ so khớp ngay từ đầu.
///
/// CỐ Ý KHÔNG TỰ SỬA, giữ đúng nguyên tắc của bộ dò cũ: tự ghi vào bảng định danh là loại thao
/// tác mà một lần sai sẽ quy toàn bộ ván của một đội cho đội khác — im lặng và rất khó lần ra.
/// Báo cho người, người khai vào teams.json.
/// </summary>
public class BracketTeamGapDetector(
    Ti2026DbContext db, OpenDotaClient openDota, ILogger<BracketTeamGapDetector> logger)
{
    /// <summary>Từ mức này đã đáng báo — 4/5 có thể là một người đá thay, vẫn là đội đó.</summary>
    public const int SuspectMatch = 4;

    public async Task<List<BracketGap>> FindAsync(CancellationToken ct)
    {
        var unknown = await db.ScheduledSeries
            .Where(s => s.TeamId1 == null && s.ValveTeamId1 != null)
            .Select(s => s.ValveTeamId1!.Value)
            .Union(db.ScheduledSeries
                .Where(s => s.TeamId2 == null && s.ValveTeamId2 != null)
                .Select(s => s.ValveTeamId2!.Value))
            .Distinct()
            .ToListAsync(ct);

        if (unknown.Count == 0) return [];

        var roster = (await db.RosterEntries
                .Where(r => r.ValidTo == null && r.Role != "COACH"
                            && r.Player!.OpenDotaAccountId != null)
                .Select(r => new { r.TeamId, Account = r.Player!.OpenDotaAccountId!.Value })
                .ToListAsync(ct))
            .GroupBy(x => x.TeamId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Account).ToHashSet());

        if (roster.Count == 0) return [];

        var slugs = await db.Teams.ToDictionaryAsync(t => t.Id, t => t.Slug, ct);
        var found = new List<BracketGap>();

        foreach (var valveId in unknown)
        {
            List<OpenDotaTeamPlayer> players;
            try
            {
                players = await openDota.GetTeamPlayersAsync(valveId, ct);
            }
            catch (Exception ex)
            {
                // Một id tra không được không được làm hỏng cả bước dò: những id còn lại vẫn
                // đáng kiểm, và đây là bước CẢNH BÁO chứ không phải bước nạp dữ liệu.
                logger.LogWarning(ex, "Không tra được roster của team_id {Id}", valveId);
                continue;
            }

            var accounts = players
                .Where(p => p.AccountId is long a && a > 0)
                .Select(p => p.AccountId!.Value)
                .ToHashSet();

            if (accounts.Count == 0) continue;

            var best = roster
                .Select(kv => (TeamId: kv.Key, Hit: accounts.Count(kv.Value.Contains)))
                .OrderByDescending(x => x.Hit)
                .First();

            if (best.Hit < SuspectMatch) continue;

            var slug = slugs.GetValueOrDefault(best.TeamId, "?");
            found.Add(new BracketGap(valveId, players.FirstOrDefault()?.Name,
                best.TeamId, slug, best.Hit));

            logger.LogWarning(
                "Bảng đấu TI có team_id {ValveId} chưa khai, và {Hit}/5 người của nó trùng đội "
                + "hình {Slug}. Nhiều khả năng đội này đăng ký giải dưới một bản ghi mới — thêm "
                + "{ValveId} vào openDotaTeamIds của {Slug} trong teams.json TRƯỚC khi giải bắt "
                + "đầu, nếu không toàn bộ trận của họ sẽ không vào hệ thống.",
                valveId, best.Hit, slug, valveId, slug);
        }

        return found;
    }
}
