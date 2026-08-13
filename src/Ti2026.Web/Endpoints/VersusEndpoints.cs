using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// api/versus — hai đội đặt cạnh nhau, ghép theo TỪNG VỊ TRÍ.
///
/// KHÁC api/h2h Ở CHỖ NÀO. api/h2h so hai đội như hai khối: tỷ lệ thắng, số mạng, first blood.
/// Endpoint này bẻ khối đó ra thành năm cặp người và hỏi từng cặp một — pos1 của đội A so với
/// pos1 của đội B, cùng thước đo, cùng vai trò.
///
/// BA LUẬT CỦA DỰ ÁN QUYẾT ĐỊNH THIẾT KẾ Ở ĐÂY, và vi phạm bất kỳ luật nào là ra kết luận SAI
/// chứ không phải xấu:
///
/// 1. `lane_role` là LANE, không phải VỊ TRÍ. Hard support đứng safelane nên mang nhãn `safe`
///    y hệt carry. Đếm hero theo lane cho PARIVISION ra 68 hero "safelane" — con số đó trộn
///    carry với hard support thành một. Mọi phép nhóm ở đây đi qua <see cref="RoleResolver"/>.
///
/// 2. Số thô của hai đội KHÔNG cùng thang: khác giải, khác bản game, khác đối thủ. Nên mọi trục
///    là TỈ SỐ so với mốc `pro` trong StyleAnchors, dùng lại đúng bảy trục của IdolStyle.
///
/// 3. Chỉ so CÙNG vai trò. So chéo chỉ chứng minh được rằng carry chết ít hơn support.
///
/// KHÔNG TRẢ VỀ "ĐỘI NÀO MẠNH HƠN". Đó là việc của api/predict. Một màn vừa mô tả vừa phán
/// thắng thua sẽ khiến người đọc coi phần mô tả là bằng chứng cho phần phán — mà chúng được đo
/// bằng hai cách khác hẳn nhau.
/// </summary>
public static class VersusEndpoints
{
    /// <summary>Cửa sổ thời gian, cùng mức với api/idols: xa hơn là đã đổi bản và đổi cả vai trò.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(730);

    /// <summary>
    /// Số ván tối thiểu để một hero được tính là "có trong pool".
    ///
    /// 1 ván thì mọi hero từng bị thử một lần đều vào danh sách, và tập "chung" phình ra tới mức
    /// không phân biệt được gì: đo thật ở ngưỡng 1, PARIVISION và Team Spirit chung 112 hero trên
    /// tổng 127 hero của game.
    /// </summary>
    public const int MinHeroGames = 3;

    /// <summary>
    /// Dưới ngần này ván đối đầu thì KHÔNG hiện tỷ số.
    ///
    /// Đo trên 120 cặp có thể có giữa 16 đội: 20 cặp chưa gặp nhau lần nào, trung vị chỉ 8 ván,
    /// và 21 cặp có tối đa 2 ván. Hiện "thắng 2–0" dựng trên hai ván là hiện một con số trông như
    /// kết luận trong khi nó là nhiễu.
    /// </summary>
    public const int MinHeadToHead = 5;

    /// <summary>Số hero lệch nhiều nhất đưa ra giao diện, mỗi chiều.</summary>
    public const int TopHeroDiffs = 5;

    /// <summary>Thứ tự vị trí trên giao diện. pos1 xuống pos5, đúng cách người ta đọc đội hình.</summary>
    private static readonly string[] Positions = ["pos1", "pos2", "pos3", "pos4", "pos5"];

    public static void MapVersusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/versus", async (Ti2026DbContext db, string? a, string? b) =>
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return Results.BadRequest(new { error = "Thiếu tham số: cần cả a và b là slug đội." });

            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Hai đội phải khác nhau." });

            var teams = await db.Teams
                .Where(t => t.Slug == a || t.Slug == b)
                .ToDictionaryAsync(t => t.Slug);

            if (!teams.TryGetValue(a, out var teamA) || !teams.TryGetValue(b, out var teamB))
                return Results.NotFound(new { error = "Không tìm thấy đội với slug đã cho." });

            var since = DateTime.UtcNow - Window;

