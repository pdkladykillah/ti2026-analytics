namespace Ti2026.Ingest.Analytics;

/// <summary>Một ván rút gọn còn đúng những gì cần để dựng chữ ký lối chơi.</summary>
public readonly record struct StyleGame(
    int Kills, int Deaths, int Assists,
    int? LastHits, int? HeroDamage, int? TowerDamage, int? NetWorth,
    long? TeamNetWorth, int? LaneEfficiency, int DurationSeconds, int? LaneRole);

/// <summary>
/// Tổng của cả 10 người trong một ván — một quan sát về "người bình thường trong loại ván này".
/// </summary>
public readonly record struct StylePool(
    int AllKills, int AllAssists, int AllDeaths,
    long AllNetWorth, long AllHeroDamage, long AllLastHits, long AllTowerDamage,
    long AllLaneEfficiency, int LaneEfficiencyCount, int PlayerCount, int DurationSeconds);

/// <param name="Kind">
/// "chat-luong" = cao hơn thì giỏi hơn. "phong-cach" = cao hơn chỉ là KHÁC, không phải hơn.
/// Phân biệt này bắt buộc: một biểu đồ vươn ra ở "sát thương trên mỗi vàng" mà ngầm bảo đó là
/// điểm mạnh sẽ khuyên người đọc bỏ farm đi đánh nhau, tức khuyên ngược.
/// </param>
/// <param name="PlotLabel">
/// Tên dùng khi vẽ biểu đồ chữ ký. Khác Label ở đúng những trục mà THẤP mới tốt.
///
/// Vì sao cần một tên thứ hai. Biểu đồ nhiều góc luôn đọc là "vươn ra = nhiều hơn", và với trục
/// "giá mỗi pha kill" thì vươn ra nghĩa là chết đắt hơn, tức yếu hơn — hình sẽ nói ngược. Cách
/// chữa là đảo giá trị lúc vẽ VÀ đổi tên theo chiều đã đảo, chứ không phải chỉ đảo số rồi giữ
/// nguyên tên: "Giá mỗi pha kill 1,4" đảo thành 0,71 mà vẫn mang tên cũ thì sai hẳn nghĩa.
/// Giữ cả hai tên ở đây để phần hiển thị không phải tự suy ra — suy ra ở hai nơi là lệch ở hai nơi.
/// </param>
public readonly record struct StyleAxis(
    string Key, string Label, string Group, string Kind, bool LowerIsBetter, string Hint,
    string PlotLabel);

/// <param name="Raw">Giá trị thô theo đơn vị tự nhiên của trục.</param>
/// <param name="Norm">Mốc "người bình thường" đo từ chính hồ ván của người này.</param>
/// <param name="Index">Raw chia Norm. 1,0 nghĩa là đúng bằng người bình thường cùng loại ván.</param>
public readonly record struct StyleValue(
    string Key, int Games, double Raw, double? Norm, double? Index);

