using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Phần đồng đội. Rủi ro ở đây không phải lỗi chạy mà là những câu nghe rất thuyết phục:
/// "chơi với A thì thắng 62%" — trong khi người này thắng 62% ở mọi ván.
/// </summary>
public class TeammateAnalysisTests
{
    private const long Ban = 111;
    private const long NguoiLa = 222;

    private static MateGame With(bool won, params (long Acc, bool Party)[] mates) =>
        new(won, mates.Select(m => new MatePresence(m.Acc, $"P{m.Acc}", m.Party, 60)).ToList());

    private static List<MateGame> Build(
        int gamesWithFriend, int winsWithFriend, int gamesAlone, int winsAlone)
    {
        var list = new List<MateGame>();

        for (var i = 0; i < gamesWithFriend; i++)
            list.Add(With(i < winsWithFriend, (Ban, true)));

        for (var i = 0; i < gamesAlone; i++)
            list.Add(With(i < winsAlone));

        return list;
    }

    // ---------- Phép so phải có đối chứng ----------

    /// <summary>
    /// Đây là điểm cốt lõi. Tỷ lệ thắng KHI CÓ một người chẳng nói lên điều gì nếu không đặt
    /// cạnh tỷ lệ thắng khi VẮNG người đó — mà nhóm đối chứng phải là những ván không có họ,
    /// không phải toàn bộ lịch sử (toàn bộ lịch sử đã bao gồm chính những ván đang xét).
    /// </summary>
    [Fact]
    public void Chenh_lech_tinh_so_voi_nhung_van_VANG_nguoi_do()
    {
        // Cùng A: 60/100 = 60%. Vắng A: 40/100 = 40%. Chênh phải là +20, không phải 60 - 50.
        var line = TeammateAnalysis.Read(Build(100, 60, 100, 40)).Single();

        line.Winrate.Should().Be(60);
        line.WithoutWinrate.Should().Be(40);
        line.WithoutGames.Should().Be(100);
        line.Lift.Should().Be(20);
    }

    [Fact]
    public void Choi_cung_ai_cung_the_thi_chenh_bang_khong_va_khong_dang_noi()
    {
        var line = TeammateAnalysis.Read(Build(100, 55, 100, 55)).Single();

        line.Lift.Should().Be(0);
        line.Notable.Should().BeFalse();
    }

    // ---------- Riêng tư và thống kê trùng nhau ----------

    /// <summary>
    /// Người ghép ngẫu nhiên trúng nhiều ván vẫn KHÔNG được nêu tên: họ không tự nguyện xuất
    /// hiện trên một trang công khai. Ngưỡng đó cũng chính là ngưỡng thống kê — gặp lại ngẫu
    /// nhiên thì không nói lên điều gì về việc hợp nhau.
    /// </summary>
    [Fact]
    public void Nguoi_ghep_trung_thi_khong_bao_gio_len_bang()
    {
        var games = new List<MateGame>();
        for (var i = 0; i < 200; i++) games.Add(With(i % 2 == 0, (NguoiLa, false)));
        for (var i = 0; i < 200; i++) games.Add(With(false));

        TeammateAnalysis.Read(games).Should().BeEmpty();
    }

    [Fact]
    public void Cung_nhom_du_nhieu_thi_len_bang_va_dem_dung_so_van_cung_nhom()
    {
        var games = new List<MateGame>();
        for (var i = 0; i < 30; i++) games.Add(With(true, (Ban, true)));
        for (var i = 0; i < 10; i++) games.Add(With(true, (Ban, false)));
        for (var i = 0; i < 100; i++) games.Add(With(false));

        var line = TeammateAnalysis.Read(games).Single();

        line.Games.Should().Be(40, "tính mọi ván cùng phe");
        line.PartyGames.Should().Be(30, "nhưng chỉ ván cùng nhóm mới quyết định có được nêu không");
    }

    [Fact]
    public void Nhom_doi_chung_qua_mong_thi_khong_ket_luan()
    {
        var games = new List<MateGame>();
        for (var i = 0; i < 50; i++) games.Add(With(true, (Ban, true)));
        for (var i = 0; i < TeammateAnalysis.MinWithoutGames - 1; i++) games.Add(With(false));

        TeammateAnalysis.Read(games).Should().BeEmpty();
    }

    // ---------- So sánh bội ----------

    /// <summary>
    /// Cùng một chênh lệch, cùng một cỡ mẫu — nhưng xét 1 người thì kết luận được, xét 40 người
    /// thì không. Đúng cái lỗi đã mắc ở phần "khắc tinh" của các đội: 11/16 đội có khắc tinh cho
    /// tới khi hiệu chỉnh, rồi còn 0.
    /// </summary>
    [Fact]
    public void Xet_cang_nhieu_nguoi_thi_nguong_cang_chat()
    {
        List<MateGame> Make(int mates)
        {
            var games = new List<MateGame>();

            // Người 900 thắng 59% qua 200 ván. Mọi ván còn lại — dù đi cùng ai — đều đúng 50%,
            // nên nhóm đối chứng của người 900 không đổi khi thêm người vào bảng.
            for (var i = 0; i < 200; i++) games.Add(With(i < 118, (900, true)));

            for (var m = 1; m < mates; m++)
                for (var i = 0; i < 120; i++)
                    games.Add(With(i < 60, (900 + m, true)));

            for (var i = 0; i < 800; i++) games.Add(With(i < 400));
            return games;
        }

        var mot = TeammateAnalysis.Read(Make(1)).Single(l => l.AccountId == 900);
        var bonMuoi = TeammateAnalysis.Read(Make(40)).Single(l => l.AccountId == 900);

        mot.Notable.Should().BeTrue();
        bonMuoi.Notable.Should().BeFalse("cùng dữ liệu, nhưng đây là cực trị của 40 phép so");
    }

    /// <summary>
    /// Ở vài nghìn ván, 1,5 điểm phần trăm có thể "có ý nghĩa thống kê" mà chẳng có ý nghĩa gì
    /// với người đọc. Phải đủ CẢ hai: tách được khỏi nhiễu, và đủ lớn để đáng nói.
    /// </summary>
    [Fact]
    public void Chenh_qua_nho_thi_khong_dang_noi_du_mau_rat_lon()
    {
        var games = new List<MateGame>();
        for (var i = 0; i < 4000; i++) games.Add(With(i < 2060, (Ban, true)));   // 51,5%
        for (var i = 0; i < 4000; i++) games.Add(With(i < 2000));                // 50,0%

        var line = TeammateAnalysis.Read(games).Single();

        line.Lift.Should().BeApproximately(1.5, 0.1);
        line.Notable.Should().BeFalse();
    }

    // ---------- Mốc z ----------

    [Fact]
    public void Moc_z_tinh_dung_chu_khong_viet_cung_1_96()
    {
        TeammateAnalysis.ZFor(0.05).Should().BeApproximately(1.9600, 0.001);
        TeammateAnalysis.ZFor(0.01).Should().BeApproximately(2.5758, 0.001);
        TeammateAnalysis.ZFor(0.05 / 40).Should().BeApproximately(3.2272, 0.01);
        TeammateAnalysis.ZFor(0.05 / 40).Should().BeGreaterThan(TeammateAnalysis.ZFor(0.05));
    }

    [Fact]
    public void Khong_co_van_nao_thi_tra_bang_rong_chu_khong_no()
    {
        TeammateAnalysis.Read([]).Should().BeEmpty();
    }
}