            var rosterA = await RosterAsync(db, teamA.Id);
            var rosterB = await RosterAsync(db, teamB.Id);

            var ids = rosterA.Keys.Concat(rosterB.Keys).ToHashSet();

            if (ids.Count == 0)
                return Results.Ok(new
                {
                    ready = false,
                    note = "Chưa có đội hình đang hiệu lực cho một trong hai đội.",
                });

            // NẠP CẢ 10 NGƯỜI MỖI VÁN, không chỉ người của hai đội này.
            //
            // Bắt buộc, vì hai đại lượng dưới đây chỉ có nghĩa khi biết cả phe: thứ hạng net worth
            // trong đội (thứ quyết định vị trí) và tổng net worth của phe (mẫu số của trục phân tài
            // nguyên). Lọc sẵn về người của ta thì hạng tính trên một tập thiếu người và sẽ lệch
            // đúng những ván có đồng đội không thuộc roster đang hiệu lực.
            var matchIds = await db.MatchPlayers
                .Where(mp => mp.PlayerId != null && ids.Contains(mp.PlayerId.Value))
                .Join(db.Matches, mp => mp.MatchId, m => m.Id, (mp, m) => new { mp.MatchId, m.StartTime })
                .Where(x => x.StartTime >= since)
                .Select(x => x.MatchId)
                .Distinct()
                .ToListAsync();

            var rows = await db.MatchPlayers
                .Where(mp => matchIds.Contains(mp.MatchId))
                .ToListAsync();

            var durations = await db.Matches
                .Where(m => matchIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.DurationSeconds);

            var normalizer = await StyleSupport.ProNormalizerAsync(db);
            var heroes = await db.Heroes.ToDictionaryAsync(h => h.Id);

            // Một dòng đã gắn vị trí thật và bối cảnh phe của nó.
            var tagged = TagRows(rows, durations);

