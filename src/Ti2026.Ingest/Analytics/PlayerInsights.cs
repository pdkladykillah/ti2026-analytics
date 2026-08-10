namespace Ti2026.Ingest.Analytics;

/// <summary>Một ván của người được theo dõi, rút gọn còn đúng những gì cần để suy luận.</summary>
public readonly record struct PlayerGame(
    int HeroId, DateTime StartTime, int DurationSeconds, bool Won,
    int Kills, int Deaths, int Assists,
    int? GoldPerMin, int? LastHits, int? PartySize, int? AverageRank, bool IsRadiant);

/// <summary>Thành tích trên một hero, kèm mốc so sánh của pro nếu có.</summary>
public readonly record struct HeroLine(
    int HeroId, string Name, int Games, int Wins, double Winrate,
    double AvgGpm, double AvgLastHitsPerMin, double Kda,
    double? ProGpm, int ProGames, bool Notable);

/// <param name="Tone">good | bad | warn | flat</param>
public readonly record struct PlayerInsight(string Kind, string Tone, string Text, double Strength);

/// <summary>
/// Đọc lịch sử pub của một người và nói ra điểm mạnh, điểm yếu, xu hướng.
///
/// BA NGUYÊN TẮC, giống hệt bộ nhận định đội — vì cùng một loại rủi ro:
///
/// 1. Mỗi câu phải nêu CON SỐ và MỐC ĐỂ SO. "Bạn chơi Void Spirit tốt" là lời khen, không phải
///    phân tích.
///
/// 2. Chọn hero tốt nhất/tệ nhất trong một pool 40 hero là lấy CỰC TRỊ của 40 phép so. Chấm nó
///    bằng ngưỡng dành cho một phép so duy nhất thì gần như hero nào cũng "xuất sắc" hoặc "tệ" —
///    lỗi so sánh bội. Phải hiệu chỉnh, xem <see cref="Notable"/>.
///
/// 3. VỊ TRÍ suy từ mức farm, KHÔNG từ lane_role: đo trên tài khoản thật thì lane_role chỉ có ở
///    6% số ván. Dựng phân tích vai trò trên 6% dữ liệu rồi trình bày như thể đủ là cách nhanh
///    nhất để cả trang mất đáng tin.
/// </summary>
public static class PlayerInsights
{
    /// <summary>Dưới ngần này ván trên một hero thì mọi tỷ lệ đều là nhiễu.</summary>
    public const int MinGamesPerHero = 8;

    /// <summary>Cỡ pool giả định khi hiệu chỉnh so sánh bội — số hero thường xuyên chơi.</summary>
    public const int TypicalPoolSize = 40;

    /// <summary>Last hit mỗi phút từ mức này trở lên là lối chơi ăn farm (core).</summary>
    public const double CoreLastHitsPerMin = 5.0;

    /// <summary>Dưới mức này là lối chơi nhường farm (hỗ trợ).</summary>
    public const double SupportLastHitsPerMin = 2.5;

    /// <summary>
    /// Thành tích trên một hero có ĐÁNG NÓI không, sau khi đã tính tới việc ta đang xét cả pool.
    ///
    /// Dùng nhị thức chính xác rồi chia ngưỡng cho cỡ pool. Không hiệu chỉnh thì với 40 hero,
    /// chỉ riêng may rủi đã đủ tạo ra vài hero "thắng 75%" và trang sẽ khen nhầm.
    /// </summary>
    public static bool Notable(int games, int wins, int poolSize = TypicalPoolSize)
    {
        if (games < MinGamesPerHero) return false;

        var tail = wins * 2 >= games
            ? UpperTailAtHalf(games, wins)
            : LowerTailAtHalf(games, wins);

        return tail < 0.05 / Math.Max(poolSize, 1);
    }

