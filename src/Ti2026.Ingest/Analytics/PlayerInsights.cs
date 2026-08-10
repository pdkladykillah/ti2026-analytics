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
/// 3. VAI TRÒ CHỈ ĐẾN TỪ <see cref="RoleResolver"/>, không bao giờ từ mức farm. Bản đầu của bộ
///    này có một mục "hồ sơ lối chơi" đọc last hit mỗi phút rồi tuyên bố người dùng là core hay
///    hỗ trợ. Người dùng phản bác và họ đúng: đo trên chính tài khoản đó, last hit theo lane
///    thật là 299 (safe) / 345 (mid) / 282 (off) — ba lane gần như bằng nhau, nên một người
///    chơi offlane tốt sẽ bị xếp thành carry MỘT CÁCH CÓ HỆ THỐNG.
/// </summary>
public static class PlayerInsights
{
    /// <summary>Dưới ngần này ván trên một hero thì mọi tỷ lệ đều là nhiễu.</summary>
    public const int MinGamesPerHero = 8;

    /// <summary>Chênh dưới ngần này điểm phần trăm so với 50% thì không đáng gắn nhãn.</summary>
    public const double MinWinrateGap = 5.0;

    /// <summary>
    /// Số ván tối thiểu để một vai trò được gọi là "hiệu quả nhất". Ở 21 ván, sai số chuẩn của
    /// tỷ lệ thắng đã là 11 điểm phần trăm — đủ để bất kỳ vai trò nào cũng có thể ngẫu nhiên
    /// đứng đầu bảng.
    /// </summary>
    public const int MinGamesForBestRole = 60;

    /// <summary>
    /// Hero nào trong pool có thành tích ĐÁNG NÓI, xét CẢ POOL cùng lúc.
    ///
    /// PHẢI LÀ HÀM TRÊN CẢ TẬP, không phải hàm trên từng hero. Bản đầu nhận (số ván, số thắng)
    /// của một hero rồi chia ngưỡng cho một cỡ pool GIẢ ĐỊNH là 40. Hai chỗ sai:
    ///
    /// • Cỡ pool giả định không khớp thực tế — người dùng thật có 125 hero đạt ngưỡng số ván.
    /// • Bonferroni khống chế xác suất mắc DÙ CHỈ MỘT sai lầm, quá chặt cho câu hỏi thăm dò
    ///   "hero nào nổi bật". Đo trên dữ liệu thật: với 125 hero, KHÔNG hero nào bật — kể cả
    ///   những hero lệch hơn ba lần sai số chuẩn. Một cột không bao giờ bật thì vô dụng.
    ///
    /// Nay dùng Benjamini–Hochberg trên đúng số phép so thực tế. Xem <see cref="MultipleTests"/>.
    /// </summary>
    public static bool[] NotableSet(
        IReadOnlyList<(int Games, int Wins)> pool, double q = MultipleTests.DefaultQ)
    {
        var tails = pool
            .Select(h => h.Games < MinGamesPerHero
                ? 1.0
                : MultipleTests.BinomialTwoSided(h.Games, h.Wins, 0.5))
            .ToList();

        var discovered = MultipleTests.BenjaminiHochberg(tails, q);

        return pool
            .Select((h, i) => discovered[i]
                && Math.Abs(h.Wins * 100.0 / Math.Max(h.Games, 1) - 50) >= MinWinrateGap)
            .ToArray();
    }

    public static List<PlayerInsight> Read(
        IReadOnlyList<PlayerGame> games,
        IReadOnlyList<HeroLine> heroes,
        double lifetimeWinrate,
        IReadOnlyList<SkillComponent>? components = null,
        IReadOnlyList<RoleSlice>? roles = null,
        IReadOnlyList<RoleEra>? eras = null,
        IReadOnlyList<TeammateLine>? mates = null,
        IReadOnlyList<MetaHero>? meta = null)
    {
        var found = new List<PlayerInsight>();
        if (games.Count == 0) return found;

        AddOverall(games, lifetimeWinrate, found);
        AddSkillProfile(components, found);
        AddRoleMix(roles, found);
        AddRoleShift(eras, found);
        AddHeroExtremes(heroes, found);
        AddMetaGap(meta, found);
        AddProBenchmark(heroes, found);
        AddMates(mates, found);
        AddSideSplit(games, found);
        AddParty(games, found);
        AddGameLength(games, found);
        AddPoolWidth(heroes, games, found);

        return found.OrderByDescending(x => x.Strength).ToList();
    }

