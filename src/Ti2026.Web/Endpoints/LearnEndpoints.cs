using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Analytics;

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
    /// Đồ tiêu hao: mua đi mua lại nên mốc mua không nói lên chiến thuật gì.
    /// Lọc ở lúc ĐỌC, không phải lúc nạp — dữ liệu thô vẫn còn nguyên, và bật lại được bằng
    /// includeConsumables=true. Lọc lúc nạp thì mất hẳn, và danh sách này chắc chắn sẽ lỗi thời.
    /// </summary>
    private static readonly HashSet<string> Consumables = new(StringComparer.OrdinalIgnoreCase)
    {
        "tango", "tango_single", "clarity", "flask", "enchanted_mango", "great_famango",
        "faerie_fire", "tpscroll", "ward_observer", "ward_sentry", "ward_dispenser",
        "smoke_of_deceit", "dust", "blood_grenade", "tome_of_knowledge", "bottle_of_stars",
    };

    public static void MapLearnEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

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
                .ToDictionaryAsync(h => h.Id, h => new { h.LocalizedName, h.Name, h.ImageUrl });

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
                        image = meta?.ImageUrl,

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
            bool includeConsumables = false) =>
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

            var excludedConsumables = includeConsumables
                ? 0
                : firstBuys.Count(x => Consumables.Contains(x.ItemKey));

            var considered = includeConsumables
                ? firstBuys
                : firstBuys.Where(x => !Consumables.Contains(x.ItemKey)).ToList();

            var items = considered
                .GroupBy(x => x.ItemKey)
                .Where(g => g.Count() >= minSamples)
                .Select(g =>
                {
                    var wins = g.Where(x => x.Won).Select(x => (double)x.TimeSeconds).ToList();
                    var losses = g.Where(x => !x.Won).Select(x => (double)x.TimeSeconds).ToList();

                    var all = DistributionStats.Describe(g.Select(x => (double)x.TimeSeconds).ToList());

                    return new
                    {
                        itemKey = g.Key,
                        name = PrettyItemName(g.Key),
                        image = $"https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/items/{g.Key}.png",
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
                items,

                // Nói ra những gì đã bị lọc: một bảng đã cắt bớt mà không khai thì đọc như
                // thể đó là toàn bộ sự thật.
                excludedConsumables,
                consumablesNote = includeConsumables
                    ? "Đang hiển thị cả đồ tiêu hao."
                    : $"Đã ẩn {excludedConsumables} lượt mua đồ tiêu hao (tango, tp, mắt…) vì "
                    + "mua đi mua lại nên mốc mua không nói lên chiến thuật. "
                    + "Thêm includeConsumables=true để xem.",
                caveat = "Mốc của pro giả định có người hỗ trợ nhường lính và không bị bỏ lane. "
                       + "Ở pub chậm hơn 2–4 phút là bình thường; hãy dùng khoảng P25–P75 làm "
                       + "mục tiêu, đừng lấy trung vị làm chuẩn phải đạt.",
            });
        });
    }

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
    /// "black_king_bar" -> "Black King Bar". Chỉ là làm đẹp khoá kỹ thuật, không phải bản dịch
    /// chính thức — đổi lấy việc không phải nạp và đồng bộ thêm một bảng constants nữa.
    /// </summary>
    private static string PrettyItemName(string key) =>
        string.Join(' ', key.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length <= 2 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..]));
}
