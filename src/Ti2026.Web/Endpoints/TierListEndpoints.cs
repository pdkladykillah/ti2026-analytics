using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// Tier list TÍNH TỪ DỮ LIỆU, thay cho bảng biên tập đóng băng.
///
/// Bảng cũ là ảnh chụp tay ngày 31/07: khung lấy từ ba tier list công khai, neo bằng winrate pub
/// nhập từ Dotabuff. Nó chính xác hơn về bản game (ghi rõ 7.41e) nhưng đứng yên kể từ ngày nhập,
/// và số liệu trong đó đã lệch — ghi chú của nó nói Shadow Fiend bị cấm nhiều nhất giải (58 lần)
/// trong khi đo được là Lone Druid 244 lần.
///
/// Bảng này ngược lại: tự cập nhật mỗi vòng ingest, và nói rõ mỗi con số đến từ đâu.
/// </summary>
public static class TierListEndpoints
{
    /// <summary>
    /// Trọng số khi gộp pro với pub. Pro nặng hơn vì đây là trang về một giải đấu chuyên
    /// nghiệp — nhưng pub vẫn có tiếng nói, vì mẫu của nó lớn gấp hàng nghìn lần.
    /// Con số này là LỰA CHỌN BIÊN TẬP, không phải kết quả đo. Nói ra để người đọc tự trừ hao.
    /// </summary>
    private const double ProWeight = 0.65;

    /// <summary>Dưới mức này thì tỷ lệ theo vị trí nhảy quá mạnh, không đáng xếp hạng.</summary>
    private const int MinGamesPerPosition = 6;

    /// <summary>
    /// Ngưỡng phân tier theo phân vị. Chọn để S hiếm và C rộng — một bảng mà một phần tư số
    /// hero là "thống trị meta" thì chữ S không còn nghĩa gì.
    /// </summary>
    private const double SCut = 0.92;
    private const double ACut = 0.75;
    private const double BCut = 0.45;

