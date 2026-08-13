namespace Ti2026.Ingest.Analytics;

/// <summary>Một ván đã có phân vị, rút gọn còn đúng những gì cần để chấm điểm thành phần.</summary>
public readonly record struct RatedGame(
    DateTime StartTime,
    bool Won,
    string Role,
    int? Gpm, int? Xpm, int? LastHits, int? Denies,
    int? Kills, int? Deaths, int? Assists,
    int? HeroDamage, int? HeroHealing, int? TowerDamage);

/// <summary>
/// Điểm một mặt kỹ năng, tính bằng phân vị so với mọi người chơi cùng hero.
/// </summary>
/// <param name="Median">0..100, đã đảo chiều nếu cần. Càng cao càng tốt, không có ngoại lệ.</param>
/// <param name="Low">Phân vị 25 của chính người này — mức của ván dở.</param>
/// <param name="High">Phân vị 75 — mức của ván hay.</param>
/// <param name="Recent">Median của 50 ván gần nhất, null khi chưa đủ dữ liệu để so.</param>
/// <param name="Won">Median chỉ tính trong ván THẮNG. null khi chưa đủ ván thắng.</param>
/// <param name="Lost">Median chỉ tính trong ván THUA.</param>
public readonly record struct SkillComponent(
    string Key, string Label, string Group, int Games,
    int Median, int Low, int High, int? Recent, bool Inverted,
    int? Won, int? Lost);

/// <summary>
/// Chấm từng mặt kỹ năng bằng PHÂN VỊ THEO HERO của OpenDota, thay vì bằng con số tuyệt đối.
///
/// VÌ SAO PHẢI LÀ PHÂN VỊ. 600 GPM là kém với Anti-Mage và phi thường với Crystal Maiden. Mọi
/// bảng "GPM trung bình" gộp chung các hero đều đang cộng hai thang đo khác nhau, và kết quả
/// chỉ phản ánh người đó hay chơi hero nào. Phân vị của OpenDota so người này với những người
/// khác CÙNG chơi hero đó, nên nó trừ đi đúng phần lệch ấy.
///
/// Và nó phủ 100% số ván — khác lane_role vốn chỉ có ở 6%. Đây là lý do phần này làm được cho cả
/// lịch sử 5.877 ván trong khi phân tích vị trí chính xác thì không.
///
/// BỐN QUYẾT ĐỊNH ĐÃ CÂN NHẮC:
///
/// 1. DÙNG TRUNG VỊ, KHÔNG DÙNG TRUNG BÌNH. Phân vị là thang thứ hạng, không phải thang khoảng:
///    khoảng cách từ phân vị 50 lên 60 không bằng từ 89 lên 99 xét theo giá trị thật. Cộng rồi
///    chia là phép tính không có nghĩa trên thang đó. Trung vị thì chỉ cần thứ tự nên luôn đúng,
///    và nó cũng miễn nhiễm với vài ván thảm hoạ.
///
/// 2. SỐ CHẾT ĐẢO CHIỀU. Phân vị 90 của deaths_per_min nghĩa là chết nhiều hơn 90% người chơi —
///    tệ, không phải giỏi. Đảo thành 100 trừ đi, để mọi cột trên trang đọc cùng một chiều.
///
/// 3. KHÔNG GỘP THÀNH MỘT ĐIỂM TỔNG. Cám dỗ lớn nhất là trung bình mọi thành phần thành một con
///    số "trình độ". Nhưng trọng số giữa farm và sát thương là do người viết mã chọn chứ không
///    có trong dữ liệu, và một con số duy nhất sẽ che mất đúng thứ hữu ích: người này mạnh mặt
///    nào, yếu mặt nào. Nên trang nêu từng mặt và để người đọc tự nhìn.
///
/// 4. TÁCH THEO VAI TRÒ Ở TẦNG TRÊN. Bộ này chấm một tập ván bất kỳ; bên gọi quyết định tập đó
///    là "tất cả" hay "chỉ ván đi mid". Nhờ vậy cùng một phép tính dùng được cho cả hai mà không
///    có nhánh if nào theo vai trò.
/// </summary>
public static class SkillComponents
{
    /// <summary>Dưới ngần này ván có phân vị thì trung vị chỉ là nhiễu.</summary>
    public const int MinGames = 15;

