using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Analytics;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// Dành cho NGƯỜI CHƠI, không phải người xem cá cược: bản này hero nào đang được coi trọng,
/// và pro lên đồ theo mốc nào.
///
/// Ranh giới phải nói rõ ngay từ đầu: dữ liệu chuyên nghiệp KHÔNG phải dữ liệu pub. Nó trả lời
/// tốt hai câu — "bản này cái gì mạnh" và "mốc thời gian chuẩn là bao nhiêu" — và trả lời tệ
/// mọi thứ cần 5 người phối hợp. Endpoint nào cũng kèm mẫu và ngưỡng để người đọc tự thấy chỗ
/// nào là số đo, chỗ nào là nhiễu.
/// </summary>
public static class LearnEndpoints
{
    /// <summary>
    /// Dưới ngưỡng này thì tỷ lệ phần trăm nhảy quá mạnh theo từng ván, không đáng hiển thị.
    /// </summary>
    private const int DefaultMinDraftAppearances = 5;

    private const int DefaultMinItemSamples = 5;

    /// <summary>
    /// Lượt cấm được coi là "sớm". Thể thức Captains Mode đổi cấu trúc theo bản, nên đây là
    /// PHỎNG ĐOÁN theo thứ tự lượt chứ không phải khai báo đúng luật — và phải đọc như vậy.
    /// </summary>
    private const int EarlyBanOrderLimit = 6;

    /// <summary>
    /// Giá tối thiểu để một món được vào bảng mốc lên đồ.
    ///
    /// Vì sao cần: bảng sắp theo tần suất, và ai cũng mua Iron Branch (55) với Circlet (155),
    /// nên không lọc thì linh kiện chiếm hết chỗ của Black King Bar. Lọc theo GIÁ chứ không
    /// theo trường qual của OpenDota, vì qual gắn nhãn "component" cho cả Blink Dagger
    /// (2250 vàng, món chủ lực của nửa số hero) — lọc theo qual sẽ vứt đúng thứ cần xem.
    ///
    /// Lọc ở lúc ĐỌC và có tham số để hạ xuống: dữ liệu thô vẫn còn nguyên trong DB.
    /// </summary>
    private const int DefaultMinItemCost = 1000;

    public static void MapLearnEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        MapLanes(api);
        MapMe(api);

