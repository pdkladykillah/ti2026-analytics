using FluentAssertions;
using Ti2026.Ingest.Analytics;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Tests;

/// <summary>
/// Chữ ký lối chơi và hệ neo chống lệch hạng đấu.
///
/// Bài kiểm quan trọng nhất ở đây là <see cref="Cung_mot_loi_choi_o_hai_hang_dau_phai_ra_cung_chi_so"/>:
/// nó dựng lại đúng cái bẫy đã đo được trên dữ liệu thật, nơi so số thô giữa ván pub và ván
/// chuyên nghiệp cho kết luận sai cả hướng.
/// </summary>
public class IdolStyleTests
{
    /// <summary>Ván chỉ có K/D/A — các trục cần chỉ số khác sẽ tự vắng mặt.</summary>
    private static List<StyleGame> Kda(int count, int k, int d, int a) =>
        Enumerable.Range(0, count)
            .Select(_ => new StyleGame(k, d, a, null, null, null, null, null, null, 2400, 2))
            .ToList();

    private static List<StylePool> Pool(int count, int kills, int assists, int deaths) =>
        Enumerable.Range(0, count)
            .Select(_ => new StylePool(kills, assists, deaths, 250_000, 300_000, 2000, 20_000,
                500, 10, 10, 2400))
            .ToList();

    /// <summary>
    /// CÁI BẪY CHÍNH, dựng lại từ số đo thật.
    ///
    /// Đo trên 126 ván chuyên nghiệp và 114 ván pub của chính người dùng: ván pub có nhiều hơn 46%
    /// số mạng mỗi 10 phút. Nên hai người chơi CÙNG một lối — cùng "rẻ hơn người bình thường quanh
    /// mình 20%" — sẽ có số thô lệch hẳn nhau. Ai so số thô sẽ kết luận người ở hồ pub chơi tệ hơn,
    /// trong khi thật ra hai người giống hệt nhau.
    ///
    /// Bài kiểm này khẳng định cả hai vế: chỉ số neo phải BẰNG nhau, và số thô phải KHÁC nhau —
    /// vế sau để chứng minh bài kiểm có răng, chứ không phải đang so hai con số vốn đã giống.
    /// </summary>
    [Fact]
    public void Cung_mot_loi_choi_o_hai_hang_dau_phai_ra_cung_chi_so()
    {
        // Hồ "chuyên nghiệp": người bình thường trả 45/150 = 0,30 mạng cho mỗi pha hạ gục.
        var proNorm = IdolStyle.Normalizer(Pool(60, kills: 50, assists: 100, deaths: 45));

        // Hồ "pub": người bình thường trả 88/200 = 0,44 — nhiều mạng hơn hẳn, đúng như đo được.
        var pubNorm = IdolStyle.Normalizer(Pool(60, kills: 70, assists: 130, deaths: 88));

        // Cả hai đều rẻ hơn mốc của mình đúng 20%.
        var pro = IdolStyle.Signature(Kda(40, k: 5, d: 6, a: 20), proNorm);      // 6/25   = 0,240
        var pub = IdolStyle.Signature(Kda(40, k: 50, d: 88, a: 200), pubNorm);   // 88/250 = 0,352

        var proAxis = pro.Single(v => v.Key == "gia-mang");
        var pubAxis = pub.Single(v => v.Key == "gia-mang");

        proAxis.Index.Should().BeApproximately(0.8, 0.001);
        pubAxis.Index.Should().BeApproximately(0.8, 0.001);

        // Vế chứng minh bài kiểm có răng: số thô lệch nhau gần 50%, nên nếu hệ neo bị gỡ bỏ thì
        // hai chỉ số trên không thể nào còn bằng nhau.
        pubAxis.Raw.Should().BeGreaterThan(proAxis.Raw * 1.4);
    }

