namespace Ti2026.Ingest.Analytics;

/// <summary>Một cặp đấu đã lên lịch, đọc từ schedule.json.</summary>
/// <param name="Format">Bo1 | Bo2 | Bo3 | Bo5 — null nếu chưa biết.</param>
public readonly record struct Fixture(
    DateTime StartsAt, string Stage, string SlugA, string SlugB, string? Format);

/// <summary>Một ván đã đá, lấy từ OpenDota.</summary>
public readonly record struct PlayedGame(
    long MatchId, long? SeriesId, DateTime StartTime, string SlugA, string SlugB, bool AWon);

/// <param name="Status">sap-toi | dang-dien-ra | da-xong | cho-ket-qua</param>
public readonly record struct ScheduleRow(
    DateTime StartsAt, string Stage, string SlugA, string SlugB, string? Format,
    string Status, int WinsA, int WinsB, int GamesPlayed, int? Target, string Text);

/// <summary>
/// Ghép LỊCH biên tập với KẾT QUẢ đo được, để lịch tự điền kết quả khi trận đã đá xong.
///
/// VÌ SAO PHẢI TỰ VIẾT LỊCH. OpenDota chỉ có trận ĐÃ ĐÁ — không có endpoint nào trả về trận
/// sắp diễn ra, và giải chính TI2026 thậm chí còn chưa có trong danh mục giải của họ (mới chỉ
/// có 5 giải vòng loại khu vực). Liquipedia có lịch nhưng không kết nối được từ máy chủ. Nên
/// phần LỊCH là dữ liệu biên tập, còn phần KẾT QUẢ thì đo được và không nên nhập tay.
///
/// VÌ SAO KHÔNG NHẬP KẾT QUẢ TAY. Nhập tay thì tỷ số trên trang và tỷ số trong DB là hai nguồn
/// sự thật, và chúng sẽ lệch nhau — thường vào đúng lúc giải đang diễn ra và không ai rảnh để
/// đối chiếu. Ở đây lịch chỉ nói AI ĐÁ VỚI AI LÚC NÀO; mọi con số đều đọc từ ván thật.
/// </summary>
public static class ScheduleBuilder
{
    /// <summary>
    /// Cửa sổ nhận ván về một cặp đấu, tính từ giờ ghi trong lịch.
    ///
    /// Lùi 2 giờ vì trận có thể bắt đầu sớm hơn giờ công bố; tiến 10 giờ vì một Bo5 kéo dài
    /// cộng thêm trận trước đó bị delay là chuyện thường. Rộng hơn nữa thì bắt đầu nuốt sang
    /// cặp đấu ngày hôm sau của cùng hai đội.
    /// </summary>
    public static readonly TimeSpan LookBack = TimeSpan.FromHours(2);
    public static readonly TimeSpan LookAhead = TimeSpan.FromHours(10);

    /// <summary>Số ván cần thắng để kết thúc loạt. null = không suy ra được từ thể thức.</summary>
    public static int? TargetWins(string? format) => format?.Trim().ToUpperInvariant() switch
    {
        "BO1" => 1,
        "BO3" => 2,
        "BO5" => 3,
        // Bo2 KHÔNG có mốc thắng: đá đủ hai ván rồi thôi, hoà 1-1 là kết quả hợp lệ. Gán mốc 2
        // cho nó thì mọi loạt hoà sẽ mãi mãi hiện là "đang diễn ra".
        _ => null,
    };

    public static ScheduleRow Build(Fixture f, IReadOnlyList<PlayedGame> allGames, DateTime now)
    {
        var window = allGames
            .Where(g => Pair(g.SlugA, g.SlugB) == Pair(f.SlugA, f.SlugB))
            .Where(g => g.StartTime >= f.StartsAt - LookBack && g.StartTime <= f.StartsAt + LookAhead)
            .OrderBy(g => g.StartTime)
            .ToList();

        // Nhiều loạt của cùng hai đội có thể rơi vào một cửa sổ (vòng bảng rồi playoff cùng
        // ngày). Lấy theo SeriesId của ván sớm nhất để không trộn hai loạt thành một tỷ số.
        if (window.Count > 0)
        {
            var seriesId = window[0].SeriesId;
            if (seriesId is long s && s > 0)
                window = window.Where(g => g.SeriesId == s).ToList();
        }

        var winsA = window.Count(g => Won(g, f.SlugA));
        var winsB = window.Count - winsA;
        var target = TargetWins(f.Format);

        var decided = target is int t
            ? winsA >= t || winsB >= t
            : f.Format?.Trim().ToUpperInvariant() == "BO2"
                ? window.Count >= 2
                : window.Count > 0 && f.StartsAt + LookAhead < now;

        var status =
            window.Count == 0 && f.StartsAt > now ? "sap-toi"
            : window.Count == 0 ? "cho-ket-qua"
            : decided ? "da-xong"
            : "dang-dien-ra";

        var text = status switch
        {
            "sap-toi" => Countdown(f.StartsAt - now),
            "cho-ket-qua" => "Đã tới giờ nhưng chưa có ván nào được nạp về.",
            "dang-dien-ra" => $"Đang diễn ra — {winsA}–{winsB} sau {window.Count} ván.",
            _ => winsA == winsB
                ? $"Hoà {winsA}–{winsB}."
                : $"{(winsA > winsB ? f.SlugA : f.SlugB)} thắng {Math.Max(winsA, winsB)}–{Math.Min(winsA, winsB)}.",
        };

        return new ScheduleRow(f.StartsAt, f.Stage, f.SlugA, f.SlugB, f.Format,
            status, winsA, winsB, window.Count, target, text);
    }

    /// <summary>
    /// Ván đã đá mà KHÔNG khớp cặp đấu nào trong lịch.
    ///
    /// Phải hiện chứ không được bỏ: khi lịch chưa nhập, hoặc nhập sai giờ, đây là toàn bộ nội
    /// dung của trang. Bỏ đi thì tab trống trơn trong khi dữ liệu đang nằm sẵn trong DB.
    /// </summary>
    public static List<PlayedGame> Unscheduled(
        IReadOnlyList<Fixture> fixtures, IReadOnlyList<PlayedGame> allGames)
    {
        var claimed = new HashSet<long>();

        foreach (var f in fixtures)
        {
            foreach (var g in allGames)
            {
                if (Pair(g.SlugA, g.SlugB) != Pair(f.SlugA, f.SlugB)) continue;
                if (g.StartTime < f.StartsAt - LookBack || g.StartTime > f.StartsAt + LookAhead) continue;
                claimed.Add(g.MatchId);
            }
        }

        return allGames.Where(g => !claimed.Contains(g.MatchId))
            .OrderByDescending(g => g.StartTime)
            .ToList();
    }

    private static bool Won(PlayedGame g, string slug) =>
        g.SlugA == slug ? g.AWon : !g.AWon;

    private static string Pair(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";

    private static string Countdown(TimeSpan left)
    {
        if (left.TotalMinutes < 1) return "Sắp bắt đầu.";
        if (left.TotalHours < 1) return $"Còn {left.TotalMinutes:0} phút.";
        if (left.TotalHours < 24) return $"Còn {left.TotalHours:0} giờ.";
        return $"Còn {left.TotalDays:0} ngày.";
    }
}
