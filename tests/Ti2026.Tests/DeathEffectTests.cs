using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// "Cái chết của tôi có đổi được gì không" — đo bằng kinh tế ĐỒNG ĐỘI.
///
/// Bộ này thay cho cách cũ dùng chỉ số hỗ trợ làm proxy. Người dùng bác đúng: hỗ trợ chỉ ghi
/// nhận việc CÓ MẶT lúc hạ gục, mà người đã chết thì không thể có mặt ở pha hạ gục sau đó — nên
/// một cái chết mua thời gian cho đồng đội đi farm hoàn toàn không để lại dấu vết trong đó.
/// </summary>
public class DeathEffectTests
{
    // Mọi ván dài ĐÚNG BẰNG NHAU trong bộ này, để phép lọc độ dài không loại ván nào — ở đây
    // đang kiểm phần khác. Riêng bài kiểm về độ dài thì tự truyền giá trị khác.
    private static DeathGame G(bool won, int deaths, int matesFarm, int lead = 0, int secs = 2400) =>
        new(won, deaths, matesFarm, 100_000 + lead, 100_000, secs);

    /// <summary>
    /// Dựng một tập ván mà trong ĐÓ, ván ta chết nhiều thì đồng đội farm tốt hơn <paramref
    /// name="effect"/> điểm — tức đúng chiều của lối chơi hi sinh khi effect dương.
    /// </summary>
    private static List<DeathGame> Build(int perOutcome, int effect)
    {
        var list = new List<DeathGame>();

        foreach (var won in new[] { true, false })
            for (var i = 0; i < perOutcome; i++)
            {
                // Phân vị số chết trải đều 0..100; đồng đội farm quanh 50 cộng phần hiệu ứng.
                var deaths = i * 100 / Math.Max(perOutcome - 1, 1);
                var matesFarm = 50 + (deaths - 50) * effect / 100 + (i % 7) - 3;
                list.Add(G(won, deaths, Math.Clamp(matesFarm, 0, 100)));
            }

        return list;
    }

    // ---------- Chiều của kết luận ----------

    [Fact]
    public void Chet_nhieu_ma_dong_doi_farm_tot_hon_thi_ket_luan_la_HO_TRO()
    {
        var r = DeathEffect.Read(Build(400, effect: 60));

        r.Verdict.Should().Be("ho-tro");
        r.Splits.Should().HaveCount(2);
        r.Splits.Should().OnlyContain(s => s.MatesFarmGap > 0);
    }

    [Fact]
    public void Chet_nhieu_ma_dong_doi_farm_kem_hon_thi_ket_luan_la_PHAN_BAC()
    {
        var r = DeathEffect.Read(Build(400, effect: -60));

        r.Verdict.Should().Be("phan-bac");
        r.Splits.Should().OnlyContain(s => s.MatesFarmGap < 0);
    }

    [Fact]
    public void Khong_co_lien_he_thi_khong_ket_luan()
    {
        DeathEffect.Read(Build(400, effect: 0)).Verdict.Should().Be("khong-ro");
    }

    // ---------- Điều kiện bắt buộc ----------

    /// <summary>
    /// Bài kiểm quan trọng nhất. Nếu ván thắng nói một đằng và ván thua nói một nẻo thì đó KHÔNG
    /// phải phát hiện — đó là hai hiệu ứng khác nhau bị gộp làm một, và chọn lấy nửa hợp ý mình
    /// là cách chắc chắn nhất để tìm ra thứ mình muốn thấy.
    /// </summary>
    [Fact]
    public void Thang_va_thua_noi_nguoc_nhau_thi_KHONG_duoc_ket_luan()
    {
        var list = new List<DeathGame>();
        for (var i = 0; i < 400; i++)
        {
            var deaths = i * 100 / 399;
            list.Add(G(true, deaths, Math.Clamp(50 + (deaths - 50) * 60 / 100, 0, 100)));
            list.Add(G(false, deaths, Math.Clamp(50 - (deaths - 50) * 60 / 100, 0, 100)));
        }

        var r = DeathEffect.Read(list);

        r.Splits.Should().HaveCount(2);
        r.Splits[0].MatesFarmGap.Should().BePositive();
        r.Splits[1].MatesFarmGap.Should().BeNegative();
        r.Verdict.Should().Be("khong-ro");
    }

    [Fact]
    public void Thieu_van_o_mot_nhom_ket_qua_thi_khong_ket_luan()
    {
        var list = Build(400, effect: 60)
            .Where(g => g.Won || Random.Shared.Next(100) < 5).ToList();

        var few = Build(400, effect: 60).Where(g => g.Won).ToList();
        few.AddRange(Build(50, effect: 60).Where(g => !g.Won));

        var r = DeathEffect.Read(few);

        r.Splits.Should().ContainSingle("chỉ nhóm thắng đủ ván");
        r.Verdict.Should().Be("khong-du-du-lieu");
        r.Text.Should().Contain("Chưa đủ");
    }

    [Fact]
    public void Chenh_qua_nho_thi_khong_ket_luan_du_mau_rat_lon()
    {
        // Hiệu ứng chỉ vài điểm phân vị: qua được phép kiểm nhưng dưới ngưỡng đáng nói.
        var r = DeathEffect.Read(Build(3000, effect: 5));

        r.Splits.Should().OnlyContain(s => Math.Abs(s.MatesFarmGap) < DeathEffect.MinFarmGap);
        r.Verdict.Should().Be("khong-ro");
    }

