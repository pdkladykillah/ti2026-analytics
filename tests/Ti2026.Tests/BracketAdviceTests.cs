using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Gợi ý chọn đội đi tiếp. Hai chỗ dễ sai và cả hai đều im lặng khi sai.
/// </summary>
public class BracketAdviceTests
{
    private static BracketPair Pair(double eloA, double eloB, int bestOf = 3) =>
        new(1, "Match", "A", "B", eloA, eloB, bestOf);

    /// <summary>
    /// BO3 KHUẾCH ĐẠI LỢI THẾ. Không đổi thang từ ván sang series thì mọi cặp trông sát nhau hơn
    /// thực tế, và chiến lược "chỉ đánh cược ở cặp sát nhau" sẽ đánh cược ở cả cặp vốn không sát.
    /// </summary>
    [Fact]
    public void Bo3_khuech_dai_loi_the_so_voi_mot_van()
    {
        var p = BracketAdvice.MapProbability(1600, 1500);
        p.Should().BeApproximately(0.640, 0.005);

        var bo3 = BracketAdvice.SeriesProbability(p, 3);
        bo3.Should().BeGreaterThan(p, "thắng 2 trong 3 dễ hơn cho đội mạnh hơn");
        // p = 0,6402 → p²(3 − 2p) = 0,4098 × 1,7196 = 0,7046. Chênh 100 điểm Elo biến một lợi
        // thế 64% mỗi ván thành 70% cả series — đó chính là phần Bo3 khuếch đại.
        bo3.Should().BeApproximately(0.705, 0.005);

        var bo5 = BracketAdvice.SeriesProbability(p, 5);
        bo5.Should().BeGreaterThan(bo3, "càng nhiều ván thì may rủi càng ít chỗ");

        // Bo1 giữ nguyên — không được lặng lẽ áp công thức Bo3.
        BracketAdvice.SeriesProbability(p, 1).Should().Be(p);
    }

    /// <summary>Hai đội bằng Elo thì 50-50 ở mọi thể thức — không thể thức nào tạo ra lợi thế.</summary>
    [Fact]
    public void Ngang_diem_thi_luon_nam_muoi_nam_muoi()
    {
        foreach (var bo in new[] { 1, 3, 5 })
            BracketAdvice.SeriesProbability(BracketAdvice.MapProbability(1500, 1500), bo)
                .Should().BeApproximately(0.5, 1e-9);
    }

    /// <summary>
    /// CHỈ ĐÁNH CƯỢC Ở CẶP SÁT NHAU. Ở cặp chênh lệch rõ thì chọn ngược là vứt điểm đi, không
    /// phải chiến thuật — và đó đúng là cách một chiến lược "phương sai cao" biến thành thua đều.
    /// </summary>
    [Fact]
    public void Chi_danh_cuoc_o_cap_sat_nhau()
    {
        var close = BracketAdvice.Build([Pair(1520, 1500)], contrarian: true)[0];
        close.IsClose.Should().BeTrue();
        close.PickIsFavourite.Should().BeFalse("cặp sát nhau thì chọn ngược");
        close.Pick.Should().Be("B");
        close.Cost.Should().BeGreaterThan(0);

        var lopsided = BracketAdvice.Build([Pair(1800, 1400)], contrarian: true)[0];
        lopsided.IsClose.Should().BeFalse();
        lopsided.PickIsFavourite.Should().BeTrue("chênh lệch rõ thì bám cửa trên");
        lopsided.Pick.Should().Be("A");
        lopsided.Cost.Should().Be(0);
    }

    /// <summary>Tắt chế độ đánh cược thì luôn theo cửa trên, kể cả ở cặp sát nhau.</summary>
    [Fact]
    public void Tat_danh_cuoc_thi_luon_theo_cua_tren()
    {
        var c = BracketAdvice.Build([Pair(1520, 1500)], contrarian: false)[0];

        c.IsClose.Should().BeTrue("vẫn phải nói ra là cặp này sát nhau");
        c.PickIsFavourite.Should().BeTrue();
        c.Pick.Should().Be("A");
        c.Cost.Should().Be(0);
    }

