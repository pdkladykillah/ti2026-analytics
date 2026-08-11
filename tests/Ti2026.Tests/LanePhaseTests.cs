using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Giai đoạn lane. Phần này sạch hơn mọi chỉ số khác vì nó đo TRƯỚC KHI ván ngã ngũ — nên so
/// giữa ván thắng và ván thua ở mốc phút 10 không vướng vòng nhân quả như chỉ số cuối trận.
/// </summary>
public class LanePhaseTests
{
    private static List<LaneGame> Make(
        int perSide, int effWin, int effLoss, int advWin, int advLoss, string role = "pos2")
    {
        var g = new List<LaneGame>();
        for (var i = 0; i < perSide; i++)
        {
            g.Add(new LaneGame(true, role, role, effWin, advWin, advWin * 2, advWin * 3));
            g.Add(new LaneGame(false, role, role, effLoss, advLoss, advLoss * 2, advLoss * 3));
        }
        return g;
    }

    [Fact]
    public void Thua_ngay_tu_lane_thi_ket_luan_dung_the()
    {
        var r = LanePhase.Read(Make(100, effWin: 60, effLoss: 48, advWin: 3000, advLoss: -3000))!.Value;

        r.Verdict.Should().Be("thua-tu-lane");
        r.Text.Should().Contain("lệch ngay từ lane");
        r.Text.Should().Contain("mười phút đầu");
    }

    /// <summary>
    /// Ca quan trọng hơn: lane hai bên GIỐNG NHAU, chênh lệch chỉ mở ra về sau. Kết luận phải
    /// ngược hẳn — và lời khuyên cũng phải ngược, vì bảo một người thắng lane đi luyện lane là
    /// chữa sai bệnh.
    /// </summary>
    [Fact]
    public void Lane_deu_nhau_ma_mat_ve_sau_thi_ket_luan_nguoc_lai()
    {
        var g = new List<LaneGame>();
        for (var i = 0; i < 100; i++)
        {
            // Phút 10 gần như bằng nhau; phút 20 và 30 mới tách hẳn.
            g.Add(new LaneGame(true, "pos2", "Mid", 55, 300, 6000, 12000));
            g.Add(new LaneGame(false, "pos2", "Mid", 54, -200, -6000, -12000));
        }

        var r = LanePhase.Read(g)!.Value;

        r.Verdict.Should().Be("mat-ve-sau");
        r.Text.Should().Contain("KHÔNG thua từ lane");
        r.Text.Should().Contain("chuyển giai đoạn");
    }

    [Fact]
    public void Neu_du_ba_moc_thoi_gian_de_thay_hinh_dang()
    {
        var r = LanePhase.Read(Make(100, 60, 48, 3000, -3000))!.Value;

        var win = r.Sides.Single(s => s.Outcome == "thắng");
        win.Adv10.Should().Be(3000);
        win.Adv20.Should().Be(6000);
        win.Adv30.Should().Be(9000);
        r.Text.Should().Contain("phút 10");
    }

    [Fact]
    public void Tach_duoc_hieu_suat_lane_theo_vai_tro()
    {
        var g = Make(60, 60, 50, 1000, -1000, role: "pos2");
        g.AddRange(Make(40, 40, 35, 500, -500, role: "pos5"));

        var by = LanePhase.Read(g)!.Value.ByRole.ToDictionary(x => x.Role);

        by["pos2"].Games.Should().Be(120);
        by["pos5"].Efficiency.Should().BeLessThan(by["pos2"].Efficiency,
            "hỗ trợ vốn lấy được ít tài nguyên lane hơn");
    }

    [Fact]
    public void Vai_tro_qua_it_van_thi_khong_dung_rieng_thanh_dong()
    {
        var g = Make(60, 60, 50, 1000, -1000, role: "pos2");
        g.AddRange(Make(5, 40, 35, 0, 0, role: "pos5"));

        LanePhase.Read(g)!.Value.ByRole.Should().ContainSingle()
            .Which.Role.Should().Be("pos2");
    }

    [Fact]
    public void Thieu_van_mot_ben_thi_khong_ket_luan()
    {
        var g = new List<LaneGame>();
        for (var i = 0; i < 100; i++) g.Add(new LaneGame(true, "pos2", "Mid", 60, 3000, 6000, 9000));
        for (var i = 0; i < 5; i++) g.Add(new LaneGame(false, "pos2", "Mid", 40, -3000, -6000, -9000));

        var r = LanePhase.Read(g)!.Value;

        r.Verdict.Should().Be("khong-du-du-lieu");
        r.Sides.Should().BeEmpty();
    }

    /// <summary>
    /// Ván kết thúc trước phút 30 thì mốc đó phải để TRỐNG, không được điền 0 — 0 nghĩa là hai
    /// phe ngang nhau, khác hẳn "ván đã xong rồi".
    /// </summary>
    [Fact]
    public void Van_ket_thuc_som_thi_moc_phut_30_de_trong()
    {
        var g = new List<LaneGame>();
        for (var i = 0; i < 100; i++)
        {
            g.Add(new LaneGame(true, "pos2", "Mid", 60, 3000, 6000, null));
            g.Add(new LaneGame(false, "pos2", "Mid", 48, -3000, -6000, null));
        }

        var r = LanePhase.Read(g)!.Value;

        r.Sides.Should().OnlyContain(s => s.Adv30 == null);
        r.Text.Should().Contain("—");
    }

    [Fact]
    public void Van_chua_parse_thi_khong_tinh_vao()
    {
        var g = Make(100, 60, 48, 3000, -3000);
        g.AddRange(Enumerable.Range(0, 500).Select(_ =>
            new LaneGame(true, "", "", null, null, null, null)));

        LanePhase.Read(g)!.Value.Games.Should().Be(200);
    }

    [Fact]
    public void Rong_thi_tra_null()
    {
        LanePhase.Read([]).Should().BeNull();
    }
}
