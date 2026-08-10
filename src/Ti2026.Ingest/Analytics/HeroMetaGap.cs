namespace Ti2026.Ingest.Analytics;

/// <summary>Một hero trong pool, đặt cạnh mức chung của chính hero đó.</summary>
/// <param name="MetaWinrate">Tỷ lệ thắng của hero đó ở bậc rank cao, trên toàn bộ người chơi.</param>
/// <param name="Edge">Bạn hơn (hoặc kém) mức chung của hero đó bao nhiêu điểm phần trăm.</param>
public readonly record struct MetaHero(
    int HeroId, string Name, int Games, int Wins, double Winrate,
    double MetaWinrate, double Edge, bool Notable);

/// <summary>
/// So hero pool với META, và điểm mấu chốt là so với mức chung CỦA CHÍNH HERO ĐÓ.
///
/// VÌ SAO KHÔNG SO VỚI 50%. Bộ nhận định hero hiện có kiểm mọi hero với mốc 50%, và mốc đó sai
/// một cách âm thầm: Huskar thắng 53% trên toàn bộ người chơi bậc cao, Broodmother 47%. Thắng
/// 53% với Huskar là ĐÚNG BẰNG mọi người, còn thắng 50% với Broodmother là hơn hẳn mức chung —
/// nhưng mốc 50% sẽ khen ngược lại cả hai. Hero mạnh sẵn thì ai chơi cũng thắng; điều đáng biết
/// là bạn có hơn được mức đó không.
///
/// Nên phép kiểm ở đây là nhị thức với p₀ = tỷ lệ thắng chung của hero, chứ không phải p₀ = 0,5.
///
/// VẪN LÀ SO SÁNH BỘI. Chọn hero "bạn chơi hơn người" trong một pool 40 hero là lấy cực trị của
/// 40 phép so, nên ngưỡng phải chia cho cỡ pool — giống hệt <see cref="PlayerInsights.Notable"/>.
///
/// GIỚI HẠN PHẢI NÓI RA: mốc chung lấy ở bậc rank cao của OpenDota, gộp mọi vị trí và mọi bản
/// game gần đây. Nó KHÔNG khớp chính xác với bậc rank của người dùng, và một hero đổi mạnh yếu
/// theo bản thì mốc cũng trôi theo. Đây là mốc để định hướng, không phải một chuẩn để chấm điểm.
/// </summary>
public static class HeroMetaGap
{
    /// <summary>Dưới ngần này ván trên một hero thì mọi so sánh đều là nhiễu.</summary>
    public const int MinGames = 10;

    /// <summary>Chênh dưới ngần này điểm phần trăm thì không đáng nói, dù mẫu có lớn tới đâu.</summary>
    public const double MinEdge = 4.0;

    /// <summary>Hero mạnh mà bạn chơi dưới ngần này ván thì coi như chưa có trong pool.</summary>
    public const int UntouchedBelow = 5;

    /// <param name="pool">(heroId, tên, số ván, số thắng) của người chơi.</param>
    /// <param name="metaWinrate">heroId → tỷ lệ thắng chung ở bậc cao. Thiếu hero nào thì bỏ qua.</param>
    public static List<MetaHero> Read(
        IEnumerable<(int HeroId, string Name, int Games, int Wins)> pool,
        IReadOnlyDictionary<int, double> metaWinrate)
    {
        var rows = pool.Where(h => h.Games >= MinGames && metaWinrate.ContainsKey(h.HeroId)).ToList();
        if (rows.Count == 0) return [];

        var alpha = 0.05 / rows.Count;

        return rows
            .Select(h =>
            {
                var meta = metaWinrate[h.HeroId];
                var mine = h.Wins * 100.0 / h.Games;
                var edge = mine - meta;

                var tail = TwoSidedTail(h.Games, h.Wins, meta / 100.0);
                var notable = tail < alpha && Math.Abs(edge) >= MinEdge;

                return new MetaHero(
                    h.HeroId, h.Name, h.Games, h.Wins,
                    Math.Round(mine, 1), Math.Round(meta, 1), Math.Round(edge, 1), notable);
            })
            .OrderByDescending(h => h.Edge)
            .ToList();
    }

    /// <summary>
    /// Những hero đang mạnh trong meta mà pool gần như chưa đụng tới.
    ///
    /// Cố ý KHÔNG gọi là "nên học hero này". Một hero mạnh trong meta chung chưa chắc hợp với
    /// vị trí hay lối chơi của một người cụ thể, và trang không có cách nào biết điều đó.
    /// </summary>
    public static List<(int HeroId, double MetaWinrate)> Untouched(
        IReadOnlyDictionary<int, int> gamesPerHero,
        IReadOnlyDictionary<int, double> metaWinrate,
        int take = 5)
    {
        return metaWinrate
            .Where(m => gamesPerHero.GetValueOrDefault(m.Key) < UntouchedBelow)
            .OrderByDescending(m => m.Value)
            .Take(take)
            .Select(m => (m.Key, m.Value))
            .ToList();
    }

    /// <summary>
    /// Xác suất hai phía của việc lệch khỏi p₀ ít nhất bằng mức đã quan sát, theo nhị thức chính xác.
    ///
    /// TÍNH TRONG KHÔNG GIAN LOGARIT. Cách viết thẳng — bắt đầu từ (1−p)ⁿ rồi nhân dần — tràn số
    /// xuống 0 ngay khi n vài trăm, và điều nguy hiểm là nó tràn ÂM THẦM: mọi số hạng sau đó đều
    /// bằng 0, tổng bằng 0, và bằng 0 thì luôn "đáng nói". Tức là ở đúng những hero chơi nhiều
    /// nhất — nơi có nhiều dữ liệu nhất — hàm sẽ tuyên bố mọi thứ đều có ý nghĩa.
    /// </summary>
    public static double TwoSidedTail(int n, int k, double p)
    {
        if (n <= 0) return 1;
        if (p <= 0) return k > 0 ? 0 : 1;
        if (p >= 1) return k < n ? 0 : 1;

        var logs = new double[n + 1];
        var logP = Math.Log(p);
        var logQ = Math.Log(1 - p);

        logs[0] = n * logQ;
        for (var i = 0; i < n; i++)
            logs[i + 1] = logs[i] + Math.Log((double)(n - i) / (i + 1)) + logP - logQ;

        // Mọi kết cục KHÔNG có khả năng xảy ra cao hơn kết cục đã quan sát. Đây là định nghĩa
        // hai phía của Fisher — đúng cả khi phân phối lệch, khác với lối nhân đôi một phía.
        var observed = logs[k] + 1e-9;
        var acc = double.NegativeInfinity;

        for (var i = 0; i <= n; i++)
            if (logs[i] <= observed)
                acc = LogAdd(acc, logs[i]);

        return Math.Min(1, Math.Exp(acc));
    }

    private static double LogAdd(double a, double b)
    {
        if (double.IsNegativeInfinity(a)) return b;
        if (double.IsNegativeInfinity(b)) return a;

        var hi = Math.Max(a, b);
        return hi + Math.Log(1 + Math.Exp(-Math.Abs(a - b)));
    }
}
