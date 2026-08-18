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
