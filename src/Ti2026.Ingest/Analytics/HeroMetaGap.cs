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
/// VẪN LÀ SO SÁNH BỘI, NHƯNG HIỆU CHỈNH BẰNG BENJAMINI–HOCHBERG chứ không phải Bonferroni. Đo
/// trên pool thật 125 hero: Bonferroni cho ngưỡng 0,0004, và Invoker 9 thắng/34 ván so với mức
/// chung 51,9% — lệch hơn ba lần sai số chuẩn — vẫn trượt. Không hero nào bật, tức cột "đáng kể"
/// vô dụng ngang với không có. Xem <see cref="MultipleTests"/> để biết vì sao hai câu hỏi khác
/// nhau cần hai phép hiệu chỉnh khác nhau.
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

        var tails = rows
            .Select(h => TwoSidedTail(h.Games, h.Wins, metaWinrate[h.HeroId] / 100.0))
            .ToList();

        var discovered = MultipleTests.BenjaminiHochberg(tails);

        return rows
            .Select((h, i) =>
            {
                var meta = metaWinrate[h.HeroId];
                var mine = h.Wins * 100.0 / h.Games;
                var edge = mine - meta;

                // Cần CẢ hai: tách được khỏi nhiễu, VÀ đủ lớn để đáng nói. Ở vài trăm ván, 1,5
                // điểm phần trăm có thể qua được phép kiểm mà chẳng có nghĩa gì với người đọc.
                var notable = discovered[i] && Math.Abs(edge) >= MinEdge;

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

    /// <summary>Nhị thức hai phía. Chỉ là lối vào của <see cref="MultipleTests.BinomialTwoSided"/>.</summary>
    public static double TwoSidedTail(int n, int k, double p) =>
        MultipleTests.BinomialTwoSided(n, k, p);
}
