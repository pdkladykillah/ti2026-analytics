namespace Ti2026.Ingest.Analytics;

/// <summary>Một điều hệ thống rút ra được về một đội.</summary>
/// <param name="Tone">good | bad | warn | flat — dùng để tô màu, không phải để xếp hạng.</param>
/// <param name="Strength">Độ đáng chú ý, chỉ để sắp thứ tự. Không hiển thị.</param>
public readonly record struct Insight(string Kind, string Tone, string Text, double Strength);

/// <summary>
/// Số liệu của một đội, đã lọc theo đội hình TI2026, gom sẵn để không phải truy vấn trong
/// lúc suy luận.
/// </summary>
/// <param name="RecentResults">Kết quả các ván ĐÚNG ĐỘI HÌNH, MỚI NHẤT TRƯỚC.</param>
public readonly record struct TeamFacts(
    string Slug,
    string Name,
    int Maps,
    double Winrate,
    double KillDiff,
    double AvgDurationMinutes,
    double? FirstBloodRate,
    double? WinWhenFbRate,
    double? F10Rate,
    double? WinWhenF10Rate,
    double? Elo,
    int EloGames,
    int LineupGames,
    string? LineupSince,
    IReadOnlyList<bool> RecentResults,
    int RadiantGames, int RadiantWins,
    int DireGames, int DireWins,
    string? NemesisName, int NemesisLosses, int NemesisGames);

/// <summary>
/// Đọc số liệu một đội và NÓI RA những gì đáng nói.
///
/// VÌ SAO CẦN. Bản trước chỉ có một câu duy nhất — Elo đang lên hay đang xuống — nên trang
/// phong độ về cơ bản là một biểu đồ kèm nhãn hướng. Mọi thứ khác (đội này thắng nhờ đâu, có
/// tận dụng được lợi thế đầu trận không, đội hình mới hay đã ăn ý, ai đang khắc chế) đều nằm
/// sẵn trong dữ liệu nhưng bắt người đọc tự ghép.
///
/// NGUYÊN TẮC. Mỗi nhận định phải (1) nêu con số, (2) nêu mốc để so, (3) chỉ xuất hiện khi
/// vượt được ngưỡng nhiễu. Một danh sách 8 nhận định mà 6 cái là nhiễu thì tệ hơn 2 nhận định
/// chắc chắn — người đọc mất khả năng phân biệt cái nào đáng tin.
/// </summary>
public static class TeamInsights
{
    /// <summary>Dưới ngần này ván thì không kết luận gì ngoài chính lời cảnh báo mẫu nhỏ.</summary>
    public const int MinMaps = 8;

    /// <summary>Đội hình dưới ngần này ván đá chung thì còn quá mới để tin các con số.</summary>
    public const int YoungLineupGames = 25;

    /// <summary>Chuỗi từ ngần này ván trở lên mới đáng gọi là chuỗi.</summary>
    public const int MinStreak = 3;

    public static List<Insight> For(TeamFacts t, IReadOnlyList<TeamFacts> all)
    {
        var found = new List<Insight>();
        var peers = all.Where(x => x.Slug != t.Slug && x.Maps >= MinMaps).ToList();

        AddSampleWarning(t, found);

        if (t.Maps >= MinMaps)
        {
            AddStreak(t, found);
            AddRankStandout(t, peers, found);
            AddFirstBloodConversion(t, found);
            AddKillsVersusWins(t, found);
            AddPace(t, peers, found);
            AddSideSplit(t, found);
        }

        AddNemesis(t, found);

        return found.OrderByDescending(x => x.Strength).ToList();
    }

