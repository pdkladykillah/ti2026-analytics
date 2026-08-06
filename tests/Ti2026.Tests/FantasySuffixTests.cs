using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Xác suất thật của suffix. Bản luật gốc chỉ dán nhãn định tính; ta có dữ liệu nên đo được,
/// và đo thì đổi hẳn kết luận: một suffix +24% mà chỉ ăn 6% số ván thì kém hơn một suffix +6%
/// ăn gần một nửa số ván.
/// </summary>
public class FantasySuffixTests
{
    private static readonly Dictionary<string, double> Bonus = new()
    {
        ["underdog"] = 6, ["decisive"] = 24, ["lucky"] = 21, ["luckyTheoPhut"] = 21,
        ["clutch"] = 16, ["tormented"] = 23, ["patient"] = 23, ["flayedTwins"] = 9,
    };

    private static SuffixGame G(int dur, int? fb = 300, bool won = true,
        bool? torm = false, bool last = false) => new(dur, fb, won, torm, last);

    private static SuffixOdds Get(List<SuffixOdds> rows, string key) => rows.First(r => r.Key == key);

    /// <summary>
    /// Bài kiểm chính: hệ số to không có nghĩa là đáng chọn. Lợi kỳ vọng mới là thứ so được.
    /// </summary>
    [Fact]
    public void He_so_to_ma_hiem_thi_kem_he_so_nho_ma_hay_xay_ra()
    {
        // 20 ván: 1 ván kết thúc sớm (decisive), 10 ván thua (underdog)
        var games = Enumerable.Range(0, 20)
            .Select(i => G(i == 0 ? 1200 : 2400, won: i >= 10))
            .ToList();

        var rows = FantasySuffix.Compute(games, Bonus);

        var decisive = Get(rows, "decisive");
        var underdog = Get(rows, "underdog");

        decisive.Probability.Should().Be(0.05);
        decisive.ExpectedBonusPercent.Should().Be(1.2, "24% × 5%");

        underdog.Probability.Should().Be(0.5);
        underdog.ExpectedBonusPercent.Should().Be(3.0, "6% × 50%");

        underdog.ExpectedBonusPercent.Should().BeGreaterThan(decisive.ExpectedBonusPercent!.Value,
            "suffix 6% hay xảy ra ĂN ĐỨT suffix 24% hiếm khi xảy ra");
    }

    /// <summary>
    /// "Tận cùng bằng 8" có hai cách đọc vì đồng hồ hiện phút:giây. Đo cả hai và khai ra là
    /// chưa chắc, thay vì chọn bừa rồi trình bày như đã biết.
    /// </summary>
    [Fact]
    public void Lucky_co_hai_cach_doc_va_do_ca_hai()
    {
        // 28:08 — giây tận cùng bằng 8, phút thì không
        FantasySuffix.LuckyBySecond(28 * 60 + 8).Should().BeTrue();
        FantasySuffix.LuckyByMinute(28 * 60 + 8).Should().BeTrue("phút 28 cũng tận cùng bằng 8");

        // 38:12 — phút tận cùng bằng 8, giây thì không
        FantasySuffix.LuckyBySecond(38 * 60 + 12).Should().BeFalse();
        FantasySuffix.LuckyByMinute(38 * 60 + 12).Should().BeTrue();

        // 31:24 — không cách nào
        FantasySuffix.LuckyBySecond(31 * 60 + 24).Should().BeFalse();
        FantasySuffix.LuckyByMinute(31 * 60 + 24).Should().BeFalse();
    }

    /// <summary>
    /// first_blood_time ÂM là first blood trước tiếng còi — dữ liệu thật, không phải hỏng.
    /// Đây chính là điều kiện của "the Flayed Twins Acolyte".
    /// </summary>
    [Fact]
    public void First_blood_truoc_tieng_coi_la_du_lieu_that()
    {
        var rows = FantasySuffix.Compute(
            [G(2400, fb: -5), G(2400, fb: 300), G(2400, fb: 700), G(2400, fb: 800)], Bonus);

        Get(rows, "flayedTwins").Hits.Should().Be(1);
        Get(rows, "patient").Hits.Should().Be(2, "hai ván không có first blood trước phút 10");
    }

