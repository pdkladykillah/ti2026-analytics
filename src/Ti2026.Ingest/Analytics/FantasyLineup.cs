namespace Ti2026.Ingest.Analytics;

/// <summary>
/// Một ứng viên cho đội hình fantasy.
///
/// <see cref="PrefixPct"/> là tỷ lệ ván người này chơi hero thuộc từng nhóm màu; nó quyết định
/// prefix nào đáng chọn. <see cref="SuffixRate"/> là tỷ lệ ván của người này thoả từng điều
/// kiện suffix — phụ thuộc ĐỘI của họ, vì đội hay thua thì "the Underdog" ăn nhiều hơn, đội
/// kết thúc nhanh thì "the Decisive" ăn nhiều hơn.
///
/// Cả hai để null nghĩa là chưa có dữ liệu; khi đó người này vẫn được xếp đội hình nhưng
/// không đóng góp gì vào việc chọn danh hiệu.
/// </summary>
public readonly record struct LineupCandidate(
    int PlayerId, string Nick, int? Position, int? TeamId, string? TeamName,
    double? Average, int Matches,
    IReadOnlyDictionary<string, double>? PrefixPct = null,
    IReadOnlyDictionary<string, double>? SuffixRate = null);

public readonly record struct LineupPick(string Slot, LineupCandidate Player);

/// <summary>Danh hiệu đã chọn kèm phần điểm nó mang lại cho ĐÚNG đội hình này.</summary>
public readonly record struct TitleChoice(string Key, double BonusPercent, double ExpectedPoints);

public sealed record LineupResult(
    IReadOnlyList<LineupPick> Picks,
    double BasePoints,
    double Total,
    TitleChoice? Prefix,
    TitleChoice? Suffix,
    IReadOnlyList<string> Shortfall);

/// <summary>
/// Xếp đội hình fantasy TI2026 theo đúng luật: MỘT cặp core (carry + offlane) CÙNG một đội,
/// MỘT cặp hỗ trợ (số 4 + số 5) CÙNG một đội, và một mid tự do.
///
/// "Cùng đội" là RÀNG BUỘC chứ không phải điểm thưởng. Cách cũ — lấy hai core điểm cao nhất
/// bất kể đội nào — cho ra đội hình KHÔNG THỂ ĐĂNG KÝ, và luôn cho tổng CAO HƠN đáp án hợp lệ
/// nên kết quả sai lại trông thuyết phục hơn.
///
/// DANH HIỆU PHẢI CHỌN CÙNG LÚC VỚI ĐỘI HÌNH, KHÔNG PHẢI SAU.
///
/// Đây là chỗ bản trước còn sai. Nó chọn xong năm người rồi mới đi tìm prefix hợp nhất — nhưng
/// một prefix áp cho CẢ NĂM người, nên giá trị của nó phụ thuộc vào việc chọn ai. Chọn tách rời
/// là bỏ qua đúng sự phụ thuộc ấy: một cặp hỗ trợ kém hơn 200 điểm nhưng hero pool ăn khớp với
/// prefix mà ba người kia đang dùng có thể cho tổng cao hơn.
///
/// Cách giải: duyệt mọi tổ hợp (đội lấy cặp core) × (đội lấy cặp hỗ trợ) × (mid), với mỗi tổ
/// hợp thì chọn prefix và suffix tốt nhất CHO CHÍNH tổ hợp đó, rồi lấy tổng lớn nhất. Chỉ 16
/// đội nên số tổ hợp vẫn nhỏ, và đây là đáp án tối ưu THẬT chứ không phải xấp xỉ.
///
/// TIER VÀ TRAIT CỐ TÌNH KHÔNG THAM GIA. Chúng là thứ QUAY TRÚNG chứ không phải thứ chọn
/// được, và chúng nhân vào mọi ứng viên theo cùng một cách — nên đưa vào đây không đổi được
/// NÊN CHỌN AI, chỉ làm con số phồng lên và trông như một dự báo chắc chắn hơn thực tế.
/// </summary>
public static class FantasyLineup
{
    public const int Carry = 1;
    public const int Mid = 2;
    public const int Offlane = 3;
    public const int Support4 = 4;
    public const int Support5 = 5;