    /// <summary>
    /// Cảnh báo mẫu nhỏ đứng ĐẦU danh sách và luôn hiện khi đúng, kể cả khi có nhận định khác
    /// mạnh hơn: nó là điều kiện để đọc mọi câu còn lại, không phải một mục ngang hàng.
    /// </summary>
    private static void AddSampleWarning(TeamFacts t, List<Insight> found)
    {
        if (t.LineupGames >= YoungLineupGames && t.Maps >= MinMaps) return;

        var since = t.LineupSince is null ? "" : $" (từ {t.LineupSince})";

        found.Add(new Insight("mau-nho", "warn",
            $"Đội hình TI2026 mới đá chung {t.LineupGames} ván{since}. Mọi con số bên dưới đều "
            + "dựa trên chừng đó ván, nên đọc như một dấu hiệu sớm chứ không phải kết luận.",
            1000));
    }

    private static void AddStreak(TeamFacts t, List<Insight> found)
    {
        if (t.RecentResults.Count == 0) return;

        var won = t.RecentResults[0];
        var n = t.RecentResults.TakeWhile(r => r == won).Count();
        if (n < MinStreak) return;

        // KHÔNG gọi đây là "chuỗi dài nhất": n là chuỗi ĐANG diễn ra, không phải chuỗi dài nhất
        // trong cửa sổ. Bản trước ghi "chuỗi dài nhất trong 20 ván gần nhất" — một câu sai sự
        // thật mà nghe rất xuôi, đúng loại câu không ai đi kiểm.
        found.Add(new Insight("chuoi", won ? "good" : "bad",
            $"Đang {(won ? "thắng" : "thua")} {n} ván liên tiếp "
            + $"(xét {t.RecentResults.Count} ván gần nhất đúng đội hình).",
            50 + n * 5));
    }

    /// <summary>
    /// Chỉ nêu khi đội đứng NHẤT hoặc BÉT, và chỉ khi cách biệt với đội liền kề đủ rõ. Xếp
    /// hạng 3/16 thì đúng nhưng không nói lên điều gì người đọc dùng được.
    /// </summary>
    private static void AddRankStandout(TeamFacts t, List<TeamFacts> peers, List<Insight> found)
    {
        if (peers.Count < 4) return;

        Check("winrate", "tỷ lệ thắng", x => x.Winrate, higherIsBetter: true, "{0:0}%", 4);
        Check("killdiff", "chênh lệch hạ gục mỗi ván", x => x.KillDiff, true, "{0:+0.0;-0.0}", 1.5);

        void Check(string kind, string label, Func<TeamFacts, double> pick,
                   bool higherIsBetter, string format, double minGap)
        {
            var mine = pick(t);
            var others = peers.Select(pick).OrderByDescending(v => v).ToList();

            var best = others[0];
            var worst = others[^1];

            if (mine > best && mine - best >= minGap)
            {
                found.Add(new Insight(kind, "good",
                    $"Dẫn đầu 16 đội về {label}: {string.Format(format, mine)}, hơn đội thứ hai "
                    + $"{string.Format(format, best)}.", 80));
            }
            else if (mine < worst && worst - mine >= minGap)
            {
                found.Add(new Insight(kind, "bad",
                    $"Thấp nhất 16 đội về {label}: {string.Format(format, mine)}, sau cả đội áp chót "
                    + $"{string.Format(format, worst)}.", 75));
            }
        }
    }

    /// <summary>
    /// Lấy được first blood là một chuyện, biến nó thành chiến thắng là chuyện khác. So winrate
    /// KHI có first blood với winrate chung của chính đội đó — mốc so phải là chính họ, vì so
    /// với mặt bằng thì đội mạnh lúc nào cũng trông như "biết tận dụng".
    /// </summary>
    private static void AddFirstBloodConversion(TeamFacts t, List<Insight> found)
    {
        if (t.WinWhenFbRate is not double whenFb || t.FirstBloodRate is not double fbRate) return;

        var lift = whenFb - t.Winrate;

        if (lift >= 12)
        {
            found.Add(new Insight("first-blood", "good",
                $"Biết biến lợi thế đầu trận thành chiến thắng: thắng {whenFb:0}% số ván lấy được "
                + $"first blood, cao hơn {lift:0} điểm so với tỷ lệ thắng chung ({t.Winrate:0}%). "
                + $"Họ lấy first blood ở {fbRate:0}% số ván.", 70));
        }
        else if (lift <= -8)
        {
            found.Add(new Insight("first-blood", "bad",
                $"Lấy được first blood nhưng không giữ được lợi thế: thắng {whenFb:0}% số ván có "
                + $"first blood, THẤP hơn {-lift:0} điểm so với tỷ lệ thắng chung ({t.Winrate:0}%).",
                68));
        }
    }

