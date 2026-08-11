using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Analytics;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// api/idols — chân dung lối chơi của những tuyển thủ được chọn để HỌC, và phép so với chính mình.
///
/// KHÁC api/learn Ở CHỖ NÀO. api/learn trả lời câu hỏi về META: bản này hero nào mạnh, pro lên đồ
/// theo mốc nào, ai cấm gì trước. Nó gộp mọi tuyển thủ lại thành một khối. Endpoint này thì ngược
/// hẳn — nó hỏi về TỪNG CON NGƯỜI: Collapse khác ATF chỗ nào khi cả hai cùng đi offlane.
///
/// ĐIỀU QUAN TRỌNG NHẤT PHẢI GIỮ: mọi phép so đều đi qua hệ neo của IdolStyle. So thẳng số thô
/// giữa ván pub và ván chuyên nghiệp là nói dối có hệ thống — đã đo, ván pub nhiều hơn 46% số
/// mạng và cao hơn 28% sát thương trên mỗi vàng. Và mọi phép so đều CÙNG VAI TRÒ, vì so chéo vai
/// trò chỉ chứng minh được rằng carry chết ít hơn support.
/// </summary>
public static class IdolEndpoints
{
    /// <summary>
    /// Cửa sổ thời gian tối đa. Xa hơn ngần này thì đã đổi bản, đổi meta, đổi cả vai trò — đo
    /// thật: ATF cả đời chỉ 64% số ván có nhãn là offlane, nhưng 48/51 ván gần nhất thì có.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(730);

    /// <summary>Số hero nhiều ván nhất đưa ra giao diện.</summary>
    private const int TopHeroes = 8;

    public static void MapIdolEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/idols", async (Ti2026DbContext db, long? player) =>
        {
            var idols = await db.IdolPlayers.OrderBy(x => x.SortOrder).ToListAsync();
            if (idols.Count == 0)
                return Results.Ok(new { ready = false, note = "Chưa nạp dữ liệu tuyển thủ nào." });

            var since = DateTime.UtcNow - Window;

            var matches = await db.IdolMatches
                .Where(m => m.DetailFetchedAt != null && m.StartTime >= since && m.DurationSeconds > 600)
                .ToListAsync();

            var heroes = await db.Heroes
                .Select(h => new { h.Id, h.Name, h.LocalizedName })
                .ToDictionaryAsync(h => h.Id);

            var anchors = await db.StyleAnchors.ToListAsync();
            var byPool = anchors
                .GroupBy(a => a.Pool)
                .ToDictionary(g => g.Key, g => IdolStyle.Normalizer(g.Select(ToPool).ToList()));

            var cards = new List<object>();
            var signatures = new Dictionary<int, (string Role, List<StyleValue> Values)>();

            foreach (var idol in idols)
            {
                var mine = matches.Where(m => m.IdolPlayerId == idol.Id).ToList();
                if (mine.Count == 0) continue;

                // Chọn nhánh có nhiều ván hơn. Ba người có gần như toàn ván thi đấu, riêng Topson
                // gần như chỉ còn ván xếp hạng — nên nhánh phải do DỮ LIỆU chọn, không phải do một
                // hằng số "pro thì xem ván thi đấu" vốn sẽ để trống hẳn một người.
                var tracks = mine
                    .Select(m => (Track: IdolIngester.TrackFor(m.LobbyType), Match: m))
                    .Where(x => x.Track is not null)
                    .GroupBy(x => x.Track!)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Match).ToList());

                if (tracks.Count == 0) continue;

                var track = tracks.OrderByDescending(t => t.Value.Count).First();
                var games = track.Value;
                var pool = track.Key == "thi-dau" ? IdolIngester.ProPool : IdolIngester.IdolPubPool;
                var norm = byPool.GetValueOrDefault(pool) ?? new Dictionary<string, double>();

                var sig = IdolStyle.Signature(games.Select(ToGame).ToList(), norm);

                var labelled = games.Where(g => IdolStyle.RoleOf(g.LaneRole) is not null).ToList();
                var roleGroups = labelled
                    .GroupBy(g => IdolStyle.RoleOf(g.LaneRole)!)
                    .OrderByDescending(g => g.Count())
                    .ToList();

                var measuredRole = roleGroups.FirstOrDefault()?.Key;
                if (measuredRole is not null) signatures[idol.Id] = (measuredRole, sig);

