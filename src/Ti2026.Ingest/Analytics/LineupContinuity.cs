namespace Ti2026.Ingest.Analytics;

/// <summary>Một ván đối đầu, kèm số người của ĐỘI HÌNH TI2026 thực sự có mặt ở mỗi bên.</summary>
/// <param name="KeptA">Số người của đội A (slug đứng trước theo alphabet) đã ra trận ván đó.</param>
public readonly record struct H2hGame(
    string Date, string League, string? WinnerSlug, int KeptA, int KeptB);

/// <param name="Basis">dung-doi-hinh | lech-1-nguoi | khong-du</param>
/// <param name="Decisive">Cách biệt có vượt được mức may rủi ở cỡ mẫu này không.</param>
public readonly record struct H2hVerdict(
    string Basis, int Games, int WinsA, int WinsB,
    string? First, string? Last, int Ignored, bool Decisive, string Text);

/// <summary>
/// Lọc lịch sử đối đầu theo ĐỘI HÌNH, không theo thời gian.
///
/// Vì sao cần. Một cặp đấu chỉ là "cùng một cặp đấu" khi cả mười người trên sân vẫn là mười
/// người đó. Trong dữ liệu đang có, chỉ 1/16 đội từng ra trận với đúng đội hình TI2026 trước
/// tháng 10/2025, và 12/16 đội mãi tới năm 2026 mới lần đầu đủ mặt. Nên phần lớn "lịch sử đối
/// đầu" là trận của những đội khác mang cùng tên: Falcons–Liquid có 71 ván, nhưng 51 ván trong
/// đó Liquid chỉ còn 3/5 người của hôm nay.
///
/// Vì sao lọc theo đội hình chứ không theo ngày. Ngày chỉ là biến thay thế; nguyên nhân thật
/// khiến ván cũ mất giá trị chính là người trên sân đã khác. Lọc theo ngày sẽ vừa vứt nhầm
/// (đội giữ nguyên đội hình ba năm) vừa giữ nhầm (đội vừa thay hai người tháng trước).
///
/// Vì sao là bậc thang chứ không phải trung bình có trọng số. Trung bình có trọng số cho ra
/// một con số không ai kiểm lại được bằng tay và trộn lẫn các thời kỳ. Bậc thang trả về một
/// tập ván ĐẾM ĐƯỢC kèm nhãn của chính nó, và chỉ nới xuống bậc dưới khi bậc trên không đủ.
/// </summary>
public static class LineupContinuity
{
    /// <summary>Dưới ngần này ván thì bậc đang xét chưa đủ để kết luận, phải nới xuống bậc dưới.</summary>
    public const int MinGames = 6;

    /// <summary>
    /// Trọng số của một ván theo số người còn lại, cho những chỗ cần một con số liên tục thay
    /// vì một tập ván. Ba mức đầu không phải là ước lượng lý thuyết mà là mức đã đo:
    ///
    /// 5/5 = 1. Đúng đội hình.
    ///
    /// 4/5 = 0,5. Gộp mọi đội lại thì thay 1 người gần như không đổi kết quả (52,4% so với
    /// 50,9%, nằm gọn trong sai số). NHƯNG tách theo từng đội thì có đội lệch rất xa: Falcons
    /// thắng 60,9% khi đủ 5 người và 34,2% khi thiếu 1, và khoảng tin cậy hai bên không chạm
    /// nhau. Nên không được coi thay 1 người là cùng một đội, mà cũng không được vứt đi.
    ///
    /// 3/5 = 0,2. Vẫn quá nửa nhưng trên thực tế là đội khác.
    ///
    /// ≤2/5 = 0. Ít hơn một nửa thì giữ lại là giữ thành tích của người khác.
    /// </summary>
    public static double Weight(int kept) => kept switch
    {
        >= 5 => 1.0,
        4 => 0.5,
        3 => 0.2,
        _ => 0.0,
    };

    /// <summary>Trọng số của cả ván: hai bên nhân nhau, vì hai bên lệch nhẹ thì cộng dồn lại thành lệch nặng.</summary>
    public static double GameWeight(int keptA, int keptB) => Weight(keptA) * Weight(keptB);