    /// <summary>Ván chưa parse thì không có mốc first blood, phải bị LOẠI khỏi mẫu chứ không tính là 0.</summary>
    [Fact]
    public void Van_khong_co_moc_first_blood_bi_loai_khoi_mau()
    {
        var rows = FantasySuffix.Compute(
            [G(2400, fb: 700), G(2400, fb: null), G(2400, fb: null)], Bonus);

        var patient = Get(rows, "patient");
        patient.Sample.Should().Be(1, "chỉ một ván có mốc first blood");
        patient.Probability.Should().Be(1.0, "chứ không phải 1/3");
    }

    /// <summary>
    /// Suffix không đo được vẫn phải có mặt trong bảng, đánh dấu rõ. Bỏ nó đi thì người đọc
    /// tưởng nó không tồn tại, và sẽ không bao giờ hỏi vì sao thiếu.
    /// </summary>
    [Fact]
    public void Suffix_khong_do_duoc_van_hien_ra_va_danh_dau_ro()
    {
        var rows = FantasySuffix.Compute([G(2400)], Bonus);
        var cruel = Get(rows, "cruel");

        cruel.Measurable.Should().BeFalse();
        cruel.Probability.Should().BeNull("không đo được thì để trống, không điền 0");
    }

    /// <summary>
    /// Cột chưa nạp thì phải trả về TRỐNG, không phải 0,0%.
    ///
    /// Đây đúng là chuyện đã xảy ra thật: "the Tormented" hiện 0,0% trên 3369 ván, trông y hệt
    /// một kết quả đã đo, trong khi thật ra cột đó chưa được nạp lần nào.
    /// </summary>
    [Fact]
    public void Cot_chua_nap_thi_de_trong_chu_khong_bao_0_phan_tram()
    {
        var rows = FantasySuffix.Compute(
            [G(2400, torm: null), G(2400, torm: null)], Bonus);

        var t = Get(rows, "tormented");
        t.Sample.Should().Be(0);
        t.Probability.Should().BeNull("chưa nạp thì không được kết luận là không bao giờ xảy ra");
    }

    /// <summary>
    /// OpenDota trả 0 cho ván không có số liệu first blood, mà "first blood ở giây thứ 0" không
    /// phải sự kiện có thật. Bên gọi quy 0 về null; test này ghim hệ quả: những ván đó bị LOẠI
    /// khỏi mẫu chứ không đếm thành "first blood rất sớm".
    /// </summary>
    [Fact]
    public void Van_khong_ro_first_blood_khong_duoc_dem_thanh_rat_som()
    {
        var rows = FantasySuffix.Compute(
            [G(2400, fb: 700), G(2400, fb: null), G(2400, fb: null), G(2400, fb: null)], Bonus);

        var patient = Get(rows, "patient");
        patient.Sample.Should().Be(1);
        patient.Probability.Should().Be(1.0, "không phải 0,25 — ba ván kia là chưa rõ");
    }

    [Fact]
    public void Chua_co_van_nao_thi_khong_bia_ra_xac_suat()
    {
        var rows = FantasySuffix.Compute([], Bonus);
        Get(rows, "underdog").Probability.Should().BeNull();
    }

    [Fact]
    public void Chet_vi_Tormentor_dem_dung()
    {
        var rows = FantasySuffix.Compute(
            [G(2400, torm: true), G(2400), G(2400), G(2400)], Bonus);

        Get(rows, "tormented").Probability.Should().Be(0.25);
        Get(rows, "tormented").ExpectedBonusPercent.Should().Be(5.75, "23% × 25%");
    }
}
