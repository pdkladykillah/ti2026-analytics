using FluentAssertions;
using Ti2026.Ingest.Analytics;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Tests;

/// <summary>
/// Chấm kỹ năng bằng phân vị theo hero. Hai cái bẫy của nguồn dữ liệu được khoá ở đây, vì cả hai
/// đều KHÔNG làm chương trình đổ vỡ — chúng chỉ tạo ra những câu khen sai trên trang.
/// </summary>
public class SkillComponentsTests
{
    private static RatedGame G(
        int day, bool won = true, string role = "core",
        int? gpm = null, int? deaths = null, int? dmg = null, int? lh = null) =>
        new(new DateTime(2026, 1, 1).AddDays(day), won, role,
            Gpm: gpm, Xpm: null, LastHits: lh, Denies: null,
            Kills: null, Deaths: deaths, Assists: null,
            HeroDamage: dmg, HeroHealing: null, TowerDamage: null);

    // ---------- Bẫy 1: phân vị của một giá trị bằng 0 ----------

    /// <summary>
    /// Đo thật ở ván 8937662260: hero_healing_per_min raw 0 nhưng pct 0,93 — vì Centaur không có
    /// kỹ năng hồi máu nên gần như ai chơi cũng hồi 0, và cả khối bằng nhau đó bị xếp chung một
    /// bậc. Tin nó thì trang viết "hồi máu tốt hơn 93% người chơi" cho một ván hồi đúng 0 máu.
    /// </summary>
    [Fact]
    public void Raw_bang_khong_ma_phan_vi_cao_thi_la_rac_phai_bo()
    {
        var b = new Dictionary<string, OpenDotaBenchmark>
        {
            ["hero_healing_per_min"] = new() { Raw = 0, Pct = 0.9326 },
        };

        TrackedMatchDetailIngester.Pct(b, "hero_healing_per_min").Should().BeNull();
    }

    /// <summary>
    /// Nhưng KHÔNG được chặn mọi raw = 0. Với số chết, raw = 0 nghĩa là không chết lần nào —
    /// thành tích thật và hiếm, nên phân vị nằm thấp và hoàn toàn có nghĩa. Chặn tất cả sẽ vứt
    /// đi đúng những ván chơi hay nhất.
    /// </summary>
    [Fact]
    public void Raw_bang_khong_ma_phan_vi_thap_thi_van_giu()
    {
        var b = new Dictionary<string, OpenDotaBenchmark>
        {
            ["deaths_per_min"] = new() { Raw = 0, Pct = 0.02 },
        };

        TrackedMatchDetailIngester.Pct(b, "deaths_per_min").Should().Be(2);
    }

    [Fact]
    public void Gia_tri_that_thi_chep_nguyen_va_doi_sang_thang_tram()
    {
        var b = new Dictionary<string, OpenDotaBenchmark>
        {
            ["gold_per_min"] = new() { Raw = 696, Pct = 0.9634146341463414 },
        };

        TrackedMatchDetailIngester.Pct(b, "gold_per_min").Should().Be(96);
    }

    [Fact]
    public void Thieu_o_hoac_phan_vi_ngoai_khoang_thi_tra_null_chu_khong_tra_khong()
    {
        TrackedMatchDetailIngester.Pct(null, "gold_per_min").Should().BeNull();
        TrackedMatchDetailIngester.Pct([], "gold_per_min").Should().BeNull();

        var lung = new Dictionary<string, OpenDotaBenchmark>
        {
            ["gold_per_min"] = new() { Raw = 500, Pct = 1.4 },
            ["xp_per_min"] = new() { Raw = 500, Pct = null },
        };

        TrackedMatchDetailIngester.Pct(lung, "gold_per_min").Should().BeNull();
        TrackedMatchDetailIngester.Pct(lung, "xp_per_min").Should().BeNull();
    }

    // ---------- Bẫy 2: số chết ngược chiều ----------

    /// <summary>
    /// Phân vị 90 của deaths_per_min nghĩa là chết nhiều hơn 90% người chơi — tệ nhất bảng, chứ
    /// không phải giỏi nhất. Không đảo chiều thì người chết nhiều nhất giải có thanh dài nhất.
    /// </summary>
    [Fact]
    public void Chet_nhieu_phai_ra_diem_thap()
    {
        var games = Enumerable.Range(0, 20).Select(i => G(i, deaths: 90)).ToList();

        var deaths = SkillComponents.Read(games).Single(c => c.Key == "deaths");

        deaths.Median.Should().Be(10, "phân vị 90 số chết = chết nhiều = điểm 10");
        deaths.Inverted.Should().BeTrue();
    }

