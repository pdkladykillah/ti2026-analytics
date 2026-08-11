namespace Ti2026.Ingest.Analytics;

/// <summary>Một ván đã parse, rút gọn còn phần giai đoạn lane.</summary>
public readonly record struct LaneGame(
    bool Won, string Role, string RoleLabel,
    int? Efficiency, int? Adv10, int? Adv20, int? Adv30);

/// <param name="Efficiency">Hiệu suất lane trung vị, phần trăm.</param>
public readonly record struct LaneSide(
    string Outcome, int Games, int Efficiency, int? Adv10, int? Adv20, int? Adv30);

public readonly record struct LaneRoleRow(string Role, string Label, int Games, int Efficiency);

/// <param name="Verdict">thua-tu-lane | mat-ve-sau | deu | khong-du-du-lieu</param>
public readonly record struct LaneReading(
    int Games, string Verdict, string Text, List<LaneSide> Sides, List<LaneRoleRow> ByRole);

/// <summary>
/// GIAI ĐOẠN LANE — và vì sao phần này sạch hơn mọi chỉ số khác trên trang.
///
/// Mọi thứ khác ở đây đo lúc ván đã kết thúc, nên so giữa ván thắng và ván thua luôn vướng vòng
/// nhân quả: thắng thì chỉ số nào cũng đẹp, và cả buổi làm việc này đã có bốn lần một kết luận
/// bị lật vì đúng chuyện đó.
///
/// Hiệu suất lane và chênh lệch vàng ở phút 10 thì được đo TRƯỚC KHI ván ngã ngũ. Chênh lệch
/// giữa ván thắng và ván thua ở mốc đó vì thế nói được điều thật, và nó trả lời một câu rất cụ
/// thể: bạn thua TỪ LANE, hay thắng lane rồi mất về sau?
///
/// Ba mốc 10/20/30 phút biến câu trả lời thành một hình: nếu phút 10 hai bên gần nhau mà phút 20
/// đã tụt hẳn thì vấn đề nằm ở đoạn giữa, không nằm ở lane.
///
/// GIỚI HẠN: chỉ có ở ván đã parse — replay Valve hết hạn sau khoảng hai tháng, nên đây luôn là
/// mẫu nhỏ nhất trang. Và lane_efficiency_pct của OpenDota cũng là một ước lượng, không phải một
/// phép đo tuyệt đối.
/// </summary>
public static class LanePhase
{
    /// <summary>Mỗi nhóm kết quả cần ngần này ván thì phép so mới đáng làm.</summary>
    public const int MinGamesPerSide = 40;

    /// <summary>Vai trò cần ngần này ván mới đứng riêng một dòng.</summary>
    public const int MinGamesPerRole = 30;

    /// <summary>Chênh hiệu suất lane từ ngần này điểm mới gọi là thua từ lane.</summary>
    public const int LaneGapPoints = 5;

    /// <summary>Chênh vàng ở phút 10 từ ngần này mới coi là đã lệch ngay từ lane.</summary>
    public const int LaneGapGold = 1500;

    public static LaneReading? Read(IReadOnlyList<LaneGame> games)
    {
        var g = games.Where(x => x.Efficiency is int).ToList();
        if (g.Count == 0) return null;

        var win = g.Where(x => x.Won).ToList();
        var loss = g.Where(x => !x.Won).ToList();

        if (win.Count < MinGamesPerSide || loss.Count < MinGamesPerSide)
            return new LaneReading(g.Count, "khong-du-du-lieu",
                $"Mới {g.Count} ván đã parse, chưa đủ {MinGamesPerSide} ván mỗi bên để so.",
                [], []);

        var sides = new List<LaneSide> { Side("thắng", win), Side("thua", loss) };

        var effGap = sides[0].Efficiency - sides[1].Efficiency;
        var goldGap = (sides[0].Adv10 ?? 0) - (sides[1].Adv10 ?? 0);

        var verdict = effGap >= LaneGapPoints || goldGap >= LaneGapGold
            ? "thua-tu-lane"
            : "mat-ve-sau";

        return new LaneReading(g.Count, verdict,
            Describe(verdict, sides, effGap, goldGap), sides, ByRole(g));
    }

    private static LaneSide Side(string label, List<LaneGame> g) =>
        new(label, g.Count,
            Med(g, x => x.Efficiency), Med2(g, x => x.Adv10),
            Med2(g, x => x.Adv20), Med2(g, x => x.Adv30));

    private static List<LaneRoleRow> ByRole(List<LaneGame> g) =>
        g.Where(x => !string.IsNullOrEmpty(x.Role))
            .GroupBy(x => (x.Role, x.RoleLabel))
            .Where(b => b.Count() >= MinGamesPerRole)
            .Select(b => new LaneRoleRow(b.Key.Role, b.Key.RoleLabel, b.Count(),
                Med(b.ToList(), x => x.Efficiency)))
            .OrderByDescending(r => r.Games)
            .ToList();

    private static string Describe(string verdict, List<LaneSide> s, int effGap, int goldGap)
    {
        var w = s[0];
        var l = s[1];

        var shape =
            $"Ván THẮNG: hiệu suất lane {w.Efficiency}%, chênh vàng phút 10 là "
            + $"{Sign(w.Adv10)}, phút 20 {Sign(w.Adv20)}, phút 30 {Sign(w.Adv30)}. "
            + $"Ván THUA: {l.Efficiency}%, {Sign(l.Adv10)} / {Sign(l.Adv20)} / {Sign(l.Adv30)}.";

        var read = verdict == "thua-tu-lane"
            ? $" Ván thua của bạn đã lệch ngay từ lane — chênh {effGap} điểm hiệu suất và "
              + $"{goldGap:N0} vàng ở phút 10. Chỗ đáng luyện là mười phút đầu."
            : " Ở phút 10 ván thắng và ván thua của bạn gần như giống nhau — tức bạn KHÔNG thua "
              + "từ lane. Chênh lệch mở ra ở đoạn sau, nên chỗ đáng luyện là chuyển giai đoạn và "
              + "quyết định giữa trận, không phải kỹ năng lane.";

        return shape + read;
    }

    private static string Sign(int? v) => v is null ? "—" : (v >= 0 ? "+" : "") + v.Value.ToString("N0");

    private static int Med(List<LaneGame> g, Func<LaneGame, int?> pick)
    {
        var v = g.Select(pick).OfType<int>().OrderBy(x => x).ToList();
        return v.Count == 0 ? 0 : Middle(v);
    }

    private static int? Med2(List<LaneGame> g, Func<LaneGame, int?> pick)
    {
        var v = g.Select(pick).OfType<int>().OrderBy(x => x).ToList();
        return v.Count < MinGamesPerSide / 2 ? null : Middle(v);
    }

    private static int Middle(List<int> s) =>
        s.Count % 2 == 1 ? s[s.Count / 2]
            : (int)Math.Round((s[s.Count / 2 - 1] + s[s.Count / 2]) / 2.0);
}
