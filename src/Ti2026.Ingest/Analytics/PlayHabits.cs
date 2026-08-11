namespace Ti2026.Ingest.Analytics;

/// <summary>Một ván, rút gọn còn những gì cần để hỏi "chơi lúc nào thì hay".</summary>
public readonly record struct HabitGame(
    DateTime StartTime, int DurationSeconds, bool Won, int? PctFarm, int? PctDeaths);

/// <param name="FarmGap">Phân vị ăn lính sau khi THUA trừ đi sau khi THẮNG. Âm = tụt sau khi thua.</param>
/// <param name="SurviveGap">Như trên với việc giữ mạng.</param>
public readonly record struct TiltReading(
    int GamesAfterWin, int GamesAfterLoss,
    int FarmAfterWin, int FarmAfterLoss, int FarmGap, double FarmP,
    int SurviveAfterWin, int SurviveAfterLoss, int SurviveGap, double SurviveP);

/// <param name="Position">Ván thứ mấy trong phiên. Nhóm cuối gộp mọi ván từ đó trở đi.</param>
public readonly record struct StreakRow(
    int Position, bool IsTail, int Games, int Farm, int Survive, double Winrate);

/// <param name="Hour">Giờ bắt đầu của khối 4 tiếng, theo giờ địa phương.</param>
public readonly record struct HourRow(int Hour, int Games, int Farm, int Survive, double Winrate);

public readonly record struct HabitReading(
    int Sessions, double GamesPerSession, int Rated,
    TiltReading? Tilt, List<StreakRow> ByPosition, List<HourRow> ByHour, string Text);

/// <summary>
/// KHI NÀO BẠN CHƠI HAY NHẤT — ba câu hỏi về hành vi, không phải về kỹ năng.
///
/// Toàn bộ phần này chạy trên dữ liệu ĐÃ CÓ: thời điểm bắt đầu, độ dài ván, thắng thua, phân vị.
/// Không thêm một lời gọi nào, và phủ toàn bộ lịch sử chứ không phải riêng phần đã parse.
///
/// ĐO BẰNG PHÂN VỊ, KHÔNG ĐO BẰNG TỶ LỆ THẮNG. Đây là điều khiến cả phần này có nghĩa. Hệ thống
/// ghép trận ghim tỷ lệ thắng quanh 50% bất kể người ta chơi hay hay dở — cả hai tài khoản trong
/// dự án đều 50,1% và 50,2% qua gần mười nghìn ván. Nên nếu hỏi "thua rồi có chơi tệ đi không"
/// bằng tỷ lệ thắng thì câu trả lời sẽ luôn là không, kể cả khi có. Phân vị thì so người này với
/// người khác CÙNG HERO, nên nó đo chính đầu ra của người chơi.
///
/// CHỌN HAI CỘT CÓ TÊN, KHÔNG GỘP THÀNH ĐIỂM TỔNG. Ăn lính vì nó phủ rộng nhất và phụ thuộc ít
/// nhất vào chín người còn lại; giữ mạng vì tilt trong Dota thể hiện ra đúng ở đó. Gộp mọi cột
/// thành một con số thì trọng số là do người viết chọn chứ không có trong dữ liệu.
/// </summary>
public static class PlayHabits
{
    /// <summary>
    /// Nghỉ quá ngần này phút thì tính là sang phiên mới.
    ///
    /// CON SỐ NÀY LÀ ĐO ĐƯỢC, KHÔNG PHẢI ĐOÁN. Phân bố khoảng nghỉ giữa hai ván có khe rất rõ:
    /// trung vị 16 phút, phân vị 60 là 43 phút, rồi nhảy vọt lên 245 phút ở phân vị 70. Tức người
    /// ta hoặc chơi liền tay hoặc nghỉ hẳn nhiều tiếng, gần như không có gì ở giữa.
    ///
    /// Nhờ vậy kết quả gần như KHÔNG phụ thuộc vào lựa chọn này: ngưỡng 60 phút gom 62,1% khoảng
    /// nghỉ vào cùng phiên, ngưỡng 180 phút gom 67,9% — chênh 5,8 điểm trên toàn bộ dải. Chọn 90
    /// phút vì nó nằm giữa khe.
    /// </summary>
    public const int SessionBreakMinutes = 90;