    /// <summary>
    /// Hạ gục nhiều mà không thắng, hoặc thắng mà không cần hạ gục nhiều. Đây là nhận định mà
    /// nhìn hai con số rời rạc trên bảng sẽ không bao giờ thấy.
    /// </summary>
    private static void AddKillsVersusWins(TeamFacts t, List<Insight> found)
    {
        if (t.KillDiff >= 2 && t.Winrate < 45)
        {
            found.Add(new Insight("kill-vs-win", "warn",
                $"Thắng giao tranh nhưng thua trận: hơn đối thủ {t.KillDiff:+0.0} mạng mỗi ván mà "
                + $"tỷ lệ thắng chỉ {t.Winrate:0}%. Dấu hiệu của việc đổi mạng lời nhưng không "
                + "chuyển hoá được thành mục tiêu.", 65));
        }
        else if (t.KillDiff <= 0 && t.Winrate >= 55)
        {
            found.Add(new Insight("kill-vs-win", "good",
                $"Thắng không cần hơn về mạng: chênh lệch hạ gục {t.KillDiff:+0.0} mỗi ván mà vẫn "
                + $"thắng {t.Winrate:0}%. Thường là lối chơi bám mục tiêu thay vì giao tranh.", 62));
        }
    }

    private static void AddPace(TeamFacts t, List<TeamFacts> peers, List<Insight> found)
    {
        if (peers.Count < 4 || t.AvgDurationMinutes <= 0) return;

        var median = Median(peers.Select(x => x.AvgDurationMinutes).ToList());
        var gap = t.AvgDurationMinutes - median;
        if (Math.Abs(gap) < 3) return;

        found.Add(new Insight("nhip-tran", "flat",
            gap < 0
                ? $"Đánh nhanh hơn mặt bằng: trung bình {t.AvgDurationMinutes:0.0} phút mỗi ván, "
                  + $"ngắn hơn trung vị giải {Math.Abs(gap):0.0} phút."
                : $"Kéo trận dài hơn mặt bằng: trung bình {t.AvgDurationMinutes:0.0} phút mỗi ván, "
                  + $"dài hơn trung vị giải {gap:0.0} phút.",
            40 + Math.Abs(gap)));
    }

    /// <summary>
    /// Chênh lệch Radiant/Dire chỉ đáng nói khi vượt được may rủi. Ở cỡ mẫu vài chục ván, lệch
    /// 20 điểm phần trăm vẫn hoàn toàn có thể là ngẫu nhiên — nên phải kiểm, không phải cứ thấy
    /// lệch là in ra.
    /// </summary>
    private static void AddSideSplit(TeamFacts t, List<Insight> found)
    {
        if (t.RadiantGames < 8 || t.DireGames < 8) return;

        var rad = t.RadiantWins * 100.0 / t.RadiantGames;
        var dire = t.DireWins * 100.0 / t.DireGames;
        var gap = rad - dire;

        // Sai số chuẩn của HIỆU hai tỷ lệ, ước lượng thận trọng ở p = 0,5.
        var se = Math.Sqrt(2500.0 / t.RadiantGames + 2500.0 / t.DireGames);
        if (Math.Abs(gap) < 1.96 * se) return;

        var strong = gap > 0 ? "Radiant" : "Dire";
        found.Add(new Insight("ben-san", "flat",
            $"Lệch rõ theo bên: thắng {rad:0}% khi ở Radiant ({t.RadiantGames} ván) so với "
            + $"{dire:0}% khi ở Dire ({t.DireGames} ván) — mạnh hơn hẳn ở bên {strong}, và cách "
            + "biệt này đã vượt mức giải thích được bằng may rủi.", 60));
    }