    /// <summary>
    /// Chọn tập ván đáng dùng rồi NÓI RA kết quả trên chính tập đó.
    ///
    /// Bậc thang: đúng đội hình cả hai bên → nới cho lệch tối đa 1 người mỗi bên → không đủ.
    /// Chỉ nới khi bậc trên chưa đạt <see cref="MinGames"/>; đã có đủ ván của chính cặp đấu
    /// hôm nay thì ván của đội hình cũ không thêm thông tin, chỉ thêm nhiễu.
    /// </summary>
    public static H2hVerdict Read(IReadOnlyList<H2hGame> games, string nameA, string nameB,
                                  string slugA)
    {
        var total = games.Count;
        var strict = games.Where(g => Math.Min(g.KeptA, g.KeptB) >= 5).ToList();
        var lenient = games.Where(g => Math.Min(g.KeptA, g.KeptB) >= 4).ToList();

        // lenient LUÔN chứa strict, nên khi cả hai đều thiếu thì lấy lenient là lấy được nhiều
        // nhất có thể — không có trường hợp nào chọn strict lại nhiều ván hơn.
        var chosen = strict.Count >= MinGames ? strict
                   : lenient.Count >= MinGames ? lenient
                   : lenient.Count > 0 ? lenient
                   : strict;

        if (chosen.Count == 0)
            return new H2hVerdict("khong-du", 0, 0, 0, null, null, total, false,
                total == 0
                    ? "Hai đội chưa từng gặp nhau trong dữ liệu đã nạp."
                    : $"Hai đội chưa từng gặp nhau với đội hình TI2026. Cả {total} ván trong lịch "
                      + "sử đều có ít nhất một bên thiếu từ 2 người trở lên, tức là đội khác mang "
                      + "cùng tên — không dùng để đoán trận sắp tới được.");

        // Nhãn đọc từ chính dữ liệu, không đọc từ nhánh đã chọn: khi bậc nới ra mà không nhặt
        // thêm ván lệch nào thì nó vẫn là đúng đội hình, gọi tên khác đi là nói sai.
        var anySwap = chosen.Any(g => Math.Min(g.KeptA, g.KeptB) < 5);
        var basis = anySwap ? "lech-1-nguoi" : "dung-doi-hinh";

        var winsA = chosen.Count(g => g.WinnerSlug == slugA);
        var winsB = chosen.Count(g => g.WinnerSlug is not null && g.WinnerSlug != slugA);
        var n = winsA + winsB;

        // Cách biệt tối thiểu để không còn giải thích được bằng may rủi, xấp xỉ chuẩn hai phía
        // ở mức 95%: |thắng − thua| > 1,96·√n. Nói ra con số này để người đọc thấy vì sao 12–8
        // vẫn là chưa nói lên gì.
        var needed = 1.96 * Math.Sqrt(n);
        var gap = Math.Abs(winsA - winsB);
        var decisive = n > 0 && gap > needed;

        var first = chosen.Min(g => g.Date);
        var last = chosen.Max(g => g.Date);
        var ignored = total - chosen.Count;

        var head = basis == "dung-doi-hinh"
            ? $"Đúng đội hình TI2026: {chosen.Count} ván"
            : $"Chỉ có {strict.Count} ván đúng đội hình TI2026 — quá ít, nên nới sang mức lệch "
              + $"tối đa 1 người mỗi bên: {chosen.Count} ván";

        var record = winsA == winsB
            ? $"hai đội hoà nhau {winsA}–{winsB}"
            : $"{(winsA > winsB ? nameA : nameB)} thắng {Math.Max(winsA, winsB)}–{Math.Min(winsA, winsB)}";

        var judgement = decisive
            ? "Cách biệt này đã vượt mức giải thích được bằng may rủi."
            : $"Cách biệt {gap} ván CHƯA vượt được may rủi — ở cỡ mẫu {n} ván cần cách biệt trên "
              + $"{needed:0.#} ván mới dám nói ai trên cơ ai.";

        var rest = ignored > 0
            ? $" Đã bỏ {ignored} ván của đội hình cũ."
            : "";

        return new H2hVerdict(basis, chosen.Count, winsA, winsB, first, last, ignored, decisive,
            $"{head} (sớm nhất {first}), {record}. {judgement}{rest}");
    }
}