    /// <summary>Cỡ cửa sổ "gần đây" khi so tiến bộ.</summary>
    public const int RecentWindow = 50;

    /// <summary>Chênh từ ngần này điểm phân vị trở lên mới gọi là mạnh/yếu rõ rệt.</summary>
    public const int NotableGap = 15;

    private static readonly (string Key, string Label, string Group, bool Inverted,
        Func<RatedGame, int?> Pick)[] Metrics =
    [
        ("farm-gpm", "Kiếm vàng", "Kinh tế", false, g => g.Gpm),
        ("farm-lh", "Ăn lính", "Kinh tế", false, g => g.LastHits),
        ("farm-dn", "Deny", "Kinh tế", false, g => g.Denies),
        ("xpm", "Lên cấp", "Kinh tế", false, g => g.Xpm),
        ("dmg", "Sát thương lên hero", "Giao tranh", false, g => g.HeroDamage),
        ("kills", "Số kill", "Giao tranh", false, g => g.Kills),
        ("assists", "Assist", "Giao tranh", false, g => g.Assists),
        ("deaths", "Giữ mạng", "Giao tranh", true, g => g.Deaths),
        ("tower", "Sát thương trụ", "Mục tiêu", false, g => g.TowerDamage),
        ("heal", "Hồi máu đồng đội", "Mục tiêu", false, g => g.HeroHealing),
    ];

    public static List<SkillComponent> Read(IReadOnlyList<RatedGame> games, int minGames = MinGames)
    {
        var found = new List<SkillComponent>();
        if (games.Count == 0) return found;

        // Sắp mới trước MỘT LẦN ở đây, để cửa sổ "gần đây" của mọi thành phần cùng nói về một
        // khoảng thời gian. Sắp riêng trong từng thành phần thì mỗi cột lấy 50 ván khác nhau —
        // vì mỗi chỉ số thiếu ở những ván khác nhau — và không cột nào so được với cột nào.
        var ordered = games.OrderByDescending(g => g.StartTime).ToList();

        foreach (var (key, label, group, inverted, pick) in Metrics)
        {
            var all = ordered
                .Select(g => (Game: g, Pct: pick(g)))
                .Where(x => x.Pct is int)
                .Select(x => (x.Game, Value: Orient(x.Pct!.Value, inverted)))
                .ToList();

            if (all.Count < minGames) continue;

            var values = all.Select(x => x.Value).ToList();

            // Cửa sổ gần đây chỉ có nghĩa khi phần CÒN LẠI cũng đủ dày để so. Không có điều kiện
            // này thì người mới chơi 20 ván sẽ thấy "gần đây" và "tổng thể" gần như trùng nhau
            // rồi tưởng mình vừa tiến bộ.
            int? recent = all.Count >= RecentWindow + minGames
                ? Percentile(all.Take(RecentWindow).Select(x => x.Value).ToList(), 50)
                : null;

            // TÁCH THEO KẾT QUẢ TRẬN. Đây là thứ một con số gộp giấu mất hoàn toàn.
            //
            // Đo trên tài khoản thật: cột "giữ mạng" gộp lại là 42 — nghe như một điểm yếu rõ.
            // Nhưng tách ra thì trong ván THẮNG là 60 (trên trung bình) và trong ván THUA là 26.
            // Hai câu chuyện hoàn toàn khác nhau nằm sau cùng một con số, và câu đúng không phải
            // "người này chết nhiều" mà "những ván hỏng của người này hỏng rất nặng".
            //
            // Không thay con số gộp bằng con số theo kết quả: mốc so của OpenDota cũng là quần
            // thể gộp cả thắng lẫn thua, nên gộp mới là phép so cùng thang. Tách ra là để KỂ
            // đúng câu chuyện, không phải để đổi thước đo.
            var won = all.Where(x => x.Game.Won).Select(x => x.Value).ToList();
            var lost = all.Where(x => !x.Game.Won).Select(x => x.Value).ToList();

            found.Add(new SkillComponent(
                key, label, group, all.Count,
                Percentile(values, 50), Percentile(values, 25), Percentile(values, 75),
                recent, inverted,
                won.Count >= minGames ? Percentile(won, 50) : null,
                lost.Count >= minGames ? Percentile(lost, 50) : null));
        }

        return found;
    }