                var heroIds = games.Select(g => g.HeroId).ToList();
                var heroCounts = heroIds.GroupBy(h => h)
                    .Select(g =>
                    {
                        var meta = heroes.GetValueOrDefault(g.Key);
                        return new
                        {
                            heroId = g.Key,
                            name = meta?.LocalizedName ?? meta?.Name ?? "Hero " + g.Key,
                            image = DotaImages.Hero(meta?.Name),
                            games = g.Count(),
                            wins = games.Count(m => m.HeroId == g.Key && m.Won),
                        };
                    })
                    .OrderByDescending(h => h.games)
                    .Take(TopHeroes)
                    .ToList();

                cards.Add(new
                {
                    id = idol.Id,
                    accountId = idol.AccountId,
                    name = idol.Name,
                    team = idol.TeamName,
                    note = idol.Note,
                    avatar = idol.AvatarUrl,
                    declaredRole = idol.DeclaredRole,
                    track = track.Key,
                    trackCounts = tracks.ToDictionary(t => t.Key, t => t.Value.Count),
                    window = new
                    {
                        games = games.Count,
                        from = games.Min(g => g.StartTime),
                        to = games.Max(g => g.StartTime),
                        winrate = games.Count > 0 ? 100.0 * games.Count(g => g.Won) / games.Count : 0,
                    },
                    role = measuredRole is null ? null : new
                    {
                        role = measuredRole,
                        labelled = labelled.Count,
                        share = labelled.Count > 0
                            ? 100.0 * roleGroups[0].Count() / labelled.Count
                            : 0,
                        spread = roleGroups.ToDictionary(g => g.Key, g => g.Count()),
                    },
                    heroPool = new
                    {
                        distinct = heroIds.Distinct().Count(),
                        covering80 = IdolStyle.HeroesCovering(heroIds),
                        top = heroCounts,
                    },
                    signature = sig.Select(Shape).ToList(),
                    depth = Depth(games),
                });
            }

            var me = await MeAsync(db, player, byPool);

            // So khớp CHỈ khi cùng vai trò. Không có ràng buộc này thì bảng "bạn giống ai nhất"
            // biến thành bảng "vai trò của bạn giống vai trò của ai nhất" — một sự thật hiển
            // nhiên đội lốt phát hiện, và người dùng đã bác đúng một lần trước đây.
            var compared = new List<IdolStyle.StyleMatchup>();

            if (me is not null)
                foreach (var (idolId, (role, theirs)) in signatures)
                {
                    if (!me.ByRole.TryGetValue(role, out var mineSig)) continue;
                    if (IdolStyle.Compare(idolId, role, mineSig, theirs) is { } cmp)
                        compared.Add(cmp);
                }

            // Gần nhất lên đầu. Sắp trên bản ghi có kiểu chứ không sắp trên đối tượng ẩn danh:
            // ((dynamic)x).distance dịch được nhưng chỉ nổ lúc chạy nếu ai đó đổi tên trường.
            var matchups = compared
                .OrderBy(c => c.Distance)
                .Select(cmp => new
                {
                    idolId = cmp.IdolId,
                    role = cmp.Role,
                    sharedAxes = cmp.SharedAxes,
                    distance = cmp.Distance,
                    diffs = cmp.Diffs.Select(d => new
                    {
                        axis = d.Axis, mine = d.Mine, theirs = d.Theirs, logGap = d.LogGap,
                    }).ToList(),
                })
                .ToList();

            return Results.Ok(new
            {
                ready = true,
                axes = IdolStyle.Axes.Select(a => new
                {
                    key = a.Key, label = a.Label, group = a.Group, plotLabel = a.PlotLabel,
                    kind = a.Kind, lowerIsBetter = a.LowerIsBetter, hint = a.Hint,
                }).ToList(),
                idols = cards,
                me = me is null ? null : new
                {
                    name = me.Name,
                    accountId = me.AccountId,
                    roles = me.ByRole.ToDictionary(
                        r => r.Key,
                        r => (object)new
                        {
                            games = r.Value.Count > 0 ? r.Value.Max(v => v.Games) : 0,
                            signature = r.Value.Select(Shape).ToList(),
                        }),
                },
                matchups,
                notes = new
                {
                    anchor = "Mọi chỉ số là TỈ SỐ so với người bình thường trong cùng loại ván. "
                           + "1,00 nghĩa là đúng bằng mức đó. Cần vậy vì ván pub và ván chuyên "
                           + "nghiệp không cùng thang: đã đo được ván pub nhiều hơn 46% số mạng "
                           + "và cao hơn 28% sát thương trên mỗi vàng.",
                    role = "Chỉ so giữa những ván CÙNG vai trò. So chéo vai trò chỉ chứng minh "
                         + "được rằng carry chết ít hơn support.",
                    kind = "Trục 'phong cách' cao hơn nghĩa là KHÁC, không phải giỏi hơn.",
                },
            });
        });
    }

    private sealed record MeData(
        string Name, long AccountId, Dictionary<string, List<StyleValue>> ByRole);

    private static async Task<MeData?> MeAsync(
        Ti2026DbContext db, long? player,
        Dictionary<string, Dictionary<string, double>> byPool)
    {
        var people = await db.TrackedPlayers
            .OrderByDescending(p => p.IsOwner)
            .ThenBy(p => p.DisplayName)
            .ToListAsync();

        if (people.Count == 0) return null;

        var me = player is long a ? people.FirstOrDefault(p => p.AccountId == a) : people[0];
        if (me is null) return null;

        var norm = byPool.GetValueOrDefault(IdolIngester.PubPool(me.Id))
                   ?? new Dictionary<string, double>();

        // CHỈ ván có nhãn vai trò thật từ replay. Ván chưa parse không biết vị trí, và đoán vị trí
        // rồi so với tuyển thủ chuyên nghiệp là xây kết luận trên một phỏng đoán.
        var rows = await db.TrackedPlayerMatches
            .Where(m => m.TrackedPlayerId == me.Id
                        && m.LaneRole != null
                        && m.DurationSeconds > 600
                        && m.HeroDamage != null)
            .ToListAsync();

        var byRole = new Dictionary<string, List<StyleValue>>();

        foreach (var group in rows.GroupBy(r => IdolStyle.RoleOf(r.LaneRole)))
        {
            if (group.Key is not { } role) continue;

            var sig = IdolStyle.Signature(group.Select(ToGame).ToList(), norm);
            if (sig.Count > 0) byRole[role] = sig;
        }

        return new MeData(me.DisplayName, me.AccountId, byRole);
    }

    /// <summary>
    /// Những trục CHỈ đọc được ở tuyển thủ, không so được với người dùng: bảng ván của người dùng
    /// không lưu tổng sát thương hay tổng mạng của cả đội, và lấy lại gần mười nghìn ván chỉ để
    /// có chúng thì đắt hơn nhiều lần phần giá trị thu được.
    ///
    /// Nêu riêng ra thay vì trộn chung vào chữ ký, để không ai tưởng đây cũng là phép so.
    /// </summary>
    private static object Depth(List<IdolMatch> games)
    {
        double? Med(Func<IdolMatch, double?> pick)
        {
            var values = games.Select(pick).OfType<double>().OrderBy(x => x).ToList();
            if (values.Count < IdolStyle.MinGames) return null;

            var mid = values.Count / 2;
            return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2.0;
        }

        return new
        {
            teamfight = Med(g => g.TeamfightParticipation),
            damageShare = Med(g => g.TeamHeroDamage > 0 ? g.HeroDamage / (double)g.TeamHeroDamage : null),
            tankShare = Med(g => g.TeamDamageTaken > 0 ? g.DamageTaken / (double)g.TeamDamageTaken : null),
            deathShare = Med(g => g.TeamDeaths > 0 ? g.Deaths / (double)g.TeamDeaths : null),
            laneEfficiency = Med(g => g.LaneEfficiency),
            goldAdv10 = Med(g => g.GoldAdv10),
            firstKill = Med(g => g.FirstKillSecond),
            roaming = games.Count(g => g.IsRoaming == true) is var r && games.Count > 0
                ? 100.0 * r / games.Count
                : 0,
        };
    }

    private static object Shape(StyleValue v) => new
    {
        key = v.Key, games = v.Games, raw = v.Raw, norm = v.Norm, index = v.Index,
    };

    private static StyleGame ToGame(IdolMatch m) => new(
        m.Kills, m.Deaths, m.Assists, m.LastHits, m.HeroDamage, m.TowerDamage, m.NetWorth,
        m.TeamNetWorth, m.LaneEfficiency, m.DurationSeconds, m.LaneRole);

    private static StyleGame ToGame(TrackedPlayerMatch m) => new(
        m.Kills, m.Deaths, m.Assists, m.LastHits, m.HeroDamage, m.TowerDamage, m.NetWorth,
        m.TeamNetWorth, m.LaneEfficiency, m.DurationSeconds, m.LaneRole);

    private static StylePool ToPool(StyleAnchor a) => new(
        a.AllKills, a.AllAssists, a.AllDeaths, a.AllNetWorth, a.AllHeroDamage,
        a.AllLastHits, a.AllTowerDamage, a.AllLaneEfficiency, a.LaneEfficiencyCount,
        a.PlayerCount, a.DurationSeconds);
}
