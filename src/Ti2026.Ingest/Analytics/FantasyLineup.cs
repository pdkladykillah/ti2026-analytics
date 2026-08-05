namespace Ti2026.Ingest.Analytics;

/// <summary>Một ứng viên cho đội hình fantasy: điểm trung bình, vị trí, và ĐỘI.</summary>
public readonly record struct LineupCandidate(
    int PlayerId, string Nick, int? Position, int? TeamId, string? TeamName,
    double? Average, int Matches);

public readonly record struct LineupPick(string Slot, LineupCandidate Player);

public sealed record LineupResult(
    IReadOnlyList<LineupPick> Picks, double Total, IReadOnlyList<string> Shortfall);

/// <summary>
/// Xếp đội hình fantasy TI2026 theo đúng luật: MỘT cặp core (carry + offlane) CÙNG một đội,
/// MỘT cặp hỗ trợ (số 4 + số 5) CÙNG một đội, và một mid tự do.
///
/// "Cùng đội" là RÀNG BUỘC chứ không phải điểm thưởng, và đó là lý do phải có tệp này.
/// Cách cũ — lấy hai core điểm cao nhất bất kể đội nào — không phải là "chưa tối ưu", nó cho
/// ra một đội hình KHÔNG THỂ ĐĂNG KÝ được. Tệ hơn nữa, nó luôn cho tổng điểm CAO HƠN đáp án
/// hợp lệ, nên kết quả sai lại trông thuyết phục hơn kết quả đúng.
///
/// Bài toán vì thế không phải "chọn top N mỗi nhóm" mà là "chọn ĐỘI nào để lấy cặp". Giải TI
/// chỉ có 16 đội nên duyệt hết mọi đội là đủ, và đó là đáp án tối ưu THẬT chứ không phải một
/// phép xấp xỉ.
/// </summary>
public static class FantasyLineup
{
    public const int Carry = 1;
    public const int Mid = 2;
    public const int Offlane = 3;
    public const int Support4 = 4;
    public const int Support5 = 5;

    public static LineupResult Build(IEnumerable<LineupCandidate> candidates)
    {
        var all = candidates.Where(c => c.Average is not null).ToList();
        var picks = new List<LineupPick>();
        var shortfall = new List<string>();
        double total = 0;

        var pairable = all.Where(c => c.TeamId is int && c.Position is int).ToList();

        // MaxBy trên chuỗi struct trả về default() khi rỗng chứ KHÔNG trả null, nên phép kiểm
        // "is not null" trên nó luôn đúng và một đội thiếu người sẽ lặng lẽ ghép cặp với một
        // ứng viên rỗng mang 0 điểm. Ép sang nullable để chỗ rỗng thật sự thành null.
        static LineupCandidate? Best(IEnumerable<LineupCandidate> pool, int position) => pool
            .Where(c => c.Position == position)
            .Select(c => (LineupCandidate?)c)
            .MaxBy(c => c!.Value.Average);

        // Cặp cùng đội tốt nhất cho hai vị trí bắt buộc. Duyệt theo ĐỘI, không theo người —
        // đây chính là chỗ ràng buộc được áp.
        (LineupCandidate A, LineupCandidate B)? BestPair(int posA, int posB) => pairable
            .GroupBy(c => c.TeamId!.Value)
            .Select(t => new { A = Best(t, posA), B = Best(t, posB) })
            .Where(x => x.A is not null && x.B is not null)
            .OrderByDescending(x => (x.A!.Value.Average ?? 0) + (x.B!.Value.Average ?? 0))
            .Select(x => ((LineupCandidate, LineupCandidate)?)(x.A!.Value, x.B!.Value))
            .FirstOrDefault();

        void Take(string slot, LineupCandidate c)
        {
            picks.Add(new LineupPick(slot, c));
            total += c.Average ?? 0;
        }

        if (BestPair(Carry, Offlane) is var (carry, off))
        {
            Take("core", carry);
            Take("core", off);
        }
        else shortfall.Add("Không đội nào có đủ cả carry lẫn offlane đạt ngưỡng mẫu để ghép cặp");

        if (BestPair(Support4, Support5) is var (s4, s5))
        {
            Take("support", s4);
            Take("support", s5);
        }
        else shortfall.Add("Không đội nào có đủ cả hỗ trợ 4 lẫn hỗ trợ 5 đạt ngưỡng mẫu để ghép cặp");

        // Mid là suất TỰ DO: không ràng buộc đội, nên chọn thẳng người điểm cao nhất.
        if (Best(all, Mid) is { } mid)
            Take("mid", mid);
        else
            shortfall.Add("Chưa có mid nào đạt ngưỡng mẫu");

        return new LineupResult(picks, Math.Round(total, 2), shortfall);
    }
}
