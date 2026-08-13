namespace Ti2026.Ingest.Analytics;

/// <summary>Một series đấu trong ngày, rút gọn còn thứ cần để đúc kết.</summary>
public readonly record struct DaySeries(
    int NodeId, string? Name, string? Group,
    string? Team1, string? Team2, string? Slug1, string? Slug2,
    int Wins1, int Wins2, bool Completed, DateTime? At,
    double? Winrate1, double? Winrate2);

/// <summary>Một ván đã đọc được chi tiết.</summary>
public readonly record struct DayMatch(
    long MatchId, int DurationSeconds, string? RadiantTeam, string? DireTeam, bool RadiantWin);

/// <summary>Một lượt cấm hoặc chọn.</summary>
public readonly record struct DayDraft(long MatchId, int HeroId, bool IsPick, bool WonSide);

/// <summary>Một dòng điểm nhấn đã đúc kết.</summary>
/// <param name="Kind">Mã loại, giao diện dựa vào đây để chọn cách vẽ.</param>
/// <param name="Tone">good | bad | flat — dùng cho màu, KHÔNG bao giờ là kênh duy nhất.</param>
public readonly record struct Highlight(string Kind, string Tone, string Text, object? Data);

/// <summary>
/// Đúc kết một ngày thi đấu thành vài câu ĐỌC ĐƯỢC, từ luật đo được chứ không từ văn mẫu.
///
/// RANH GIỚI QUYẾT ĐỊNH MỤC NÀY DÙNG ĐƯỢC HAY KHÔNG. Một ngày Swiss có 8 series. Với cỡ mẫu đó,
/// mọi câu mang tính SUY LUẬN — "đội X đang lên phong độ", "meta nghiêng về hero Y" — đều là
/// nhiễu được phát biểu như kết luận, đúng loại lỗi mà dự án đã dựng cả MultipleTests để tránh
/// và người dùng đã bác đúng một lần.
///
/// Nên ở đây chỉ có hai loại câu, và cả hai đều đúng bất kể cỡ mẫu:
///   MÔ TẢ  — "LGD thắng Falcons 2–1"
///   ĐẾM    — "Muerta bị cấm 7/8 series"
///
/// Loại thứ ba, SO SÁNH GIỮA CÁC NGÀY, mạnh dần theo thời gian và chỉ xuất hiện khi đã có
/// digest của ngày trước. Đó cũng chính là lý do việc lưu lại có giá trị thật.
///
/// Mọi điểm nhấn mang theo con số sinh ra nó. Không có câu nào không truy ngược được về ván.
/// </summary>
public static class DayHighlights
{
    /// <summary>
    /// Chênh winrate tối thiểu để gọi một kết quả là "ngược kèo".
    ///
    /// Dưới ngưỡng này thì hai đội vốn ngang nhau, và gắn nhãn bất ngờ cho một trận 50-50 là
    /// biến tiếng ồn thành câu chuyện. 5 điểm phần trăm là mức mà cửa sổ 180 ngày còn phân biệt
    /// được — hẹp hơn nữa thì chính con số winrate cũng đã nằm trong sai số của nó.
    /// </summary>
    public const double UpsetGap = 5.0;

    /// <summary>Số hero đưa ra mỗi danh sách cấm/chọn.</summary>
    public const int TopHeroes = 5;

    /// <summary>Số ván tối thiểu để danh sách cấm/chọn có nghĩa.</summary>
    public const int MinDraftMatches = 2;

