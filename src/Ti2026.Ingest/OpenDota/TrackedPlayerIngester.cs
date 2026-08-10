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
    /// MỌI trường ta đọc, khai đủ không thiếu cái nào.
    ///
    /// project= là danh sách TRẮNG, không phải danh sách bổ sung: hỏi tên nào thì chỉ nhận về
    /// tên đó. Bản đầu quên khai hero_id và start_time, và hậu quả là 500 ván vào DB với
    /// hero_id = 0 và ngày 01/01/1970 — mất đúng hai trục mà cả trang này dựng lên: hero pool
    /// và diễn biến theo thời gian. Không có gì báo lỗi, vì 0 và 1970 đều là giá trị hợp lệ.
    ///
    /// Nên: khai TẤT CẢ, kể cả những trường vốn nằm trong bộ mặc định. Một danh sách dài mà
    /// khớp đúng bộ cột đang đọc thì đọc mã là thấy ngay; dựa vào bộ mặc định thì không.
    /// </summary>
    public static readonly string[] Projected =
    [
        "match_id", "hero_id", "start_time", "duration", "player_slot", "radiant_win",
        "kills", "deaths", "assists", "gold_per_min", "xp_per_min", "last_hits", "denies",
        "hero_damage", "tower_damage", "hero_healing", "lane_role", "average_rank",
        "party_size", "lobby_type", "game_mode",
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

            // Nguồn đổi hợp đồng thì phải LỘ RA, không được ghi âm thầm. hero_id = 0 và
            // start_time = 0 đều là giá trị "hợp lệ" nên không có gì đổ vỡ — chỉ có dữ liệu
            // vô dụng nằm im trong DB cho tới khi có người mở trang ra và thấy toàn hero rỗng.
            var broken = matches.Count(m => m.HeroId <= 0 || m.StartTime <= 0);
            if (broken > 0)
            {
                p.SyncNote = $"OpenDota trả về {broken}/{matches.Count} ván thiếu hero_id hoặc "
                           + "start_time — hợp đồng của endpoint có thể đã đổi. Kiểm lại danh "
                           + "sách project= trong TrackedPlayerIngester.";
                logger.LogError("{Name}: {Broken}/{Total} ván thiếu trường bắt buộc",
                    p.DisplayName, broken, matches.Count);
            }

            // UPSERT chứ không chỉ insert. Chỉ insert thì mọi ván đã lưu sai sẽ nằm sai vĩnh
            // viễn — sửa được lỗi ở nguồn cũng không cứu được 500 hàng đã vào DB.
            var existing = await db.TrackedPlayerMatches
                .Where(m => m.TrackedPlayerId == p.Id)
                .ToDictionaryAsync(m => m.MatchId, ct);

            var added = 0;

            foreach (var m in matches)
            {
                if (m.HeroId <= 0 || m.StartTime <= 0) continue;

                if (!existing.TryGetValue(m.MatchId, out var row))
                {
                    row = new TrackedPlayerMatch { TrackedPlayerId = p.Id, MatchId = m.MatchId };
                    db.TrackedPlayerMatches.Add(row);
                    existing[m.MatchId] = row;
                    added++;
                }

                row.HeroId = m.HeroId;
                row.StartTime = DateTimeOffset.FromUnixTimeSeconds(m.StartTime).UtcDateTime;
                row.DurationSeconds = m.Duration;

                // player_slot < 128 là phe Radiant. Đây là cách DUY NHẤT biết người này thắng
                // hay thua — endpoint chỉ trả radiant_win cho cả ván.
                row.Won = (m.PlayerSlot < 128) == m.RadiantWin;

                row.Kills = m.Kills ?? 0;
                row.Deaths = m.Deaths ?? 0;
                row.Assists = m.Assists ?? 0;
                row.GoldPerMin = m.GoldPerMin;
                row.XpPerMin = m.XpPerMin;
                row.LastHits = m.LastHits;
                row.Denies = m.Denies;
                row.HeroDamage = m.HeroDamage;
                row.TowerDamage = m.TowerDamage;
                row.HeroHealing = m.HeroHealing;
                row.LaneRole = m.LaneRole is int lr && lr > 0 ? lr : null;
                row.LobbyType = m.LobbyType;
                row.GameMode = m.GameMode;
                row.PartySize = m.PartySize;
                row.AverageRank = m.AverageRank;
            }

            logger.LogInformation("{Name}: {Added} ván mới, cập nhật {Total} ván",
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