    public static List<PlayerInsight> Read(
        IReadOnlyList<PlayerGame> games,
        IReadOnlyList<HeroLine> heroes,
        double lifetimeWinrate)
    {
        var found = new List<PlayerInsight>();
        if (games.Count == 0) return found;

        AddOverall(games, lifetimeWinrate, found);
        AddFarmProfile(games, found);
        AddHeroExtremes(heroes, found);
        AddProBenchmark(heroes, found);
        AddSideSplit(games, found);
        AddParty(games, found);
        AddGameLength(games, found);
        AddPoolWidth(heroes, games, found);

        return found.OrderByDescending(x => x.Strength).ToList();
    }

    private static void AddOverall(
        IReadOnlyList<PlayerGame> games, double lifetime, List<PlayerInsight> found)
    {
        var wins = games.Count(g => g.Won);
        var wr = wins * 100.0 / games.Count;
        var gap = wr - lifetime;

        // Cần cả hai: tách được khỏi nhiễu VÀ đủ lớn để đáng nói. Ở 500 ván, 1 điểm phần trăm
        // là tách được về mặt thống kê nhưng không có nghĩa gì với người đọc.
        var se = Math.Sqrt(0.25 / games.Count) * 100;
        var real = Math.Abs(gap) > 1.96 * se && Math.Abs(gap) >= 3;

        found.Add(new PlayerInsight("tong-quan", real ? (gap > 0 ? "good" : "bad") : "flat",
            real
                ? $"{games.Count} ván gần đây thắng {wr:0.0}%, {(gap > 0 ? "cao hơn" : "thấp hơn")} "
                  + $"tỷ lệ thắng cả đời {lifetime:0.0}% tới {Math.Abs(gap):0.0} điểm — đây là "
                  + "chênh lệch thật, không phải dao động."
                : $"{games.Count} ván gần đây thắng {wr:0.0}%, sát với tỷ lệ cả đời "
                  + $"{lifetime:0.0}%. Phong độ đang ổn định.",
            real ? 90 : 40));
    }

    /// <summary>
    /// Vị trí suy từ MỨC FARM. lane_role chỉ có ở 6% số ván nên không dùng được; last hit mỗi
    /// phút thì có ở 100% và nói đúng thứ cần biết: người này ăn farm hay nhường farm.
    /// </summary>
    private static void AddFarmProfile(IReadOnlyList<PlayerGame> games, List<PlayerInsight> found)
    {
        var withLh = games.Where(g => g.LastHits is int && g.DurationSeconds > 0).ToList();
        if (withLh.Count < 20) return;

        var lhpm = withLh.Average(g => g.LastHits!.Value / (g.DurationSeconds / 60.0));
        var gpm = games.Where(g => g.GoldPerMin is int).Select(g => (double)g.GoldPerMin!.Value)
            .DefaultIfEmpty(0).Average();

        var (label, tone) = lhpm >= CoreLastHitsPerMin ? ("core ăn farm", "flat")
            : lhpm <= SupportLastHitsPerMin ? ("hỗ trợ nhường farm", "flat")
            : ("linh hoạt, giữa core và hỗ trợ", "flat");

        found.Add(new PlayerInsight("muc-farm", tone,
            $"Hồ sơ lối chơi: {label} — trung bình {lhpm:0.0} last hit mỗi phút và {gpm:0} GPM "
            + $"qua {withLh.Count} ván. Suy từ chỉ số đo được, không phải từ vai trò khai báo "
            + "(Dota chỉ ghi lại vai trò ở một phần rất nhỏ số ván).",
            85));
    }

