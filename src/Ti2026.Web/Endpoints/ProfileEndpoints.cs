using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// api/profile — hồ sơ và phân tích của người chơi được THEO DÕI LÂU DÀI.
///
/// CÔNG KHAI có chủ ý: đây là trang giới thiệu bản thân, không phải khu vực riêng tư. Không có
/// gì nhạy cảm ở đây — mọi con số đều lấy từ hồ sơ Dota 2 vốn đã công khai.
///
/// KHÁC api/me Ở CHỖ NÀO, và vì sao phải là hai endpoint:
///
/// api/me (trong LearnEndpoints) tra cứu BẤT KỲ AI theo yêu cầu — gọi thẳng ra OpenDota, có
/// trần gọi, có bộ đệm, KHÔNG lưu gì xuống DB. Nó phục vụ người lạ ghé qua trang.
///
/// api/profile phục vụ những người đã khai trong tracked-players.json: dữ liệu được LƯU LẠI
/// theo thời gian, nên trả lời được "ba tháng qua tiến bộ thế nào" — điều mà một lần tra cứu
/// tức thời không bao giờ làm được.
///
/// Vì thế endpoint này CÓ trả avatar còn api/me thì không: người ở đây đã chủ động khai mình
/// vào danh sách theo dõi, còn api/me thì tra ra bất kỳ ai và không nên vọng lại ảnh đại diện
/// của người lạ.
///
/// Nhận ?player=accountId; không truyền thì lấy chủ trang.
/// </summary>
public static class ProfileEndpoints
{
    /// <summary>Bậc rank của Dota: hàng chục của rank_tier.</summary>
    private static readonly string[] RankNames =
        ["", "Herald", "Guardian", "Crusader", "Archon", "Legend", "Ancient", "Divine", "Immortal"];

    public static void MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        // Danh sách người được theo dõi — để giao diện biết có thể xem hồ sơ của ai.
        app.MapGet("/api/profile/people", async (Ti2026DbContext db) =>
            Results.Ok(new
            {
                people = await db.TrackedPlayers
                    .OrderByDescending(p => p.IsOwner)
                    .ThenBy(p => p.DisplayName)
                    .Select(p => new
                    {
                        accountId = p.AccountId, name = p.DisplayName,
                        isOwner = p.IsOwner, note = p.Note,
                    })
                    .ToListAsync(),
            }));