    /// <summary>
    /// Lệch giờ so với UTC khi xếp ván theo giờ trong ngày.
    ///
    /// Viết cứng +7 vì đây là trang tiếng Việt cho người chơi ở Việt Nam, và "chơi lúc 2 giờ sáng"
    /// chỉ có nghĩa theo giờ người đó sống. Nếu về sau theo dõi người ở múi giờ khác thì phải đưa
    /// vào tracked-players.json chứ không được để nguyên — một biểu đồ giờ sai múi thì không chỉ
    /// lệch mà còn đảo hẳn kết luận ngày/đêm.
    /// </summary>
    public const int LocalUtcOffsetHours = 7;

    /// <summary>Số ván tối thiểu mỗi nhóm để một phép so đáng làm.</summary>
    public const int MinGamesPerGroup = 60;

    /// <summary>Ván thứ này trở đi thì gộp vào một nhóm cuối — càng về sau mẫu càng mỏng.</summary>
    public const int TailPosition = 5;

    /// <summary>Chênh dưới ngần này điểm phân vị thì không đáng nói.</summary>
    public const int MinGap = 4;

    public static HabitReading? Read(IReadOnlyList<HabitGame> games)
    {
        var ordered = games.Where(g => g.DurationSeconds > 0)
            .OrderBy(g => g.StartTime).ToList();

        if (ordered.Count < MinGamesPerGroup * 2) return null;

        // Gắn số thứ tự trong phiên và kết quả ván LIỀN TRƯỚC trong cùng phiên.
        var pos = new int[ordered.Count];
        var prevWon = new bool?[ordered.Count];
        var sessions = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            if (i == 0) { pos[i] = 1; sessions = 1; continue; }

            var prevEnd = ordered[i - 1].StartTime.AddSeconds(ordered[i - 1].DurationSeconds);
            var gap = (ordered[i].StartTime - prevEnd).TotalMinutes;

            if (gap >= 0 && gap <= SessionBreakMinutes)
            {
                pos[i] = pos[i - 1] + 1;
                prevWon[i] = ordered[i - 1].Won;
            }
            else
            {
                pos[i] = 1;
                sessions++;
            }
        }

        var rated = ordered.Count(g => g.PctFarm is int);