/// <summary>
/// Chữ ký lối chơi: đo một người bằng những TỈ SỐ tự triệt tiêu hạng đấu, rồi neo lại vào chính
/// hồ ván của người đó, để so người chơi pub với tuyển thủ chuyên nghiệp mà không nói dối.
///
/// VẤN ĐỀ PHẢI GIẢI. Ván pub và ván chuyên nghiệp không cùng thang. Đã đo trên 126 ván pro và
/// 114 ván pub của chính người dùng:
///   • tổng số mạng mỗi 10 phút: pub 18,96 so với pro 12,97 — pub nhiều hơn 46%;
///   • sát thương trên mỗi vàng tính trên MỌI người: pub 1,416 so với pro 1,110 — cao hơn 28%.
/// Bỏ qua hai con số đó thì "bạn chết gấp 3 lần một mid chuyên nghiệp" (thật ra 1,64 lần) và
/// "bạn đổi vàng ra sát thương giỏi hơn cả pro" (thật ra đúng bằng pro). Cái thứ hai sai cả
/// HƯỚNG, không chỉ sai độ lớn — và nó sẽ dẫn tới lời khuyên ngược hẳn.
///
/// BA TẦNG PHÒNG VỆ, theo thứ tự mạnh dần:
///
/// 1. CHỌN TỈ SỐ TỰ TRIỆT TIÊU. Trục "giá mỗi pha kill" là D/(K+A): tổng mạng chết trong một
///    ván luôn xấp xỉ tổng mạng giết, nên tử số và mẫu số phồng lên cùng nhau và phần lạm phát
///    tự trừ đi. Bằng chứng nó hiệu quả: nene ở mid đạt 0,235, NẰM TRONG dải của bốn tuyển thủ
///    (0,133–0,273). Một trục mà người chơi pub chạm được tới vùng pro là trục so sánh thật;
///    nhịp chết thô thì không ai chạm tới.
///
/// 2. NEO VÀO HỒ VÁN CỦA CHÍNH MÌNH. Mỗi trục còn được chia cho mốc "người bình thường" đo từ
///    tổng của cả 10 người trong chính loại ván đó. Kết quả là chỉ số tương đối: 1,0 nghĩa là
///    bằng người bình thường cùng loại ván. Đây là thứ thay cho hằng số cắm cứng — hằng số thì
///    mục theo bản game, còn cái neo tự cập nhật theo dữ liệu.
///
/// 3. CHỈ SO CÙNG VAI TRÒ. Xem <see cref="Compare"/>. Không có ràng buộc này thì mọi so sánh
///    biến thành so vị trí chứ không phải so người, và kết luận rút ra sẽ là "carry chết ít hơn
///    support" — một sự thật hiển nhiên đội lốt phát hiện.
///
/// KHÔNG GỘP THÀNH MỘT ĐIỂM TỔNG, cùng lý do như SkillComponents: trọng số giữa các trục là do
/// người viết mã chọn chứ không có trong dữ liệu.
/// </summary>
public static class IdolStyle
{
    /// <summary>Dưới ngần này ván thì trung vị của một tỉ số còn nhảy quá mạnh.</summary>
    public const int MinGames = 25;

    /// <summary>Dưới ngần này ván neo thì mốc "người bình thường" chưa đứng yên.</summary>
    public const int MinPoolMatches = 40;

    /// <summary>
    /// Chênh lệch chỉ số tương đối phải đạt ngần này mới gọi là khác biệt đáng nói.
    ///
    /// 0,15 nghĩa là lệch 15% so với mốc. Chọn theo cái đo được: hai người cùng đi mid là Topson
    /// và Malr1ne lệch nhau 0,240 so với 0,133 ở trục giá mạng, tức 1,8 lần — thừa ngưỡng. Còn
    /// phần tài nguyên đội của cả bốn người nằm gọn trong 22,2%–24,5%, tức dưới ngưỡng, và đúng
    /// là trục đó không phân biệt được ai với ai.
    /// </summary>
    public const double NotableGap = 0.15;

    public static readonly StyleAxis[] Axes =
    [
        new("gia-mang", "Giá mỗi pha kill", "Rủi ro", "chat-luong", true,
            "Số lần chết phải trả cho mỗi pha có công kill. Thấp là đổi chác lời.",
            "Đổi chác lời"),
        new("st-tren-vang", "Sát thương trên mỗi vàng", "Phong cách", "phong-cach", false,
            "Cao là thiên về giao tranh, thấp là thiên về farm và trụ. Không có chiều nào tốt hơn.",
            "Thiên giao tranh"),
        new("ket-lieu", "Chốt kill so với mở đường", "Phong cách", "phong-cach", false,
            "Trong các pha có công, bao nhiêu phần là cú chốt kill chứ không phải assist. Cao là người chốt hạ, thấp là người mở đường.",
            "Chốt hạ"),
        new("hieu-suat-lane", "Hiệu suất lane", "Nền tảng", "chat-luong", false,
            "Phần tài nguyên lane thực sự lấy được. Đo TRƯỚC khi ván ngã ngũ nên ít vòng nhân quả nhất.",
            "Hiệu suất lane"),
        new("toc-do-farm", "Tốc độ farm", "Nền tảng", "phong-cach", false,
            "Last hit mỗi phút. Cao hay thấp phụ thuộc vai trò, không phải trình độ.",
            "Tốc độ farm"),
        new("suc-ep-cong-trinh", "Sức ép trụ", "Phong cách", "phong-cach", false,
            "Sát thương lên trụ so với độ giàu. Cao là chơi theo bản đồ chứ không theo mạng.",
            "Sức ép trụ"),
        new("phan-tai-nguyen", "Phần tài nguyên đội", "Phong cách", "phong-cach", false,
            "Phần net worth của đội mà người này chiếm. Ở cấp chuyên nghiệp gần như không phân biệt được ai.",
            "Phần tài nguyên"),
    ];

