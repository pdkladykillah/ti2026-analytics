using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// api/profile/matches — lịch sử đấu từng ván của người được theo dõi.
///
/// VÌ SAO ĐÁNG CÓ, KHI DOTA ĐÃ CÓ LỊCH SỬ TRONG GAME. Vì hai thứ trả lời hai câu khác nhau.
/// Lịch sử trong game nói ván đó ra sao; chỗ này nói ván đó ra sao SO VỚI MỨC THƯỜNG của chính
/// hero đó — 289 lính Anti-Mage là bình thường hay xuất sắc. Phân vị theo hero là thứ OpenDota
/// tính sẵn và ta đã lưu 10 cột, còn client thì không hiện.
///
/// Và nó nói VỊ TRÍ THẬT, thứ Dota cũng không cho biết: `lane_role` chỉ là lane, nên hard
/// support và carry cùng mang nhãn `safe`. Vị trí ở đây đi qua RoleResolver.
///
/// PHÂN VỊ ĐÃ SẠCH TỪ CHỖ GHI, không phải lọc lại ở đây: TrackedMatchDetailIngester.Pct trả
/// null khi raw bằng 0 mà phân vị trên 0,5 — đo được ở ván 8937662260, hồi máu thô 0 mà phân vị
/// 0,93 chỉ vì đa số người chơi hero đó cũng bằng 0. Đừng thêm một lớp lọc thứ hai ở đây: hai
/// nơi cùng quyết định một luật là hai nơi sẽ lệch nhau.
/// </summary>
public static class MatchHistoryEndpoints
{
    /// <summary>Số ván mỗi trang. Đủ để cuộn một lượt mà không kéo cả 5.895 dòng xuống trình duyệt.</summary>
    public const int PageSize = 40;

    /// <summary>
    /// Hạn replay của Valve, đo được: ván 61 ngày parse được, ván 70 ngày thì không.
    ///
    /// Dùng để giao diện nói rõ vì sao ván cũ không có vị trí — đo trên chính tài khoản này:
    /// trong 60 ngày gần nhất nhãn lane phủ 100%, ngoài đó chỉ còn 9,9%. Không nói ra thì người
    /// đọc tưởng dữ liệu hỏng, trong khi đó là giới hạn không ai sửa được.
    /// </summary>
    public const int ReplayWindowDays = 60;