    public static LineupResult Build(
        IEnumerable<LineupCandidate> candidates,
        IReadOnlyDictionary<string, double>? prefixBonuses = null,
        IReadOnlyDictionary<string, double>? suffixBonuses = null)
    {
        var all = candidates.Where(c => c.Average is not null).ToList();
        var shortfall = new List<string>();

        var pairable = all.Where(c => c.TeamId is int && c.Position is int).ToList();

        // MaxBy trên chuỗi struct trả về default() khi rỗng chứ KHÔNG trả null, nên phép kiểm
        // "is not null" trên nó luôn đúng và một đội thiếu người sẽ lặng lẽ ghép cặp với một
        // ứng viên rỗng mang 0 điểm. Ép sang nullable để chỗ rỗng thật sự thành null.
        static LineupCandidate? Best(IEnumerable<LineupCandidate> pool, int position) => pool
            .Where(c => c.Position == position)
            .Select(c => (LineupCandidate?)c)
            .MaxBy(c => c!.Value.Average);

        // Mỗi đội góp TỐI ĐA một cặp. Roster thật chỉ có một carry và một offlane, nên "người
        // giỏi nhất ở vị trí đó trong đội" gần như luôn là lựa chọn duy nhất.
        var corePairs = pairable
            .GroupBy(c => c.TeamId!.Value)
            .Select(t => (A: Best(t, Carry), B: Best(t, Offlane)))
            .Where(x => x.A is not null && x.B is not null)
            .Select(x => (x.A!.Value, x.B!.Value))
            .ToList();

        var supportPairs = pairable
            .GroupBy(c => c.TeamId!.Value)
            .Select(t => (A: Best(t, Support4), B: Best(t, Support5)))
            .Where(x => x.A is not null && x.B is not null)
            .Select(x => (x.A!.Value, x.B!.Value))
            .ToList();

        var mids = all.Where(c => c.Position == Mid).ToList();

        if (corePairs.Count == 0)
            shortfall.Add("Không đội nào có đủ cả carry lẫn offlane đạt ngưỡng mẫu để ghép cặp");
        if (supportPairs.Count == 0)
            shortfall.Add("Không đội nào có đủ cả hỗ trợ 4 lẫn hỗ trợ 5 đạt ngưỡng mẫu để ghép cặp");
        if (mids.Count == 0)
            shortfall.Add("Chưa có mid nào đạt ngưỡng mẫu");

        // Thiếu bất kỳ suất nào thì không có tổ hợp nào để duyệt — trả về phần ghép được, đúng
        // như trước, thay vì im lặng trả đội hình rỗng.
        if (corePairs.Count == 0 || supportPairs.Count == 0 || mids.Count == 0)
            return Partial(corePairs, supportPairs, mids, shortfall);

        LineupResult? best = null;

        foreach (var (carry, off) in corePairs)
        foreach (var (s4, s5) in supportPairs)
        foreach (var mid in mids)
        {
            var five = new[] { carry, off, s4, s5, mid };
            var basePoints = five.Sum(p => p.Average ?? 0);

            var prefix = BestTitle(five, prefixBonuses, p => p.PrefixPct);
            var suffix = BestTitle(five, suffixBonuses, p => p.SuffixRate);

            var total = basePoints
                        + (prefix?.ExpectedPoints ?? 0)
                        + (suffix?.ExpectedPoints ?? 0);

            if (best is not null && total <= best.Total) continue;

            best = new LineupResult(
                [
                    new LineupPick("core", carry), new LineupPick("core", off),
                    new LineupPick("support", s4), new LineupPick("support", s5),
                    new LineupPick("mid", mid),
                ],
                Math.Round(basePoints, 2), Math.Round(total, 2), prefix, suffix, shortfall);
        }

        return best!;
    }

    /// <summary>
    /// Danh hiệu tốt nhất cho ĐÚNG năm người này.
    ///
    /// Lợi kỳ vọng cộng theo ĐIỂM chứ không lấy trung bình tỷ lệ: cùng một danh hiệu, hợp với
    /// người ghi 5000 điểm đáng hơn hẳn hợp với người ghi 800 điểm.
    ///
    /// Người chưa có dữ liệu bị bỏ qua chứ không tính tỷ lệ 0 — tính 0 thì một đội hình chưa có
    /// dữ liệu trông y hệt đội hình toàn người không bao giờ thoả điều kiện.
    /// </summary>
    private static TitleChoice? BestTitle(
        IReadOnlyList<LineupCandidate> five,
        IReadOnlyDictionary<string, double>? bonuses,
        Func<LineupCandidate, IReadOnlyDictionary<string, double>?> rateOf)
    {
        if (bonuses is null || bonuses.Count == 0) return null;
        if (five.All(p => rateOf(p) is null)) return null;

        TitleChoice? best = null;

        foreach (var (key, bonus) in bonuses)
        {
            var gain = five.Sum(p =>
            {
                var rates = rateOf(p);
                if (rates is null) return 0;
                return (p.Average ?? 0) * bonus / 100 * rates.GetValueOrDefault(key) / 100;
            });

            if (best is null || gain > best.Value.ExpectedPoints)
                best = new TitleChoice(key, bonus, Math.Round(gain, 2));
        }

        return best;
    }

    /// <summary>Ghép được suất nào lấy suất đó, khi không đủ cả ba để duyệt tổ hợp.</summary>
    private static LineupResult Partial(
        List<(LineupCandidate A, LineupCandidate B)> corePairs,
        List<(LineupCandidate A, LineupCandidate B)> supportPairs,
        List<LineupCandidate> mids,
        List<string> shortfall)
    {
        var picks = new List<LineupPick>();
        double total = 0;

        void TakePair(List<(LineupCandidate A, LineupCandidate B)> pairs, string slot)
        {
            if (pairs.Count == 0) return;
            var top = pairs.MaxBy(p => (p.A.Average ?? 0) + (p.B.Average ?? 0));
            picks.Add(new LineupPick(slot, top.A));
            picks.Add(new LineupPick(slot, top.B));
            total += (top.A.Average ?? 0) + (top.B.Average ?? 0);
        }

        TakePair(corePairs, "core");
        TakePair(supportPairs, "support");

        if (mids.Count > 0)
        {
            var mid = mids.MaxBy(m => m.Average)!;
            picks.Add(new LineupPick("mid", mid));
            total += mid.Average ?? 0;
        }

        return new LineupResult(
            picks, Math.Round(total, 2), Math.Round(total, 2), null, null, shortfall);
    }
}