    [Fact]
    public void Chi_so_thuan_chieu_thi_giu_nguyen()
    {
        var games = Enumerable.Range(0, 20).Select(i => G(i, gpm: 90)).ToList();

        SkillComponents.Read(games).Single(c => c.Key == "farm-gpm").Median.Should().Be(90);
    }

    // ---------- Trung vị, không phải trung bình ----------

    /// <summary>
    /// Phân vị là thang THỨ HẠNG: khoảng cách 50→60 không bằng 89→99 xét theo giá trị thật, nên
    /// cộng rồi chia là phép tính không có nghĩa. Trung vị chỉ cần thứ tự nên luôn đúng, và nó
    /// cũng không bị một ván thảm hoạ kéo lệch.
    /// </summary>
    [Fact]
    public void Mot_van_tham_hoa_khong_duoc_keo_lech_ca_cot()
    {
        var games = Enumerable.Range(0, 19).Select(i => G(i, gpm: 80)).ToList();
        games.Add(G(19, gpm: 0));

        var median = SkillComponents.Read(games).Single(c => c.Key == "farm-gpm").Median;

        median.Should().Be(80);
        median.Should().NotBe(76, "76 là trung bình — chính là con số phải tránh");
    }

    [Fact]
    public void Phan_vi_noi_suy_giua_hai_phan_tu_ke_nhau()
    {
        SkillComponents.Percentile([10, 20, 30, 40, 50], 50).Should().Be(30);
        SkillComponents.Percentile([10, 20, 30, 40], 50).Should().Be(25);
        SkillComponents.Percentile([0, 100], 25).Should().Be(25);
        SkillComponents.Percentile([], 50).Should().Be(0);
        SkillComponents.Percentile([42], 75).Should().Be(42);
    }

    // ---------- Ngưỡng dữ liệu ----------

    [Fact]
    public void Thieu_van_thi_khong_ra_cot_nao()
    {
        var games = Enumerable.Range(0, SkillComponents.MinGames - 1)
            .Select(i => G(i, gpm: 80)).ToList();

        SkillComponents.Read(games).Should().BeEmpty();
    }

    [Fact]
    public void Van_thieu_phan_vi_thi_khong_tinh_vao_so_van_cua_cot_do()
    {
        var games = Enumerable.Range(0, 20).Select(i => G(i, gpm: i < 16 ? 70 : null)).ToList();

        SkillComponents.Read(games).Single(c => c.Key == "farm-gpm").Games.Should().Be(16);
    }

    /// <summary>
    /// "Gần đây" chỉ so được khi phần CÒN LẠI cũng đủ dày. Không có điều kiện này thì người mới
    /// chơi 20 ván sẽ thấy cửa sổ gần đây trùng gần hết với tổng thể rồi tưởng mình vừa tiến bộ.
    /// </summary>
    [Fact]
    public void Chua_du_lich_su_thi_khong_dua_ra_con_so_gan_day()
    {
        var few = Enumerable.Range(0, 30).Select(i => G(i, gpm: 60)).ToList();
        SkillComponents.Read(few).Single(c => c.Key == "farm-gpm").Recent.Should().BeNull();

        // Cũ = 30, mới = 60 (ngày lớn hơn). Đủ dày thì phải thấy đúng phần mới.
        var many = Enumerable.Range(0, 100).Select(i => G(i, gpm: i < 50 ? 30 : 60)).ToList();
        SkillComponents.Read(many).Single(c => c.Key == "farm-gpm").Recent.Should().Be(60);
    }

    /// <summary>
    /// Cửa sổ gần đây của mọi cột phải nói về CÙNG một khoảng thời gian, nên phải sắp một lần
    /// cho cả bộ. Bài kiểm đưa vào theo thứ tự đảo để bắt lỗi quên sắp.
    /// </summary>
    [Fact]
    public void Van_dua_vao_lon_xon_van_ra_dung_cua_so_gan_day()
    {
        var games = Enumerable.Range(0, 100)
            .Select(i => G(i, gpm: i < 50 ? 30 : 60))
            .OrderBy(g => g.Gpm).ThenBy(g => g.StartTime)
            .ToList();

        SkillComponents.Read(games).Single(c => c.Key == "farm-gpm").Recent.Should().Be(60);
    }