    /// <summary>Số đối thủ một đội có thể gặp — mẫu số của phép hiệu chỉnh so sánh bội.</summary>
    public const int OpponentCount = 15;

    /// <summary>
    /// Cặp đối đầu lệch nhất.
    ///
    /// CHỖ DỄ SAI NHẤT của cả bộ, và bản đầu đã sai: ngưỡng "thua ≥70% trong ≥4 ván" làm nhận
    /// định này bật cho 11/16 đội. Lý do không phải là 11 đội thật sự bị khắc chế, mà là ta
    /// chọn cặp CỰC ĐOAN NHẤT trong 15 đối thủ rồi chấm nó bằng ngưỡng dành cho một phép so
    /// duy nhất. Lấy cực trị của 15 lần thử thì gần như luôn có một cặp trông rất lệch — đó là
    /// lỗi so sánh bội, không phải phát hiện.
    ///
    /// Nên: vẫn nêu con số (nó hữu ích khi chuẩn bị cho giải), nhưng chỉ dùng chữ "khắc chế"
    /// khi vượt được ngưỡng ĐÃ HIỆU CHỈNH. Không đạt thì nói thẳng rằng phần lớn có thể là
    /// ngẫu nhiên, và hạ độ ưu tiên để nó không chiếm chỗ của nhận định thật.
    /// </summary>
    private static void AddNemesis(TeamFacts t, List<Insight> found)
    {
        if (t.NemesisName is null || t.NemesisGames < 5) return;

        var lossRate = t.NemesisLosses * 100.0 / t.NemesisGames;
        if (lossRate < 70) return;

        var wins = t.NemesisGames - t.NemesisLosses;
        var decisive = LowerTailAtHalf(t.NemesisGames, wins) < 0.05 / OpponentCount;

        found.Add(decisive
            ? new Insight("khac-tinh", "bad",
                $"Bị {t.NemesisName} khắc chế: thua {t.NemesisLosses}/{t.NemesisGames} ván gặp nhau "
                + "khi cả hai đều đúng đội hình TI2026. Cách biệt này vẫn đứng vững kể cả sau khi "
                + "tính tới việc đây là cặp lệch nhất trong 15 đối thủ.", 72)
            : new Insight("khac-tinh", "flat",
                $"Cặp đối đầu lệch nhất: thua {t.NemesisName} {t.NemesisLosses}/{t.NemesisGames} ván. "
                + "Đây là cặp lệch nhất trong 15 đối thủ nên phần lớn có thể chỉ là ngẫu nhiên — "
                + "chưa đủ để gọi là bị khắc chế.", 25));
    }

    /// <summary>
    /// P(thắng ≤ <paramref name="wins"/> trong <paramref name="games"/> ván) nếu hai đội thật sự
    /// ngang nhau. Tính chính xác bằng nhị thức — cỡ mẫu ở đây chỉ vài chục ván nên không cần
    /// xấp xỉ, và xấp xỉ chuẩn sai khá nhiều ở đuôi phân phối, đúng chỗ đang dùng.
    /// </summary>
    private static double LowerTailAtHalf(int games, int wins)
    {
        double total = 0, c = 1;
        for (var k = 0; k <= wins; k++)
        {
            total += c;
            c = c * (games - k) / (k + 1);
        }
        return total / Math.Pow(2, games);
    }

    private static double Median(List<double> values)
    {
        var s = values.OrderBy(v => v).ToList();
        var mid = s.Count / 2;
        return s.Count % 2 == 1 ? s[mid] : (s[mid - 1] + s[mid]) / 2;
    }
}