        // ---------- Ưu tiên cấm/chọn ----------
        api.MapGet("/draft", async (
            Ti2026DbContext db, string? patch = null, int minAppearances = DefaultMinDraftAppearances) =>
        {
            // Mặc định chỉ bản mới nhất: đây là trang "học bản hiện tại", trộn 7.35 vào sẽ
            // biến nó thành trang lịch sử và khuyên sai.
            var currentPatch = patch ?? await CurrentPatchAsync(db);

            var draftMatchIds = await db.Matches
                .Where(m => currentPatch == null || m.PatchVersion == currentPatch)
                .Where(m => db.DraftEvents.Any(d => d.MatchId == m.Id))
                .Select(m => m.Id)
                .ToListAsync();

            if (draftMatchIds.Count == 0)
                return Results.Ok(new
                {
                    patch = PatchIndex.Name(currentPatch),
                    matchesWithDraft = 0,
                    heroes = Array.Empty<object>(),
                    note = "Chưa có ván nào nạp được bàn draft. Cần nạp lại match detail.",
                });

            var events = await db.DraftEvents
                .Where(d => draftMatchIds.Contains(d.MatchId))
                .Select(d => new { d.MatchId, d.HeroId, d.IsPick, d.IsRadiant, d.Order })
                .ToListAsync();

            // Bên nào thắng, để tính winrate của hero khi ĐƯỢC CHỌN
            var winners = await db.Matches
                .Where(m => draftMatchIds.Contains(m.Id))
                .Select(m => new { m.Id, m.RadiantWin })
                .ToDictionaryAsync(m => m.Id, m => m.RadiantWin);

            var heroNames = await db.Heroes
                .ToDictionaryAsync(h => h.Id, h => new { h.LocalizedName, h.Name });

            var total = draftMatchIds.Count;

            var heroes = events
                .GroupBy(e => e.HeroId)
                .Where(g => g.Count() >= minAppearances)
                .Select(g =>
                {
                    var picks = g.Where(x => x.IsPick).ToList();
                    var bans = g.Where(x => !x.IsPick).ToList();

                    var pickWins = picks.Count(x =>
                        winners.TryGetValue(x.MatchId, out var radiantWin) && radiantWin == x.IsRadiant);

                    heroNames.TryGetValue(g.Key, out var meta);

                    return new
                    {
                        heroId = g.Key,
                        name = meta?.LocalizedName ?? meta?.Name ?? $"hero {g.Key}",
                        image = DotaImages.Hero(meta?.Name),

                        picks = picks.Count,
                        bans = bans.Count,

                        // Tỷ lệ được coi trọng: bị cấm cũng là một hình thức được coi trọng,
                        // và với hero mạnh nhất thì đó lại là hình thức chủ yếu.
                        contestRate = Math.Round(g.Count() * 100.0 / total, 1),
                        pickRate = Math.Round(picks.Count * 100.0 / total, 1),
                        banRate = Math.Round(bans.Count * 100.0 / total, 1),

                        // Cấm sớm = sợ. Càng nhỏ càng đáng ngại.
                        avgBanOrder = bans.Count == 0
                            ? (double?)null
                            : Math.Round(bans.Average(x => x.Order), 1),
                        earlyBans = bans.Count(x => x.Order < EarlyBanOrderLimit),

                        winrate = picks.Count == 0
                            ? (double?)null
                            : Math.Round(pickWins * 100.0 / picks.Count, 1),
                    };
                })
                .OrderByDescending(h => h.contestRate)
                .ToList();

            return Results.Ok(new
            {
                patch = PatchIndex.Name(currentPatch),
                matchesWithDraft = total,
                minAppearances,
                earlyBanOrderLimit = EarlyBanOrderLimit,
                heroes,
                note = "contestRate = tỷ lệ ván mà hero bị cấm HOẶC được chọn. Bị cấm cũng là "
                     + "được coi trọng — với hero mạnh nhất thì đó là hình thức chủ yếu, nên "
                     + "winrate một mình sẽ bỏ sót chúng.",
                caveat = "Đây là dữ liệu Captains Mode chuyên nghiệp. Ở pub không có cấm chọn "
                       + "theo lượt và không có 5 người phối hợp, nên hãy đọc bảng này như "
                       + "'hero nào đang mạnh ở bản này', đừng đọc như 'hero nào leo rank tốt'.",
            });
        });