    // ---------- Mạnh / yếu ----------

    [Fact]
    public void Chi_goi_la_manh_yeu_khi_cach_xa_muc_trung_binh()
    {
        var games = Enumerable.Range(0, 20)
            .Select(i => G(i, gpm: 88, deaths: 90, dmg: 53))
            .ToList();

        var (strong, weak) = SkillComponents.Extremes(SkillComponents.Read(games));

        strong.Should().ContainSingle().Which.Key.Should().Be("farm-gpm");
        weak.Should().ContainSingle().Which.Key.Should().Be("deaths");
        strong.Concat(weak).Should().NotContain(c => c.Key == "dmg",
            "phân vị 53 là ngang trung bình, không phải điểm mạnh");
    }

    // ---------- Tách theo kết quả trận ----------

    /// <summary>
    /// Tách theo kết quả trận là BỐI CẢNH đáng hiện, không phải căn cứ để phán.
    ///
    /// Bản trước có hàm OnlyWhenLosing với lập luận: cột giữ mạng gộp lại 42 nhưng ván thắng 60
    /// và ván thua 26, nên "vấn đề là ván hỏng hỏng nặng chứ không phải kỹ năng". Rồi đo trên
    /// người thứ hai thì khoảng cách thắng/thua từng cột của họ gần như TRÙNG KHÍT với người thứ
    /// nhất. Tức mọi chỉ số đều sụp khi thua, với mọi người, ở cùng một mức — đó là tính chất
    /// của thước đo, không phân biệt được ai với ai.
    /// </summary>
    [Fact]
    public void Tach_duoc_van_thang_va_van_thua()
    {
        var games = new List<RatedGame>();
        for (var i = 0; i < 40; i++) games.Add(G(i, won: true, deaths: 40));    // giữ mạng 60
        for (var i = 40; i < 80; i++) games.Add(G(i, won: false, deaths: 74));  // giữ mạng 26

        var c = SkillComponents.Read(games).Single(x => x.Key == "deaths");

        c.Median.Should().Be(43, "gộp lại nằm giữa hai nhóm");
        c.Won.Should().Be(60);
        c.Lost.Should().Be(26);
        SkillComponents.ResultGap(c).Should().Be(34);
    }

    [Fact]
    public void Thieu_van_thang_hoac_thua_thi_de_trong_chu_khong_doan()
    {
        var games = Enumerable.Range(0, 40).Select(i => G(i, won: true, deaths: 40)).ToList();

        var c = SkillComponents.Read(games).Single(x => x.Key == "deaths");

        c.Won.Should().Be(60);
        c.Lost.Should().BeNull("không có ván thua nào thì không có gì để nói");
        SkillComponents.ResultGap(c).Should().BeNull();
    }

    /// <summary>
    /// Khoá lại điều đã học: không được dựng lại phép "gỡ nhãn mặt yếu vì ván thắng vẫn ổn".
    /// Đo thật, khoảng cách thắng/thua của hai người trên từng cột là 34/37, 11/13, 8/5, 23/25,
    /// 12/14, 26/28, 40/39, 34/33, 36/49, 14/14 — một nhãn bật theo khoảng cách đó sẽ bật cho
    /// cả hai như nhau và chẳng nói lên điều gì.
    /// </summary>
    [Fact]
    public void Khong_duoc_dung_lai_phep_go_nhan_mat_yeu_vi_van_thang_van_on()
    {
        typeof(SkillComponents).GetMethod("OnlyWhenLosing").Should().BeNull(
            "mọi chỉ số đều sụp khi thua với mọi người ở cùng một mức, nên đó là tính chất của "
            + "thước đo chứ không phải phát hiện về một người");
    }

    [Fact]
    public void Khong_gop_thanh_mot_diem_tong()
    {
        typeof(SkillComponents).GetMethod("OverallRating").Should().BeNull(
            "trọng số giữa farm và sát thương là do người viết mã chọn chứ không có trong dữ "
            + "liệu; một con số duy nhất che mất đúng thứ hữu ích là mạnh mặt nào yếu mặt nào");
    }
}
