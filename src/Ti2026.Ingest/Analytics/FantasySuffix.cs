namespace Ti2026.Ingest.Analytics;

/// <summary>Một ván, rút gọn về đúng những gì cần để xét điều kiện suffix.</summary>
public readonly record struct SuffixGame(
    int DurationSeconds,
    int? FirstBloodSeconds,
    bool PlayerTeamWon,
    bool AnyDeathToTormentor,
    bool IsLastPossibleGameOfSeries);

public readonly record struct SuffixOdds(
    string Key, int Hits, int Sample, double? Probability, double? ExpectedBonusPercent,
    bool Measurable);

/// <summary>
/// Xác suất THẬT của từng suffix, đo từ ván đã chơi.
///
/// Bản luật gốc chỉ dán nhãn định tính — ổn định / hên xui / nên né. Ta có dữ liệu thật nên đo
/// được, và đo thì hơn hẳn dán nhãn: "the Decisive +24%" nghe rất to, nhưng nếu chỉ 6% số ván
/// kết thúc trước phút 25 thì lợi kỳ vọng chỉ là 1,4% — nhỏ hơn "the Underdog +6%" ở một giải
/// mà ai cũng thua gần nửa số ván.
///
/// ĐIỀU QUAN TRỌNG NHẤT phải nói ra: 5 trong 8 suffix là điều kiện BẤT LỢI. Chúng chỉ ăn khi
/// một chuyện xấu xảy ra, nên xác suất cao ở đó không phải tin vui.
/// </summary>
public static class FantasySuffix
{
    public const int DecisiveMaxSeconds = 25 * 60;
    public const int PatientFirstBloodSeconds = 10 * 60;

    /// <summary>
    /// "the Lucky" — thời lượng ván tận cùng bằng chữ số 8.
    ///
    /// Luật không nói rõ tận cùng của cái gì, mà đồng hồ trong game hiện dạng phút:giây nên có
    /// hai cách đọc: chữ số cuối của GIÂY, hoặc chữ số cuối của PHÚT. Cả hai đều cho xác suất
    /// quanh 10%, nhưng khác nhau ở từng ván cụ thể — nên đo cả hai và khai ra là chưa chắc,
    /// thay vì chọn bừa một cách rồi trình bày như thể đã biết.
    /// </summary>
    public static bool LuckyBySecond(int durationSeconds) => durationSeconds % 60 % 10 == 8;

    public static bool LuckyByMinute(int durationSeconds) => durationSeconds / 60 % 10 == 8;

    /// <summary>
    /// Xác suất từng suffix. <paramref name="bonuses"/> là phần trăm thưởng theo bảng luật.
    ///
    /// Suffix không đo được (fountain kill) vẫn trả về một dòng với <c>Measurable = false</c>
    /// chứ không bị bỏ đi lặng lẽ — biến mất khỏi bảng thì người đọc tưởng nó không tồn tại.
    /// </summary>
    public static List<SuffixOdds> Compute(
        IReadOnlyList<SuffixGame> games, IReadOnlyDictionary<string, double> bonuses)
    {
        var n = games.Count;

        SuffixOdds Row(string key, Func<SuffixGame, bool>? test, int? sample = null)
        {
            if (test is null)
                return new SuffixOdds(key, 0, 0, null, null, Measurable: false);

            var size = sample ?? n;
            if (size == 0) return new SuffixOdds(key, 0, 0, null, null, Measurable: true);

            var hits = games.Count(test);
            var p = (double)hits / size;
            var bonus = bonuses.GetValueOrDefault(key);

            return new SuffixOdds(
                key, hits, size, Math.Round(p, 4), Math.Round(p * bonus, 2), Measurable: true);
        }

        // first_blood_time âm nghĩa là first blood xảy ra TRƯỚC tiếng còi khai cuộc — không
        // phải dữ liệu hỏng. Ván chưa parse thì không có mốc này và bị loại khỏi mẫu.
        var withFirstBlood = games.Count(g => g.FirstBloodSeconds is not null);

        return
        [
            Row("underdog", g => !g.PlayerTeamWon),
            Row("decisive", g => g.DurationSeconds < DecisiveMaxSeconds),
            Row("lucky", g => LuckyBySecond(g.DurationSeconds)),
            Row("luckyTheoPhut", g => LuckyByMinute(g.DurationSeconds)),
            Row("clutch", g => g.IsLastPossibleGameOfSeries),
            Row("tormented", g => g.AnyDeathToTormentor),
            Row("patient",
                g => g.FirstBloodSeconds is int fb && fb >= PatientFirstBloodSeconds,
                withFirstBlood),
            Row("flayedTwins",
                g => g.FirstBloodSeconds is int fb2 && fb2 < 0,
                withFirstBlood),

            // Giết ở fountain: OpenDota không lộ toạ độ nơi chết, nên không có cách nào đo.
            Row("cruel", null),
        ];
    }
}
