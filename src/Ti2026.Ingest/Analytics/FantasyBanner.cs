namespace Ti2026.Ingest.Analytics;

public readonly record struct EmblemPick(
    int Slot, string Color, string StatKey, string StatLabel, double BasePoints);

public sealed record BannerResult(
    IReadOnlyList<EmblemPick> Picks,
    double BasePoints,
    double TierIPoints,
    double TierVPoints,
    IReadOnlyList<string> EmptySlots);

/// <summary>
/// Banner emblem — và đây là chỗ sửa một hiểu nhầm GỐC về cách tính điểm fantasy.
///
/// Bản trước cộng cả 18 chỉ số cho mọi người. Luật thật thì KHÔNG: mỗi vị trí có một banner ba
/// ô (vòng bảng), mỗi ô mang một MÀU cố định, và người chơi chỉ ăn điểm ở những chỉ số nằm trên
/// emblem của mình. Tức là mỗi người chỉ ghi điểm bằng BA chỉ số, không phải mười tám.
///
///     core     đỏ · lá · đỏ
///     mid      đỏ · dương · lá
///     hỗ trợ   dương · lá · dương
///
/// Hậu quả của việc cộng cả 18: con số bị thổi lên gấp nhiều lần, và tệ hơn là THỨ HẠNG sai —
/// một người giỏi đều tám chỉ số được cộng hết tám, trong khi luật thật chỉ cho anh ta ba ô.
/// Người thắng thật là người có ba chỉ số ĐÚNG MÀU cao nhất, không phải người giỏi đều.
///
/// Chọn tối ưu ở đây là chính xác chứ không phải xấp xỉ: các ô cùng màu hoàn toàn thay thế được
/// cho nhau, nên với mỗi màu chỉ cần lấy k chỉ số điểm cao nhất của màu đó, với k là số ô mang
/// màu ấy. Không cần thuật toán ghép cặp.
/// </summary>
public static class FantasyBanner
{
    /// <summary>Tier I là +10%, tier V là +150%. Mọi emblem đều CÓ tier, nên không có mức ×1.</summary>
    public const double TierIMultiplier = 1.10;
    public const double TierVMultiplier = 2.50;

    /// <summary>
    /// Điểm mỗi ô là điểm trung bình của chỉ số đó, KHÔNG nhân tier. Tier là thứ quay trúng chứ
    /// không phải thứ chọn được, nên gộp sẵn vào một con số duy nhất sẽ biến may mắn thành
    /// năng lực. Ở đây trả cả ba mức để người đọc tự thấy khoảng dao động.
    /// </summary>
    public static BannerResult Build(
        IReadOnlyList<string> slotColors,
        IReadOnlyDictionary<string, double?> statPoints,
        IReadOnlyList<FantasyStat> stats)
    {
        var byKey = stats.ToDictionary(s => s.Key);
        var picks = new List<EmblemPick>();
        var empty = new List<string>();

        // Các ô cùng màu thay thế được cho nhau, nên giải theo từng màu là ra đáp án tối ưu.
        foreach (var group in slotColors
                     .Select((color, index) => (color, index))
                     .GroupBy(x => x.color))
        {
            var slots = group.OrderBy(x => x.index).ToList();

            var best = stats
                .Where(s => string.Equals(s.Color, group.Key, StringComparison.OrdinalIgnoreCase))
                .Select(s => (Stat: s, Points: statPoints.GetValueOrDefault(s.Key)))
                .Where(x => x.Points is not null)
                .OrderByDescending(x => x.Points)
                .Take(slots.Count)
                .ToList();

            for (var i = 0; i < slots.Count; i++)
            {
                if (i >= best.Count)
                {
                    // Chưa đo được chỉ số nào của màu này thì để TRỐNG. Điền 0 vào sẽ biến
                    // "chưa biết" thành "ô này không đáng điểm nào", và tổng vẫn trông hợp lệ.
                    empty.Add(group.Key);
                    continue;
                }

                var (stat, points) = best[i];
                picks.Add(new EmblemPick(
                    slots[i].index, group.Key, stat.Key,
                    byKey.TryGetValue(stat.Key, out var s) ? s.Label : stat.Key,
                    Math.Round(points!.Value, 2)));
            }
        }

        var total = picks.Sum(p => p.BasePoints);

        return new BannerResult(
            picks.OrderBy(p => p.Slot).ToList(),
            Math.Round(total, 2),
            Math.Round(total * TierIMultiplier, 2),
            Math.Round(total * TierVMultiplier, 2),
            empty);
    }

    /// <summary>Vị trí 1..5 về nhóm banner. Trả null khi chưa suy được vị trí.</summary>
    public static string? GroupOf(int? position) => position switch
    {
        1 or 3 => "core",
        2 => "mid",
        4 or 5 => "support",
        _ => null,
    };
}
