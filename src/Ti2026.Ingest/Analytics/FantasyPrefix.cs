namespace Ti2026.Ingest.Analytics;

/// <summary>Một người trong đội hình: điểm banner, và tỷ lệ hero theo từng nhóm màu.</summary>
public readonly record struct PrefixPlayer(
    string Nick, double Points, IReadOnlyDictionary<string, double>? PercentByPrefix);

public readonly record struct PrefixOption(
    string Key, double BonusPercent, double ExpectedPoints, double ExpectedPercentOfTotal,
    int PlayersCovered, int PlayersTotal);

/// <summary>
/// Chọn prefix cho TOÀN BỘ đội hình.
///
/// Prefix chỉ ăn khi người đó chơi hero đúng nhóm, nên giá trị của nó không nằm ở con số thưởng
/// mà ở tích <c>thưởng × tỷ lệ chơi nhóm hero đó</c> — và vì một prefix áp cho cả năm người nên
/// phải cộng theo ĐIỂM, không phải theo tỷ lệ trung bình. Một prefix hợp với người ghi 5000
/// điểm đáng hơn hẳn cũng prefix đó hợp với người ghi 800 điểm.
///
/// Điểm phải nói ra: tỷ lệ hero là số ĐẾM TAY chép từ dự án gốc, không phải số ta tự đo.
/// OpenDota không phân loại màu/chủ đề hero, nên phần này không tự cập nhật khi tuyển thủ đổi
/// hero pool — khác hẳn suffix vốn đo được từ ván thật.
/// </summary>
public static class FantasyPrefix
{
    public static List<PrefixOption> Rank(
        IReadOnlyList<PrefixPlayer> players, IReadOnlyDictionary<string, double> bonuses)
    {
        var total = players.Sum(p => p.Points);

        return bonuses
            .Select(kv =>
            {
                // Chỉ cộng người CÓ dữ liệu. Người thiếu bị bỏ qua chứ không tính tỷ lệ 0 —
                // tính 0 thì một đội hình chưa có dữ liệu trông y hệt một đội hình toàn người
                // không bao giờ chơi nhóm hero đó.
                var covered = players.Where(p => p.PercentByPrefix is not null).ToList();

                var gain = covered.Sum(p =>
                    p.Points * kv.Value / 100
                    * p.PercentByPrefix!.GetValueOrDefault(kv.Key) / 100);

                return new PrefixOption(
                    kv.Key,
                    kv.Value,
                    Math.Round(gain, 2),
                    total <= 0 ? 0 : Math.Round(gain / total * 100, 3),
                    covered.Count,
                    players.Count);
            })
            .OrderByDescending(x => x.ExpectedPoints)
            .ToList();
    }
}
