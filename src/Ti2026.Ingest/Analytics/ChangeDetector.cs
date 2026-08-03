namespace Ti2026.Ingest.Analytics;

/// <summary>Giá trị của một chỉ số tại hai thời điểm, cùng ngưỡng để coi là "đáng nói".</summary>
public readonly record struct MetricPair(
    string Key,
    string Label,
    double? Before,
    double? After,
    double NotableDelta,
    bool HigherIsBetter,
    string Unit = "");

public readonly record struct TeamChange(
    string TeamSlug,
    string TeamName,
    string MetricKey,
    string Label,
    double Before,
    double After,
    double Delta,
    bool Improved,
    double Magnitude,
    string Narrative);

/// <summary>
/// So hai lát cắt thời gian và nêu ra biến động đáng chú ý.
///
/// Đây là thứ biến kho snapshot đang tích luỹ mỗi ngày thành câu chuyện đọc được, thay vì chỉ
/// là dữ liệu cho một biểu đồ đường.
///
/// Nguyên tắc quan trọng: PHẢI BIẾT IM LẶNG. Một hệ thống luôn tìm ra "biến động đáng chú ý"
/// mỗi ngày sẽ nhanh chóng bị bỏ qua như mọi thông báo nhiễu. Chỉ nêu khi vượt ngưỡng đặt
/// riêng cho từng chỉ số, và ngưỡng đó phản ánh mức dao động tự nhiên của chính chỉ số ấy —
/// winrate nhích 2 điểm là bình thường, thời lượng lệch 4 phút thì không.
/// </summary>
public static class ChangeDetector
{
    /// <summary>
    /// Ngưỡng chọn theo mức dao động tự nhiên của từng chỉ số, không phải một con số chung.
    /// Dùng chung ngưỡng sẽ vừa bỏ sót thay đổi lớn ở chỉ số ổn định, vừa báo động giả ở chỉ
    /// số vốn nhiễu.
    /// </summary>
    public static IReadOnlyList<MetricPair> Metrics(
        Func<string, double?> before, Func<string, double?> after) =>
    [
        new("winrate",    "Winrate",            before("winrate"),    after("winrate"),    5,   true,  "%"),
        new("killDiff",   "Chênh lệch K–D",     before("killDiff"),   after("killDiff"),   1.5, true),
        new("kills",      "Kills mỗi ván",      before("kills"),      after("kills"),      2,   true),
        new("deaths",     "Deaths mỗi ván",     before("deaths"),     after("deaths"),     2,   false),
        new("firstBlood", "Tỷ lệ first blood",  before("firstBlood"), after("firstBlood"), 8,   true,  "%"),
        new("f10",        "Dẫn 10 kill đầu",    before("f10"),        after("f10"),        8,   true,  "%"),
        new("duration",   "Thời lượng trận",    before("duration"),   after("duration"),   3,   false, "′"),
        new("elo",        "Elo",                before("elo"),        after("elo"),        40,  true),
    ];

    public static List<TeamChange> Detect(
        string teamSlug, string teamName, IReadOnlyList<MetricPair> metrics)
    {
        var changes = new List<TeamChange>();

        foreach (var m in metrics)
        {
            // Thiếu một trong hai đầu thì KHÔNG suy diễn. Đội mới có dữ liệu hôm nay không
            // phải là đội "vừa tăng vọt từ 0".
            if (m.Before is not double b || m.After is not double a) continue;

            var delta = a - b;
            if (Math.Abs(delta) < m.NotableDelta) continue;

            var improved = m.HigherIsBetter ? delta > 0 : delta < 0;

            changes.Add(new TeamChange(
                TeamSlug: teamSlug,
                TeamName: teamName,
                MetricKey: m.Key,
                Label: m.Label,
                Before: Math.Round(b, 2),
                After: Math.Round(a, 2),
                Delta: Math.Round(delta, 2),
                Improved: improved,
                // Chuẩn hoá theo ngưỡng để so được giữa các chỉ số khác đơn vị:
                // "gấp mấy lần mức đáng chú ý"
                Magnitude: Math.Round(Math.Abs(delta) / m.NotableDelta, 2),
                Narrative: Narrate(teamName, m, b, a, delta, improved)));
        }

        return changes;
    }

    private static string Narrate(
        string team, MetricPair m, double before, double after, double delta, bool improved)
    {
        var verb = delta > 0 ? "tăng" : "giảm";
        var toward = delta > 0 ? "lên" : "xuống";
        var judgement = improved ? "" : " — theo hướng bất lợi";

        string Fmt(double v) => m.Unit is "%" or "′"
            ? Math.Round(v).ToString("0")
            : Math.Round(v, 2).ToString("0.##");

        return $"{team}: {m.Label.ToLowerInvariant()} {verb} từ {Fmt(before)}{m.Unit} " +
               $"{toward} {Fmt(after)}{m.Unit}{judgement}.";
    }
}
