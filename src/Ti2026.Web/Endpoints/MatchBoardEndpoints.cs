using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Analytics;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// api/profile/board — bảng điểm đầy đủ của một ván, cả mười người hai phe.
///
/// NẠP THEO YÊU CẦU, CÓ BỘ ĐỆM VĨNH VIỄN. Lần đầu mở một ván thì tốn ĐÚNG MỘT lời gọi OpenDota;
/// từ lần thứ hai là đọc từ DB, miễn phí. Nạp sẵn cả 9.900 ván của hai tài khoản sẽ tốn 9.900
/// lời gọi — năm ngày hạn mức miễn phí — cho một thứ mà phần lớn không ai mở ra xem.
///
/// CHỖ NGUY HIỂM, và đây là lý do có <see cref="BelongsToTrackedAsync"/>: nếu endpoint nhận bất
/// kỳ match id nào thì nó thành một proxy OpenDota miễn phí, và chi phí của trang phụ thuộc vào
/// lưu lượng chứ không vào dữ liệu. Đó đúng là cái bẫy mà ProPubIngester đã tránh bằng cách chạy
/// theo lịch thay vì theo lượt tải trang — một ngày đông khách là một ngày bị chặn IP. Nên chỉ
/// những ván THẬT SỰ nằm trong lịch sử của người được theo dõi mới được nạp.
/// </summary>
public static class MatchBoardEndpoints
{
    public static void MapMatchBoardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/profile/board", async (
            Ti2026DbContext db, OpenDotaClient client, long match, CancellationToken ct) =>
        {
            if (match <= 0) return Results.BadRequest(new { error = "Thiếu hoặc sai tham số match." });

            if (!await BelongsToTrackedAsync(db, match, ct))
                return Results.NotFound(new
                {
                    error = "Ván này không nằm trong lịch sử của người được theo dõi. Chỉ nạp bảng "
                          + "điểm cho ván đã có trong lịch sử — nếu không, endpoint này thành một "
                          + "proxy OpenDota và chi phí của trang phụ thuộc lưu lượng.",
                });

            var rows = await db.TrackedMatchBoards
                .Where(x => x.MatchId == match)
                .OrderBy(x => x.PlayerSlot)
                .ToListAsync(ct);

            var cached = rows.Count > 0;

            if (!cached)
            {
                try
                {
                    rows = await FetchAsync(db, client, match, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                                or TimeoutException or System.Text.Json.JsonException)
                {
                    return Results.Ok(new
                    {
                        ready = false,
                        note = "Chưa lấy được bảng điểm từ OpenDota. Thử lại sau một lát — ván vẫn "
                             + "còn đó, chỉ là nguồn đang không trả lời.",
                    });
                }
            }

            if (rows.Count == 0)
                return Results.Ok(new
                {
                    ready = false,
                    note = "OpenDota chưa công bố bảng điểm của ván này.",
                });

            var heroes = await db.Heroes.ToDictionaryAsync(h => h.Id, ct);
            // Danh mục khoá theo id SỐ. Vật phẩm chưa có OpenDotaItemId (danh mục chưa nạp lại
            // sau khi thêm cột) thì bị bỏ ra — sáu ô đồ sẽ trống thay vì hiện ảnh vỡ, và nó tự
            // đầy lên ở vòng ingest kế tiếp.
            var items = await db.Items
                .Where(i => i.OpenDotaItemId != null)
                .ToDictionaryAsync(i => i.OpenDotaItemId!.Value, ct);

            var mine = await db.TrackedPlayerMatches
                .Where(m => m.MatchId == match)
                .Select(m => new { m.MatchId, m.Won, m.TrackedPlayer!.AccountId })
                .FirstOrDefaultAsync(ct);

            var radiantWon = mine is not null
                && rows.Any(r => r.AccountId == mine.AccountId && r.IsRadiant == mine.Won);

            return Results.Ok(new
            {
                ready = true,
                matchId = match,
                cached,
                radiantWon,

                // Người đang xem, để giao diện tô đậm đúng một dòng trong mười dòng.
                meAccountId = mine?.AccountId,

                sides = new[] { true, false }.Select(side => new
                {
                    isRadiant = side,
                    label = side ? "Radiant" : "Dire",
                    won = side == radiantWon,
                    netWorth = rows.Where(r => r.IsRadiant == side).Sum(r => r.NetWorth ?? 0),
                    kills = rows.Where(r => r.IsRadiant == side).Sum(r => r.Kills),
                    players = rows.Where(r => r.IsRadiant == side)
                        .OrderBy(r => r.PlayerSlot)
                        .Select(r => Shape(r, heroes, items))
                        .ToList(),
                }).ToList(),
            });
        });
    }

    /// <summary>
    /// Ván có nằm trong lịch sử của ai đang được theo dõi không.
    ///
    /// Không kiểm bằng "người dùng nói nó là của tôi" mà kiểm bằng chính bảng lịch sử — tham số
    /// từ URL thì ai gõ gì cũng được.
    /// </summary>
    private static Task<bool> BelongsToTrackedAsync(Ti2026DbContext db, long match, CancellationToken ct) =>
        db.TrackedPlayerMatches.AnyAsync(m => m.MatchId == match, ct);

    private static async Task<List<TrackedMatchBoard>> FetchAsync(
        Ti2026DbContext db, OpenDotaClient client, long match, CancellationToken ct)
    {
        var detail = await client.GetMatchAsync(match, ct);
        var now = DateTime.UtcNow;

        var rows = (detail.Players ?? [])
            .Select(p => new TrackedMatchBoard
            {
                MatchId = match,
                PlayerSlot = p.PlayerSlot,

                // isRadiant có thể thiếu ở một số phản hồi; player_slot thì luôn có, và dưới 128
                // là Radiant theo đúng quy ước của Valve.
                IsRadiant = p.IsRadiant ?? p.PlayerSlot < 128,

                AccountId = p.AccountId,
                PersonaName = p.PersonaName,
                HeroId = p.HeroId,

                Kills = p.Kills, Deaths = p.Deaths, Assists = p.Assists,
                Level = p.Level, NetWorth = p.NetWorth,
                LastHits = p.LastHits, Denies = p.Denies,
                GoldPerMin = p.GoldPerMin, XpPerMin = p.XpPerMin,
                HeroDamage = p.HeroDamage, TowerDamage = p.TowerDamage,
                HeroHealing = p.HeroHealing,
                PartyId = p.PartyId, RankTier = p.RankTier,

                Item0 = Slot(p.Item0), Item1 = Slot(p.Item1), Item2 = Slot(p.Item2),
                Item3 = Slot(p.Item3), Item4 = Slot(p.Item4), Item5 = Slot(p.Item5),

                FetchedAt = now,
            })
            .ToList();

        if (rows.Count == 0) return rows;

        db.TrackedMatchBoards.AddRange(rows);
        await db.SaveChangesAsync(ct);

        return rows.OrderBy(r => r.PlayerSlot).ToList();
    }

    /// <summary>0 là Ô TRỐNG, không phải vật phẩm id 0 — quy về null ngay ở chỗ đọc.</summary>
    private static int? Slot(int? id) => id is int v && v > 0 ? v : null;

    private static object Shape(
        TrackedMatchBoard r, IReadOnlyDictionary<int, Hero> heroes,
        IReadOnlyDictionary<int, Item> items)
    {
        var hero = heroes.GetValueOrDefault(r.HeroId);

        object? ItemDto(int? id)
        {
            if (id is not int v) return null;
            var meta = items.GetValueOrDefault(v);
            if (meta is null) return null;

            return new
            {
                id = v,
                name = meta.Name ?? meta.Key,

                // Ảnh tra theo KEY chứ không theo Name: Name là tên hiển thị có dấu cách
                // ("Blink Dagger"), còn đường dẫn ảnh của Valve dùng key ("blink").
                image = DotaImages.Item(meta.Key),
            };
        }

        return new
        {
            accountId = r.AccountId,

            // Hồ sơ riêng tư thì OpenDota không trả tên. Nói rõ "hồ sơ riêng tư" thay vì để
            // trống — một ô trống trong bảng điểm đọc như dữ liệu bị mất.
            name = r.PersonaName ?? (r.AccountId is null ? "hồ sơ riêng tư" : "#" + r.AccountId),

            heroId = r.HeroId,
            heroName = hero?.LocalizedName ?? hero?.Name ?? "Hero " + r.HeroId,
            heroImage = DotaImages.Hero(hero?.Name),

            kills = r.Kills, deaths = r.Deaths, assists = r.Assists,
            level = r.Level, netWorth = r.NetWorth,
            lastHits = r.LastHits, denies = r.Denies,
            gpm = r.GoldPerMin, xpm = r.XpPerMin,
            heroDamage = r.HeroDamage, towerDamage = r.TowerDamage, heroHealing = r.HeroHealing,

            partyId = r.PartyId,
            items = new[] { r.Item0, r.Item1, r.Item2, r.Item3, r.Item4, r.Item5 }
                .Select(ItemDto).ToList(),
        };
    }
}
