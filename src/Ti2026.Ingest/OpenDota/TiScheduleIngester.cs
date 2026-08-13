using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Nạp bảng đấu và lịch thi đấu The International từ API chính chủ của Valve.
///
/// TỰ TÌM GIẢI, không viết cứng id. Quy tắc: tier == 5. Kiểm trên toàn bộ 9.797 giải Valve
/// biết thì tier 5 khớp đúng 9 giải, đúng bằng số kỳ TI từ 2018 tới 2026 và không lẫn một giải
/// nào khác — kể cả năm giải vòng loại khu vực của chính TI2026 (chúng ở tier 2). Viết cứng id
/// thì sang TI2027 sẽ có người phải nhớ đi sửa, mà không có gì nhắc.
/// </summary>
public class TiScheduleIngester(
    Ti2026DbContext db, Dota2WebClient client, LeagueIdCache leagueCache,
    ILogger<TiScheduleIngester> logger)
{
    /// <summary>Bậc giải mà Valve chỉ dùng cho The International.</summary>
    public const int InternationalTier = 5;

    /// <summary>Số lần thử lấy bảng đấu trước khi bỏ lượt. Xem <see cref="LoadWithRetryAsync"/>.</summary>
    public const int LoadTries = 3;

    /// <summary>Nghỉ giữa hai lần thử. Ngắn thôi — đây là lỗi chớp nhoáng, không phải quá tải.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Lấy bảng đấu, THỬ LẠI khi Valve trả về thân rỗng.
    ///
    /// ĐO ĐƯỢC, KHÔNG PHẢI PHÒNG XA. Trong lúc TI đang đá: gọi thẳng 8 lần cách nhau 15 giây thì
    /// 8/8 trả đủ 27 nút, nhưng app gọi 2 lần thì 1 lần nhận về thân `null`. Tức không phải Valve
    /// chặn nhịp — nếu chặn thì probe 15 giây/lần đã dính trước — mà là lỗi chớp nhoáng phía họ,
    /// nhiều khả năng vì backend đang tải nặng giữa giải.
    ///
    /// Vì sao đáng thử lại thay vì bỏ lượt: nhịp làm tươi là 15 phút, nên một lượt hụt nghĩa là
    /// bảng đấu đứng yên 15 phút giữa lúc đang có trận. Một lời gọi thêm tới Valve gần như không
    /// tốn gì và KHÔNG đụng hạn mức OpenDota.
    ///
    /// Chỉ thử lại ca THÂN RỖNG. Lỗi mạng và HTTP hỏng thì ném ra ngoài như cũ — bên gọi đã có
    /// try/catch riêng, và nuốt chúng ở đây sẽ giấu mất một nguồn hỏng khác hẳn.
    /// </summary>
    private async Task<Dota2LeagueData?> LoadWithRetryAsync(long leagueId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var data = await client.GetLeagueDataAsync(leagueId, ct);
            if (data is not null || attempt >= LoadTries) return data;

            logger.LogInformation(
                "Bảng đấu trả về rỗng (lần {Attempt}/{Tries}), thử lại sau {Seconds}s",
                attempt, LoadTries, RetryDelay.TotalSeconds);

            await Task.Delay(RetryDelay, ct);
        }
    }

    public async Task<int> IngestAsync(CancellationToken ct)
    {
        var leagueId = await ResolveLeagueIdAsync(ct);
        if (leagueId is null)
        {
            logger.LogInformation("Chưa tìm thấy giải The International nào trong danh mục Valve");
            return 0;
        }

        var data = await LoadWithRetryAsync(leagueId.Value, ct);
        if (data is null)
        {
            logger.LogWarning("GetLeagueData cho giải {League} trả về rỗng sau {Tries} lần thử",
                leagueId, LoadTries);
            return 0;
        }

        var valveIds = await db.TeamOpenDotaIds
            .Select(x => new { x.OpenDotaTeamId, x.TeamId })
            .ToDictionaryAsync(x => x.OpenDotaTeamId, x => x.TeamId, ct);

        foreach (var t in await db.Teams.Where(t => t.OpenDotaTeamId != null).ToListAsync(ct))
            valveIds.TryAdd(t.OpenDotaTeamId!.Value, t.Id);

        var existing = await db.ScheduledSeries
            .Where(s => s.LeagueId == leagueId.Value)
            .ToDictionaryAsync(s => s.NodeId, ct);

        var now = DateTime.UtcNow;
        var touched = 0;

        foreach (var (group, n) in Dota2WebClient.Flatten(data.NodeGroups))
        {
            if (!existing.TryGetValue(n.NodeId, out var row))
            {
                row = new ScheduledSeries { LeagueId = leagueId.Value, NodeId = n.NodeId };
                db.ScheduledSeries.Add(row);
                existing[n.NodeId] = row;
            }

            row.GroupName = group;
            row.Name = string.IsNullOrWhiteSpace(n.Name) ? null : n.Name;

            // 0 = CHƯA BIẾT ĐỘI NÀO VÀO, không phải "đội có id 0". Không quy về null thì bước
            // ánh xạ bên dưới sẽ đi tra một đội không tồn tại và sinh ra cặp đấu ma.
            row.ValveTeamId1 = Positive(n.TeamId1);
            row.ValveTeamId2 = Positive(n.TeamId2);
            row.TeamId1 = MapTeam(row.ValveTeamId1);
            row.TeamId2 = MapTeam(row.ValveTeamId2);

            row.ScheduledAt = FromUnix(n.ScheduledTime);
            row.ActualAt = FromUnix(n.ActualTime);
            row.Wins1 = n.Team1Wins;
            row.Wins2 = n.Team2Wins;
            row.HasStarted = n.HasStarted;
            row.IsCompleted = n.IsCompleted;
            row.SeriesId = n.SeriesId is long s && s > 0 ? s : null;
            row.WinningNodeId = Positive(n.WinningNodeId);
            row.LosingNodeId = Positive(n.LosingNodeId);
            row.IncomingNodeId1 = Positive(n.IncomingNodeId1);
            row.IncomingNodeId2 = Positive(n.IncomingNodeId2);
            row.SyncedAt = now;

            touched++;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Bảng đấu {Name} ({League}): đồng bộ {Count} nút, {Scheduled} nút đã có giờ, "
            + "{Done} nút đã xong",
            data.Info?.Name, leagueId, touched,
            existing.Values.Count(x => x.ScheduledAt != null),
            existing.Values.Count(x => x.IsCompleted));

        return touched;

        int? MapTeam(int? valveId) =>
            valveId is int v && valveIds.TryGetValue(v, out var id) ? id : null;
    }

    /// <summary>
    /// Id kỳ TI mới nhất.
    ///
    /// TRƯỚC ĐÂY TRA LẠI MỖI VÒNG, và ở nhịp 6 giờ thì đúng là không đáng bàn: 315 KB nén nhân
    /// bốn lượt một ngày là khoảng 1,3 MB. Nhưng khi bảng đấu chuyển sang nhịp 15 phút để bám
    /// theo giải đang đá thì cùng phép tính đó ra ~30 MB mỗi ngày, để lấy về một số nguyên gần
    /// như không bao giờ đổi — kỳ TI mới xuất hiện mỗi năm một lần.
    ///
    /// Nên nhớ lại, nhưng CÓ HẠN DÙNG. Nhớ vĩnh viễn thì sang TI2027 phải khởi động lại
    /// container mới thấy giải mới, và đó đúng là loại việc không ai nhớ làm.
    /// </summary>
    private async Task<long?> ResolveLeagueIdAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (leagueCache.TryGet(now, out var cached)) return cached;

        var all = await client.GetLeagueInfoListAsync(ct);

        // Kỳ TI mới nhất theo mốc bắt đầu. Trong lúc TI đang diễn ra thì đó là giải đang chạy;
        // ngoài mùa thì đó là kỳ gần nhất, và bảng đấu của nó vẫn là thứ đáng hiện.
        var ti = all
            .Where(l => l.Tier == InternationalTier)
            .OrderByDescending(l => l.StartTimestamp ?? 0)
            .FirstOrDefault();

        if (ti is not null)
        {
            logger.LogInformation("Giải The International đang theo dõi: {Name} ({Id})",
                ti.Name, ti.LeagueId);
            leagueCache.Set(ti.LeagueId, now);
        }

        return ti?.LeagueId;
    }

    private static int? Positive(int? v) => v is int x && x > 0 ? x : null;

    private static DateTime? FromUnix(long? seconds) =>
        seconds is long s && s > 0 ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime : null;
}