    public static List<Highlight> Build(
        IReadOnlyList<DaySeries> series,
        IReadOnlyList<DayMatch> matches,
        IReadOnlyList<DayDraft> drafts,
        IReadOnlyDictionary<int, string> heroNames,
        IReadOnlyCollection<int>? bannedYesterday)
    {
        var list = new List<Highlight>();

        var done = series.Where(s => s.Completed).ToList();

        // Đếm ván trên MỌI series, không chỉ series đã xong.
        //
        // Một series đang đánh dở vẫn đã có ván kết thúc, và ta vẫn đọc được chi tiết của chúng.
        // Cộng riêng series đã xong thì mẫu số nhỏ hơn tử số — thấy thật trên trang: "đọc được
        // 9/7 ván", một câu tự bác bỏ chính nó.
        var expected = series.Sum(s => s.Wins1 + s.Wins2);

        list.Add(new Highlight("tong-quan", "flat",
            $"{done.Count}/{series.Count} series đã xong · {expected} ván · đọc được chi tiết {matches.Count} ván",
            new
            {
                seriesDone = done.Count,
                seriesTotal = series.Count,
                matchesExpected = expected,
                matchesRead = matches.Count,
            }));

        AddUpsets(list, done);
        AddDrafts(list, drafts, heroNames, bannedYesterday);
        AddDurations(list, matches);

        return list;
    }

    /// <summary>
    /// Ngược kèo: bên THẮNG có winrate 180 ngày thấp hơn bên thua.
    ///
    /// Dùng winrate của snapshot chứ không dùng thứ hạng bảng đấu, vì thứ hạng ở vòng Swiss đổi
    /// theo từng lượt và một đội có thể "xếp trên" chỉ vì lịch dễ hơn. Winrate cửa sổ 180 ngày
    /// là thứ đã có sẵn, đã được tính đều cho cả 16 đội, và không phụ thuộc vào lịch.
    /// </summary>
    private static void AddUpsets(List<Highlight> list, List<DaySeries> done)
    {
        var upsets = new List<(DaySeries S, string W, string L, double Gap)>();

        foreach (var s in done)
        {
            if (s.Winrate1 is not double w1 || s.Winrate2 is not double w2) continue;
            if (s.Wins1 == s.Wins2) continue;

            var oneWon = s.Wins1 > s.Wins2;
            var winnerRate = oneWon ? w1 : w2;
            var loserRate = oneWon ? w2 : w1;
            var gap = loserRate - winnerRate;

            if (gap < UpsetGap) continue;

            upsets.Add((s, oneWon ? s.Team1 ?? "?" : s.Team2 ?? "?",
                oneWon ? s.Team2 ?? "?" : s.Team1 ?? "?", gap));
        }

        foreach (var u in upsets.OrderByDescending(x => x.Gap))
            list.Add(new Highlight("nguoc-keo", "good",
                $"{u.W} thắng {u.L} {Math.Max(u.S.Wins1, u.S.Wins2)}–{Math.Min(u.S.Wins1, u.S.Wins2)}"
                + $" — winrate 180 ngày thấp hơn {u.Gap:0.#} điểm",
                new { winner = u.W, loser = u.L, gap = u.Gap, node = u.S.NodeId }));
    }