        app.MapGet("/api/profile", async (Ti2026DbContext db, long? player) =>
        {
            var people = await db.TrackedPlayers
                .OrderByDescending(p => p.IsOwner)
                .ThenBy(p => p.DisplayName)
                .ToListAsync();

            if (people.Count == 0)
                return Results.Ok(new { ready = false, note = "Chưa cấu hình người theo dõi nào." });

            // Truyền id thì phải là id ĐANG theo dõi; không truyền thì lấy chủ trang. Không im
            // lặng rơi về chủ trang khi id sai — bên gọi sẽ tưởng đang xem hồ sơ mình vừa hỏi.
            var me = player is long a
                ? people.FirstOrDefault(p => p.AccountId == a)
                : people[0];

            if (me is null)
                return Results.NotFound(new
                {
                    error = "Tài khoản này không nằm trong danh sách theo dõi. Dùng api/me để tra "
                          + "cứu một lần bất kỳ ai, hoặc thêm vào data/tracked-players.json để "
                          + "được lưu lịch sử.",
                });

            var rows = await db.TrackedPlayerMatches
                .Where(m => m.TrackedPlayerId == me.Id)
                .OrderByDescending(m => m.StartTime)
                .ToListAsync();

            var heroNames = await db.Heroes.ToDictionaryAsync(h => h.Id, h => h.LocalizedName ?? h.Name);

            // Mốc PRO trên cùng hero, lấy từ trận đấu GIẢI đã nạp sẵn. Đây là thứ một trang theo
            // dõi cá nhân thường không có, còn ở đây thì dữ liệu đã nằm trong DB.
            var pro = (await db.MatchPlayers
                    .Where(p => p.GoldPerMin > 0)
                    .GroupBy(p => p.HeroId)
                    .Select(g => new { HeroId = g.Key, Games = g.Count(), Gpm = g.Average(x => (double)x.GoldPerMin) })
                    .ToListAsync())
                .ToDictionary(x => x.HeroId, x => (x.Games, x.Gpm));

            var games = rows.Select(m => new PlayerGame(
                m.HeroId, m.StartTime, m.DurationSeconds, m.Won,
                m.Kills, m.Deaths, m.Assists, m.GoldPerMin, m.LastHits,
                m.PartySize, m.AverageRank, m.IsRadiant)).ToList();

            var heroes = rows
                .GroupBy(m => m.HeroId)
                .Select(g =>
                {
                    var wins = g.Count(x => x.Won);
                    var gpm = g.Where(x => x.GoldPerMin is int).Select(x => (double)x.GoldPerMin!.Value)
                        .DefaultIfEmpty(0).Average();
                    var lhpm = g.Where(x => x.LastHits is int && x.DurationSeconds > 0)
                        .Select(x => x.LastHits!.Value / (x.DurationSeconds / 60.0))
                        .DefaultIfEmpty(0).Average();
                    var deaths = g.Sum(x => x.Deaths);

                    pro.TryGetValue(g.Key, out var p);

                    return new HeroLine(
                        g.Key,
                        heroNames.GetValueOrDefault(g.Key, $"#{g.Key}"),
                        g.Count(), wins, wins * 100.0 / g.Count(),
                        Math.Round(gpm), Math.Round(lhpm, 1),
                        Math.Round((g.Sum(x => x.Kills) + g.Sum(x => x.Assists))
                                   / (double)Math.Max(deaths, 1), 2),
                        p.Games >= 10 ? Math.Round(p.Gpm) : null,
                        p.Games,
                        PlayerInsights.Notable(g.Count(), wins));
                })
                .OrderByDescending(h => h.Games)
                .ToList();

            var lifetime = me.Wins + me.Losses > 0
                ? me.Wins * 100.0 / (me.Wins + me.Losses)
                : 0;

            // Diễn biến theo THÁNG. Tháng có dưới 10 ván thì vẫn hiện nhưng đánh dấu mỏng —
            // giấu đi thì đường biểu đồ có lỗ mà không ai biết vì sao.
            var byMonth = rows
                .GroupBy(m => new DateTime(m.StartTime.Year, m.StartTime.Month, 1, 0, 0, 0, DateTimeKind.Utc))
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    month = g.Key.ToString("yyyy-MM"),
                    games = g.Count(),
                    winrate = Math.Round(g.Count(x => x.Won) * 100.0 / g.Count(), 1),
                    avgGpm = Math.Round(g.Where(x => x.GoldPerMin is int)
                        .Select(x => (double)x.GoldPerMin!.Value).DefaultIfEmpty(0).Average()),
                    thin = g.Count() < 10,
                })
                .ToList();

            return Results.Ok(new
            {
                updatedAt = DateTime.UtcNow,
                ready = rows.Count > 0,
                syncNote = me.SyncNote,

                people = people.Select(p => new
                {
                    accountId = p.AccountId, name = p.DisplayName, isOwner = p.IsOwner, note = p.Note,
                }).ToList(),

                me = new
                {
                    accountId = me.AccountId,
                    name = me.DisplayName,
                    persona = me.PersonaName,
                    avatar = me.AvatarUrl,
                    note = me.Note,
                    rank = RankLabel(me.RankTier),
                    wins = me.Wins,
                    losses = me.Losses,
                    lifetimeWinrate = Math.Round(lifetime, 1),
                    lastSyncedAt = me.LastSyncedAt,
                    storedGames = rows.Count,
                    from = rows.Count > 0 ? rows[^1].StartTime : (DateTime?)null,
                    to = rows.Count > 0 ? rows[0].StartTime : (DateTime?)null,
                },

                insights = PlayerInsights.Read(games, heroes, lifetime)
                    .Select(i => new { kind = i.Kind, tone = i.Tone, text = i.Text }).ToList(),

                heroes = heroes.Select(h => new
                {
                    heroId = h.HeroId, name = h.Name, games = h.Games, wins = h.Wins,
                    winrate = Math.Round(h.Winrate, 1), gpm = h.AvgGpm,
                    lastHitsPerMin = h.AvgLastHitsPerMin, kda = h.Kda,
                    proGpm = h.ProGpm, proGames = h.ProGames, notable = h.Notable,
                }).ToList(),

                months = byMonth,

                method = "Chỉ số lấy từ hồ sơ Dota 2 công khai qua OpenDota. Vị trí suy từ mức "
                       + "farm chứ không từ vai trò khai báo — Dota chỉ ghi lại vai trò ở khoảng "
                       + "6% số ván, quá ít để kết luận. Hero được gọi là mạnh/yếu chỉ khi cách "
                       + "biệt còn đứng vững sau khi tính tới việc bạn chơi hàng chục hero.",
            });
        });
    }

    private static string? RankLabel(int? tier)
    {
        if (tier is not int t || t < 10) return null;
        var band = t / 10;
        var star = t % 10;
        return band < RankNames.Length ? $"{RankNames[band]} {star}" : null;
    }
}
