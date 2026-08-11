namespace Ti2026.Ingest.Analytics;

/// <summary>Số liệu đối đầu với một hero.</summary>
public readonly record struct FacedHero(int HeroId, string Name, int Games, int Wins);

/// <param name="Edge">Tỷ lệ thắng khi gặp hero này, trừ đi tỷ lệ thắng nền. Âm = hero khắc bạn.</param>
public readonly record struct NemesisLine(
    int HeroId, string Name, int Games, int Wins, double Winrate, double Edge, bool Notable);

/// <summary>
/// HERO NÀO KHẮC CHẾ BẠN — và vì sao câu này khó hơn vẻ ngoài của nó.
///
/// SỐ LẦN BỊ GIẾT LÀ CÁI BẪY. Cách hiển nhiên là đếm hero nào giết ta nhiều nhất. Nhưng nó chỉ
/// đo hero nào PHỔ BIẾN: gặp Pudge 400 lần thì tất nhiên Pudge giết ta nhiều hơn một hero ta chỉ
/// gặp 40 lần. Phải có mẫu số.
///
/// Mẫu số đó — số ván ĐỐI ĐẦU từng hero — chính là thứ đắt: tự đếm thì phải lấy lại chi tiết cả
/// gần mười nghìn ván để biết năm hero phe địch mỗi ván. OpenDota đã đếm sẵn ở
/// players/{id}/heroes, một lời gọi cho mỗi người.
///
/// MỐC SO LÀ TỶ LỆ THẮNG NỀN CỦA CHÍNH NGƯỜI ĐÓ, không phải 50%. Người thắng 55% mà gặp hero X
/// chỉ thắng 52% thì hero đó vẫn đang khắc họ, dù 52% nghe như trên trung bình.
///
/// VÀ LẠI LÀ SO SÁNH BỘI. 127 hero là 127 phép so; chấm bằng ngưỡng của một phép so duy nhất thì
/// riêng may rủi đã đủ tạo ra vài "khắc tinh". Dùng đúng bộ công cụ đã dựng cho hero pool:
/// nhị thức chính xác so với tỷ lệ nền, rồi hiệu chỉnh Benjamini–Hochberg.
///
/// GIỚI HẠN PHẢI NÓI RA: đây là ĐỐI ĐẦU, không phải nhân quả. Một hero khắc bạn có thể vì bản
/// thân nó mạnh ở bậc rank này, chứ không phải vì nó khắc riêng lối chơi của bạn — phần đó phải
/// đặt cạnh tỷ lệ thắng chung của hero để tách ra, và trang đã có sẵn con số đó ở bảng hero pool.
/// </summary>
public static class NemesisHeroes
{
    /// <summary>Dưới ngần này ván đối đầu thì mọi tỷ lệ đều là nhiễu.</summary>
    public const int MinGames = 60;

    /// <summary>Chênh dưới ngần này điểm phần trăm thì không đáng nói, dù mẫu lớn tới đâu.</summary>
    public const double MinEdge = 4.0;

    /// <param name="baseWinrate">Tỷ lệ thắng nền của chính người đó, tính theo phần trăm.</param>
    public static List<NemesisLine> Read(
        IEnumerable<FacedHero> faced, double baseWinrate, double q = MultipleTests.DefaultQ)
    {
        var rows = faced.Where(h => h.Games >= MinGames).ToList();
        if (rows.Count == 0) return [];

        var p0 = Math.Clamp(baseWinrate / 100.0, 0.01, 0.99);

        var tails = rows
            .Select(h => MultipleTests.BinomialTwoSided(h.Games, h.Wins, p0))
            .ToList();

        var discovered = MultipleTests.BenjaminiHochberg(tails, q);

        return rows
            .Select((h, i) =>
            {
                var wr = h.Wins * 100.0 / h.Games;
                var edge = wr - baseWinrate;

                return new NemesisLine(
                    h.HeroId, h.Name, h.Games, h.Wins,
                    Math.Round(wr, 1), Math.Round(edge, 1),
                    discovered[i] && Math.Abs(edge) >= MinEdge);
            })
            .OrderBy(h => h.Edge)
            .ToList();
    }
}