    private static readonly Dictionary<string, Func<StyleGame, double?>> GamePick = new()
    {
        ["gia-mang"] = g => g.Kills + g.Assists > 0 ? (double)g.Deaths / (g.Kills + g.Assists) : null,
        ["st-tren-vang"] = g => g.HeroDamage is int hd && g.NetWorth is int nw && nw > 0
            ? (double)hd / nw
            : null,
        ["ket-lieu"] = g => g.Kills + g.Assists > 0 ? (double)g.Kills / (g.Kills + g.Assists) : null,
        ["hieu-suat-lane"] = g => g.LaneEfficiency,
        ["toc-do-farm"] = g => g.LastHits is int lh && g.DurationSeconds > 0
            ? lh / (g.DurationSeconds / 60.0)
            : null,
        ["suc-ep-cong-trinh"] = g => g.TowerDamage is int td && g.NetWorth is int nw2 && nw2 > 0
            ? (double)td / nw2
            : null,
        ["phan-tai-nguyen"] = g => g.NetWorth is int nw3 && g.TeamNetWorth is long tnw && tnw > 0
            ? nw3 / (double)tnw
            : null,
    };

    private static readonly Dictionary<string, Func<StylePool, double?>> PoolPick = new()
    {
        ["gia-mang"] = p => p.AllKills + p.AllAssists > 0
            ? (double)p.AllDeaths / (p.AllKills + p.AllAssists)
            : null,
        ["st-tren-vang"] = p => p.AllNetWorth > 0 ? (double)p.AllHeroDamage / p.AllNetWorth : null,
        ["ket-lieu"] = p => p.AllKills + p.AllAssists > 0
            ? (double)p.AllKills / (p.AllKills + p.AllAssists)
            : null,
        ["hieu-suat-lane"] = p => p.LaneEfficiencyCount > 0
            ? (double)p.AllLaneEfficiency / p.LaneEfficiencyCount
            : null,
        ["toc-do-farm"] = p => p.PlayerCount > 0 && p.DurationSeconds > 0
            ? p.AllLastHits / (double)p.PlayerCount / (p.DurationSeconds / 60.0)
            : null,
        ["suc-ep-cong-trinh"] = p => p.AllNetWorth > 0
            ? (double)p.AllTowerDamage / p.AllNetWorth
            : null,

        // Phần tài nguyên của "người bình thường" trong một đội 5 người luôn đúng bằng 1/5, không
        // cần đo. Trả hằng số thay vì tính từ tổng: tính từ tổng ra đúng 0,2 nhưng qua một phép
        // chia thừa, và một người đọc mã sau này sẽ tưởng con số ấy mang thông tin.
        ["phan-tai-nguyen"] = _ => 0.2,
    };

    /// <summary>
    /// Mốc "người bình thường" cho từng trục, đo từ các ván neo.
    ///
    /// Lấy trung vị của tỉ số TỪNG VÁN chứ không lấy tỉ số của tổng cộng dồn: một ván 90 phút có
    /// tổng gấp đôi ván 45 phút, nên cộng dồn rồi mới chia là để ván dài tự bỏ phiếu nặng gấp đôi.
    /// </summary>
    public static Dictionary<string, double> Normalizer(IReadOnlyList<StylePool> pool)
    {
        var result = new Dictionary<string, double>();
        if (pool.Count < MinPoolMatches) return result;

        foreach (var axis in Axes)
        {
            var pick = PoolPick[axis.Key];
            var values = new List<double>(pool.Count);
            foreach (var p in pool)
                if (pick(p) is double v && v > 0)
                    values.Add(v);

            if (values.Count >= MinPoolMatches) result[axis.Key] = Median(values);
        }

        return result;
    }