    [Fact]
    public void Van_thieu_du_lieu_dong_doi_thi_bo_qua_chu_khong_coi_la_khong()
    {
        var list = Build(400, effect: 60);
        list.AddRange(Enumerable.Range(0, 200).Select(_ =>
            new DeathGame(true, 90, null, null, null, 2400)));

        var r = DeathEffect.Read(list);

        r.Splits.Single(s => s.Outcome == "thắng").Games.Should().Be(400);
    }

    // ---------- Khống chế độ dài ván ----------

    /// <summary>
    /// BÀI KIỂM QUAN TRỌNG NHẤT CỦA BỘ NÀY, vì nó khoá lại một kết luận đã bị đảo dấu trên dữ
    /// liệu thật.
    ///
    /// Bản đầu chỉ khống chế thắng/thua và cho ra: ván thua mà chết nhiều thì đồng đội farm KÉM
    /// hơn 13 điểm — nghe như bằng chứng phản bác lối chơi hi sinh. Nhưng hai nhóm đó không so
    /// được: ván thua chết ít dài trung bình 47 phút (thua dai dẳng, ai cũng kịp farm), ván thua
    /// chết nhiều chỉ 35 phút (bị đè). Phép so đó đang so ĐỘ DÀI VÁN.
    ///
    /// Lọc về cùng dải độ dài thì dấu đảo: −13 thành +8. Đo trên người thứ hai cũng vậy: −9
    /// thành +8.
    ///
    /// Bộ dữ liệu dưới đây tái dựng đúng cái bẫy: ván ngắn thì mọi người farm ít VÀ ta chết
    /// nhiều, ván dài thì ngược lại — không hề có liên hệ thật giữa cái chết và farm đồng đội.
    /// Không lọc độ dài thì phép đo sẽ thấy một liên hệ âm rất mạnh và hoàn toàn giả.
    /// </summary>
    [Fact]
    public void Do_dai_van_khong_duoc_phep_lot_vao_phep_so()
    {
        var list = new List<DeathGame>();

        foreach (var won in new[] { true, false })
            for (var i = 0; i < 600; i++)
            {
                // Ván càng ngắn thì càng chết nhiều và đồng đội càng farm ít — cả hai đều do độ
                // dài, không do nhau. Trong CÙNG một độ dài thì không có liên hệ nào.
                var shortness = i % 3;                       // 0 dài, 1 vừa, 2 ngắn
                var secs = 2900 - shortness * 500;
                var deaths = Math.Clamp(30 + shortness * 25 + (i % 5) - 2, 0, 100);
                var farm = Math.Clamp(70 - shortness * 25 + (i % 5) - 2, 0, 100);
                list.Add(G(won, deaths, farm, secs: secs));
            }

        var r = DeathEffect.Read(list);

        r.Splits.Should().HaveCount(2);
        r.Splits.Should().OnlyContain(s => Math.Abs(s.MatesFarmGap) < DeathEffect.MinFarmGap,
            "trong cùng một dải độ dài thì không còn liên hệ nào");
        r.Verdict.Should().Be("khong-ro");

        // Và phải nói rõ mình đang so ở dải độ dài nào, chứ không lặng lẽ vứt bớt ván.
        r.Text.Should().Contain("phút");
        r.Splits.Should().OnlyContain(s => s.MedianMinutes > 0);
    }

    [Fact]
    public void Loc_do_dai_lam_mong_qua_thi_khong_ket_luan()
    {
        // Độ dài trải rất rộng nên dải quanh trung vị chỉ giữ lại một nhúm ván.
        var list = new List<DeathGame>();
        foreach (var won in new[] { true, false })
            for (var i = 0; i < 300; i++)
                list.Add(G(won, i % 100, 50 + (i % 20), secs: 600 + i * 12));

        DeathEffect.Read(list).Verdict.Should().Be("khong-du-du-lieu");
    }

    // ---------- Mann–Whitney ----------

    [Fact]
    public void Hai_nhom_giong_het_nhau_thi_p_gan_1()
    {
        var a = Enumerable.Range(0, 200).Select(i => i % 50).ToList();
        DeathEffect.MannWhitney(a, [.. a]).Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void Hai_nhom_tach_han_thi_p_rat_nho()
    {
        var a = Enumerable.Repeat(80, 100).ToList();
        var b = Enumerable.Repeat(20, 100).ToList();

        DeathEffect.MannWhitney(a, b).Should().BeLessThan(1e-10);
    }

    /// <summary>
    /// Phân vị làm tròn về số nguyên nên chỉ có 101 giá trị khả dĩ, và với vài trăm ván thì đồng
    /// hạng dày đặc. Bỏ hiệu chỉnh đồng hạng sẽ ước lượng phương sai cao hơn thực tế và làm phép
    /// kiểm quá dễ dãi — ở đây mọi giá trị bằng nhau, phương sai đúng phải là 0 nên p phải là 1.
    /// </summary>
    [Fact]
    public void Tat_ca_dong_hang_thi_khong_the_ket_luan_gi()
    {
        var a = Enumerable.Repeat(50, 100).ToList();
        var b = Enumerable.Repeat(50, 100).ToList();

        DeathEffect.MannWhitney(a, b).Should().Be(1);
    }

    [Fact]
    public void Nhom_rong_thi_khong_no()
    {
        DeathEffect.MannWhitney([], [1, 2, 3]).Should().Be(1);
        DeathEffect.Read([]).Verdict.Should().Be("khong-du-du-lieu");
    }
}
