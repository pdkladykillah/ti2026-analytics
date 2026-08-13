using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.OpenDota;

/// <summary>Một bên của một ván có người của đội ta nhưng không nhận diện được đội.</summary>
public readonly record struct UnknownSide(
    long MatchId, bool IsRadiant, int TeamId, string Slug, int RosterMatched, DateTime StartTime);

/// <summary>
/// Bắt trường hợp đội của ta ra trận dưới một team_id OpenDota mà hệ thống chưa biết.
///
/// VÌ SAO CẦN. TeamResolver chỉ phân giải đội có OpenDotaTeamId là null — đã gán một lần thì
/// không bao giờ kiểm lại. Nên khi một roster đăng ký lại dưới bản ghi mới, ánh xạ cũ trỏ vào
/// một bản ghi đã chết, ingest không nhận được ván nào nữa, và MỌI VÒNG VẪN BÁO "Succeeded".
/// Không có gì đổ vỡ để ai đó chú ý.
///
/// Đã xảy ra thật với 4/16 đội, và tệ nhất là PariVision: mất nguyên giải EWC 2026 mà họ VÔ
/// ĐỊCH. Phát hiện ra chỉ vì có người hỏi "EWC xong rồi, kiểm tra thử đã cập nhật đủ chưa".
/// Một hệ thống chỉ đúng khi có người tình cờ hỏi đúng câu là chưa đúng.
///
/// CÁCH DÒ. Không dò theo tên — tên đổi luôn, và đó chính là thứ đã lừa được bộ so khớp. Dò
/// theo ACCOUNT_ID: nếu một bên có từ 4 người trở lên thuộc đội hình đang hiệu lực của một đội
/// mà bên đó lại không gắn được với đội nào, thì gần như chắc chắn đó là đội ấy dưới một bản
/// ghi khác. Dữ liệu này đã nằm sẵn trong MatchPlayers, không tốn thêm lời gọi nào.
/// </summary>
public class StaleTeamIdDetector(Ti2026DbContext db, ILogger<StaleTeamIdDetector> logger)
{
    /// <summary>Đủ cả 5 người thì gần như chắc chắn là cùng một đội.</summary>
    public const int CertainMatch = 5;

    /// <summary>Từ mức này đã đáng báo — 4/5 có thể là một người đánh thay, vẫn là đội đó.</summary>
    public const int SuspectMatch = 4;

    // CỐ Ý không tự sửa ánh xạ. Bộ dò biết "đội nào" nhưng không biết "team_id nào", vì ta
    // không lưu opposing_team_id khi chưa ánh xạ được. Đoán thêm một bước nữa để tự ghi vào
    // bảng định danh là loại thao tác mà một lần sai sẽ quy toàn bộ ván của một đội cho đội
    // khác — im lặng và rất khó lần ra. Báo cho người, người khai vào teams.json.

    /// <summary>
    /// Chỉ xét ván trong ngần này ngày.
    ///
    /// 45 chứ không phải 180, và đây là con số quyết định bộ dò này có dùng được hay không.
    ///
    /// Với 180 ngày, nó báo vĩnh viễn hai ca KHÔNG cần sửa: đội hình 1win từng thi đấu dưới màu
    /// Tundra Esports (05/2026) và đội hình LGD từng dưới màu HEROIC (04/2026). Cùng năm người,
    /// nhưng đó là tổ chức khác VẪN ĐANG HOẠT ĐỘNG với roster khác — nhận id của họ về sẽ kéo
    /// theo ván của những người hoàn toàn khác và gán nhầm cho đội ta. Cố ý không nhận.
    ///
    /// Mà một cảnh báo lúc nào cũng sáng thì chẳng khác gì tắt: người ta học cách bỏ qua nó, và
    /// đúng hôm nó báo chuyện thật thì không ai nhìn. 45 ngày đủ để một ánh xạ vừa chết lộ ra
    /// (PariVision đánh EWC cách đây 19 ngày, thừa sức bắt được) và đủ ngắn để chuyện đổi tổ chức
    /// của mùa trước tự rơi ra khỏi tầm nhìn.
    /// </summary>
    public const int LookbackDays = 45;

    public async Task<List<UnknownSide>> FindAsync(DateTime now, CancellationToken ct)
    {
        var roster = (await db.RosterEntries
                .Where(r => r.ValidTo == null && r.Role != "COACH"
                            && r.Player!.OpenDotaAccountId != null)
                .Select(r => new { r.TeamId, Account = r.Player!.OpenDotaAccountId!.Value })
                .ToListAsync(ct))
            .GroupBy(x => x.TeamId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Account).ToHashSet());

        if (roster.Count == 0) return [];

        var since = now.AddDays(-LookbackDays);

        // Chỉ những ván CÓ một bên chưa nhận diện được. Ván đủ hai bên thì không có gì để dò.
        var matches = await db.Matches
            .Where(m => m.StartTime >= since && (m.RadiantTeamId == null || m.DireTeamId == null))
            .Select(m => new { m.Id, m.StartTime, m.RadiantTeamId, m.DireTeamId })
            .ToListAsync(ct);

        if (matches.Count == 0) return [];

        var ids = matches.Select(m => m.Id).ToHashSet();

        var players = (await db.MatchPlayers
                .Where(p => p.AccountId != null && ids.Contains(p.MatchId))
                .Select(p => new { p.MatchId, Account = p.AccountId!.Value, p.IsRadiant })
                .ToListAsync(ct))
            .GroupBy(p => (p.MatchId, p.IsRadiant))
            .ToDictionary(g => g.Key, g => g.Select(x => x.Account).ToHashSet());

        var found = new List<UnknownSide>();

        foreach (var m in matches)
        {
            foreach (var (isRadiant, mapped) in
                     new[] { (true, m.RadiantTeamId), (false, m.DireTeamId) })
            {
                if (mapped is not null) continue;
                if (!players.TryGetValue((m.Id, isRadiant), out var accounts)) continue;

                foreach (var (teamId, five) in roster)
                {
                    var hit = accounts.Count(five.Contains);
                    if (hit >= SuspectMatch)
                        found.Add(new UnknownSide(m.Id, isRadiant, teamId, "", hit, m.StartTime));
                }
            }
        }

        if (found.Count == 0) return found;

        var slugs = await db.Teams.ToDictionaryAsync(t => t.Id, t => t.Slug, ct);
        found = found.Select(f => f with { Slug = slugs.GetValueOrDefault(f.TeamId, "?") }).ToList();

        foreach (var g in found.GroupBy(f => f.Slug))
        {
            var certain = g.Count(x => x.RosterMatched >= CertainMatch);
            logger.LogWarning(
                "Đội {Slug} xuất hiện trong {Total} ván ở một bên KHÔNG nhận diện được "
                + "({Certain} ván khớp đủ {N}/5 người). Nhiều khả năng đội này đang thi đấu "
                + "dưới một team_id OpenDota chưa khai — thêm vào openDotaTeamIds trong "
                + "teams.json. Ván gần nhất: {Latest:yyyy-MM-dd}.",
                g.Key, g.Count(), certain, CertainMatch, g.Max(x => x.StartTime));
        }

        return found;
    }
}