    /// <summary>
    /// Chữ ký của một người trên tập ván đã cho. Bên gọi quyết định tập đó là "tất cả" hay
    /// "chỉ ván đi mid" — cùng một phép tính dùng cho cả hai, không có nhánh theo vai trò.
    /// </summary>
    public static List<StyleValue> Signature(
        IReadOnlyList<StyleGame> games, IReadOnlyDictionary<string, double> normalizer)
    {
        var result = new List<StyleValue>(Axes.Length);

        foreach (var axis in Axes)
        {
            var pick = GamePick[axis.Key];
            var values = new List<double>(games.Count);
            foreach (var g in games)
                if (pick(g) is double v)
                    values.Add(v);

            if (values.Count < MinGames) continue;

            var raw = Median(values);
            double? norm = normalizer.TryGetValue(axis.Key, out var n) && n > 0 ? n : null;

            result.Add(new StyleValue(axis.Key, values.Count, raw, norm,
                norm is double d ? raw / d : null));
        }

        return result;
    }

    /// <param name="Axis">Trục lệch nhiều nhất giữa hai người.</param>
    /// <param name="LogGap">
    /// Khoảng cách trên thang log. Dùng log vì đây là TỈ SỐ: gấp đôi và bằng một nửa phải lệch
    /// như nhau, mà hiệu thường thì không — 2,0 trừ 1,0 ra 1,0 còn 1,0 trừ 0,5 chỉ ra 0,5, nên
    /// hiệu thường sẽ luôn kết luận rằng người vượt trội thì khác biệt hơn người thua kém.
    /// </param>
    public readonly record struct StyleDiff(string Axis, double Mine, double Theirs, double LogGap);

    /// <param name="SharedAxes">Số trục cả hai bên đều đo được. Dưới 3 thì đừng đọc Distance.</param>
    public readonly record struct StyleMatchup(
        int IdolId, string Role, int SharedAxes, double Distance, List<StyleDiff> Diffs);

    /// <summary>
    /// So một người với một tuyển thủ, CHỈ trên những ván cùng vai trò.
    ///
    /// Ràng buộc cùng vai trò là điều kiện tiên quyết chứ không phải tuỳ chọn. Người dùng đã bác
    /// đúng một lần khi phần trước so chéo vai trò và kết luận anh "chết quá nhiều": người đi
    /// support và người đi carry chết khác nhau vì công việc khác nhau, không vì ai giỏi hơn.
    /// Nên hàm này nhận vào tập ván ĐÃ lọc theo vai trò ở cả hai phía, và <paramref name="role"/>
    /// chỉ để ghi lại phép so đã diễn ra ở vai trò nào.
    ///
    /// Trả null khi không đủ trục chung — thà không nói gì còn hơn xếp hạng độ giống nhau dựa
    /// trên một trục duy nhất.
    /// </summary>
    public static StyleMatchup? Compare(
        int idolId, string role,
        IReadOnlyList<StyleValue> mine, IReadOnlyList<StyleValue> theirs)
    {
        var byKey = theirs.Where(t => t.Index is not null).ToDictionary(t => t.Key);
        var diffs = new List<StyleDiff>();
        var sum = 0.0;

        foreach (var m in mine)
        {
            if (m.Index is not double mi || mi <= 0) continue;
            if (!byKey.TryGetValue(m.Key, out var t) || t.Index is not double ti || ti <= 0) continue;

            var gap = Math.Log(mi) - Math.Log(ti);
            diffs.Add(new StyleDiff(m.Key, mi, ti, gap));
            sum += Math.Abs(gap);
        }

        if (diffs.Count < 3) return null;

        diffs.Sort((a, b) => Math.Abs(b.LogGap).CompareTo(Math.Abs(a.LogGap)));
        return new StyleMatchup(idolId, role, diffs.Count, sum / diffs.Count, diffs);
    }