    /// <summary>
    /// Mặt mạnh và mặt yếu, đo bằng phân vị so với người chơi CÙNG HERO.
    ///
    /// Đây là mục thay cho "hồ sơ lối chơi" cũ vốn suy vai trò từ mức farm. Khác biệt căn bản:
    /// mục này không nói người dùng chơi vị trí nào, nó nói họ làm tốt việc gì — và câu đó thì
    /// phân vị trả lời được cho 100% số ván.
    /// </summary>
    private static void AddSkillProfile(
        IReadOnlyList<SkillComponent>? components, List<PlayerInsight> found)
    {
        if (components is null || components.Count == 0) return;

        var (strong, weak) = SkillComponents.Extremes(components);

        foreach (var c in strong.Take(2))
        {
            found.Add(new PlayerInsight("manh-mat", "good",
                $"{c.Label}: bạn ở phân vị {c.Median} so với mọi người chơi CÙNG HERO, qua "
                + $"{c.Games:N0} ván. Tức trong 100 người chơi những hero bạn hay dùng, khoảng "
                + $"{c.Median} người làm việc này kém hơn bạn.",
                88));
        }

        foreach (var c in weak.Take(2))
        {
            found.Add(new PlayerInsight("yeu-mat", "warn",
                $"{c.Label} là mặt yếu nhất: phân vị {c.Median} qua {c.Games:N0} ván — dưới mức "
                + "trung bình của những người chơi cùng hero. Đây là chỗ đáng sửa nhất vì nó đã "
                + "so trên cùng hero, tức không phải do bạn hay chọn hero khó.",
                86));
        }

        AddBroadShift(components, found);

        // Tiến bộ đo bằng chính cửa sổ gần đây của từng mặt, chứ không bằng tỷ lệ thắng: tỷ lệ
        // thắng còn phụ thuộc 9 người khác, phân vị thì chỉ phụ thuộc người này.
        foreach (var c in components
                     .Where(c => c.Recent is int r && Math.Abs(r - c.Median) >= SkillComponents.NotableGap)
                     .OrderByDescending(c => Math.Abs(c.Recent!.Value - c.Median))
                     .Take(2))
        {
            var up = c.Recent!.Value > c.Median;
            found.Add(new PlayerInsight("tien-bo", up ? "good" : "warn",
                $"{c.Label} {(up ? "đang lên" : "đang xuống")}: {SkillComponents.RecentWindow} ván "
                + $"gần nhất ở phân vị {c.Recent}, so với {c.Median} tính trên toàn bộ lịch sử "
                + $"({c.Games:N0} ván).",
                84));
        }
    }

    /// <summary>
    /// Dịch chuyển TRÊN TOÀN BỘ các mặt, thứ mà ngưỡng theo từng mặt bỏ sót.
    ///
    /// Vì sao cần. Đo trên dữ liệu thật: cả 10 mặt đều cao hơn ở 50 ván gần nhất, nhưng mặt lệch
    /// nhiều nhất cũng chỉ +13 điểm phân vị — dưới ngưỡng <see cref="SkillComponents.NotableGap"/>
    /// nên KHÔNG mặt nào sinh ra nhận định. Trang sẽ im lặng trước một tín hiệu rất rõ, chỉ vì
    /// mỗi mảnh của nó nhỏ hơn ngưỡng dành cho một mảnh.
    ///
    /// KHÔNG NÊU XÁC SUẤT. Cám dỗ là viết "10/10 mặt đi lên, ngẫu nhiên chỉ 1/1024". Sai, vì các
    /// mặt KHÔNG độc lập: kiếm vàng và ăn lính gần như là một, sát thương và số mạng hạ đi cùng
    /// nhau. Mười mặt tương quan không phải mười lần tung đồng xu, nên con số 1/1024 sẽ là một
    /// khẳng định mạnh hơn nhiều lần so với những gì dữ liệu đỡ nổi. Nêu SỐ ĐẾM và MỨC DỊCH, rồi
    /// nói thẳng rằng chúng tương quan.
    /// </summary>
    private static void AddBroadShift(
        IReadOnlyList<SkillComponent> components, List<PlayerInsight> found)
    {
        var withRecent = components.Where(c => c.Recent is int).ToList();
        if (withRecent.Count < 6) return;

        var up = withRecent.Count(c => c.Recent!.Value > c.Median);
        var down = withRecent.Count(c => c.Recent!.Value < c.Median);

        // Chỉ nói khi gần như TẤT CẢ cùng chiều. Đa số mong manh thì đúng là nhiễu.
        var many = Math.Max(up, down);
        if (many < withRecent.Count - 1) return;

        var shifts = withRecent.Select(c => (double)(c.Recent!.Value - c.Median)).ToList();
        var move = Median(shifts);

        var rising = up > down;

        found.Add(new PlayerInsight("dich-chuyen-chung", rising ? "good" : "warn",
            $"{many}/{withRecent.Count} mặt kỹ năng đều {(rising ? "cao hơn" : "thấp hơn")} ở "
            + $"{SkillComponents.RecentWindow} ván gần nhất so với toàn bộ lịch sử, dịch trung vị "
            + $"{(move >= 0 ? "+" : "")}{move:0} điểm phân vị. Từng mặt riêng lẻ chưa đủ lớn để "
            + "kết luận, nhưng cùng chiều gần như toàn bộ thì đáng chú ý. Lưu ý các mặt này "
            + "tương quan với nhau (kiếm vàng và ăn lính gần như là một), nên đây là MỘT tín "
            + "hiệu rộng chứ không phải mười tín hiệu độc lập.",
            87));
    }