    /// <summary>
    /// Không đủ ván neo thì KHÔNG có mốc, và chữ ký phải ra chỉ số null chứ không được lặng lẽ
    /// rơi về 1,0 — "bằng người bình thường" là một khẳng định, còn "chưa đo được" thì không.
    /// </summary>
    [Fact]
    public void Thieu_van_neo_thi_khong_bia_ra_moc()
    {
        var norm = IdolStyle.Normalizer(Pool(IdolStyle.MinPoolMatches - 1, 50, 100, 45));
        norm.Should().BeEmpty();

        var sig = IdolStyle.Signature(Kda(40, 5, 6, 20), norm);
        sig.Should().NotBeEmpty("số thô vẫn tính được kể cả khi chưa có mốc");
        sig.Single(v => v.Key == "gia-mang").Index.Should().BeNull();
        sig.Single(v => v.Key == "gia-mang").Norm.Should().BeNull();
    }

    [Fact]
    public void Duoi_nguong_so_van_thi_truc_do_vang_mat()
    {
        var norm = IdolStyle.Normalizer(Pool(60, 50, 100, 45));
        var sig = IdolStyle.Signature(Kda(IdolStyle.MinGames - 1, 5, 6, 20), norm);

        sig.Should().BeEmpty();
    }

    /// <summary>
    /// Dưới ba trục chung thì không xếp hạng độ giống nhau. Thà không nói gì còn hơn tuyên bố
    /// "bạn giống Collapse nhất" dựa trên đúng một trục.
    /// </summary>
    [Fact]
    public void Duoi_ba_truc_chung_thi_khong_so_sanh()
    {
        var norm = IdolStyle.Normalizer(Pool(60, 50, 100, 45));
        var mine = IdolStyle.Signature(Kda(40, 5, 6, 20), norm);
        var theirs = IdolStyle.Signature(Kda(40, 6, 5, 19), norm);

        // Ván chỉ có K/D/A nên chỉ dựng được hai trục: giá mạng và kết liễu.
        mine.Should().HaveCount(2);
        IdolStyle.Compare(1, "mid", mine, theirs).Should().BeNull();
    }

    /// <summary>
    /// Khoảng cách phải đo trên thang log vì đây là TỈ SỐ.
    ///
    /// Gấp đôi và bằng một nửa là lệch như nhau. Dùng hiệu thường thì 2,0 − 1,0 = 1,0 còn
    /// 1,0 − 0,5 = 0,5, tức người vượt trội luôn bị coi là "khác biệt hơn" người thua kém —
    /// và bảng "bạn giống ai nhất" sẽ nghiêng một cách có hệ thống.
    /// </summary>
    [Fact]
    public void Khoang_cach_doi_xung_giua_gap_doi_va_mot_nua()
    {
        var a = Sig(1.0, 1.0, 1.0);
        var gapUp = IdolStyle.Compare(1, "mid", a, Sig(2.0, 1.0, 1.0))!.Value.Distance;
        var gapDown = IdolStyle.Compare(1, "mid", a, Sig(0.5, 1.0, 1.0))!.Value.Distance;

        gapUp.Should().BeApproximately(gapDown, 1e-9);
    }

    [Fact]
    public void Truc_lech_nhieu_nhat_dung_dau_danh_sach()
    {
        var cmp = IdolStyle.Compare(7, "off", Sig(1.0, 1.0, 1.0), Sig(1.05, 1.0, 3.0))!.Value;

        cmp.IdolId.Should().Be(7);
        cmp.Role.Should().Be("off");
        cmp.SharedAxes.Should().Be(3);
        cmp.Diffs[0].Axis.Should().Be("hieu-suat-lane", "trục lệch gấp ba phải đứng trước trục lệch 5%");
    }

    /// <summary>Ba trục có chỉ số cho trước, để kiểm phần so sánh mà không phải dựng ván giả.</summary>
    private static List<StyleValue> Sig(double giaMang, double ketLieu, double lane) =>
    [
        new("gia-mang", 40, 0.3, 0.3, giaMang),
        new("ket-lieu", 40, 0.3, 0.3, ketLieu),
        new("hieu-suat-lane", 40, 70, 70, lane),
    ];

