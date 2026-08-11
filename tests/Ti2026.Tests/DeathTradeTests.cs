using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Cái chết đổi được gì, đo ở mức TỪNG PHA GIAO TRANH.
///
/// Ý người dùng đưa ra: một cái chết kéo 2-3 người địch đi xa để giết mình là cái chết có giá
/// trị, vì đồng đội dọn được phần còn lại — và càng có giá khi mình nghèo mà đứa chết bên kia
/// thì giàu. Tiền thưởng của Dota tỉ lệ với net worth nạn nhân nên vế thứ hai tự nằm trong con
/// số vàng, không phải ước lượng thêm.
/// </summary>
public class DeathTradeTests
{
    private static TradeGame G(
        int died, int ahead, int swingDied, int survived = 10, int swingSurvived = 0,
        int myGold = 0, int foeGold = 0, int foeDeaths = 0) =>
        new(died, ahead, swingDied, survived, swingSurvived, myGold, foeGold, foeDeaths);

    [Fact]
    public void Ty_le_pha_van_loi_tinh_tren_so_pha_co_ta_chet()
    {
        // 10 ván, mỗi ván 6 pha ta chết, 3 trong số đó đội vẫn lời.
        var r = DeathTrade.Read(Enumerable.Repeat(G(died: 6, ahead: 3, swingDied: 0), 10))!.Value;

        r.Fights.Should().Be(60);
        r.AheadShare.Should().Be(50);
        r.Matches.Should().Be(10);
    }

    /// <summary>
    /// Đây là con số trả lời thẳng ý người dùng: chết rẻ đổi lấy mạng đắt. Chênh lệch phải tính
    /// trên SỐ LƯỢT ĐỔI (số kẻ địch chết cùng pha), không phải trên số ván — một ván có bốn lượt
    /// đổi phải nặng gấp bốn một ván có một lượt.
    /// </summary>
    [Fact]
    public void Do_giau_doi_chac_tinh_theo_so_luot_doi_chu_khong_theo_van()
    {
        var games = new List<TradeGame>
        {
            // 1 lượt: ta 5.000, địch 9.000
            G(died: 20, ahead: 10, swingDied: 0, myGold: 5_000, foeGold: 9_000, foeDeaths: 1),
            // 3 lượt: ta 3×4.000, địch 3×10.000
            G(died: 20, ahead: 10, swingDied: 0, myGold: 12_000, foeGold: 30_000, foeDeaths: 3),
        };

        var r = DeathTrade.Read(games)!.Value;

        r.Trades.Should().Be(4);
        r.MyGold.Should().Be(4_250, "(5.000 + 12.000) / 4");
        r.FoeGold.Should().Be(9_750, "(9.000 + 30.000) / 4");
        r.GoldEdge.Should().Be(5_500);
    }

    [Fact]
    public void Mang_ta_dat_hon_thi_noi_dung_nhu_the()
    {
        var r = DeathTrade.Read(Enumerable.Repeat(
            G(died: 20, ahead: 5, swingDied: -900, myGold: 12_000, foeGold: 4_000, foeDeaths: 1), 40))!.Value;

        r.GoldEdge.Should().Be(-8_000);
        r.Text.Should().Contain("mạng bạn đắt hơn");
        r.Text.Should().NotContain("rẻ lấy mạng đắt");
    }

    /// <summary>
    /// Chênh lệch vàng phải tính TRUNG BÌNH MỖI PHA, không phải tổng: một người chơi 5.000 ván
    /// sẽ có tổng khổng lồ mà chẳng nói lên điều gì về từng pha.
    /// </summary>
    [Fact]
    public void Chenh_lech_vang_la_trung_binh_moi_pha()
    {
        var r = DeathTrade.Read(Enumerable.Repeat(
            G(died: 10, ahead: 5, swingDied: 8_000, survived: 20, swingSurvived: 30_000), 20))!.Value;

        r.SwingDied.Should().Be(800, "8.000 vàng chia 10 pha");
        r.SwingSurvived.Should().Be(1_500, "30.000 chia 20 pha");
    }

    // ---------- Khi nào thì IM ----------

    [Fact]
    public void Chua_du_pha_thi_khong_ket_luan()
    {
        var few = Enumerable.Repeat(G(died: 3, ahead: 2, swingDied: 500), 5);
        DeathTrade.Read(few).Should().BeNull("15 pha là quá ít");
    }

    [Fact]
    public void Van_chua_parse_thi_khong_tinh_vao()
    {
        var games = Enumerable.Repeat(G(died: 10, ahead: 5, swingDied: 0), 10).ToList();
        games.AddRange(Enumerable.Repeat(
            new TradeGame(null, null, null, null, null, null, null, null), 500));

        var r = DeathTrade.Read(games)!.Value;

        r.Matches.Should().Be(10, "chỉ ván đã parse mới có dữ liệu giao tranh");
        r.Fights.Should().Be(100);
    }

    /// <summary>
    /// Đủ pha để nói về vàng nhưng chưa đủ lượt đổi để nói về độ giàu — lúc đó phải nói phần
    /// nói được và IM phần chưa, chứ không chia cho 0 rồi in ra một con số bịa.
    /// </summary>
    [Fact]
    public void Du_pha_nhung_chua_du_luot_doi_thi_chi_noi_phan_noi_duoc()
    {
        var r = DeathTrade.Read(Enumerable.Repeat(
            G(died: 10, ahead: 6, swingDied: 5_000, myGold: 5_000, foeGold: 9_000, foeDeaths: 1), 10))!.Value;

        r.Trades.Should().Be(10, "dưới ngưỡng 30");
        r.Text.Should().Contain("pha giao tranh có bạn chết");
        r.Text.Should().NotContain("lúc bạn ngã xuống");
    }

    [Fact]
    public void Khong_co_van_nao_thi_tra_null_chu_khong_no()
    {
        DeathTrade.Read([]).Should().BeNull();
        DeathTrade.Read([new TradeGame(null, null, null, null, null, null, null, null)])
            .Should().BeNull();
    }
}