    public static void MapTierListEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tierlist", async (
            Ti2026DbContext db, string? source = null, int? position = null, string? patch = null) =>
        {
            var proOnly = !string.Equals(source, "combined", StringComparison.OrdinalIgnoreCase);

            var currentPatch = patch ?? await CurrentPatchAsync(db);
            if (currentPatch is null)
                return Results.Ok(Empty("Chưa có trận nào biết bản game."));

            var matchIds = await db.Matches
                .Where(m => m.PatchVersion == currentPatch)
                .Select(m => m.Id)
                .ToListAsync();

            if (matchIds.Count == 0)
                return Results.Ok(Empty($"Chưa có ván nào ở bản {PatchIndex.Name(currentPatch)}."));

            // ---------- Phần pro: đo từ chính 16 đội TI ----------
            var draftMatchIds = await db.DraftEvents
                .Where(d => matchIds.Contains(d.MatchId))
                .Select(d => d.MatchId)
                .Distinct()
                .ToListAsync();

            var events = await db.DraftEvents
                .Where(d => matchIds.Contains(d.MatchId))
                .Select(d => new { d.HeroId, d.IsPick, d.Order })
                .ToListAsync();

            var draftTotal = draftMatchIds.Count;

            var contest = events
                .GroupBy(e => e.HeroId)
                .ToDictionary(g => g.Key, g => new
                {
                    Picks = g.Count(x => x.IsPick),
                    Bans = g.Count(x => !x.IsPick),
                    Rate = draftTotal == 0 ? 0 : g.Count() * 100.0 / draftTotal,
                    AvgBanOrder = g.Any(x => !x.IsPick)
                        ? g.Where(x => !x.IsPick).Average(x => x.Order)
                        : (double?)null,
                });

            // ---------- Vị trí: suy từ lane + tài sản ----------
            var players = await db.MatchPlayers
                .Where(mp => matchIds.Contains(mp.MatchId) && mp.LaneRole != null)
                .Select(mp => new
                {
                    mp.MatchId, mp.IsRadiant, mp.LaneRole, mp.NetWorth, mp.HeroId,
                    Won = mp.IsRadiant == mp.Match!.RadiantWin,
                })
                .ToListAsync();

            var byPosition = players
                .GroupBy(p => new { p.MatchId, p.IsRadiant, p.LaneRole })
                .SelectMany(g => PositionInference.InferGroup(g, x => x.LaneRole, x => x.NetWorth))
                .GroupBy(x => new { x.Row.HeroId, x.Position })
                .ToDictionary(
                    g => (g.Key.HeroId, g.Key.Position),
                    g => new { Games = g.Count(), Wins = g.Count(x => x.Row.Won) });

            // ---------- Phần pub: từ heroStats của OpenDota ----------
            var stats = await db.HeroStats.ToDictionaryAsync(s => s.HeroId);
            var heroes = await db.Heroes.ToListAsync();

            // Hero nào được xét: nếu lọc theo vị trí thì phải có đủ mẫu Ở VỊ TRÍ ĐÓ
            var candidates = heroes
                .Select(h => new
                {
                    Hero = h,
                    Contest = contest.GetValueOrDefault(h.Id),
                    Stat = stats.GetValueOrDefault(h.Id),
                    Pos = position is int p ? byPosition.GetValueOrDefault((h.Id, p)) : null,
                })
                .Where(x => position is null
                    ? x.Contest is not null || (proOnly == false && x.Stat is not null)
                    : x.Pos is not null && x.Pos.Games >= MinGamesPerPosition)
                .ToList();

            if (candidates.Count == 0)
                return Results.Ok(Empty(position is null
                    ? "Chưa đủ dữ liệu để xếp hạng."
                    : $"Chưa đủ {MinGamesPerPosition} ván cho vị trí này ở bản hiện tại."));

            // Phân vị thay vì điểm thô: hai thang hoàn toàn khác nhau (tỷ lệ được coi trọng
            // 0–100% và winrate quanh 50%) không cộng thẳng được. Phân vị thì không có đơn vị.
            var proRank = Percentiles(candidates.Select(c => c.Contest?.Rate ?? 0).ToList());
            var pubRank = Percentiles(candidates.Select(c => c.Stat?.HighWinrate ?? 50).ToList());

            var rows = candidates
                .Select((c, i) =>
                {
                    var pro = proRank[i];
                    var pub = pubRank[i];
                    var score = proOnly ? pro : ProWeight * pro + (1 - ProWeight) * pub;

                    return new
                    {
                        heroId = c.Hero.Id,
                        name = c.Hero.LocalizedName ?? c.Hero.Name,
                        image = DotaImages.Hero(c.Hero.Name),

                        tier = Tier(score),
                        score = Math.Round(score * 100, 1),

                        // Mọi thành phần đều lộ ra, để không ai phải tin một con số tổng hợp
                        proContestRate = c.Contest is null ? (double?)null : Math.Round(c.Contest.Rate, 1),
                        proPicks = c.Contest?.Picks,
                        proBans = c.Contest?.Bans,
                        avgBanOrder = c.Contest?.AvgBanOrder is double b ? Math.Round(b, 1) : (double?)null,

                        pubWinrate = c.Stat?.PubWinrate is double pw ? Math.Round(pw, 1) : (double?)null,
                        highWinrate = c.Stat?.HighWinrate is double hw ? Math.Round(hw, 1) : (double?)null,
                        pubPicks = c.Stat?.PubPick,

                        positionGames = c.Pos?.Games,
                        positionWinrate = c.Pos is null || c.Pos.Games == 0
                            ? (double?)null
                            : Math.Round(c.Pos.Wins * 100.0 / c.Pos.Games, 1),
                    };
                })
                .OrderByDescending(r => r.score)
                .ToList();

            return Results.Ok(new
            {
                patch = PatchIndex.Name(currentPatch),
                source = proOnly ? "pro" : "combined",
                position,
                positionName = position is int pn ? PositionInference.Name(pn) : null,
                positionDesc = position is int pd ? PositionInference.Description(pd) : null,

                draftsAnalysed = draftTotal,
                proWeight = proOnly ? 1.0 : ProWeight,
                minGamesPerPosition = MinGamesPerPosition,
                heroes = rows,

                method = proOnly
                    ? "Xếp theo tỷ lệ ĐƯỢC COI TRỌNG ở giải: số ván hero bị cấm hoặc được chọn, "
                    + "chia cho tổng số bàn draft ở bản này. Bị cấm cũng là được coi trọng."
                    : $"Gộp hai nguồn theo phân vị: {ProWeight:P0} tỷ lệ được coi trọng ở giải, "
                    + $"{1 - ProWeight:P0} winrate pub bậc Divine/Immortal. Trọng số là lựa chọn "
                    + "biên tập, không phải kết quả đo.",

                patchNote = proOnly
                    ? $"Chỉ tính ván ở bản {PatchIndex.Name(currentPatch)}. Tự chuyển sang bản mới "
                    + "khi có ván đầu tiên của bản đó."
                    : $"Phần giải chỉ tính bản {PatchIndex.Name(currentPatch)}. Phần pub là cửa sổ "
                    + "gần đây của OpenDota, KHÔNG gắn với một bản cụ thể — nên đừng đọc nó như "
                    + "'đúng bản này'.",

                limitation = "Chỉ số bản game của OpenDota không tách được các bản vá chữ cái: "
                    + "7.41a đến 7.41e đều là 7.41. Và bảng này chỉ có S/A/B/C — không có tier "
                    + "'đặc thù' hay 'tình huống' như bản biên tập cũ, vì 'đòi hỏi kỹ năng cá "
                    + "nhân' là nhận định của con người, dữ liệu không nói ra được.",
            });
        });
    }

    private static object Empty(string note) => new
    {
        heroes = Array.Empty<object>(),
        note,
    };

    private static string Tier(double score) => score switch
    {
        >= SCut => "S",
        >= ACut => "A",
        >= BCut => "B",
        _ => "C",
    };

    /// <summary>
    /// Đổi danh sách giá trị thành phân vị 0–1, giữ nguyên thứ tự đầu vào.
    /// Giá trị bằng nhau nhận cùng phân vị — nếu không thì thứ tự tình cờ của dữ liệu sẽ quyết
    /// định hero nào lên tier trên.
    /// </summary>
    private static List<double> Percentiles(List<double> values)
    {
        if (values.Count <= 1) return values.Select(_ => 1.0).ToList();

        var sorted = values.OrderBy(v => v).ToList();

        return values
            .Select(v =>
            {
                // Số phần tử NHỎ HƠN hẳn, cộng nửa số bằng nhau — cách xử lý hoà chuẩn mực
                var below = sorted.Count(s => s < v);
                var equal = sorted.Count(s => Math.Abs(s - v) < 1e-9);
                return (below + equal / 2.0) / values.Count;
            })
            .ToList();
    }

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
}