    /// <summary>
    /// Tỷ trọng và hiệu quả từng vai trò. Phải nói rõ phần nào là nhãn thật, phần nào là suy
    /// luận — vì hai loại đó khác nhau về độ chắc chứ không chỉ khác nhau về tên gọi.
    /// </summary>
    private static void AddRoleMix(IReadOnlyList<RoleSlice>? roles, List<PlayerInsight> found)
    {
        if (roles is null || roles.Count == 0) return;

        var exact = roles.Where(r => r.Exact).ToList();
        if (exact.Count >= 2)
        {
            var total = exact.Sum(r => r.Games);
            var top = exact.OrderByDescending(r => r.Games).First();

            // "Vai trò hiệu quả nhất" chỉ được nêu khi vai trò đó có ĐỦ VÁN. Trên dữ liệu thật,
            // bản đầu chọn pos4 với 21 ván thắng 61,9% và gọi đó là vai trò mạnh nhất — trong khi
            // ở 21 ván, sai số chuẩn của tỷ lệ thắng đã là 11 điểm phần trăm. Đó là nhiễu được
            // trình bày như một phát hiện, và tệ hơn là nó khuyên người đọc đi chơi vị trí đó.
            var best = exact
                .Where(r => r.Games >= MinGamesForBestRole)
                .OrderByDescending(r => r.Winrate)
                .FirstOrDefault();

            found.Add(new PlayerInsight("vai-tro", "flat",
                $"Trong {total:N0} ván có nhãn vị trí thật từ replay, bạn chơi nhiều nhất là "
                + $"{top.Label} ({top.Games} ván, {top.Winrate:0.0}%)."
                + (best.Games >= MinGamesForBestRole
                    ? $" Hiệu quả nhất trong số các vai trò đủ mẫu là {best.Label} "
                      + $"({best.Games} ván, {best.Winrate:0.0}%)."
                    : $" Chưa vai trò nào đủ {MinGamesForBestRole} ván có nhãn để so hiệu quả — "
                      + "phần lớn lịch sử chưa được parse nên chưa có vị trí chính xác."),
                82));
        }

        var inferred = roles.Where(r => !r.Exact).ToList();
        if (inferred.Count >= 2)
        {
            var core = inferred.FirstOrDefault(r => r.Code == "core");
            var sup = inferred.FirstOrDefault(r => r.Code == "support");
            if (core.Games > 0 && sup.Games > 0)
            {
                found.Add(new PlayerInsight("core-ho-tro", "flat",
                    $"Trên toàn bộ lịch sử, {core.Games:N0} ván đi core (thắng {core.Winrate:0.0}%) "
                    + $"và {sup.Games:N0} ván đi hỗ trợ (thắng {sup.Winrate:0.0}%). Cách chia này "
                    + "suy từ thứ hạng tài sản trong đội — chắc ở mức core/hỗ trợ, nhưng KHÔNG "
                    + "tách được mid với offlane.",
                    72));
            }
        }
    }