            var byPlayer = tagged
                .Where(t => t.Row.PlayerId is int pid && ids.Contains(pid))
                .GroupBy(t => t.Row.PlayerId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var seatsA = Seats(byPlayer, rosterA);
            var seatsB = Seats(byPlayer, rosterB);

            var positions = Positions.Select(pos => new
            {
                position = pos,
                label = IdolStyle.PositionLabel(pos),
                a = SeatDto(seatsA.GetValueOrDefault(pos), normalizer, heroes),
                b = SeatDto(seatsB.GetValueOrDefault(pos), normalizer, heroes),
                heroes = HeroSplit(seatsA.GetValueOrDefault(pos), seatsB.GetValueOrDefault(pos), heroes),
            }).ToList();

            return Results.Ok(new
            {
                ready = true,
                window = new { days = (int)Window.TotalDays, from = since, to = DateTime.UtcNow },
                minHeroGames = MinHeroGames,
                minGamesForAxes = IdolStyle.MinGames,

                axes = IdolStyle.Axes.Select(x => new
                {
                    key = x.Key, label = x.Label, plotLabel = x.PlotLabel,
                    kind = x.Kind, lowerIsBetter = x.LowerIsBetter, hint = x.Hint,
                }).ToList(),

                a = TeamDto(teamA),
                b = TeamDto(teamB),
                head2head = await HeadToHeadAsync(db, teamA.Id, teamB.Id),
                positions,
            });
        });
    }

    private static object TeamDto(Team t) => new { slug = t.Slug, name = t.Name, logo = t.LogoUrl };

    /// <summary>Đội hình ĐANG hiệu lực. ValidTo null là quy ước "còn trong đội" của bảng này.</summary>
    private static async Task<Dictionary<int, RosterMember>> RosterAsync(Ti2026DbContext db, int teamId)
    {
        // LOẠI HLV. Bảng RosterEntries chứa cả huấn luyện viên, và ở đây điều đó không vô hại:
        // Puppey của PARIVISION và Milan của Team Spirit đều từng là tuyển thủ thi đấu, nên nếu
        // dữ liệu có ván cũ của họ thì phép ghép sẽ trao một trong năm chỗ cho người không còn ra
        // sân — và ô đó trông y hệt một ô hợp lệ. `Role != "COACH"` là quy ước sẵn có của dự án,
        // dùng ở LineupLookup, StaleTeamIdDetector và BracketTeamGapDetector.
        var rows = await db.RosterEntries
            .Where(r => r.TeamId == teamId && r.ValidTo == null && r.Role != "COACH"
                     && r.Player != null)
            .Select(r => new
            {
                r.PlayerId,
                r.Player!.Nick,
                r.Player.RealName,
                Asset = db.MediaAssets.FirstOrDefault(m => m.Id == r.Player.PhotoMediaAssetId),
            })
            .ToListAsync();

        // Ảnh phục vụ từ CHÍNH MÁY MÌNH. dltv.org chặn hotlink theo referrer, nên trả URL gốc ra
        // giao diện là trả về một ô ảnh vỡ — xem ghi chú dài hơn ở DataEndpoints.
        return rows.ToDictionary(
            r => r.PlayerId,
            r => new RosterMember(r.Nick, r.RealName,
                r.Asset != null ? $"media/{r.Asset.LocalPath}" : null));
    }

    private sealed record RosterMember(string Nick, string? RealName, string? Photo);

    /// <summary>Một dòng người chơi kèm vị trí đã suy ra và bối cảnh phe của ván đó.</summary>
    private sealed record Tagged(MatchPlayer Row, string? Position, long TeamNetWorth, int DurationSeconds);

    /// <summary>
    /// Gắn vị trí thật cho từng dòng.
    ///
    /// Hạng net worth tính TRONG PHE của chính ván đó, không phải trong toàn ván: mười người chia
    /// hai đội, và "giàu thứ nhất đội mình" mới là thứ phân biệt carry với hard support. Xếp hạng
    /// trên cả mười người thì một đội thua toàn diện sẽ có carry rơi xuống hạng 6 và bị đọc thành
    /// người hỗ trợ.
    /// </summary>
    private static List<Tagged> TagRows(
        List<MatchPlayer> rows, IReadOnlyDictionary<long, int> durations)
    {
        var result = new List<Tagged>(rows.Count);

        foreach (var side in rows.GroupBy(r => (r.MatchId, r.IsRadiant)))
        {
            var seats = side.OrderByDescending(r => r.NetWorth ?? 0).ToList();
            var teamNetWorth = seats.Sum(r => (long)(r.NetWorth ?? 0));
            var duration = durations.GetValueOrDefault(side.Key.MatchId);

            for (var i = 0; i < seats.Count; i++)
            {
                var verdict = RoleResolver.Resolve(seats[i].LaneRole, i + 1);

                // CHỈ nhận kết luận chính xác. Verdict "core"/"support" suy từ thứ hạng là đủ cho
                // một trang thống kê chung, nhưng ở đây nó sẽ ghép nhầm pos1 với pos3 vào cùng một
                // ô — mà cả tính năng này dựng trên đúng chuyện "cùng vị trí mới so được".
                result.Add(new Tagged(seats[i], verdict.IsExact ? verdict.Code : null,
                    teamNetWorth, duration));
            }
        }

        return result;
    }

    /// <summary>Một người và tập ván ở vị trí hay gặp nhất của họ.</summary>
    private sealed record Seat(
        RosterMember Member, string Position, int Games, int Labelled, double Share, List<Tagged> Rows);

    /// <summary>
    /// Ghép mỗi vị trí với đúng một người của đội.
    ///
    /// Vị trí HAY GẶP NHẤT, không phải vị trí biên tập gán: hai thứ lệch nhau thật — Nisha có 30%
    /// số ván ở vị trí khác vị trí ghi trong roster. Và khi hai người cùng đội cùng hay gặp một vị
    /// trí thì người nhiều ván hơn giữ chỗ, người kia rơi về vị trí hay gặp thứ hai của mình —
    /// nếu không thì một đội sẽ có hai pos2 và một ô trống.
    /// </summary>
    private static Dictionary<string, Seat> Seats(
        Dictionary<int, List<Tagged>> byPlayer, Dictionary<int, RosterMember> roster)
    {
        var ranked = new List<(int PlayerId, string Position, int Games, int Labelled)>();

        foreach (var (playerId, member) in roster)
        {
            if (!byPlayer.TryGetValue(playerId, out var rows)) continue;

            var labelled = rows.Count(r => r.Position != null);

            foreach (var g in rows.Where(r => r.Position != null).GroupBy(r => r.Position!))
                ranked.Add((playerId, g.Key, g.Count(), labelled));
        }

        var seats = new Dictionary<string, Seat>();
        var taken = new HashSet<int>();

        // Nhận chỗ theo thứ tự số ván giảm dần: cặp (người, vị trí) chắc chắn nhất được chọn trước,
        // nên một người đánh 250 ván mid không bị đẩy sang chỗ khác bởi người đánh 30 ván mid.
        foreach (var r in ranked.OrderByDescending(x => x.Games))
        {
            if (taken.Contains(r.PlayerId) || seats.ContainsKey(r.Position)) continue;

            var rows = byPlayer[r.PlayerId].Where(x => x.Position == r.Position).ToList();

            seats[r.Position] = new Seat(
                roster[r.PlayerId], r.Position, r.Games, r.Labelled,
                r.Labelled > 0 ? 100.0 * r.Games / r.Labelled : 0, rows);

            taken.Add(r.PlayerId);
        }

        return seats;
    }

    private static object? SeatDto(
        Seat? seat, Dictionary<string, double> normalizer, Dictionary<int, Hero> heroes)
    {
        if (seat is null) return null;

        var games = seat.Rows.Select(ToStyleGame).ToList();
        var signature = IdolStyle.Signature(games, normalizer);

        return new
        {
            nick = seat.Member.Nick,
            real = seat.Member.RealName,
            photo = seat.Member.Photo,

            games = seat.Games,
            labelled = seat.Labelled,
            share = seat.Share,

            // Đủ ván để dựng trục hay chưa — giao diện phải nói ra thay vì vẽ một thanh trống.
            enough = seat.Games >= IdolStyle.MinGames,

            signature = signature.Select(v => new
            {
                key = v.Key, games = v.Games, raw = v.Raw, norm = v.Norm, index = v.Index,
            }).ToList(),

            raw = RawStats(seat.Rows),
            topHeroes = TopHeroes(seat.Rows, heroes, 6),
        };
    }

    /// <summary>
    /// Số thô, để sau nếp gấp. Trung vị chứ không phải trung bình: một ván 90 phút kéo mọi số
    /// trung bình theo nó, và bảng này để đọc "mức thường của người này", không phải tổng cộng.
    /// </summary>
    private static object RawStats(List<Tagged> rows) => new
    {
        gpm = Median(rows.Select(r => (double)r.Row.GoldPerMin)),
        xpm = Median(rows.Select(r => (double)r.Row.XpPerMin)),
        kills = Median(rows.Select(r => (double)r.Row.Kills)),
        deaths = Median(rows.Select(r => (double)r.Row.Deaths)),
        assists = Median(rows.Select(r => (double)r.Row.Assists)),
        lastHits = Median(rows.Where(r => r.Row.LastHits != null).Select(r => (double)r.Row.LastHits!)),
        laneEfficiency = Median(rows.Where(r => r.Row.LaneEfficiencyPct != null)
            .Select(r => r.Row.LaneEfficiencyPct!.Value)),
    };

    private static double? Median(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return null;

        list.Sort();
        var mid = list.Count / 2;
        return list.Count % 2 == 1 ? list[mid] : (list[mid - 1] + list[mid]) / 2;
    }

    private static StyleGame ToStyleGame(Tagged t) => new(
        t.Row.Kills, t.Row.Deaths, t.Row.Assists,
        t.Row.LastHits, t.Row.HeroDamage, t.Row.TowerDamage, t.Row.NetWorth,
        t.TeamNetWorth,
        t.Row.LaneEfficiencyPct is double e ? (int)Math.Round(e) : null,
        t.DurationSeconds, t.Row.LaneRole);

    private static List<object> TopHeroes(
        List<Tagged> rows, Dictionary<int, Hero> heroes, int take) =>
        rows.GroupBy(r => r.Row.HeroId)
            .OrderByDescending(g => g.Count())
            .Take(take)
            .Select(g => HeroDto(g.Key, g.Count(), heroes))
            .ToList();

    private static object HeroDto(int heroId, int games, Dictionary<int, Hero> heroes)
    {
        var meta = heroes.GetValueOrDefault(heroId);
        return new
        {
            heroId,
            name = meta?.LocalizedName ?? meta?.Name ?? "Hero " + heroId,
            image = DotaImages.Hero(meta?.Name),
            games,
        };
    }

    /// <summary>
    /// Hero chung, hero riêng, và những hero lệch tần suất nhiều nhất.
    ///
    /// HAI CÁCH ĐỌC, CỐ Ý GIỮ CẢ HAI. Tập cứng theo ngưỡng trả lời câu "bên kia có bao giờ đụng
    /// hero này chưa" — nhìn là hiểu, nhưng nó xếp hero đánh 20 lần và hero đánh 3 lần vào cùng
    /// một ô. Dải lệch bắt đúng phần đó: cả hai cùng chơi, nhưng một bên ưu tiên hơn hẳn.
    /// </summary>
    private static object HeroSplit(Seat? a, Seat? b, Dictionary<int, Hero> heroes)
    {
        var ca = Counts(a);
        var cb = Counts(b);

        var poolA = ca.Where(kv => kv.Value >= MinHeroGames).Select(kv => kv.Key).ToHashSet();
        var poolB = cb.Where(kv => kv.Value >= MinHeroGames).Select(kv => kv.Key).ToHashSet();

        List<object> Shape(IEnumerable<int> keys, Dictionary<int, int> from) =>
            keys.OrderByDescending(k => from.GetValueOrDefault(k))
                .Select(k => HeroDto(k, from.GetValueOrDefault(k), heroes))
                .ToList();

        var diffs = poolA.Union(poolB)
            .Select(k => new { k, ga = ca.GetValueOrDefault(k), gb = cb.GetValueOrDefault(k) })
            .Select(x => new { x.k, x.ga, x.gb, gap = x.ga - x.gb })
            .Where(x => x.gap != 0)
            .ToList();

        List<object> Lean(bool towardsA) => diffs
            .Where(x => towardsA ? x.gap > 0 : x.gap < 0)
            .OrderByDescending(x => Math.Abs(x.gap))
            .Take(TopHeroDiffs)
            .Select(x =>
            {
                var meta = heroes.GetValueOrDefault(x.k);
                return (object)new
                {
                    heroId = x.k,
                    name = meta?.LocalizedName ?? meta?.Name ?? "Hero " + x.k,
                    image = DotaImages.Hero(meta?.Name),
                    gamesA = x.ga,
                    gamesB = x.gb,
                };
            })
            .ToList();

        return new
        {
            onlyA = Shape(poolA.Except(poolB), ca),
            onlyB = Shape(poolB.Except(poolA), cb),
            both = Shape(poolA.Intersect(poolB), ca),
            leanA = Lean(true),
            leanB = Lean(false),
        };

        static Dictionary<int, int> Counts(Seat? s) =>
            s is null ? [] : s.Rows.GroupBy(r => r.Row.HeroId).ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Đối đầu trực tiếp. Trả về số ván LUÔN LUÔN, nhưng chỉ đánh dấu `enough` khi đủ mẫu — giao
    /// diện cần biết cả "hai đội chưa gặp nhau" lẫn "gặp 2 lần", và hai câu đó khác nhau.
    /// </summary>
    private static async Task<object> HeadToHeadAsync(Ti2026DbContext db, int idA, int idB)
    {
        var rows = await db.Matches
            .Where(m => (m.RadiantTeamId == idA && m.DireTeamId == idB)
                     || (m.RadiantTeamId == idB && m.DireTeamId == idA))
            .Select(m => new { m.RadiantTeamId, m.RadiantWin, m.StartTime })
            .ToListAsync();

        var winsA = rows.Count(r => r.RadiantWin == (r.RadiantTeamId == idA));

        return new
        {
            games = rows.Count,
            enough = rows.Count >= MinHeadToHead,
            winsA,
            winsB = rows.Count - winsA,
            lastAt = rows.Count > 0 ? rows.Max(r => r.StartTime) : (DateTime?)null,
            minGames = MinHeadToHead,
        };
    }
}
