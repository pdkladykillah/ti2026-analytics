namespace Ti2026.Ingest.Analytics;

/// <summary>Một emblem ĐÃ QUAY RA: chỉ số nào, tier mấy, trait gì.</summary>
public readonly record struct Emblem(string StatKey, string Tier, string Trait);

public readonly record struct EmblemScore(
    int Slot, string StatKey, string Tier, string Trait,
    double BaseValue, double TierBonusPercent, double TraitFactor, double Factor, double Points);

/// <summary>
/// Tier và trait của emblem.
///
/// Cả hai là thứ QUAY TRÚNG chứ không phải thứ chọn được, nên tệp này là một máy tính: đưa vào
/// bộ emblem đã quay ra, trả về điểm. Nó không đi tìm bộ emblem tốt nhất, vì bộ tốt nhất không
/// phải một lựa chọn có thật.
///
/// Điểm mấu chốt khiến trait không gộp được vào tầng banner: <b>benevolent và vampiric tác động
/// sang Ô KỀ BÊN</b>. Vì thế không còn tính từng ô độc lập được nữa — phải xét cả banner cùng
/// lúc, và thứ tự các ô trở nên có ý nghĩa.
///
/// Ngữ nghĩa lấy đúng theo bản luật gốc (lib/optimizer.ts của dự án dota2-fantasy-optimizer-2026),
/// gồm cả những chi tiết dễ đoán sai:
///
///   fractal     ×1,6 cho CHÍNH ô đó, chỉ khi MỌI tier trên banner đều khác nhau
///   benevolent  ×1,2 cho các ô KỀ BÊN — KHÔNG cộng gì cho chính nó
///   vampiric    ×1,5 cho chính nó VÀ ×0,9 cho các ô kề bên
///   unique      ×1,3 chỉ khi trên banner có ĐÚNG MỘT emblem unique
///   friendly    ×1,5 chỉ khi trên banner có TỪ BA emblem friendly trở lên
///
/// Hai chỗ hay hiểu nhầm nhất: benevolent không tự cộng cho mình, và unique mất tác dụng khi
/// có emblem unique thứ hai — tức là nó tự vô hiệu hoá lẫn nhau.
/// </summary>
public static class FantasyTraits
{
    public static readonly IReadOnlyDictionary<string, double> TierBonusPercent =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["I"] = 10, ["II"] = 30, ["III"] = 60, ["IV"] = 100, ["V"] = 150,
        };

    public const double FractalFactor = 1.6;
    public const double BenevolentNeighbour = 1.2;
    public const double VampiricSelf = 1.5;
    public const double VampiricNeighbour = 0.9;
    public const double UniqueFactor = 1.3;
    public const double FriendlyFactor = 1.5;
    public const int FriendlyMinimum = 3;

    /// <summary>Tier lạ hoặc bỏ trống thì coi là I — mức thấp nhất, không phải mức không có.</summary>
    public static string NormalizeTier(string? tier)
    {
        var key = (tier ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return TierBonusPercent.ContainsKey(key) ? key.ToUpperInvariant() : "I";
    }

    private static bool Adjacent(int a, int b) => Math.Abs(a - b) == 1;

    private static bool Is(string? trait, string name) =>
        string.Equals(trait, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Hệ số trait của từng ô. Tách riêng khỏi phép tính điểm vì đây là phần duy nhất mà các ô
    /// ảnh hưởng lẫn nhau — muốn kiểm được thì phải nhìn thấy nó một mình.
    /// </summary>
    public static double[] TraitFactors(IReadOnlyList<Emblem> emblems)
    {
        var factors = Enumerable.Repeat(1.0, emblems.Count).ToArray();

        var tiers = emblems.Select(e => NormalizeTier(e.Tier)).ToList();
        var allTiersDifferent = tiers.Distinct(StringComparer.OrdinalIgnoreCase).Count() == emblems.Count;

        var uniqueCount = emblems.Count(e => Is(e.Trait, "unique"));
        var friendlyCount = emblems.Count(e => Is(e.Trait, "friendly"));

        for (var i = 0; i < emblems.Count; i++)
        {
            var trait = emblems[i].Trait;

            if (Is(trait, "fractal") && allTiersDifferent) factors[i] *= FractalFactor;
            if (Is(trait, "unique") && uniqueCount == 1) factors[i] *= UniqueFactor;
            if (Is(trait, "friendly") && friendlyCount >= FriendlyMinimum) factors[i] *= FriendlyFactor;

            // Benevolent KHÔNG cộng cho chính nó — chỉ cho hàng xóm.
            if (Is(trait, "benevolent"))
            {
                for (var j = 0; j < emblems.Count; j++)
                    if (Adjacent(i, j)) factors[j] *= BenevolentNeighbour;
            }

            if (Is(trait, "vampiric"))
            {
                factors[i] *= VampiricSelf;
                for (var j = 0; j < emblems.Count; j++)
                    if (Adjacent(i, j)) factors[j] *= VampiricNeighbour;
            }
        }

        return factors;
    }

    /// <summary>
    /// Điểm từng ô: <c>điểm gốc × (1 + tier%) × hệ số trait</c>.
    /// Trait nhân SAU tier, đúng theo cách diễn đạt của bảng luật.
    /// </summary>
    public static List<EmblemScore> Score(
        IReadOnlyList<Emblem> emblems, IReadOnlyDictionary<string, double?> statPoints)
    {
        var traitFactors = TraitFactors(emblems);
        var result = new List<EmblemScore>(emblems.Count);

        for (var i = 0; i < emblems.Count; i++)
        {
            var tier = NormalizeTier(emblems[i].Tier);
            var bonus = TierBonusPercent[tier];

            // Chưa đo được chỉ số thì điểm gốc là 0 — nhưng ô vẫn hiện ra để người đọc thấy
            // mình đang đặt emblem lên một chỗ chưa có dữ liệu.
            var baseValue = statPoints.GetValueOrDefault(emblems[i].StatKey) ?? 0;

            var factor = (1 + bonus / 100) * traitFactors[i];

            result.Add(new EmblemScore(
                i, emblems[i].StatKey, tier, emblems[i].Trait ?? "none",
                Math.Round(baseValue, 2), bonus, Math.Round(traitFactors[i], 4),
                Math.Round(factor, 4), Math.Round(baseValue * factor, 2)));
        }

        return result;
    }

    public static double Total(
        IReadOnlyList<Emblem> emblems, IReadOnlyDictionary<string, double?> statPoints) =>
        Math.Round(Score(emblems, statPoints).Sum(x => x.Points), 2);
}
