using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Hệ thống đọc số liệu rồi NÓI RA những gì đáng nói.
///
/// Rủi ro lớn nhất của một bộ nhận định không phải là bỏ sót, mà là nói bừa: một danh sách 8
/// câu mà 6 câu là nhiễu thì tệ hơn 2 câu chắc chắn, vì người đọc mất khả năng phân biệt câu
/// nào đáng tin. Nên phần lớn bài kiểm dưới đây kiểm chiều NGƯỢC LẠI — khi nào thì IM.
/// </summary>
public class TeamInsightsTests
{
    private static TeamFacts Team(
        string slug = "alpha", int maps = 40, double winrate = 50, double killDiff = 0,
        double duration = 35, double? fbRate = null, double? winWhenFb = null,
        int lineupGames = 60, IReadOnlyList<bool>? recent = null,
        int radG = 20, int radW = 10, int direG = 20, int direW = 10,
        string? nemesis = null, int nemLosses = 0, int nemGames = 0) =>
        new(slug, slug.ToUpperInvariant(), maps, winrate, killDiff, duration,
            fbRate, winWhenFb, null, null, 1500, maps, lineupGames, "2026-01-01",
            recent ?? [], radG, radW, direG, direW, nemesis, nemLosses, nemGames);

    private static List<TeamFacts> Peers(params double[] winrates) =>
        winrates.Select((w, i) => Team($"peer{i}", winrate: w, killDiff: 0)).ToList();

    private static bool Has(IEnumerable<Insight> ins, string kind) => ins.Any(i => i.Kind == kind);

    // ---------- Cảnh báo mẫu nhỏ ----------

    /// <summary>
    /// Cảnh báo mẫu nhỏ là ĐIỀU KIỆN để đọc mọi câu còn lại, nên phải đứng đầu — không phải
    /// một mục ngang hàng bị đẩy xuống dưới khi có câu khác "mạnh" hơn.
    /// </summary>
    [Fact]
    public void Doi_hinh_con_moi_thi_canh_bao_dung_dau_danh_sach()
    {
        var t = Team(lineupGames: 9, winrate: 90, recent: [true, true, true, true]);
        var r = TeamInsights.For(t, Peers(50, 50, 50, 50));

        r[0].Kind.Should().Be("mau-nho");
        r[0].Text.Should().Contain("9 ván");
    }

    [Fact]
    public void Doi_hinh_da_lau_thi_khong_canh_bao_nua()
    {
        var t = Team(lineupGames: 80);
        TeamInsights.For(t, Peers(50, 50, 50, 50)).Should().NotContain(i => i.Kind == "mau-nho");
    }

    // ---------- Chuỗi ----------

    [Fact]
    public void Chuoi_tu_ba_van_tro_len_moi_duoc_goi_la_chuoi()
    {
        var hai = Team(recent: [true, true, false, true]);
        var ba = Team(recent: [true, true, true, false]);

        Has(TeamInsights.For(hai, Peers(50, 50, 50, 50)), "chuoi").Should().BeFalse();
        Has(TeamInsights.For(ba, Peers(50, 50, 50, 50)), "chuoi").Should().BeTrue();
    }

    [Fact]
    public void Chuoi_thua_bao_giong_chuoi_thang_nhung_khac_sac_thai()
    {
        var r = TeamInsights.For(Team(recent: [false, false, false, false, true]),
            Peers(50, 50, 50, 50));

        var s = r.Single(i => i.Kind == "chuoi");
        s.Tone.Should().Be("bad");
        s.Text.Should().Contain("thua 4 ván liên tiếp");
    }

    // ---------- Đứng nhất / bét ----------

    /// <summary>Hơn đội thứ hai đúng một chút thì KHÔNG phải "dẫn đầu" theo nghĩa dùng được.</summary>
    [Fact]
    public void Dan_dau_nhung_cach_biet_khong_dang_ke_thi_khong_noi()
    {
        var sat = Team(winrate: 61);          // hơn đội thứ hai 1 điểm
        var xa = Team(winrate: 70);           // hơn 10 điểm

        Has(TeamInsights.For(sat, Peers(60, 55, 50, 45)), "winrate").Should().BeFalse();
        Has(TeamInsights.For(xa, Peers(60, 55, 50, 45)), "winrate").Should().BeTrue();
    }