    /// <summary>
    /// Đưa mọi phân vị về cùng một chiều: cao = tốt.
    ///
    /// Không có bước này thì cột "số chết" đọc ngược mọi cột còn lại, và một người chết nhiều
    /// nhất giải sẽ có thanh dài nhất trang.
    /// </summary>
    public static int Orient(int pct, bool inverted) => inverted ? 100 - pct : pct;

    /// <summary>
    /// Phân vị theo lối nội suy tuyến tính giữa hai phần tử kề nhau.
    ///
    /// Nội suy chứ không lấy phần tử gần nhất: với tập nhỏ, phép lấy gần nhất làm p25 và p75
    /// nhảy theo bậc thang mỗi khi thêm một ván, và biểu đồ trông như đang biến động thật.
    /// </summary>
    public static int Percentile(List<int> values, double p)
    {
        if (values.Count == 0) return 0;
        if (values.Count == 1) return values[0];

        var s = values.OrderBy(x => x).ToList();
        var pos = (s.Count - 1) * p / 100.0;
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);

        return (int)Math.Round(s[lo] + (s[hi] - s[lo]) * (pos - lo));
    }

    /// <summary>
    /// Ba mặt mạnh nhất và ba mặt yếu nhất, chỉ lấy những mặt CÁCH XA mức trung bình chung.
    ///
    /// Mốc so là 50 vì phân vị đã tự chuẩn hoá: 50 nghĩa là ngang người chơi trung bình trên
    /// cùng hero đó. Ngưỡng <see cref="NotableGap"/> tồn tại để trang không tuyên bố "điểm mạnh"
    /// cho một mặt đứng ở phân vị 53.
    /// </summary>
    public static (List<SkillComponent> Strong, List<SkillComponent> Weak) Extremes(
        IReadOnlyList<SkillComponent> components)
    {
        var strong = components
            .Where(c => c.Median >= 50 + NotableGap)
            .OrderByDescending(c => c.Median).Take(3).ToList();

        var weak = components
            .Where(c => c.Median <= 50 - NotableGap)
            .OrderBy(c => c.Median).Take(3).ToList();

        return (strong, weak);
    }

    /// <summary>
    /// Khoảng cách giữa ván thắng và ván thua của một cột.
    ///
    /// CỐ Ý KHÔNG CÓ HÀM "CHỈ SỤP KHI THUA". Bản trước có, và nó SAI — đây là lỗi đã suýt lên
    /// trang, bắt được nhờ so hai người theo dõi với nhau.
    ///
    /// Lập luận cũ: cột giữ mạng gộp lại 42, nhưng ván thắng 60 và ván thua 26, nên "vấn đề là
    /// ván hỏng hỏng nặng chứ không phải kỹ năng". Nghe rất thuyết phục. Rồi đo trên người thứ
    /// hai thì khoảng cách thắng/thua của HỌ trên từng cột là 37, 13, 5, 25, 14, 28, 39, 33, 49,
    /// 14 — so với 34, 11, 8, 23, 12, 26, 40, 34, 36, 14 của người thứ nhất. Gần như trùng khít.
    ///
    /// Nghĩa là mọi chỉ số đều sụp khi thua, với MỌI người, ở cùng một mức. Đó là tính chất của
    /// thước đo chứ không phải phát hiện về một người — và một nhãn bật cho 3/10 cột của người
    /// này và 4/10 cột của người kia thì không phân biệt được ai với ai.
    ///
    /// Điều còn lại sau khi trừ đi phần chung: trên cột giữ mạng, người thứ nhất là 60/26 còn
    /// người thứ hai là 83/50 — thấp hơn 23 điểm ở CẢ HAI đầu. Chênh lệch đều, không dồn vào
    /// phần thua. Nên kết luận đúng vẫn là kết luận ban đầu, và phép "hiệu chỉnh" kia chỉ là
    /// một cách bào chữa nghe có vẻ khoa học.
    ///
    /// Giữ lại con số thắng/thua để HIỂN THỊ vì nó là bối cảnh có ích, nhưng không dùng nó để
    /// phán.
    /// </summary>
    public static int? ResultGap(SkillComponent c) =>
        c.Won is int w && c.Lost is int l ? w - l : null;
}