        return new HabitReading(
            sessions,
            Math.Round((double)ordered.Count / Math.Max(sessions, 1), 2),
            rated,
            Tilt(ordered, pos, prevWon),
            ByPosition(ordered, pos),
            ByHour(ordered),
            "");
    }

    /// <summary>
    /// Thua rồi có chơi tệ đi không — KHỚP THEO SỐ THỨ TỰ TRONG PHIÊN.
    ///
    /// Vì sao phải khớp: ván đi sau trong phiên vừa dễ là ván "sau khi thua" hơn, vừa là ván
    /// người ta đã mỏi hơn. Không khớp thì phép so sẽ trộn hai hiệu ứng và tính cả phần mỏi vào
    /// phần tilt. Đây đúng loại nhiễu đã làm đảo dấu kết quả ở phần khác của trang khi bỏ qua độ
    /// dài ván, nên lần này khớp ngay từ đầu.
    ///
    /// Cách khớp: tính chênh lệch RIÊNG trong từng nhóm cùng thứ tự, rồi gộp lại có trọng số.
    /// </summary>
    private static TiltReading? Tilt(List<HabitGame> g, int[] pos, bool?[] prevWon)
    {
        var afterWin = new List<HabitGame>();
        var afterLoss = new List<HabitGame>();

        // Trọng số cho phần gộp: mỗi nhóm thứ tự góp theo số ván nhỏ hơn giữa hai bên.
        double wFarm = 0, sFarm = 0, wSurv = 0, sSurv = 0;

        for (var p = 2; p <= 12; p++)
        {
            var win = new List<HabitGame>();
            var loss = new List<HabitGame>();

            for (var i = 0; i < g.Count; i++)
            {
                if (pos[i] != p || prevWon[i] is not bool pw) continue;
                (pw ? win : loss).Add(g[i]);
            }

            afterWin.AddRange(win);
            afterLoss.AddRange(loss);

            AddStratum(win, loss, x => x.PctFarm, ref wFarm, ref sFarm);
            AddStratum(win, loss, x => x.PctDeaths is int d ? 100 - d : null, ref wSurv, ref sSurv);
        }

        if (afterWin.Count < MinGamesPerGroup || afterLoss.Count < MinGamesPerGroup) return null;

        var fWin = Med(afterWin, x => x.PctFarm);
        var fLoss = Med(afterLoss, x => x.PctFarm);
        var sWin = Med(afterWin, x => x.PctDeaths is int d ? 100 - d : null);
        var sLoss = Med(afterLoss, x => x.PctDeaths is int d ? 100 - d : null);

        return new TiltReading(
            afterWin.Count, afterLoss.Count,
            fWin, fLoss, (int)Math.Round(wFarm > 0 ? sFarm / wFarm : 0),
            Test(afterWin, afterLoss, x => x.PctFarm),
            sWin, sLoss, (int)Math.Round(wSurv > 0 ? sSurv / wSurv : 0),
            Test(afterWin, afterLoss, x => x.PctDeaths is int d ? 100 - d : null));
    }

    /// <summary>Góp chênh lệch của một nhóm cùng thứ tự vào tổng có trọng số.</summary>
    private static void AddStratum(
        List<HabitGame> win, List<HabitGame> loss, Func<HabitGame, int?> pick,
        ref double weight, ref double sum)
    {
        var a = win.Select(pick).OfType<int>().ToList();
        var b = loss.Select(pick).OfType<int>().ToList();
        if (a.Count < 10 || b.Count < 10) return;

        var w = Math.Min(a.Count, b.Count);
        weight += w;
        sum += w * (Median(b) - Median(a));
    }

    private static List<StreakRow> ByPosition(List<HabitGame> g, int[] pos)
    {
        var rows = new List<StreakRow>();

        for (var p = 1; p <= TailPosition; p++)
        {
            var tail = p == TailPosition;
            var set = Enumerable.Range(0, g.Count)
                .Where(i => tail ? pos[i] >= p : pos[i] == p)
                .Select(i => g[i]).ToList();

            if (set.Count < MinGamesPerGroup) continue;

            rows.Add(new StreakRow(p, tail, set.Count,
                Med(set, x => x.PctFarm),
                Med(set, x => x.PctDeaths is int d ? 100 - d : null),
                Math.Round(set.Count(x => x.Won) * 100.0 / set.Count, 1)));
        }

        return rows;
    }

    /// <summary>
    /// Xếp theo KHỐI 4 TIẾNG, không theo từng giờ.
    ///
    /// 24 giờ là 24 phép so, và với vài nghìn ván thì mỗi giờ chỉ còn vài trăm — vừa mỏng vừa
    /// dính lỗi so sánh bội. Sáu khối 4 tiếng thì mỗi khối dày gấp bốn và số phép so giảm bốn lần.
    /// </summary>
    private static List<HourRow> ByHour(List<HabitGame> g)
    {
        return g
            .GroupBy(x => x.StartTime.AddHours(LocalUtcOffsetHours).Hour / 4 * 4)
            .Where(b => b.Count() >= MinGamesPerGroup)
            .OrderBy(b => b.Key)
            .Select(b => new HourRow(b.Key, b.Count(),
                Med(b.ToList(), x => x.PctFarm),
                Med(b.ToList(), x => x.PctDeaths is int d ? 100 - d : null),
                Math.Round(b.Count(x => x.Won) * 100.0 / b.Count(), 1)))
            .ToList();
    }

    private static int Med(List<HabitGame> g, Func<HabitGame, int?> pick)
    {
        var v = g.Select(pick).OfType<int>().ToList();
        return v.Count == 0 ? 0 : Median(v);
    }

    private static int Median(List<int> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return s.Count % 2 == 1
            ? s[s.Count / 2]
            : (int)Math.Round((s[s.Count / 2 - 1] + s[s.Count / 2]) / 2.0);
    }

    private static double Test(
        List<HabitGame> a, List<HabitGame> b, Func<HabitGame, int?> pick) =>
        DeathEffect.MannWhitney(
            a.Select(pick).OfType<int>().ToList(),
            b.Select(pick).OfType<int>().ToList());
}
