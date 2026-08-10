using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Nạp lịch sử thi đấu của những người được theo dõi lâu dài (chủ trang, và về sau là học trò).
///
/// DANH SÁCH ĐỌC TỪ FILE, không viết cứng: thêm một người là thêm một dòng trong
/// data/tracked-players.json rồi deploy. Đó là yêu cầu ngay từ đầu, và nó cũng là thứ giữ cho
/// mã nguồn không có một account_id nào nằm lẫn trong logic.
///
/// GIỚI HẠN CỦA NGUỒN, phải biết trước khi dựng phân tích lên trên:
///
/// • players/{id}/matches KHÔNG trả GPM, last hits, sát thương… trừ khi hỏi kèm project=.
///   Có project= thì được đủ ở 100% số ván — đó là lý do <see cref="Projected"/> tồn tại.
///
/// • lane_role thì KHÔNG cứu được bằng cách đó: đo trên tài khoản thật, nó chỉ có ở 6% số ván
///   (360/5.877), vì nó đến từ replay đã phân tích chứ không phải bảng trận. Nên mọi kết luận
///   về VAI TRÒ phải suy từ chỉ số đo được (mức farm, last hit mỗi phút), không được dựa vào
///   lane_role — dựa vào nó là dựng phân tích trên 6% dữ liệu rồi trình bày như thể đủ.
/// </summary>
/// <param name="editorialDirectory">
/// Thư mục chứa tracked-players.json. Nhận thẳng đường dẫn thay vì Ti2026Paths vì lớp giải
/// đường dẫn nằm ở tầng Web — EditorialSeeder cũng nhận đúng kiểu này.
/// </param>
public class TrackedPlayerIngester(
    Ti2026DbContext db, OpenDotaClient client, string editorialDirectory,
    ILogger<TrackedPlayerIngester> logger)
{
    /// <summary>Số ván lấy mỗi lần. 500 phủ khoảng 5 tháng với người chơi đều.</summary>
    public const int MatchesPerSync = 500;

    /// <summary>
    /// Những trường chỉ trả về khi hỏi tên. Thiếu chúng thì bảng ván chỉ có K/D/A — không đủ để
    /// nói gì về cách chơi.
    /// </summary>
    public static readonly string[] Projected =
    [
        "kills", "deaths", "assists", "gold_per_min", "xp_per_min", "last_hits", "denies",
        "hero_damage", "tower_damage", "hero_healing", "lane_role", "average_rank", "party_size",
    ];

    public async Task<int> IngestAsync(CancellationToken ct)
    {
        var configured = ReadConfig();
        if (configured.Count == 0) return 0;

        await SyncRosterAsync(configured, ct);

        var players = await db.TrackedPlayers.ToListAsync(ct);
        var written = 0;

        foreach (var p in players)
            written += await SyncOneAsync(p, ct);

        await db.SaveChangesAsync(ct);
        return written;
    }

    /// <summary>Đưa danh sách trong file vào DB. Chỉ thêm và cập nhật, KHÔNG xoá.</summary>
    private async Task SyncRosterAsync(List<ConfiguredPlayer> configured, CancellationToken ct)
    {
        var existing = await db.TrackedPlayers.ToDictionaryAsync(p => p.AccountId, ct);

        foreach (var c in configured)
        {
            if (!existing.TryGetValue(c.AccountId, out var row))
            {
                row = new TrackedPlayer
                {
                    AccountId = c.AccountId,
                    DisplayName = c.DisplayName,
                    AddedAt = DateTime.UtcNow,
                };
                db.TrackedPlayers.Add(row);
                logger.LogInformation("Thêm người theo dõi mới: {Name} ({Id})",
                    c.DisplayName, c.AccountId);
            }

            row.DisplayName = c.DisplayName;
            row.Note = c.Note;
            row.IsOwner = c.IsOwner;
        }

        // Cố ý KHÔNG xoá người đã bỏ khỏi file: xoá sẽ kéo theo toàn bộ lịch sử ván của họ
        // (cascade), và một lần sửa nhầm file là mất sạch dữ liệu không lấy lại được. Bỏ khỏi
        // file thì họ ngừng được cập nhật, thế là đủ.
        await db.SaveChangesAsync(ct);
    }

    private async Task<int> SyncOneAsync(TrackedPlayer p, CancellationToken ct)
    {
        try
        {
            var profile = await client.GetPlayerAsync(p.AccountId, ct);
            if (profile?.Profile is not null)
            {
                p.PersonaName = profile.Profile.PersonaName;
                p.AvatarUrl = profile.Profile.AvatarFull;
                p.RankTier = profile.RankTier;
            }

            var wl = await client.GetPlayerWinLossAsync(p.AccountId, ct);
            if (wl is not null) { p.Wins = wl.Win; p.Losses = wl.Lose; }

            var matches = await client.GetPlayerMatchesProjectedAsync(
                p.AccountId, MatchesPerSync, Projected, ct);

            p.LastSyncedAt = DateTime.UtcNow;

            if (matches.Count == 0)
            {
                // Hồ sơ chưa bật "Expose Public Match Data" thì OpenDota trả danh sách RỖNG chứ
                // không báo lỗi. Không ghi lại lý do thì trang trông y hệt như người này chưa
                // chơi ván nào — và không ai biết phải đi bật gì.
                p.SyncNote = "OpenDota không trả về ván nào. Nhiều khả năng tài khoản chưa bật "
                           + "\"Expose Public Match Data\" trong phần cài đặt của Dota 2.";
                logger.LogWarning("Không lấy được ván nào cho {Name} ({Id})", p.DisplayName, p.AccountId);
                return 0;
            }

            p.SyncNote = null;

            var known = await db.TrackedPlayerMatches
                .Where(m => m.TrackedPlayerId == p.Id)
                .Select(m => m.MatchId)
                .ToListAsync(ct);

            var seen = known.ToHashSet();
            var added = 0;

            foreach (var m in matches)
            {
                if (!seen.Add(m.MatchId)) continue;

                db.TrackedPlayerMatches.Add(new TrackedPlayerMatch
                {
                    TrackedPlayerId = p.Id,
                    MatchId = m.MatchId,
                    HeroId = m.HeroId,
                    StartTime = DateTimeOffset.FromUnixTimeSeconds(m.StartTime).UtcDateTime,
                    DurationSeconds = m.Duration,

                    // player_slot < 128 là phe Radiant. Đây là cách DUY NHẤT biết người này
                    // thắng hay thua — endpoint chỉ trả radiant_win cho cả ván.
                    Won = (m.PlayerSlot < 128) == m.RadiantWin,

                    Kills = m.Kills ?? 0,
                    Deaths = m.Deaths ?? 0,
                    Assists = m.Assists ?? 0,
                    GoldPerMin = m.GoldPerMin,
                    XpPerMin = m.XpPerMin,
                    LastHits = m.LastHits,
                    Denies = m.Denies,
                    HeroDamage = m.HeroDamage,
                    TowerDamage = m.TowerDamage,
                    HeroHealing = m.HeroHealing,
                    LaneRole = m.LaneRole is int lr && lr > 0 ? lr : null,
                    LobbyType = m.LobbyType,
                    GameMode = m.GameMode,
                    PartySize = m.PartySize,
                    AverageRank = m.AverageRank,
                });

                added++;
            }

            if (added > 0)
                logger.LogInformation("{Name}: thêm {Added} ván mới (tổng đã lấy {Total})",
                    p.DisplayName, added, matches.Count);

            return added;
        }
        catch (Exception ex)
        {
            // Một người hỏng không được làm hỏng những người còn lại.
            logger.LogWarning(ex, "Không đồng bộ được {Name} ({Id})", p.DisplayName, p.AccountId);
            p.SyncNote = $"Lần đồng bộ gần nhất lỗi: {ex.Message}";
            return 0;
        }
    }

    private sealed record ConfiguredPlayer(
        long AccountId, string DisplayName, string? Note, bool IsOwner);

    private List<ConfiguredPlayer> ReadConfig()
    {
        var path = Path.Combine(editorialDirectory, "tracked-players.json");
        if (!File.Exists(path)) return [];

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("players", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<ConfiguredPlayer>();

            foreach (var e in arr.EnumerateArray())
            {
                if (!e.TryGetProperty("accountId", out var id) || !id.TryGetInt64(out var accountId))
                    continue;

                list.Add(new ConfiguredPlayer(
                    accountId,
                    e.TryGetProperty("displayName", out var n) ? n.GetString() ?? "?" : "?",
                    e.TryGetProperty("note", out var note) ? note.GetString() : null,
                    e.TryGetProperty("isOwner", out var o) && o.ValueKind == JsonValueKind.True));
            }

            return list;
        }
        catch (Exception ex)
        {
            // Cùng lý do với try/catch quanh EditorialSeeder: file này sửa tay, một dấu phẩy
            // thừa là chuyện sẽ xảy ra, và nó không được phép hạ cả vòng ingest.
            logger.LogError(ex, "tracked-players.json không đọc được");
            return [];
        }
    }
}