    private static void AddHeroExtremes(IReadOnlyList<HeroLine> heroes, List<PlayerInsight> found)
    {
        var pool = heroes.Where(h => h.Games >= MinGamesPerHero).ToList();
        if (pool.Count < 3) return;

        foreach (var h in pool.Where(h => h.Notable).OrderByDescending(h => h.Winrate).Take(3))
        {
            found.Add(new PlayerInsight("hero-manh", "good",
                $"{h.Name} là hero mạnh thật của bạn: thắng {h.Winrate:0}% qua {h.Games} ván. "
                + "Cách biệt này đứng vững kể cả sau khi tính tới việc bạn chơi hàng chục hero — "
                + "tức không phải may.",
                80));
        }

        foreach (var h in pool.Where(h => h.Notable).OrderBy(h => h.Winrate).Take(2))
        {
            if (h.Winrate >= 50) continue;
            found.Add(new PlayerInsight("hero-yeu", "bad",
                $"{h.Name} đang kéo bạn xuống: thắng {h.Winrate:0}% qua {h.Games} ván, và cách "
                + "biệt này cũng đứng vững sau hiệu chỉnh. Đây là hero đáng bỏ hoặc đáng học lại "
                + "từ đầu.",
                78));
        }

    }

    /// <summary>
    /// So GPM với tuyển thủ chuyên nghiệp trên cùng hero.
    ///
    /// TÁCH RIÊNG khỏi phần chọn hero mạnh/yếu, và đó không phải chuyện sắp xếp mã: hai thứ có
    /// điều kiện hoàn toàn khác nhau. Chọn hero mạnh nhất đòi hỏi một POOL đủ rộng thì phép
    /// hiệu chỉnh so sánh bội mới có nghĩa; còn so với mốc pro là phép so MỘT hero với MỘT mốc
    /// bên ngoài, đúng ngay cả khi người ta chỉ chơi một hero duy nhất. Bản đầu gộp chung nên
    /// điều kiện "pool ít nhất 3 hero" chặn luôn cả mốc pro — một bài kiểm bắt được.
    /// </summary>
    private static void AddProBenchmark(IReadOnlyList<HeroLine> heroes, List<PlayerInsight> found)
    {
        foreach (var h in heroes
                     .Where(h => h.Games >= MinGamesPerHero && h.ProGames >= 10 && h.ProGpm is double)
                     .OrderByDescending(h => h.Games).Take(2))
        {
            var gap = h.AvgGpm - h.ProGpm!.Value;
            if (Math.Abs(gap) < 40) continue;

            found.Add(new PlayerInsight("so-voi-pro", gap >= 0 ? "good" : "warn",
                $"{h.Name}: GPM của bạn {h.AvgGpm:0} so với {h.ProGpm:0} của tuyển thủ chuyên "
                + $"nghiệp ở giải ({h.ProGames} ván) — {(gap >= 0 ? "cao hơn" : "thấp hơn")} "
                + $"{Math.Abs(gap):0}. Mốc này là trận đấu giải, nhịp khác pub, nên đọc như một "
                + "hướng chứ không phải một chuẩn.",
                70));
        }
    }

    private static void AddSideSplit(IReadOnlyList<PlayerGame> games, List<PlayerInsight> found)
    {
        var rad = games.Where(g => g.IsRadiant).ToList();
        var dire = games.Where(g => !g.IsRadiant).ToList();
        if (rad.Count < 30 || dire.Count < 30) return;

        var wr = rad.Count(g => g.Won) * 100.0 / rad.Count;
        var wd = dire.Count(g => g.Won) * 100.0 / dire.Count;
        var gap = wr - wd;

        var se = Math.Sqrt(2500.0 / rad.Count + 2500.0 / dire.Count);
        if (Math.Abs(gap) < 1.96 * se) return;

        found.Add(new PlayerInsight("ben-san", "flat",
            $"Lệch rõ theo bên: thắng {wr:0}% khi ở Radiant ({rad.Count} ván) so với {wd:0}% khi "
            + $"ở Dire ({dire.Count} ván). Cách biệt {Math.Abs(gap):0} điểm này đã vượt mức giải "
            + "thích được bằng may rủi.",
            65));
    }

