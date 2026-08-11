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

            // Lấy đồng đội bằng một truy vấn riêng rồi tự gộp, thay vì Include: Include sinh ra
            // một phép nối trả về mỗi ván lặp lại 4 lần cùng toàn bộ 30 cột của nó.
            var mateRows = await db.TrackedMatchTeammates
                .Where(t => t.Match!.TrackedPlayerId == me.Id)
                .Select(t => new
                {
                    t.TrackedPlayerMatchId, t.AccountId, t.PersonaName, t.SameParty, t.RankTier,
                })
                .ToListAsync();

            var matesByMatch = mateRows
                .GroupBy(t => t.TrackedPlayerMatchId)
                .ToDictionary(g => g.Key, g => g.ToList());

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

            // Chấm "đáng kể" cho CẢ POOL cùng lúc: phép hiệu chỉnh so sánh bội cần biết có bao
            // nhiêu phép so thực sự, và con số đó chỉ có khi đã gom đủ hero. Người dùng này có
            // 125 hero đạt ngưỡng số ván — gấp ba lần cỡ pool mà bản trước giả định.
            var pool = rows
                .GroupBy(m => m.HeroId)
                .Select(g => (Games: g.Count(), Wins: g.Count(x => x.Won)))
                .ToList();

            var poolNotable = PlayerInsights.NotableSet(pool);

            var heroes = rows
                .GroupBy(m => m.HeroId)
                .Select((g, idx) =>
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
                        poolNotable[idx]);
                })
                .OrderByDescending(h => h.Games)
                .ToList();

            var lifetime = me.Wins + me.Losses > 0
                ? me.Wins * 100.0 / (me.Wins + me.Losses)
                : 0;

            // ---------- Vai trò ----------
            // Một chỗ duy nhất gọi RoleResolver, rồi mọi phần bên dưới dùng lại kết quả đó. Gọi
            // rải rác thì sớm muộn sẽ có nơi tự chế một quy tắc riêng, và đó đúng là cái sai đã
            // phải sửa một lần (suy vai trò từ mức farm).
            var roleOf = rows.ToDictionary(
                m => m.Id, m => RoleResolver.Resolve(m.LaneRole, m.TeamFarmRank));

            var roleGames = rows
                .Select(m => new RoleGame(m.StartTime, m.Won, m.LaneRole, m.TeamFarmRank))
                .ToList();

            var roles = RoleBreakdown.Slices(roleGames);
            var eras = RoleBreakdown.Eras(roleGames);

            // ---------- Điểm thành phần, kiểu Leetify ----------
            var rated = rows.Select(m => new RatedGame(
                m.StartTime, m.Won, roleOf[m.Id].Code,
                m.PctGpm, m.PctXpm, m.PctLastHits, m.PctDenies,
                m.PctKills, m.PctDeaths, m.PctAssists,
                m.PctHeroDamage, m.PctHeroHealing, m.PctTowerDamage)).ToList();

            var components = SkillComponents.Read(rated);

            // Cùng phép tính, chạy trên từng tập con vai trò. Chỉ những vai trò có nhãn THẬT mới
            // được tách riêng: tách theo "core/hỗ trợ suy luận" sẽ cho ra hai cột trông rất chắc
            // chắn nhưng thực ra chỉ chia theo thứ hạng tài sản.
            var byRole = roles
                .Where(r => r.Exact)
                .Select(r => new
                {
                    role = r.Code,
                    label = r.Label,
                    games = r.Games,
                    winrate = r.Winrate,
                    components = SkillComponents
                        .Read(rated.Where(g => g.Role == r.Code).ToList())
                        .Select(Shape).ToList(),
                })
                .Where(x => x.components.Count > 0)
                .ToList();

            // ---------- Đồng đội ----------
            var mateGames = rows.Select(m => new MateGame(
                m.Won,
                matesByMatch.TryGetValue(m.Id, out var list)
                    ? list.Select(t => new MatePresence(
                        t.AccountId, t.PersonaName, t.SameParty, t.RankTier)).ToList()
                    : [])).ToList();

            var mates = TeammateAnalysis.Read(mateGames);

            // ---------- Cái chết có đổi được gì không ----------
            // Đo bằng kinh tế ĐỒNG ĐỘI trong chính ván đó, không mượn chỉ số hỗ trợ làm proxy:
            // hỗ trợ chỉ ghi nhận việc có mặt lúc hạ gục, mà người đã chết thì không thể có mặt
            // ở pha hạ gục sau đó.
            var deathEffect = DeathEffect.Read(rows
                .Select(m => new DeathGame(
                    m.Won, m.PctDeaths, m.MatesPctGpm, m.TeamNetWorth, m.EnemyNetWorth, m.DurationSeconds))
                .ToList());

            // Tầng thứ hai, mịn hơn: từng PHA GIAO TRANH. Chỉ có ở ván đã parse, nhưng nó phân
            // biệt được thứ mà con số cả ván không phân biệt nổi — một cái chết vô ích với một
            // cái chết kéo người địch đi để đồng đội dọn phần còn lại.
            var trade = DeathTrade.Read(rows.Select(m => new TradeGame(
                m.FightsDied, m.FightsDiedAhead, m.FightSwingDied,
                m.FightsSurvived, m.FightSwingSurvived,
                m.TradeMyGold, m.TradeFoeGold, m.TradeFoeDeaths,
                roleOf[m.Id].Code, roleOf[m.Id].Label)));

            // ---------- Chơi lúc nào thì hay ----------
            // Chạy trên dữ liệu ĐÃ CÓ, không thêm một lời gọi nào, và phủ toàn bộ lịch sử chứ
            // không riêng phần đã parse.
            var habits = PlayHabits.Read(rows
                .Select(m => new HabitGame(
                    m.StartTime, m.DurationSeconds, m.Won, m.PctLastHits, m.PctDeaths))
                .ToList());

            // ---------- Giai đoạn lane ----------
            var lane = LanePhase.Read(rows
                .Select(m => new LaneGame(
                    m.Won, roleOf[m.Id].Code, roleOf[m.Id].Label,
                    m.LaneEfficiency, m.GoldAdv10, m.GoldAdv20, m.GoldAdv30))
                .ToList());

            // ---------- Hero nào khắc chế ----------
            // Số liệu đối đầu lấy từ players/{id}/heroes — MỘT lời gọi, thay cho việc lấy lại
            // chi tiết gần mười nghìn ván chỉ để đếm hero phe địch.
            var facedRows = await db.TrackedPlayerHeroes
                .Where(h => h.TrackedPlayerId == me.Id && h.AgainstGames > 0)
                .ToListAsync();

            var nemesis = NemesisHeroes.Read(
                facedRows.Select(h => new FacedHero(
                    h.HeroId, heroNames.GetValueOrDefault(h.HeroId, $"#{h.HeroId}"),
                    h.AgainstGames, h.AgainstWins)),
                lifetime);

            // ---------- Hero pool đặt cạnh meta ----------
            // Mốc là bậc rank CAO chứ không phải toàn bộ pub: người dùng ở Ancient, còn tỷ lệ
            // thắng gộp cả Herald tới Immortal là một quần thể khác hẳn.
            //
            // Đòi tối thiểu 1.000 ván ở bậc cao thì mốc mới đủ chắc để làm chuẩn. Hiện hero ít
            // mẫu nhất cũng có 2.109 ván nên điều kiện này không loại ai — nó ở đây cho lúc có
            // hero MỚI ra: vài trăm ván đầu tiên của một hero mới là tỷ lệ hoàn toàn không ổn
            // định, và dùng nó làm mốc sẽ khiến cả bảng lệch theo mà không có gì báo.
            var metaWinrate = (await db.HeroStats.Where(s => s.HighPick >= 1000).ToListAsync())
                .ToDictionary(s => s.HeroId, s => s.HighWin * 100.0 / s.HighPick);

            var metaRows = HeroMetaGap.Read(
                heroes.Select(h => (h.HeroId, h.Name, h.Games, h.Wins)), metaWinrate);

            var gamesPerHero = heroes.ToDictionary(h => h.HeroId, h => h.Games);
            var untouched = HeroMetaGap.Untouched(gamesPerHero, metaWinrate)
                .Select(u => new
                {
                    heroId = u.HeroId,
                    name = heroNames.GetValueOrDefault(u.HeroId, $"#{u.HeroId}"),
                    metaWinrate = Math.Round(u.MetaWinrate, 1),
                    games = gamesPerHero.GetValueOrDefault(u.HeroId),
                })
                .ToList();

            // Diễn biến theo THÁNG. Tháng có dưới 10 ván thì vẫn hiện nhưng đánh dấu mỏng —
            // giấu đi thì đường biểu đồ có lỗ mà không ai biết vì sao.
            //
            // Cột thứ hai là PHÂN VỊ ăn lính, không phải GPM trung bình. Bản trước dùng GPM và
            // nó nói dối một cách khó thấy: GPM phụ thuộc nặng vào việc tháng đó hay chơi hero
            // nào — một tháng chơi nhiều support sẽ tụt GPM mà chẳng liên quan gì tới kỹ năng.
            // Phân vị thì đã so với người chơi cùng hero, nên nó đo đúng người này.
            var byMonth = rows
                .GroupBy(m => new DateTime(m.StartTime.Year, m.StartTime.Month, 1, 0, 0, 0, DateTimeKind.Utc))
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var pcts = g.Where(x => x.PctLastHits is int)
                        .Select(x => x.PctLastHits!.Value).ToList();

                    return new
                    {
                        month = g.Key.ToString("yyyy-MM"),
                        games = g.Count(),
                        winrate = Math.Round(g.Count(x => x.Won) * 100.0 / g.Count(), 1),

                        // Dưới 5 ván có phân vị thì để trống thay vì vẽ một điểm gần như ngẫu
                        // nhiên — điểm đó sẽ kéo cả đường xu hướng theo nó.
                        pctLastHits = pcts.Count >= 5
                            ? SkillComponents.Percentile(pcts, 50)
                            : (int?)null,

                        ratedGames = pcts.Count,
                        thin = g.Count() < 10,
                    };
                })
                .ToList();

            // Xu hướng đọc trên chuỗi PHÂN VỊ, và chỉ trên những tháng đủ dày. Ngưỡng "đáng nói"
            // đặt 5 điểm phân vị: tách được khỏi nhiễu và đáng quan tâm là hai chuyện khác nhau,
            // và một chuỗi gần phẳng thì mọi trôi dạt đều thành nhiều lần độ nhiễu.
            var trendSeries = byMonth
                .Where(m => m.pctLastHits is int && !m.thin)
                .TakeLast(24)
                .Select(m => (double)m.pctLastHits!.Value)
                .ToList();

            var trend = TrendVerdict.Read(trendSeries, "phân vị ăn lính", notableChange: 5);

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

                insights = PlayerInsights
                    .Read(games, heroes, lifetime, components, roles, eras, mates, metaRows)
                    .Select(i => new { kind = i.Kind, tone = i.Tone, text = i.Text }).ToList(),

                components = components.Select(Shape).ToList(),
                componentsByRole = byRole,

                deathEffect = new
                {
                    verdict = deathEffect.Verdict,
                    text = deathEffect.Text,
                    splits = deathEffect.Splits.Select(s => new
                    {
                        outcome = s.Outcome, games = s.Games,
                        medianMinutes = s.MedianMinutes,
                        highDeathMatesFarm = s.HighDeathMatesFarm,
                        lowDeathMatesFarm = s.LowDeathMatesFarm,
                        matesFarmGap = s.MatesFarmGap,
                        highDeathLead = s.HighDeathLead, lowDeathLead = s.LowDeathLead,
                        leadGap = s.LeadGap,
                        pValue = Math.Round(s.PValue, 5),
                    }).ToList(),
                },

                deathTrade = trade is null ? null : new
                {
                    matches = trade.Value.Matches,
                    fights = trade.Value.Fights,
                    aheadShare = trade.Value.AheadShare,
                    swingDied = trade.Value.SwingDied,
                    swingSurvived = trade.Value.SwingSurvived,
                    myGold = trade.Value.MyGold,
                    foeGold = trade.Value.FoeGold,
                    goldEdge = trade.Value.GoldEdge,
                    trades = trade.Value.Trades,
                    minTrades = DeathTrade.MinTrades,
                    text = trade.Value.Text,
                    byRole = trade.Value.ByRole.Select(r => new
                    {
                        role = r.Role, label = r.Label, trades = r.Trades,
                        myGold = r.MyGold, foeGold = r.FoeGold, goldEdge = r.GoldEdge,
                    }).ToList(),
                },

                roles = roles.Select(r => new
                {
                    code = r.Code, label = r.Label, games = r.Games,
                    winrate = r.Winrate, exact = r.Exact,
                }).ToList(),

                // Chỉ những năm có ván mang nhãn thật mới lên biểu đồ. Năm nào cũng vẽ thì phần
                // lớn cột sẽ là 0/0 và người xem đọc thành "năm đó không chơi".
                roleEras = eras.Where(e => e.Labelled > 0).Select(e => new
                {
                    year = e.Year, games = e.Games, labelled = e.Labelled,
                    safe = e.Safe, mid = e.Mid, off = e.Off, jungle = e.Jungle,
                    thin = e.Labelled < RoleBreakdown.MinLabelledPerYear,
                }).ToList(),

                habits = habits is null ? null : new
                {
                    sessions = habits.Value.Sessions,
                    gamesPerSession = habits.Value.GamesPerSession,
                    rated = habits.Value.Rated,
                    breakMinutes = PlayHabits.SessionBreakMinutes,
                    tilt = habits.Value.Tilt is not TiltReading t ? null : new
                    {
                        gamesAfterWin = t.GamesAfterWin, gamesAfterLoss = t.GamesAfterLoss,
                        farmAfterWin = t.FarmAfterWin, farmAfterLoss = t.FarmAfterLoss,
                        farmGap = t.FarmGap, farmP = Math.Round(t.FarmP, 5),
                        surviveAfterWin = t.SurviveAfterWin, surviveAfterLoss = t.SurviveAfterLoss,
                        surviveGap = t.SurviveGap, surviveP = Math.Round(t.SurviveP, 5),
                        minGap = PlayHabits.MinGap,
                    },
                    byPosition = habits.Value.ByPosition.Select(r => new
                    {
                        position = r.Position, isTail = r.IsTail, games = r.Games,
                        farm = r.Farm, survive = r.Survive, winrate = r.Winrate,
                    }).ToList(),
                    byHour = habits.Value.ByHour.Select(r => new
                    {
                        hour = r.Hour, games = r.Games,
                        farm = r.Farm, survive = r.Survive, winrate = r.Winrate,
                    }).ToList(),
                },

                lanePhase = lane is null ? null : new
                {
                    games = lane.Value.Games,
                    verdict = lane.Value.Verdict,
                    text = lane.Value.Text,
                    sides = lane.Value.Sides.Select(s => new
                    {
                        outcome = s.Outcome, games = s.Games, efficiency = s.Efficiency,
                        adv10 = s.Adv10, adv20 = s.Adv20, adv30 = s.Adv30,
                    }).ToList(),
                    byRole = lane.Value.ByRole.Select(r => new
                    {
                        role = r.Role, label = r.Label, games = r.Games, efficiency = r.Efficiency,
                    }).ToList(),
                },

                nemesis = nemesis.Select(n => new
                {
                    heroId = n.HeroId, name = n.Name, games = n.Games,
                    winrate = n.Winrate, edge = n.Edge, notable = n.Notable,
                }).ToList(),

                // Ngưỡng đi kèm dữ liệu để phần hiển thị không phải viết cứng lại con số — sửa
                // ngưỡng ở một nơi mà trang vẫn nói đúng.
                minPartyGames = TeammateAnalysis.MinPartyGames,

                // Vì sao bảng hero có thể KHÔNG đánh dấu hero nào. Một bảng toàn ô trống mà im
                // lặng sẽ bị đọc thành "hệ thống hỏng", trong khi sự thật là mẫu mỗi hero còn
                // quá mỏng so với mức nhiễu — và con số dưới đây nói rõ cần bao nhiêu.
                noticeNeedsGames = MultipleTests.GamesNeeded(pool.Count, 15),
                noticePoolSize = pool.Count,

                meta = metaRows.Select(h => new
                {
                    heroId = h.HeroId, name = h.Name, games = h.Games,
                    winrate = h.Winrate, metaWinrate = h.MetaWinrate,
                    edge = h.Edge, notable = h.Notable,
                }).ToList(),

                metaUntouched = untouched,

                teammates = mates.Select(t => new
                {
                    accountId = t.AccountId, name = t.Name,
                    games = t.Games, wins = t.Wins, winrate = t.Winrate,
                    partyGames = t.PartyGames,
                    withoutGames = t.WithoutGames, withoutWinrate = t.WithoutWinrate,
                    lift = t.Lift, notable = t.Notable, rank = RankLabel(t.RankTier),
                }).ToList(),

                heroes = heroes.Select(h => new
                {
                    heroId = h.HeroId, name = h.Name, games = h.Games, wins = h.Wins,
                    winrate = Math.Round(h.Winrate, 1), gpm = h.AvgGpm,
                    lastHitsPerMin = h.AvgLastHitsPerMin, kda = h.Kda,
                    proGpm = h.ProGpm, proGames = h.ProGames, notable = h.Notable,
                }).ToList(),

                months = byMonth,

                monthTrend = new
                {
                    direction = trend.Direction,
                    change = Math.Round(trend.Change, 1),
                    points = trend.Points,
                    text = trend.Text,
                },

                method = "Chỉ số lấy từ hồ sơ Dota 2 công khai qua OpenDota. Phân vị là so với "
                       + "mọi người chơi CÙNG HERO, nên nó đã trừ đi phần lệch do bạn hay chọn "
                       + "hero nào — 600 GPM là kém với Anti-Mage và phi thường với Crystal "
                       + "Maiden. Cột số chết đã đảo chiều để mọi cột cùng đọc theo hướng cao là "
                       + "tốt. Vị trí chính xác chỉ lấy từ nhãn replay, không suy từ mức farm: "
                       + "đo trên chính tài khoản này thì last hit ở safelane, mid và offlane "
                       + "lần lượt là 299 / 345 / 282, gần như bằng nhau. Hero hay đồng đội được "
                       + "gọi là hợp/khắc chỉ khi cách biệt còn đứng vững sau khi tính tới việc "
                       + "bạn có hàng chục hero và hàng chục người chơi cùng. Cố ý KHÔNG có điểm "
                       + "tổng: trọng số giữa farm và sát thương là do người viết chọn chứ không "
                       + "có trong dữ liệu.",
            });
        });
    }

    /// <summary>
    /// Một cột điểm thành phần, đưa ra JSON. <c>inverted</c> đi kèm ra tận giao diện để chỗ hiển
    /// thị nói được "đã đảo chiều" thay vì để người xem tự đoán vì sao chết nhiều lại điểm thấp.
    /// </summary>
    private static object Shape(SkillComponent c) => new
    {
        key = c.Key, label = c.Label, group = c.Group, games = c.Games,
        median = c.Median, low = c.Low, high = c.High,
        recent = c.Recent, inverted = c.Inverted,

        // Tách theo kết quả trận. Một con số gộp giấu mất câu chuyện: cột giữ mạng gộp lại là
        // 42 nhưng ván thắng là 60 còn ván thua là 26 — và lời khuyên đưa ra từ hai con số đó
        // khác hẳn nhau.
        won = c.Won, lost = c.Lost,
    };

    private static string? RankLabel(int? tier)
    {
        if (tier is not int t || t < 10) return null;
        var band = t / 10;
        var star = t % 10;
        return band < RankNames.Length ? $"{RankNames[band]} {star}" : null;
    }
}