    /// <summary>
    /// VỊ TRÍ thật, ghép từ nhãn lane với hạng net worth trong đội.
    ///
    /// VÌ SAO KHÔNG DÙNG THẲNG lane_role. Nó nói người này ĐỨNG Ở ĐÂU, không nói họ LÀM GÌ.
    /// Hard support đứng safelane cùng carry nên cả hai đều mang nhãn "safe"; soft support đứng
    /// offlane cùng offlaner nên cả hai đều là "off". Gộp chung là gộp hai công việc ngược nhau.
    ///
    /// ĐO ĐƯỢC, trên chính dữ liệu đã lưu. Trong 116 ván mang nhãn "safe" của người dùng:
    ///   • 69 ván hạng net worth 1–2: trung bình 648 GPM, 381 lính;
    ///   • 40 ván hạng 4–5:           trung bình 302 GPM,  56 lính.
    /// Chênh 345 GPM và 325 lính TRONG CÙNG MỘT Ô. Và nặng hơn nữa ở người thứ hai: 31 ván
    /// "offlane" của nene có 24 ván hạng 4–5 và đúng 1 ván là core — tức phần lớn là support,
    /// nhưng vẫn đang được đem so với những offlane core chuyên nghiệp.
    ///
    /// Ở bộ tuyển thủ cũng vậy: Yatoro (carry) 89% nhãn safe, Dukalis (hard support) 77% nhãn
    /// safe. Không tách ra thì hai người này nằm chung một ô.
    ///
    /// HẠNG 3 Ở SAFELANE TRẢ NULL. Đó là vùng chồng lấn thật — không đủ giàu để chắc là carry,
    /// không đủ nghèo để chắc là support. Thà bỏ 6% số ván còn hơn gán bừa rồi kéo lệch cả hai ô.
    /// Offlane thì không cần ngưỡng đó: offlane core vẫn thường xuyên đứng hạng 3.
    /// </summary>
    /// <remarks>
    /// GỌI LẠI <see cref="RoleResolver"/>, KHÔNG TỰ CÀI LẠI.
    ///
    /// Bản đầu của hàm này tự viết lại luật ghép lane với hạng farm, trong khi RoleResolver đã
    /// làm đúng việc đó từ trước — và trang Hồ sơ vẫn luôn dùng nó. Tức là lỗi "nhãn lane không
    /// phải vị trí" chưa bao giờ tồn tại ở trang Hồ sơ; nó chỉ tồn tại ở tab này, vì tôi dựng
    /// một bộ giải mã thứ hai thay vì dùng cái có sẵn.
    ///
    /// Hai bản cài cùng một luật thì sớm muộn cũng lệch nhau, và lúc đó hai trang sẽ nói hai
    /// điều khác nhau về cùng một ván mà không ai biết bên nào đúng.
    ///
    /// Trả null cho những gì KHÔNG phải vị trí chính xác — gồm cả "core"/"support" suy từ đội
    /// hình: phép so với tuyển thủ chuyên nghiệp cần biết đúng vị trí, và một ô "core" trộn
    /// carry với mid với offlane thì không so được với ai.
    /// </remarks>
    public static string? PositionOf(int? laneRole, int? teamFarmRank)
    {
        var verdict = RoleResolver.Resolve(laneRole, teamFarmRank);
        return verdict.IsExact ? verdict.Code : null;
    }

    /// <summary>Tên tiếng Việt của vị trí. Cũng lấy từ RoleResolver để hai trang gọi giống nhau.</summary>
    public static string PositionLabel(string position) => position switch
    {
        "pos1" => "carry (pos 1)",
        "pos2" => "mid (pos 2)",
        "pos3" => "offlane (pos 3)",
        "pos4" => "support cơ động (pos 4)",
        "pos5" => "hard support (pos 5)",
        _ => position,
    };

    /// <summary>
    /// Bao nhiêu hero phủ hết <paramref name="share"/> phần số ván — thước đo độ hẹp của hero pool.
    ///
    /// Dùng số đếm chứ không dùng chỉ số tập trung kiểu Herfindahl: "8 hero phủ 80% số ván" là
    /// câu một người đọc làm theo được, còn 0,14 thì không.
    /// </summary>
    public static int HeroesCovering(IReadOnlyList<int> heroIds, double share = 0.8)
    {
        if (heroIds.Count == 0) return 0;

        var counts = new Dictionary<int, int>();
        foreach (var h in heroIds) counts[h] = counts.GetValueOrDefault(h) + 1;

        var need = share * heroIds.Count;
        var running = 0;
        var used = 0;

        foreach (var c in counts.Values.OrderByDescending(x => x))
        {
            running += c;
            used++;
            if (running >= need) break;
        }

        return used;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1
            ? values[mid]
            : (values[mid - 1] + values[mid]) / 2.0;
    }
}