    private static void AddParty(IReadOnlyList<PlayerGame> games, List<PlayerInsight> found)
    {
        var solo = games.Where(g => g.PartySize is 1 or null).ToList();
        var party = games.Where(g => g.PartySize is int p && p > 1).ToList();
        if (solo.Count < 30 || party.Count < 30) return;

        var ws = solo.Count(g => g.Won) * 100.0 / solo.Count;
        var wp = party.Count(g => g.Won) * 100.0 / party.Count;
        var gap = wp - ws;

        var se = Math.Sqrt(2500.0 / solo.Count + 2500.0 / party.Count);
        if (Math.Abs(gap) < 1.96 * se) return;

        found.Add(new PlayerInsight("nhom", gap > 0 ? "good" : "warn",
            $"Đi nhóm thắng {wp:0}% ({party.Count} ván) so với đi một mình {ws:0}% "
            + $"({solo.Count} ván) — chênh {Math.Abs(gap):0} điểm, đã vượt mức may rủi.",
            60));
    }

    private static void AddGameLength(IReadOnlyList<PlayerGame> games, List<PlayerInsight> found)
    {
        var valid = games.Where(g => g.DurationSeconds > 0).ToList();
        if (valid.Count < 60) return;

        var median = Median(valid.Select(g => (double)g.DurationSeconds).ToList());
        var quick = valid.Where(g => g.DurationSeconds <= median).ToList();
        var slow = valid.Where(g => g.DurationSeconds > median).ToList();
        if (quick.Count < 25 || slow.Count < 25) return;

        var wq = quick.Count(g => g.Won) * 100.0 / quick.Count;
        var ws = slow.Count(g => g.Won) * 100.0 / slow.Count;
        var gap = wq - ws;

        var se = Math.Sqrt(2500.0 / quick.Count + 2500.0 / slow.Count);
        if (Math.Abs(gap) < 1.96 * se) return;

        found.Add(new PlayerInsight("do-dai-tran", "flat",
            gap > 0
                ? $"Bạn mạnh ở trận NGẮN: thắng {wq:0}% khi ván dưới {median / 60:0} phút, so với "
                  + $"{ws:0}% ở ván dài hơn. Dấu hiệu nên chốt sớm thay vì kéo về late."
                : $"Bạn mạnh ở trận DÀI: thắng {ws:0}% khi ván trên {median / 60:0} phút, so với "
                  + $"{wq:0}% ở ván ngắn hơn. Dấu hiệu nên chơi an toàn về late.",
            62));
    }

    private static void AddPoolWidth(
        IReadOnlyList<HeroLine> heroes, IReadOnlyList<PlayerGame> games, List<PlayerInsight> found)
    {
        if (games.Count < 50 || heroes.Count == 0) return;

        var top5 = heroes.OrderByDescending(h => h.Games).Take(5).Sum(h => h.Games);
        var share = top5 * 100.0 / games.Count;

        found.Add(new PlayerInsight("do-rong-pool", "flat",
            share >= 50
                ? $"Pool hẹp: 5 hero hay dùng nhất chiếm {share:0}% số ván. Dễ bị đoán và dễ bị "
                  + $"cấm, nhưng đổi lại bạn thành thạo sâu — {heroes.Count} hero từng chơi."
                : $"Pool rộng: 5 hero hay dùng nhất chỉ chiếm {share:0}% số ván, trải trên "
                  + $"{heroes.Count} hero. Khó bị cấm bài, nhưng khó lên độ sâu ở từng hero.",
            50));
    }

    private static double Median(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        var m = s.Count / 2;
        return s.Count % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2;
    }

    private static double LowerTailAtHalf(int n, int k)
    {
        double total = 0, c = 1;
        for (var i = 0; i <= k; i++) { total += c; c = c * (n - i) / (i + 1); }
        return total / Math.Pow(2, n);
    }

    private static double UpperTailAtHalf(int n, int k) => LowerTailAtHalf(n, n - k);
}