        // ---------- Mốc lên đồ ----------
        api.MapGet("/items", async (
            Ti2026DbContext db,
            int? hero = null,
            string? patch = null,
            int minSamples = DefaultMinItemSamples,
            int minCost = DefaultMinItemCost) =>
        {
            var currentPatch = patch ?? await CurrentPatchAsync(db);

            // Không truyền hero: trả danh sách hero có dữ liệu để UI dựng ô chọn.
            if (hero is null)
            {
                var available = await db.ItemPurchases
                    .Where(p => currentPatch == null || p.Match!.PatchVersion == currentPatch)
                    .GroupBy(p => p.HeroId)
                    .Select(g => new { heroId = g.Key, purchases = g.Count() })
                    .ToListAsync();

                var names = await db.Heroes.ToDictionaryAsync(h => h.Id, h => h.LocalizedName ?? h.Name);

                return Results.Ok(new
                {
                    patch = PatchIndex.Name(currentPatch),
                    heroes = available
                        .Select(x => new
                        {
                            x.heroId,
                            name = names.GetValueOrDefault(x.heroId, $"hero {x.heroId}"),
                            x.purchases,
                        })
                        .OrderByDescending(x => x.purchases),
                });
            }

            var rows = await db.ItemPurchases
                .Where(p => p.HeroId == hero)
                .Where(p => currentPatch == null || p.Match!.PatchVersion == currentPatch)
                .Select(p => new
                {
                    p.MatchId, p.PlayerSlot, p.ItemKey, p.TimeSeconds, p.IsRadiant,
                    RadiantWin = p.Match!.RadiantWin,
                })
                .ToListAsync();

            if (rows.Count == 0)
                return Results.Ok(new
                {
                    heroId = hero,
                    patch = PatchIndex.Name(currentPatch),
                    items = Array.Empty<object>(),
                    note = "Chưa có dữ liệu mua đồ cho hero này ở bản đang xét.",
                });

            // MỘT lần mua đầu tiên cho mỗi (ván, người, món). Không gộp bước này thì tango
            // mua 20 lần sẽ đè bẹp mọi món thật, và "mốc lên đồ" thành vô nghĩa.
            var firstBuys = rows
                .GroupBy(r => new { r.MatchId, r.PlayerSlot, r.ItemKey })
                .Select(g =>
                {
                    var first = g.OrderBy(x => x.TimeSeconds).First();
                    return new
                    {
                        g.Key.ItemKey,
                        first.TimeSeconds,
                        Won = first.IsRadiant == first.RadiantWin,
                    };
                })
                .ToList();

            // Bảng item cho tên hiển thị và giá. Thiếu bảng này thì không lọc được linh kiện,
            // và phải nói ra chứ không được im lặng trả về một bảng đầy Iron Branch.
            var catalogue = await db.Items.ToDictionaryAsync(i => i.Key);

            bool Keep(string key) =>
                catalogue.TryGetValue(key, out var meta)
                && !meta.IsConsumable
                && meta.Cost >= minCost;

            var considered = firstBuys.Where(x => Keep(x.ItemKey)).ToList();
            var filteredOut = firstBuys.Count - considered.Count;

            var items = considered
                .GroupBy(x => x.ItemKey)
                .Where(g => g.Count() >= minSamples)
                .Select(g =>
                {
                    catalogue.TryGetValue(g.Key, out var meta);
                    var wins = g.Where(x => x.Won).Select(x => (double)x.TimeSeconds).ToList();
                    var losses = g.Where(x => !x.Won).Select(x => (double)x.TimeSeconds).ToList();

                    var all = DistributionStats.Describe(g.Select(x => (double)x.TimeSeconds).ToList());

                    return new
                    {
                        itemKey = g.Key,
                        name = meta?.Name ?? PrettyItemName(g.Key),
                        cost = meta?.Cost,
                        image = DotaImages.Item(g.Key),
                        samples = g.Count(),

                        medianSeconds = (int)Math.Round(all.Median),
                        p25Seconds = (int)Math.Round(all.P25),
                        p75Seconds = (int)Math.Round(all.P75),

                        // Chênh lệch thắng/thua là phần đáng đọc nhất: cùng một món, lên sớm
                        // hơn trong ván thắng nghĩa là mốc đó thật sự quan trọng.
                        medianWinSeconds = wins.Count >= 3
                            ? (int?)Math.Round(DistributionStats.Describe(wins).Median)
                            : null,
                        medianLossSeconds = losses.Count >= 3
                            ? (int?)Math.Round(DistributionStats.Describe(losses).Median)
                            : null,
                        winSamples = wins.Count,
                        lossSamples = losses.Count,
                    };
                })
                .OrderByDescending(x => x.samples)
                .Take(30)
                .ToList();

            var heroName = await db.Heroes
                .Where(h => h.Id == hero)
                .Select(h => h.LocalizedName ?? h.Name)
                .FirstOrDefaultAsync();

            return Results.Ok(new
            {
                heroId = hero,
                heroName,
                patch = PatchIndex.Name(currentPatch),
                minSamples,
                minCost,
                items,

                // Nói ra những gì đã bị lọc: một bảng đã cắt bớt mà không khai thì đọc như
                // thể đó là toàn bộ sự thật.
                filteredOut,
                filterNote = catalogue.Count == 0
                    ? "CHƯA có bảng item nên không lọc được linh kiện — bảng dưới đây trộn cả "
                    + "Iron Branch với Black King Bar. Cần một vòng ingest để nạp constants/items."
                    : $"Đã ẩn {filteredOut} lượt mua đồ tiêu hao và món dưới {minCost} vàng, vì "
                    + "ai cũng mua linh kiện nên chúng sẽ chiếm hết chỗ. Hạ minCost để xem thêm.",
                caveat = "Mốc của pro giả định có người hỗ trợ nhường lính và không bị bỏ lane. "
                       + "Ở pub chậm hơn 2–4 phút là bình thường; hãy dùng khoảng P25–P75 làm "
                       + "mục tiêu, đừng lấy trung vị làm chuẩn phải đạt.",
            });
        });
    }

    /// <summary>
    /// Hiệu suất lane theo hero và theo vị trí.
    ///
    /// CỐ TÌNH KHÔNG làm thống kê cặp hero khắc chế nhau ở lane, dù đó là câu hỏi hay hơn.
    /// Lý do: khoảng 1100 ván × 3 lane chia cho hơn 120 hero thì gần như mọi cặp có mẫu dưới 5.
    /// Bảng đó sẽ hiện ra rất thuyết phục và hoàn toàn là nhiễu.
    /// </summary>
    private static void MapLanes(RouteGroupBuilder api)
    {
        api.MapGet("/lanes", async (
            Ti2026DbContext db, int? role = null, string? patch = null, int minGames = 8) =>
        {
            var currentPatch = patch ?? await CurrentPatchAsync(db);

            var rows = await db.MatchPlayers
                .Where(mp => mp.LaneRole != null && mp.LaneEfficiencyPct != null)
                .Where(mp => currentPatch == null || mp.Match!.PatchVersion == currentPatch)
                .Where(mp => role == null || mp.LaneRole == role)
                .Select(mp => new
                {
                    mp.HeroId,
                    Role = mp.LaneRole!.Value,
                    Efficiency = mp.LaneEfficiencyPct!.Value,
                    Won = mp.IsRadiant == mp.Match!.RadiantWin,
                })
                .ToListAsync();

            if (rows.Count == 0)
                return Results.Ok(new
                {
                    patch = PatchIndex.Name(currentPatch),
                    lanes = Array.Empty<object>(),
                    note = "Chưa có ván nào nạp được chỉ số lane. Cần nạp lại match detail.",
                });

            var heroNames = await db.Heroes
                .ToDictionaryAsync(h => h.Id, h => new { h.LocalizedName, h.Name });

            // Mốc so sánh của TỪNG vị trí. Không có mốc thì "hiệu suất lane 62%" là con số
            // trống rỗng — người đọc không biết đó là tốt hay tệ.
            var baselines = rows
                .GroupBy(r => r.Role)
                .ToDictionary(g => g.Key, g => DistributionStats.Describe(
                    g.Select(x => x.Efficiency).ToList()));

            var lanes = rows
                .GroupBy(r => new { r.HeroId, r.Role })
                .Where(g => g.Count() >= minGames)
                .Select(g =>
                {
                    var dist = DistributionStats.Describe(g.Select(x => x.Efficiency).ToList());
                    heroNames.TryGetValue(g.Key.HeroId, out var meta);
                    var baseline = baselines[g.Key.Role].Median;

                    return new
                    {
                        heroId = g.Key.HeroId,
                        name = meta?.LocalizedName ?? meta?.Name ?? $"hero {g.Key.HeroId}",
                        image = DotaImages.Hero(meta?.Name),

                        role = g.Key.Role,
                        roleName = RoleName(g.Key.Role),
                        games = g.Count(),

                        medianEfficiency = Math.Round(dist.Median, 1),
                        p25 = Math.Round(dist.P25, 1),
                        p75 = Math.Round(dist.P75, 1),

                        // Hơn/kém mốc trung vị của chính vị trí đó — đây mới là con số đọc được
                        vsBaseline = Math.Round(dist.Median - baseline, 1),

                        winrate = Math.Round(g.Count(x => x.Won) * 100.0 / g.Count(), 1),
                    };
                })
                .OrderByDescending(x => x.vsBaseline)
                .ToList();

            return Results.Ok(new
            {
                patch = PatchIndex.Name(currentPatch),
                minGames,
                baselines = baselines.OrderBy(kv => kv.Key).Select(kv => new
                {
                    role = kv.Key,
                    roleName = RoleName(kv.Key),
                    medianEfficiency = Math.Round(kv.Value.Median, 1),
                    samples = kv.Value.Count,
                }),
                lanes,
                note = "Hiệu suất lane là thước đo của OpenDota: phần trăm lượng vàng/kinh "
                     + "nghiệm/lính đạt được so với mức lý tưởng của lane đó. vsBaseline là "
                     + "hơn hoặc kém trung vị CỦA CHÍNH VỊ TRÍ đó — so hero đi mid với hero đi "
                     + "hỗ trợ bằng con số tuyệt đối là so hai thứ khác nhau.",
                caveat = "Không có bảng cặp hero khắc chế nhau ở lane, dù đó là câu hỏi hay hơn: "
                       + "chia hơn 1000 ván cho ba lane và hơn 120 hero thì gần như mọi cặp có "
                       + "mẫu dưới 5, và bảng đó sẽ trông thuyết phục trong khi hoàn toàn là nhiễu.",
            });
        });
    }

    /// <summary>
    /// So hero pool của MỘT người chơi với cách pro đối xử với cùng những hero đó.
    ///
    /// Đây là endpoint duy nhất gọi ra nguồn ngoài theo yêu cầu của người dùng, nên nó có trần
    /// và có bộ nhớ đệm — xem <see cref="PlayerLookup"/>. Không lưu gì xuống DB.
    /// </summary>
    private static void MapMe(RouteGroupBuilder api)
    {
        api.MapGet("/me", async (
            Ti2026DbContext db, PlayerLookup lookup, OpenDotaClient client, string? id) =>
        {
            var accountId = PlayerLookup.ParseAccountId(id);
            if (accountId is null)
                return Results.BadRequest(new
                {
                    error = "Cần tham số id là Dota account ID (ví dụ 86745912) hoặc Steam ID64 "
                          + "(ví dụ 76561198047011640).",
                });

            if (!lookup.TryGetCached(accountId.Value, out var snapshot))
            {
                if (!lookup.TryTakeBudget())
                    return Results.Json(new
                    {
                        error = "Đang tạm hết suất tra cứu. Trang này gọi trực tiếp sang OpenDota "
                              + "nên phải giữ trần — bị chặn IP thì cả ứng dụng mất nguồn dữ liệu, "
                              + "không chỉ một lần tra. Thử lại sau vài phút.",
                        retryAfterSeconds = (int)PlayerLookup.Window.TotalSeconds,
                    }, statusCode: StatusCodes.Status429TooManyRequests);

                snapshot = await lookup.FetchAsync(client, accountId.Value, CancellationToken.None);
            }

            if (snapshot is null)
                return Results.NotFound(new
                {
                    error = "Không tra được hồ sơ này. Thường là do hồ sơ để riêng tư — trong "
                          + "Dota 2 cần bật Cài đặt → Tuỳ chọn → Hiển thị dữ liệu trận công khai, "
                          + "rồi đợi OpenDota cập nhật.",
                });

            var currentPatch = await CurrentPatchAsync(db);

            var draftMatchIds = await db.Matches
                .Where(m => currentPatch == null || m.PatchVersion == currentPatch)
                .Where(m => db.DraftEvents.Any(d => d.MatchId == m.Id))
                .Select(m => m.Id)
                .ToListAsync();

            var proEvents = await db.DraftEvents
                .Where(d => draftMatchIds.Contains(d.MatchId))
                .Select(d => new { d.HeroId, d.IsPick })
                .ToListAsync();

            var proByHero = proEvents
                .GroupBy(e => e.HeroId)
                .ToDictionary(g => g.Key, g => new
                {
                    Appearances = g.Count(),
                    Picks = g.Count(x => x.IsPick),
                    Bans = g.Count(x => !x.IsPick),
                });

            var heroNames = await db.Heroes
                .ToDictionaryAsync(h => h.Id, h => new { h.LocalizedName, h.Name });

            var proTotal = draftMatchIds.Count;

            var mine = snapshot.Heroes
                .OrderByDescending(h => h.Games)
                .Take(20)
                .Select(h =>
                {
                    heroNames.TryGetValue(h.HeroId, out var meta);
                    proByHero.TryGetValue(h.HeroId, out var pro);

                    var proContest = pro is null || proTotal == 0
                        ? (double?)null
                        : Math.Round(pro.Appearances * 100.0 / proTotal, 1);

                    return new
                    {
                        heroId = h.HeroId,
                        name = meta?.LocalizedName ?? meta?.Name ?? $"hero {h.HeroId}",
                        image = DotaImages.Hero(meta?.Name),

                        myGames = h.Games,
                        myWinrate = Math.Round(h.Wins * 100.0 / h.Games, 1),

                        proContestRate = proContest,
                        proPicks = pro?.Picks,
                        proBans = pro?.Bans,

                        // Câu chuyện của từng dòng: hero bạn hay chơi mà pro cũng coi trọng thì
                        // đáng đầu tư thêm; hero bạn hay chơi mà pro đã bỏ hẳn thì công bạn bỏ
                        // ra đang chảy vào một lối chơi bản này không còn thưởng cho nữa.
                        verdict = proContest switch
                        {
                            null => "pro không dùng ở bản này",
                            >= 30 => "pro cũng coi trọng",
                            >= 10 => "pro dùng vừa phải",
                            _ => "pro gần như đã bỏ",
                        },
                    };
                })
                .ToList();

            return Results.Ok(new
            {
                accountId = snapshot.AccountId,
                name = snapshot.Name,
                avatar = snapshot.Avatar,
                patch = PatchIndex.Name(currentPatch),
                proMatches = proTotal,
                heroes = mine,

                privacy = "Chỉ đọc hồ sơ công khai theo id bạn tự nhập, và KHÔNG lưu xuống cơ sở "
                        + $"dữ liệu. Kết quả chỉ nằm trong bộ nhớ đệm {PlayerLookup.CacheTtl.TotalMinutes:0} phút.",
                caveat = "So sánh này chỉ nói về ĐỘ HỢP THỜI của hero, không nói bạn chơi hay hay "
                       + "dở. Winrate pub và winrate chuyên nghiệp không cùng thang: pro đánh "
                       + "Captains Mode với 5 người phối hợp, còn hero mạnh ở pub thường là hero "
                       + "tự chơi được một mình.",
            });
        });
    }

    private static string RoleName(int role) => role switch
    {
        1 => "Lane an toàn",
        2 => "Mid",
        3 => "Lane khó",
        4 => "Rừng",
        _ => $"vị trí {role}",
    };

    /// <summary>Bản game mới nhất có trong dữ liệu, dạng chuỗi như đang lưu ở Match.PatchVersion.</summary>
    private static async Task<string?> CurrentPatchAsync(Ti2026DbContext db)
    {
        var patches = await db.Matches
            .Where(m => m.PatchVersion != null)
            .Select(m => m.PatchVersion!)
            .Distinct()
            .ToListAsync();

        return patches
            .Select(p => new { Raw = p, Id = PatchIndex.Parse(p) })
            .Where(x => x.Id is not null)
            .OrderByDescending(x => x.Id)
            .Select(x => x.Raw)
            .FirstOrDefault();
    }

    /// <summary>
    /// Dự phòng khi bảng Items chưa có khoá đó: "black_king_bar" -> "Black King Bar".
    ///
    /// Bản đầu viết hoa mọi từ dài không quá 2 chữ cái, và cho ra "Ring OF Basilius" — lý do
    /// tên hiển thị phải lấy từ nguồn chứ không tự suy. Ở đây giữ lại chỉ để không hiện khoá
    /// kỹ thuật trần trụi khi bảng Items còn thiếu.
    /// </summary>
    private static readonly HashSet<string> LowercaseWords =
        new(StringComparer.OrdinalIgnoreCase) { "of", "the", "and" };

    private static string PrettyItemName(string key) =>
        string.Join(' ', key.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select((w, i) => i > 0 && LowercaseWords.Contains(w)
                ? w.ToLowerInvariant()
                : char.ToUpperInvariant(w[0]) + w[1..]));
}
