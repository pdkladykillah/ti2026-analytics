namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Kết quả rút ra từ một match detail, dưới góc nhìn phe Radiant.
/// Mọi field nullable đều mang nghĩa "ván này không có dữ liệu để kết luận", không phải "không".
/// </summary>
public readonly record struct MatchDetailFacts(
    bool? RadiantHadFirstBlood,
    bool? RadiantReachedTenFirst,
    int? FirstBloodTimeSeconds);

/// <summary>
/// Hàm thuần, không I/O. Tách khỏi ingester vì đây là phần logic dễ sai nhất và cần test kỹ:
/// quy sai phe first blood sẽ làm lệch chỉ số của CẢ HAI đội trong ván đó.
/// </summary>
public static class MatchDetailAnalyzer
{
    /// <summary>Ngưỡng "đạt 10 mạng trước" — cùng ý nghĩa với cột f10 trong dữ liệu cũ.</summary>
    public const int TenKillThreshold = 10;

    /// <summary>player_slot 0..127 là Radiant, 128..255 là Dire.</summary>
    public static bool SlotIsRadiant(int playerSlot) => playerSlot < 128;

    public static MatchDetailFacts Analyze(OpenDotaMatchDetail m) => new(
        RadiantHadFirstBlood: FirstBloodSide(m),
        RadiantReachedTenFirst: ReachedTenFirst(m),
        FirstBloodTimeSeconds: m.FirstBloodTime);

    /// <summary>
    /// Giây tới lần hạ Roshan ĐẦU TIÊN.
    ///
    /// Lấy min chứ không lấy phần tử đầu: objectives không được hứa là đã sắp xếp, và một ván
    /// có tới ba, bốn lần hạ Roshan.
    ///
    /// Trả null khi ván không ai hạ Roshan HOẶC khi ván chưa được parse — hai chuyện khác nhau
    /// nhưng cùng dẫn tới "không biết", và gán 0 sẽ tạo ra những ván "hạ Roshan ở giây 0".
    /// </summary>
    public static int? FirstRoshanSeconds(OpenDotaMatchDetail m)
    {
        var times = m.Objectives?
            .Where(o => string.Equals(o.Type, "CHAT_MESSAGE_ROSHAN_KILL",
                StringComparison.OrdinalIgnoreCase))
            .Select(o => o.Time)
            .Where(t => t > 0)
            .ToList();

        return times is { Count: > 0 } ? times.Min() : null;
    }

    /// <summary>
    /// Phe lấy first blood, đọc từ objective CHAT_MESSAGE_FIRSTBLOOD.
    ///
    /// Dùng objectives chứ không suy từ kills_log: objectives ghi thẳng player_slot của người
    /// lấy first blood, còn suy từ mạng sớm nhất trong kills_log sẽ sai khi ván chưa parse
    /// (kills_log rỗng) hoặc khi có hai mạng cùng giây.
    /// </summary>
    private static bool? FirstBloodSide(OpenDotaMatchDetail m)
    {
        var fb = m.Objectives?.FirstOrDefault(o =>
            string.Equals(o.Type, "CHAT_MESSAGE_FIRSTBLOOD", StringComparison.OrdinalIgnoreCase));

        if (fb?.PlayerSlot is int slot) return SlotIsRadiant(slot);

        // Không có objectives (ván chưa parse) -> không kết luận được, kể cả khi
        // first_blood_time có giá trị: biết LÚC NÀO không có nghĩa là biết AI.
        return null;
    }

    /// <summary>
    /// Phe nào đạt 10 mạng trước, tính bằng cách gộp toàn bộ kills_log theo mốc thời gian.
    ///
    /// Trả null khi ván chưa được parse (không có kills_log) HOẶC khi không phe nào kịp đạt 10
    /// mạng — cả hai đều là "không có câu trả lời", khác hẳn với "phe Dire đạt trước".
    /// </summary>
    private static bool? ReachedTenFirst(OpenDotaMatchDetail m)
    {
        var events = m.Players
            .Where(p => p.KillsLog is { Count: > 0 })
            .SelectMany(p => p.KillsLog!.Select(k => (k.Time, Radiant: p.OnRadiant)))
            .OrderBy(e => e.Time)
            .ToList();

        if (events.Count == 0) return null;

        var radiant = 0;
        var dire = 0;

        foreach (var (_, isRadiant) in events)
        {
            if (isRadiant) radiant++; else dire++;

            if (radiant >= TenKillThreshold) return true;
            if (dire >= TenKillThreshold) return false;
        }

        return null;   // trận kết thúc trước khi phe nào đạt 10 mạng
    }

    /// <summary>Số mạng player hạ được trong N phút đầu; null nếu ván chưa parse.</summary>
    public static int? KillsWithinMinutes(OpenDotaMatchPlayer p, int minutes)
    {
        if (p.KillsLog is null) return null;
        var limit = minutes * 60;
        return p.KillsLog.Count(k => k.Time <= limit);
    }
}