    [Fact]
    public void Xep_giua_bang_thi_khong_noi_gi()
    {
        var t = Team(winrate: 52);
        Has(TeamInsights.For(t, Peers(70, 60, 50, 40)), "winrate").Should().BeFalse();
    }

    // ---------- Tận dụng first blood ----------

    [Fact]
    public void Thang_cao_hon_han_khi_co_first_blood_thi_duoc_khen()
    {
        var t = Team(winrate: 50, fbRate: 55, winWhenFb: 70);
        var i = TeamInsights.For(t, Peers(50, 50, 50, 50)).Single(x => x.Kind == "first-blood");

        i.Tone.Should().Be("good");
        i.Text.Should().Contain("70%");
    }

    /// <summary>
    /// Mốc so phải là CHÍNH ĐỘI ĐÓ. So với mặt bằng thì đội mạnh lúc nào cũng trông như "biết
    /// tận dụng" dù họ chỉ đơn giản là mạnh hơn ở mọi tình huống.
    /// </summary>
    [Fact]
    public void Doi_manh_deu_o_moi_tinh_huong_thi_KHONG_bi_goi_la_biet_tan_dung()
    {
        var t = Team(winrate: 68, fbRate: 55, winWhenFb: 70);   // chỉ hơn chính mình 2 điểm
        Has(TeamInsights.For(t, Peers(50, 50, 50, 50)), "first-blood").Should().BeFalse();
    }

    [Fact]
    public void Co_first_blood_ma_van_thua_nhieu_hon_thi_bao_la_diem_yeu()
    {
        var t = Team(winrate: 55, fbRate: 60, winWhenFb: 40);
        var i = TeamInsights.For(t, Peers(50, 50, 50, 50)).Single(x => x.Kind == "first-blood");

        i.Tone.Should().Be("bad");
        i.Text.Should().Contain("không giữ được lợi thế");
    }

    // ---------- Kill nhiều mà không thắng ----------

    [Fact]
    public void Hon_ve_mang_ma_thua_tran_thi_phai_noi_ra()
    {
        var t = Team(winrate: 40, killDiff: 3.5);
        var i = TeamInsights.For(t, Peers(50, 50, 50, 50)).Single(x => x.Kind == "kill-vs-win");

        i.Text.Should().Contain("Thắng giao tranh nhưng thua trận");
    }

    [Fact]
    public void Thang_ma_khong_hon_ve_mang_cung_la_mot_nhan_dinh()
    {
        var t = Team(winrate: 60, killDiff: -0.5);
        TeamInsights.For(t, Peers(50, 50, 50, 50))
            .Single(x => x.Kind == "kill-vs-win").Tone.Should().Be("good");
    }

    // ---------- Bên sân ----------

    /// <summary>
    /// Đây là bài kiểm quan trọng nhất của cả bộ: ở cỡ mẫu nhỏ, lệch 20 điểm phần trăm giữa
    /// hai bên vẫn hoàn toàn có thể là ngẫu nhiên. Không kiểm thì trang sẽ tuyên bố mọi đội
    /// đều "mạnh hơn hẳn ở một bên".
    /// </summary>
    [Fact]
    public void Lech_ben_san_o_co_mau_nho_thi_KHONG_ket_luan()
    {
        // 6/10 so với 4/10 — lệch 20 điểm, nhưng nằm gọn trong may rủi
        var nho = Team(radG: 10, radW: 6, direG: 10, direW: 4);
        Has(TeamInsights.For(nho, Peers(50, 50, 50, 50)), "ben-san").Should().BeFalse();

        // cùng tỷ lệ nhưng 60/100 so với 40/100 thì đã tách được
        var lon = Team(radG: 100, radW: 60, direG: 100, direW: 40);
        Has(TeamInsights.For(lon, Peers(50, 50, 50, 50)), "ben-san").Should().BeTrue();
    }