    private static void AddDrafts(
        List<Highlight> list, IReadOnlyList<DayDraft> drafts,
        IReadOnlyDictionary<int, string> heroNames, IReadOnlyCollection<int>? bannedYesterday)
    {
        var matchCount = drafts.Select(d => d.MatchId).Distinct().Count();

        if (matchCount < MinDraftMatches)
        {
            list.Add(new Highlight("chua-du-draft", "flat",
                matchCount == 0
                    ? "Chưa đọc được lượt cấm/chọn của ván nào trong ngày"
                    : $"Mới đọc được lượt cấm/chọn của {matchCount} ván — chưa đủ để xếp hạng",
                new { matches = matchCount }));
            return;
        }

        string Name(int id) => heroNames.GetValueOrDefault(id, "Hero " + id);

        var bans = drafts.Where(d => !d.IsPick)
            .GroupBy(d => d.HeroId)
            .Select(g => new { HeroId = g.Key, Matches = g.Select(x => x.MatchId).Distinct().Count() })
            .OrderByDescending(x => x.Matches)
            .Take(TopHeroes)
            .ToList();

        foreach (var b in bans)
            list.Add(new Highlight("cam-nhieu", "flat",
                $"{Name(b.HeroId)} bị cấm ở {b.Matches}/{matchCount} ván",
                new { heroId = b.HeroId, name = Name(b.HeroId), matches = b.Matches, of = matchCount }));

        var picks = drafts.Where(d => d.IsPick)
            .GroupBy(d => d.HeroId)
            .Select(g => new
            {
                HeroId = g.Key,
                Matches = g.Select(x => x.MatchId).Distinct().Count(),
                Wins = g.Count(x => x.WonSide),
            })
            .OrderByDescending(x => x.Matches)
            .Take(TopHeroes)
            .ToList();

        foreach (var p in picks)
            list.Add(new Highlight("chon-nhieu", "flat",
                $"{Name(p.HeroId)} được chọn {p.Matches} lần, thắng {p.Wins}",
                new { heroId = p.HeroId, name = Name(p.HeroId), matches = p.Matches, wins = p.Wins }));

        // SO VỚI NGÀY TRƯỚC — loại câu duy nhất ở đây mạnh dần theo thời gian, và cũng là lý do
        // đáng lưu digest lại. Chỉ chạy khi thật sự có ngày trước để so.
        if (bannedYesterday is null || bannedYesterday.Count == 0) return;

        var today = bans.Select(b => b.HeroId).ToHashSet();

        foreach (var id in today.Except(bannedYesterday))
            list.Add(new Highlight("cam-moi", "flat",
                $"{Name(id)} mới vào nhóm bị cấm nhiều — hôm trước không có",
                new { heroId = id, name = Name(id) }));

        foreach (var id in bannedYesterday.Except(today))
            list.Add(new Highlight("het-cam", "flat",
                $"{Name(id)} rời khỏi nhóm bị cấm nhiều so với hôm trước",
                new { heroId = id, name = Name(id) }));
    }

    private static void AddDurations(List<Highlight> list, IReadOnlyList<DayMatch> matches)
    {
        if (matches.Count == 0) return;

        var longest = matches.MaxBy(m => m.DurationSeconds);
        var shortest = matches.MinBy(m => m.DurationSeconds);

        list.Add(new Highlight("van-dai-nhat", "flat",
            $"Ván dài nhất {longest.DurationSeconds / 60} phút — {Pair(longest)}",
            new { matchId = longest.MatchId, minutes = longest.DurationSeconds / 60 }));

        // Chỉ nói ván ngắn nhất khi nó KHÁC ván dài nhất. Một ngày mới đọc được đúng một ván thì
        // "dài nhất" và "ngắn nhất" là cùng một ván, và in ra hai lần chỉ làm người đọc bối rối.
        if (matches.Count > 1)
            list.Add(new Highlight("van-ngan-nhat", "flat",
                $"Ván ngắn nhất {shortest.DurationSeconds / 60} phút — {Pair(shortest)}",
                new { matchId = shortest.MatchId, minutes = shortest.DurationSeconds / 60 }));
    }

    private static string Pair(DayMatch m)
    {
        var rad = m.RadiantTeam ?? "Radiant";
        var dire = m.DireTeam ?? "Dire";
        return m.RadiantWin ? $"{rad} thắng {dire}" : $"{dire} thắng {rad}";
    }

    /// <summary>Trung vị thời lượng. Trung vị chứ không phải trung bình — một ván 90 phút kéo lệch hẳn.</summary>
    public static int? MedianDuration(IReadOnlyList<DayMatch> matches)
    {
        if (matches.Count == 0) return null;

        var xs = matches.Select(m => m.DurationSeconds).OrderBy(x => x).ToList();
        var mid = xs.Count / 2;
        return xs.Count % 2 == 1 ? xs[mid] : (xs[mid - 1] + xs[mid]) / 2;
    }
}