    /// <summary>
    /// Vai trò dịch chuyển qua nhiều năm. Chỉ dùng ván có nhãn thật, và phải nêu số ván có nhãn
    /// mỗi năm — một năm 3 ván mà vẽ thành "100% mid" thì con số đúng còn câu chuyện thì sai.
    /// </summary>
    private static void AddRoleShift(IReadOnlyList<RoleEra>? eras, List<PlayerInsight> found)
    {
        if (eras is null) return;
        if (RoleBreakdown.Shift(eras) is not var (first, last)) return;

        var midThen = first.Mid * 100.0 / first.Labelled;
        var midNow = last.Mid * 100.0 / last.Labelled;
        if (Math.Abs(midNow - midThen) < 20) return;

        found.Add(new PlayerInsight("doi-vai-tro", "flat",
            $"Vai trò đã dịch chuyển: năm {first.Year}, {midThen:0}% số ván có nhãn là đi mid "
            + $"({first.Mid}/{first.Labelled}); tới năm {last.Year} còn {midNow:0}% "
            + $"({last.Mid}/{last.Labelled}). "
            + $"{(midNow < midThen ? "Từ mid thuần sang chơi được nhiều vị trí." : "Đang dồn dần về mid.")} "
            + "Chỉ đếm ván có nhãn thật từ replay nên số lượng mỏng — đọc như một hướng, không "
            + "phải một phép đo.",
            76));
    }

    /// <summary>
    /// Hero bạn chơi HƠN hoặc KÉM mức chung của chính hero đó.
    ///
    /// Khác hẳn <see cref="AddHeroExtremes"/> vốn so với mốc 50%: hero mạnh sẵn thì ai chơi cũng
    /// thắng, nên thắng 53% với một hero có mức chung 53% chẳng nói lên điều gì về người chơi.
    /// Hai mục cùng tồn tại vì chúng trả lời hai câu khác nhau — "hero nào giúp bạn thắng" và
    /// "hero nào bạn chơi giỏi hơn người khác".
    /// </summary>
    private static void AddMetaGap(IReadOnlyList<MetaHero>? meta, List<PlayerInsight> found)
    {
        if (meta is null || meta.Count == 0) return;

        foreach (var h in meta.Where(h => h.Notable && h.Edge > 0).Take(2))
        {
            found.Add(new PlayerInsight("hon-meta", "good",
                $"{h.Name} là thế mạnh riêng của bạn: thắng {h.Winrate:0.0}% qua {h.Games} ván, "
                + $"trong khi mức chung của hero này ở bậc rank cao chỉ {h.MetaWinrate:0.0}% — "
                + $"hơn {h.Edge:0.0} điểm. Đây là chỗ bạn giỏi hơn người khác, chứ không phải chỗ "
                + "hero tự mạnh.",
                80));
        }

        foreach (var h in meta.Where(h => h.Notable && h.Edge < 0).OrderBy(h => h.Edge).Take(2))
        {
            found.Add(new PlayerInsight("kem-meta", "warn",
                $"{h.Name}: bạn thắng {h.Winrate:0.0}% qua {h.Games} ván nhưng mức chung của hero "
                + $"này là {h.MetaWinrate:0.0}% — kém {Math.Abs(h.Edge):0.0} điểm. Hero không có "
                + "lỗi ở đây; đây là hero đáng học lại hoặc đáng bỏ.",
                79));
        }
    }

    private static void AddMates(IReadOnlyList<TeammateLine>? mates, List<PlayerInsight> found)
    {
        if (mates is null || mates.Count == 0) return;

        foreach (var m in mates.Where(m => m.Notable)
                     .OrderByDescending(m => Math.Abs(m.Lift)).Take(2))
        {
            found.Add(new PlayerInsight(m.Lift > 0 ? "dong-doi-hop" : "dong-doi-lech",
                m.Lift > 0 ? "good" : "warn",
                $"Chơi cùng {m.Name}: thắng {m.Winrate:0.0}% qua {m.Games:N0} ván, so với "
                + $"{m.WithoutWinrate:0.0}% ở {m.WithoutGames:N0} ván vắng người này — chênh "
                + $"{(m.Lift > 0 ? "+" : "")}{m.Lift:0.0} điểm. Cách biệt này đứng vững kể cả sau "
                + "khi tính tới việc bạn có nhiều đồng đội quen.",
                74));
        }
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

}