    public static void MapMatchHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/profile/matches", async (
            Ti2026DbContext db, long? player, int? hero, string? position, int? page) =>
        {
            var people = await db.TrackedPlayers
                .OrderByDescending(p => p.IsOwner)
                .ThenBy(p => p.DisplayName)
                .ToListAsync();

            if (people.Count == 0)
                return Results.Ok(new { ready = false, note = "Chưa cấu hình người theo dõi nào." });

            var me = player is long a ? people.FirstOrDefault(p => p.AccountId == a) : people[0];

            if (me is null)
                return Results.NotFound(new { error = "Tài khoản này không nằm trong danh sách theo dõi." });

            var all = await db.TrackedPlayerMatches
                .Where(m => m.TrackedPlayerId == me.Id)
                .OrderByDescending(m => m.StartTime)
                .ToListAsync();

            var heroes = await db.Heroes.ToDictionaryAsync(h => h.Id);

            // Vị trí tính MỘT LẦN cho mọi ván, trước khi lọc: bộ lọc theo vị trí cần biết vị trí
            // của cả những ván sắp bị loại, và bộ đếm cạnh mỗi nút lọc cũng vậy.
            var tagged = all
                .Select(m => new { M = m, Role = RoleResolver.Resolve(m.LaneRole, m.TeamFarmRank) })
                .ToList();

            var filtered = tagged
                .Where(x => hero is not int h || x.M.HeroId == h)
                .Where(x => string.IsNullOrEmpty(position) || x.Role.Code == position)
                .ToList();

            var at = Math.Max(page ?? 0, 0);
            var rows = filtered.Skip(at * PageSize).Take(PageSize).ToList();

            var cutoff = DateTime.UtcNow.AddDays(-ReplayWindowDays);

            return Results.Ok(new
            {
                ready = true,
                player = new { accountId = me.AccountId, name = me.DisplayName },

                total = all.Count,
                matched = filtered.Count,
                page = at,
                pageSize = PageSize,
                hasMore = (at + 1) * PageSize < filtered.Count,

                replayWindowDays = ReplayWindowDays,

                // Bộ lọc mang theo SỐ VÁN của từng lựa chọn. Một danh sách 120 hero không kèm số
                // thì người dùng phải bấm thử từng cái mới biết cái nào có dữ liệu.
                filters = new
                {
                    heroes = tagged
                        .GroupBy(x => x.M.HeroId)
                        .Select(g => new
                        {
                            heroId = g.Key,
                            name = HeroName(heroes, g.Key),
                            image = DotaImages.Hero(heroes.GetValueOrDefault(g.Key)?.Name),
                            games = g.Count(),
                        })
                        .OrderByDescending(x => x.games)
                        .ToList(),

                    positions = tagged
                        .Where(x => x.Role.IsExact)
                        .GroupBy(x => x.Role.Code)
                        .Select(g => new
                        {
                            code = g.Key,
                            label = IdolStyle.PositionLabel(g.Key),
                            games = g.Count(),
                        })
                        .OrderBy(x => x.code)
                        .ToList(),
                },

                matches = rows.Select(x => Shape(x.M, x.Role, heroes, cutoff)).ToList(),
            });
        });
    }

    private static string HeroName(IReadOnlyDictionary<int, Hero> heroes, int id)
    {
        var meta = heroes.GetValueOrDefault(id);
        return meta?.LocalizedName ?? meta?.Name ?? "Hero " + id;
    }

    private static object Shape(
        TrackedPlayerMatch m, RoleVerdict role, IReadOnlyDictionary<int, Hero> heroes, DateTime cutoff)
    {
        var meta = heroes.GetValueOrDefault(m.HeroId);

        return new
        {
            matchId = m.MatchId,
            startTime = m.StartTime,
            durationSeconds = m.DurationSeconds,
            won = m.Won,

            heroId = m.HeroId,
            heroName = HeroName(heroes, m.HeroId),
            heroImage = DotaImages.Hero(meta?.Name),

            kills = m.Kills,
            deaths = m.Deaths,
            assists = m.Assists,

            // Vị trí kèm NGUỒN, không chỉ mã. "pos2 từ nhãn replay" và "core suy từ hạng net
            // worth" là hai mức chắc chắn khác hẳn nhau, và gộp chúng thành một chuỗi là xoá mất
            // đúng phần người đọc cần để biết tin tới đâu.
            position = role.IsExact ? role.Code : null,
            positionLabel = role.Label,
            positionSource = role.Source,

            // Ván ngoài cửa sổ replay thì KHÔNG có nhãn lane, và sẽ không bao giờ có: replay của
            // Valve hết hạn sau ~60 ngày. Cờ này để giao diện nói ra thay vì để một ô trống.
            replayExpired = m.StartTime < cutoff && m.LaneRole is null,

            detail = new
            {
                gpm = m.GoldPerMin,
                xpm = m.XpPerMin,
                lastHits = m.LastHits,
                denies = m.Denies,
                netWorth = m.NetWorth,
                level = m.Level,
                heroDamage = m.HeroDamage,
                towerDamage = m.TowerDamage,
                laneEfficiency = m.LaneEfficiency,
                goldAdv10 = m.GoldAdv10,
                goldAdv20 = m.GoldAdv20,

                // Hạng net worth trong đội — thứ cùng với lane quyết định vị trí thật, nên hiện
                // ra để người đọc kiểm được kết luận vị trí chứ không phải tin suông.
                teamFarmRank = m.TeamFarmRank,
            },

            // Phân vị theo hero — thứ client Dota không hiện. Đã sạch từ chỗ ghi, xem ghi chú lớp.
            pct = new
            {
                gpm = m.PctGpm,
                xpm = m.PctXpm,
                lastHits = m.PctLastHits,
                kills = m.PctKills,

                // Số chết ĐẢO CHIỀU: phân vị cao nghĩa là chết nhiều, tức tệ. Trả về nguyên bản
                // và đánh dấu, chứ không tự đảo ở đây — đảo ở tầng dữ liệu thì mọi nơi đọc nó
                // phải nhớ rằng nó đã bị đảo, và chỗ thứ ba sẽ quên.
                deaths = m.PctDeaths,
                deathsLowerIsBetter = true,

                heroDamage = m.PctHeroDamage,
                towerDamage = m.PctTowerDamage,
            },
        };
    }
}