    /// <summary>
    /// Cặp sát nhau xếp lên TRƯỚC: đó là những nút người dùng cần cân nhắc, còn nút chênh lệch rõ
    /// thì chỉ cần liếc qua.
    /// </summary>
    [Fact]
    public void Cap_sat_nhau_xep_len_truoc()
    {
        var calls = BracketAdvice.Build(
            [
                new BracketPair(1, "xa", "A", "B", 1800, 1400, 3),
                new BracketPair(2, "sat", "C", "D", 1510, 1500, 3),
            ],
            contrarian: true);

        calls[0].NodeId.Should().Be(2);
        calls[0].IsClose.Should().BeTrue();
    }

    /// <summary>
    /// Xác suất hai bên phải cộng đúng 1. Một lỗi làm tròn ở đây sẽ đi thẳng vào phần "cái giá
    /// phải trả khi chọn ngược", và con số đó là thứ quyết định có nên đánh cược hay không.
    /// </summary>
    [Fact]
    public void Hai_xac_suat_cong_lai_bang_mot()
    {
        foreach (var pair in new[] { Pair(1500, 1500), Pair(1687, 1517), Pair(1400, 1800) })
        {
            var c = BracketAdvice.Build([pair], contrarian: true)[0];
            (c.ProbA + c.ProbB).Should().BeApproximately(1.0, 1e-9);
        }
    }
}

/// <summary>
/// Mô phỏng cả nhánh tới chung kết. Dự đoán bảng đấu khoá một lần cho toàn giải, nên chỉ đoán
/// bốn cặp tứ kết là bỏ trống mười nút — trong đó có nút đáng giá nhất.
/// </summary>
public class BracketSimulateTests
{
    /// <summary>Một nhánh loại kép rút gọn: hai tứ kết, một bán kết thắng, một nút thua, một chung kết.</summary>
    private static List<BracketNode> Tree() =>
    [
        new(1, "TK1", "Playoff", null, null, 3, 4, "A", "B"),
        new(2, "TK2", "Playoff", null, null, 3, 4, "C", "D"),
        new(3, "BK",  "Playoff", 1, 2, 5, null, null, null),
        new(4, "NT",  "Playoff", 1, 2, 5, null, null, null),
        new(5, "CK",  "Playoff", 3, 4, null, null, null, null),
    ];

    private static readonly Dictionary<string, double> Elo =
        new() { ["A"] = 1700, ["B"] = 1500, ["C"] = 1650, ["D"] = 1450 };

    /// <summary>
    /// ĐOÁN TỚI TẬN NÚT CUỐI. Nút chưa biết đội chính là thứ mô phỏng sinh ra — nếu chỉ xử lý nút
    /// đã biết hai đội thì mãi mãi dừng ở vòng đầu.
    /// </summary>
    [Fact]
    public void Mo_phong_dien_het_moi_nut_ke_ca_nut_chua_biet_doi()
    {
        var steps = BracketAdvice.Simulate(Tree(), Elo, contrarian: false, bestOf: 3, finalBestOf: 5);

        steps.Should().HaveCount(5, "cả năm nút đều phải có dự đoán");
        steps.Select(s => s.Call.NodeId).Should().BeEquivalentTo([1, 2, 3, 4, 5]);

        steps.Count(s => s.TeamsKnown).Should().Be(2, "chỉ hai tứ kết là biết sẵn đội");
    }

    /// <summary>
    /// NGƯỜI THUA RƠI XUỐNG NHÁNH THUA, không biến mất. Nút 4 nhận người thua của 1 và 2 — đọc
    /// ngược chiều đó thì cả nửa dưới bảng đấu sai mà hình vẽ vẫn hợp lệ.
    /// </summary>
    [Fact]
    public void Nguoi_thua_roi_xuong_dung_nut_nhanh_thua()
    {
        var steps = BracketAdvice.Simulate(Tree(), Elo, contrarian: false, bestOf: 3, finalBestOf: 5);

        var semi = steps.First(s => s.Call.NodeId == 3);
        var lower = steps.First(s => s.Call.NodeId == 4);

        // A và C mạnh hơn nên thắng; B và D rơi xuống.
        new[] { semi.Call.TeamA, semi.Call.TeamB }.Should().BeEquivalentTo(["A", "C"]);
        new[] { lower.Call.TeamA, lower.Call.TeamB }.Should().BeEquivalentTo(["B", "D"]);
    }