    [Theory]
    // 10 ván nên cần phủ 8 ván: 4+2+1+1 mới đủ, tức 4 hero.
    [InlineData(new[] { 1, 1, 1, 1, 2, 2, 3, 4, 5, 6 }, 4)]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, 4)]
    [InlineData(new[] { 9, 9, 9, 9 }, 1)]
    public void Dem_so_hero_phu_het_tam_muoi_phan_tram(int[] heroes, int expected) =>
        IdolStyle.HeroesCovering(heroes).Should().Be(expected);

    [Fact]
    public void Khong_co_van_nao_thi_hero_pool_bang_khong() =>
        IdolStyle.HeroesCovering([]).Should().Be(0);

    /// <summary>
    /// Ván thi đấu chuyên nghiệp mang lobby_type = 1, KHÔNG phải 2.
    ///
    /// Đo thật: 300/300 ván gần nhất của Malr1ne là lobby_type 1 và 0 ván là lobby_type 2. Lọc
    /// theo 2 vì cái tên "tournament" nghe hợp lý thì được đúng con số không, và trang sẽ trống
    /// trơn mà không có gì báo lỗi.
    /// </summary>
    [Theory]
    [InlineData(1, IdolIngester.ProPool)]
    [InlineData(2, IdolIngester.ProPool)]
    [InlineData(7, IdolIngester.IdolPubPool)]
    [InlineData(0, IdolIngester.IdolPubPool)]
    [InlineData(9, null)]
    [InlineData(null, null)]
    public void Phan_lobby_vao_dung_ho_van(int? lobbyType, string? expected) =>
        IdolIngester.AnchorPoolFor(lobbyType).Should().Be(expected);

    /// <summary>
    /// Battle Cup và các chế độ vui KHÔNG được neo. Chúng có nhịp riêng, và trộn vào sẽ làm lệch
    /// mốc "người bình thường" của cả hai hồ kia.
    /// </summary>
    [Fact]
    public void Battle_cup_khong_neo_vao_ho_nao() =>
        IdolIngester.AnchorPoolFor(9).Should().BeNull();

    [Fact]
    public void Van_thieu_nguoi_thi_khong_lam_neo()
    {
        var detail = new OpenDotaMatchDetail
        {
            MatchId = 1, Duration = 2400,
            Players = Enumerable.Range(0, 9).Select(_ => new OpenDotaMatchPlayer()).ToList(),
        };

        IdolIngester.MakeAnchor("pro", detail).Should()
            .BeNull("một ván 9 người sẽ kéo mọi tỉ số 'người bình thường' lệch đi mà không ai thấy");
    }

    /// <summary>
    /// Sát thương phải chịu: từ điển rỗng phải ra null, không phải 0.
    ///
    /// 0 nghĩa là "không hề bị đánh" — một khẳng định. Ván chưa parse thì đơn giản là không đo
    /// được, và gộp hai thứ đó lại sẽ kéo mọi trung vị xuống theo số ván chưa parse.
    /// </summary>
    [Fact]
    public void Sat_thuong_phai_chiu_thieu_du_lieu_thi_null_chu_khong_phai_khong()
    {
        IdolIngester.TotalDamageTaken(new OpenDotaMatchPlayer()).Should().BeNull();
        IdolIngester.TotalDamageTaken(new OpenDotaMatchPlayer { DamageTaken = [] }).Should().BeNull();

        IdolIngester.TotalDamageTaken(new OpenDotaMatchPlayer
        {
            DamageTaken = new Dictionary<string, int> { ["npc_a"] = 100, ["npc_b"] = 250 },
        }).Should().Be(350);
    }

    /// <summary>
    /// Mốc của trục "phần tài nguyên đội" luôn là 1/5, không phụ thuộc hồ ván.
    ///
    /// Đây là trục ĐÃ ĐO và thấy không phân biệt được ai với ai ở cấp chuyên nghiệp: cả bốn tuyển
    /// thủ đều nằm gọn trong 22,2%–24,5%. Giữ lại vì nó vẫn tách được vai trò của người dùng
    /// (safe 25,0% so với off 21,5%), nhưng phải nhớ giới hạn đó khi đọc.
    /// </summary>
    [Fact]
    public void Moc_phan_tai_nguyen_luon_la_mot_phan_nam()
    {
        var norm = IdolStyle.Normalizer(Pool(60, 50, 100, 45));
        norm["phan-tai-nguyen"].Should().BeApproximately(0.2, 1e-9);
    }

    /// <summary>
    /// Trục nào là "chất lượng" và trục nào chỉ là "phong cách" phải khai rõ trong dữ liệu.
    ///
    /// Nếu thiếu, giao diện sẽ vẽ mọi trục như nhau và ngầm bảo rằng vươn ra ở "sát thương trên
    /// mỗi vàng" là điểm mạnh — tức khuyên người đọc bỏ farm đi đánh nhau.
    /// </summary>
    [Fact]
    public void Moi_truc_phai_khai_ro_la_chat_luong_hay_phong_cach()
    {
        IdolStyle.Axes.Should().OnlyContain(a => a.Kind == "chat-luong" || a.Kind == "phong-cach");
        IdolStyle.Axes.Should().Contain(a => a.Kind == "phong-cach");
        IdolStyle.Axes.Should().Contain(a => a.Kind == "chat-luong");

        IdolStyle.Axes.Single(a => a.Key == "gia-mang").LowerIsBetter.Should().BeTrue();
        IdolStyle.Axes.Single(a => a.Key == "st-tren-vang").Kind.Should().Be("phong-cach");
    }

    /// <summary>
    /// Trục nào THẤP mới tốt thì phải mang một tên khác khi lên biểu đồ.
    ///
    /// Biểu đồ nhiều góc luôn đọc là "vươn ra = nhiều hơn". Vẽ "giá mỗi pha hạ gục" nguyên chiều
    /// thì hình vươn ra ở đúng chỗ người đó đang yếu — và người xem sẽ đọc ngược hoàn toàn. Cách
    /// chữa là đảo giá trị VÀ đổi tên theo chiều đã đảo; bài kiểm này chặn việc chỉ làm một nửa.
    /// </summary>
    [Fact]
    public void Truc_thap_moi_tot_phai_co_ten_rieng_khi_ve_bieu_do()
    {
        IdolStyle.Axes.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.PlotLabel));

        foreach (var axis in IdolStyle.Axes.Where(a => a.LowerIsBetter))
            axis.PlotLabel.Should().NotBe(axis.Label,
                $"trục '{axis.Key}' bị đảo chiều lúc vẽ nên không được giữ nguyên tên cũ");
    }

    [Theory]
    [InlineData(1, "safe")]
    [InlineData(2, "mid")]
    [InlineData(3, "off")]
    [InlineData(4, null)]
    [InlineData(null, null)]
    public void Doc_ten_vai_tro_tu_nhan_replay(int? laneRole, string? expected) =>
        IdolStyle.RoleOf(laneRole).Should().Be(expected);

    /// <summary>
    /// Hạt giống phải khoá theo account_id và không được trùng nhau.
    ///
    /// Có bốn tài khoản mang persona "TOPSON" và ba tài khoản mang "AMMAR_THE_F" trên OpenDota.
    /// Một lần sao chép nhầm id ở đây là cả tab phân tích nhầm người mà mọi con số vẫn hợp lý.
    /// </summary>
    [Fact]
    public void Hat_giong_khong_trung_id_va_khong_trung_ten()
    {
        IdolIngester.Seed.Select(s => s.AccountId).Should().OnlyHaveUniqueItems();
        IdolIngester.Seed.Select(s => s.Name.ToUpperInvariant()).Should().OnlyHaveUniqueItems();
        IdolIngester.Seed.Should().OnlyContain(s => s.AccountId > 0);
    }
}