    [Fact]
    public void Qua_it_van_mot_ben_thi_khong_so_sanh()
    {
        var t = Team(radG: 40, radW: 30, direG: 3, direW: 0);
        Has(TeamInsights.For(t, Peers(50, 50, 50, 50)), "ben-san").Should().BeFalse();
    }

    // ---------- Cặp đối đầu lệch nhất ----------

    /// <summary>
    /// Bài kiểm quan trọng nhất của cả bộ. Cặp này được chọn là cặp CỰC ĐOAN NHẤT trong 15
    /// đối thủ, nên chấm nó bằng ngưỡng dành cho một phép so duy nhất là lỗi so sánh bội —
    /// bản đầu mắc đúng lỗi này và nhận định bật cho 11/16 đội.
    /// </summary>
    [Fact]
    public void Thua_5_5_thi_neu_con_so_nhung_KHONG_duoc_goi_la_khac_che()
    {
        var i = TeamInsights.For(Team(nemesis: "BETA", nemLosses: 5, nemGames: 5),
            Peers(50, 50, 50, 50)).Single(x => x.Kind == "khac-tinh");

        i.Text.Should().Contain("5/5");
        i.Text.Should().Contain("có thể chỉ là ngẫu nhiên");
        i.Text.Should().NotContain("Bị BETA khắc chế");
        i.Tone.Should().Be("flat");
    }

    [Fact]
    public void Cach_biet_du_lon_de_vuot_hieu_chinh_thi_moi_goi_la_khac_che()
    {
        var i = TeamInsights.For(Team(nemesis: "BETA", nemLosses: 10, nemGames: 10),
            Peers(50, 50, 50, 50)).Single(x => x.Kind == "khac-tinh");

        i.Tone.Should().Be("bad");
        i.Text.Should().Contain("khắc chế");
        i.Text.Should().Contain("15 đối thủ");
    }

    /// <summary>Cặp lệch chưa chắc chắn phải xếp DƯỚI nhận định thật, không được chiếm chỗ.</summary>
    [Fact]
    public void Cap_lech_chua_chac_chan_xep_duoi_nhan_dinh_that()
    {
        var t = Team(nemesis: "BETA", nemLosses: 5, nemGames: 5,
            recent: [false, false, false, false]);

        var r = TeamInsights.For(t, Peers(50, 50, 50, 50));
        r.FindIndex(x => x.Kind == "chuoi")
            .Should().BeLessThan(r.FindIndex(x => x.Kind == "khac-tinh"));
    }

    [Fact]
    public void Doi_dau_can_bang_hoac_qua_it_van_thi_khong_neu()
    {
        Has(TeamInsights.For(Team(nemesis: "BETA", nemLosses: 4, nemGames: 7),
            Peers(50, 50, 50, 50)), "khac-tinh").Should().BeFalse();

        Has(TeamInsights.For(Team(nemesis: "BETA", nemLosses: 4, nemGames: 4),
            Peers(50, 50, 50, 50)), "khac-tinh").Should().BeFalse();
    }

    /// <summary>Chuỗi ĐANG diễn ra, không phải chuỗi dài nhất — đừng khai điều không kiểm.</summary>
    [Fact]
    public void Chuoi_khong_duoc_khai_la_chuoi_dai_nhat()
    {
        var r = TeamInsights.For(Team(recent: [true, true, true, false, true, true, true, true, true]),
            Peers(50, 50, 50, 50));

        var s = r.Single(i => i.Kind == "chuoi");
        s.Text.Should().Contain("thắng 3 ván liên tiếp");
        s.Text.Should().NotContain("dài nhất");
    }

    // ---------- Không đủ dữ liệu ----------

    /// <summary>Quá ít ván thì CHỈ nói ra điều đó, không suy diễn thêm gì.</summary>
    [Fact]
    public void Qua_it_van_thi_chi_canh_bao_chu_khong_ket_luan()
    {
        var t = Team(maps: 3, lineupGames: 3, winrate: 100, killDiff: 12,
            recent: [true, true, true]);

        var r = TeamInsights.For(t, Peers(50, 50, 50, 50));

        r.Should().ContainSingle();
        r[0].Kind.Should().Be("mau-nho");
    }
}