    /// <summary>
    /// XÁC SUẤT NÚT SÂU PHẢI NHÂN DỒN. Một dự đoán chung kết "70%" thực chất chỉ đúng khoảng 15%
    /// nếu bốn nút trước nó mỗi nút đúng 60%. Không tách hai con số thì nút sâu trông chắc chắn
    /// ngang nút đầu.
    /// </summary>
    [Fact]
    public void Nut_cang_sau_thi_do_tin_cang_thap()
    {
        var steps = BracketAdvice.Simulate(Tree(), Elo, contrarian: false, bestOf: 3, finalBestOf: 5);

        var quarter = steps.First(s => s.Call.NodeId == 1);
        var final = steps.First(s => s.Call.NodeId == 5);

        quarter.Reached.Should().Be(1.0, "tứ kết chắc chắn diễn ra, đội đã biết");
        final.Reached.Should().BeLessThan(1.0);
        final.Reached.Should().BeLessThan(steps.First(s => s.Call.NodeId == 3).Reached,
            "chung kết nằm sau bán kết nên chỉ có thể kém chắc hơn");
    }

    /// <summary>Chung kết đá Bo5 chứ không phải Bo3 — nút không đi tiếp đâu nữa chính là nó.</summary>
    [Fact]
    public void Chung_ket_dung_the_thuc_Bo5()
    {
        var steps = BracketAdvice.Simulate(Tree(), Elo, contrarian: false, bestOf: 3, finalBestOf: 5);

        steps.First(s => s.Call.NodeId == 5).Call.BestOf.Should().Be(5);
        steps.First(s => s.Call.NodeId == 1).Call.BestOf.Should().Be(3);
    }

    /// <summary>
    /// Thiếu Elo một đội thì DỪNG chứ không treo. Vòng lặp phải tự thoát khi không tiến thêm
    /// được, nếu không một đồ thị thiếu cạnh sẽ quay vô hạn ngay trong một request.
    /// </summary>
    [Fact]
    public void Thieu_elo_thi_dung_lai_khong_treo()
    {
        var elo = new Dictionary<string, double> { ["A"] = 1700, ["B"] = 1500 };
        var steps = BracketAdvice.Simulate(Tree(), elo, contrarian: false, bestOf: 3, finalBestOf: 5);

        steps.Should().HaveCount(1, "chỉ nút 1 có đủ Elo hai bên");
        steps[0].Call.NodeId.Should().Be(1);
    }
}

/// <summary>
/// Vòng phải là ĐỘ SÂU TRONG CÂY, không phải lượt giải của vòng lặp.
///
/// Vòng lặp mô phỏng giải được nhiều nút trong cùng một lượt, nên lấy số lượt làm số vòng sẽ dồn
/// gần hết bảng đấu vào "vòng 1" — đã thấy thật trên dữ liệu TI: 13 trong 14 nút cùng mang nhãn
/// vòng 1, khiến phần nhóm theo vòng trên trang trở nên vô nghĩa.
/// </summary>
public class BracketRoundTests
{
    [Fact]
    public void Vong_la_do_sau_trong_cay()
    {
        List<BracketNode> tree =
        [
            new(1, "TK1", "Playoff", null, null, 3, 4, "A", "B"),
            new(2, "TK2", "Playoff", null, null, 3, 4, "C", "D"),
            new(3, "BK",  "Playoff", 1, 2, 5, null, null, null),
            new(4, "NT",  "Playoff", 1, 2, 5, null, null, null),
            new(5, "CK",  "Playoff", 3, 4, null, null, null, null),
        ];

        var elo = new Dictionary<string, double>
            { ["A"] = 1700, ["B"] = 1500, ["C"] = 1650, ["D"] = 1450 };

        var byId = BracketAdvice
            .Simulate(tree, elo, contrarian: false, bestOf: 3, finalBestOf: 5)
            .ToDictionary(s => s.Call.NodeId, s => s.Round);

        byId[1].Should().Be(1);
        byId[2].Should().Be(1);
        byId[3].Should().Be(2, "bán kết nằm sau tứ kết");
        byId[4].Should().Be(2);
        byId[5].Should().Be(3, "chung kết nằm sau bán kết");
    }
}
