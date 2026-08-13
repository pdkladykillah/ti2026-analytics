using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Điểm nhấn ngày chỉ được nói những câu ĐÚNG BẤT KỂ CỠ MẪU.
///
/// Một ngày Swiss có 8 loạt. Ở cỡ mẫu đó, câu suy luận là nhiễu được phát biểu như kết luận —
/// đúng loại lỗi mà dự án đã dựng cả MultipleTests để tránh. Các bài dưới đây khoá lại ranh
/// giới giữa mô tả/đếm (được phép) và suy luận (không được phép).
/// </summary>
public class DayHighlightsTests
{
    private static readonly Dictionary<int, string> Heroes = new()
    {
        [1] = "Anti-Mage", [2] = "Invoker", [3] = "Muerta", [4] = "Puck", [5] = "Pudge",
    };

    private static DaySeries Series(
        int node, string a, string b, int w1, int w2, bool done = true,
        double? r1 = null, double? r2 = null) =>
        new(node, $"Match {node}", "Swiss", a, b, a.ToLowerInvariant(), b.ToLowerInvariant(),
            w1, w2, done, new DateTime(2026, 8, 13, 3, 0, 0, DateTimeKind.Utc), r1, r2);

    private static List<Highlight> Build(
        IEnumerable<DaySeries> s, IEnumerable<DayMatch>? m = null,
        IEnumerable<DayDraft>? d = null, IEnumerable<int>? banned = null) =>
        DayHighlights.Build(s.ToList(), (m ?? []).ToList(), (d ?? []).ToList(), Heroes,
            banned?.ToList());

    /// <summary>
    /// NGƯỢC KÈO PHẢI CÓ NGƯỠNG. Gắn nhãn bất ngờ cho một trận giữa hai đội winrate 50 và 52 là
    /// biến tiếng ồn thành câu chuyện — winrate cửa sổ 180 ngày không phân biệt nổi 2 điểm.
    /// </summary>
    [Fact]
    public void Chenh_winrate_duoi_nguong_thi_khong_goi_la_nguoc_keo()
    {
        var sat = Build([Series(1, "A", "B", 2, 0, r1: 40, r2: 60)]);
        sat.Should().Contain(h => h.Kind == "nguoc-keo", "chênh 20 điểm là ngược kèo thật");

        var duoi = Build([Series(2, "A", "B", 2, 0, r1: 49, r2: 52)]);
        duoi.Should().NotContain(h => h.Kind == "nguoc-keo",
            "chênh 3 điểm nằm trong sai số của chính con số winrate");

        DayHighlights.UpsetGap.Should().Be(5.0, "bài kiểm này dựng quanh đúng ngưỡng đó");
    }

    /// <summary>Thiếu winrate một bên thì không kết luận gì — không đoán, không mặc định 50.</summary>
    [Fact]
    public void Thieu_winrate_mot_ben_thi_khong_ket_luan()
    {
        Build([Series(1, "A", "B", 2, 0, r1: null, r2: 80)])
            .Should().NotContain(h => h.Kind == "nguoc-keo");
    }

    /// <summary>
    /// Dưới ngưỡng số ván thì KHÔNG xếp hạng cấm/chọn, và phải nói ra là chưa đủ — im lặng sẽ
    /// bị đọc thành "hôm nay không ai cấm gì".
    /// </summary>
    [Fact]
    public void Chua_du_van_thi_khong_xep_hang_cam_chon()
    {
        var one = Build(
            [Series(1, "A", "B", 2, 0)],
            d: [new DayDraft(100, 3, false, false)]);

        one.Should().NotContain(h => h.Kind == "cam-nhieu");
        one.Should().Contain(h => h.Kind == "chua-du-draft");

        var enough = Build(
            [Series(1, "A", "B", 2, 0)],
            d: [
                new DayDraft(100, 3, false, false),
                new DayDraft(101, 3, false, false),
            ]);

        enough.Should().Contain(h => h.Kind == "cam-nhieu");
        enough.Should().NotContain(h => h.Kind == "chua-du-draft");
    }

    /// <summary>
    /// Đếm hero bị cấm theo SỐ VÁN, không theo số lượt: một ván có nhiều lượt cấm, và đếm lượt
    /// sẽ khiến "bị cấm 7/8 ván" thành một con số lớn hơn tổng số ván.
    /// </summary>
    [Fact]
    public void Dem_hero_theo_so_van_khong_theo_so_luot()
    {
        var h = Build(
            [Series(1, "A", "B", 2, 0)],
            d: [
                new DayDraft(100, 3, false, false),
                new DayDraft(100, 3, false, false),   // cùng ván, lặp lượt
                new DayDraft(101, 3, false, false),
            ]);

        var ban = h.First(x => x.Kind == "cam-nhieu");
        ban.Text.Should().Contain("2/2 ván", "hai ván, không phải ba lượt");
    }

    /// <summary>
    /// So với ngày trước CHỈ chạy khi thật sự có ngày trước. Ngày đầu tiên của giải mà hiện
    /// "hero X mới vào nhóm bị cấm" là so với một cái không tồn tại.
    /// </summary>
    [Fact]
    public void Ngay_dau_tien_khong_so_voi_hom_truoc()
    {
        List<DayDraft> drafts = [
            new(100, 3, false, false), new(101, 3, false, false),
        ];

        Build([Series(1, "A", "B", 2, 0)], d: drafts)
            .Should().NotContain(h => h.Kind == "cam-moi" || h.Kind == "het-cam");

        Build([Series(1, "A", "B", 2, 0)], d: drafts, banned: [4])
            .Should().Contain(h => h.Kind == "cam-moi")
            .And.Contain(h => h.Kind == "het-cam");
    }

    /// <summary>
    /// Một ván duy nhất thì "dài nhất" và "ngắn nhất" là cùng một ván — in cả hai là làm người
    /// đọc tưởng có hai ván khác nhau.
    /// </summary>
    [Fact]
    public void Mot_van_thi_khong_in_ca_dai_nhat_lan_ngan_nhat()
    {
        var one = Build([Series(1, "A", "B", 2, 0)],
            m: [new DayMatch(100, 2400, "A", "B", true)]);

        one.Should().Contain(h => h.Kind == "van-dai-nhat");
        one.Should().NotContain(h => h.Kind == "van-ngan-nhat");

        var two = Build([Series(1, "A", "B", 2, 0)],
            m: [new DayMatch(100, 2400, "A", "B", true), new DayMatch(101, 1500, "A", "B", false)]);

        two.Should().Contain(h => h.Kind == "van-ngan-nhat");
    }

    /// <summary>
    /// Không đọc được ván nào thì VẪN ra được điểm nhấn từ bảng đấu — bảng đấu làm tươi mỗi 15
    /// phút còn chi tiết ván theo vòng 6 giờ, nên trạng thái này là bình thường trong nhiều giờ.
    /// </summary>
    [Fact]
    public void Chua_co_van_nao_van_ra_duoc_diem_nhan()
    {
        var h = Build([
            Series(1, "A", "B", 2, 0, r1: 40, r2: 70),
            Series(2, "C", "D", 1, 2, done: false),
        ]);

        h.Should().Contain(x => x.Kind == "tong-quan");
        h.Should().Contain(x => x.Kind == "nguoc-keo");
        h.First(x => x.Kind == "tong-quan").Text.Should().Contain("1/2 loạt");
    }

    /// <summary>
    /// SỐ VÁN ĐẾM TRÊN MỌI LOẠT, không chỉ loạt đã xong.
    ///
    /// Một loạt đang đá dở vẫn đã có ván kết thúc, và ta vẫn đọc được chi tiết của chúng. Cộng
    /// riêng loạt đã xong thì mẫu số nhỏ hơn tử số — đã thấy thật trên trang: "đọc được 9/7 ván",
    /// một câu tự bác bỏ chính nó.
    /// </summary>
    [Fact]
    public void So_van_dem_tren_moi_loat_ke_ca_loat_dang_da_do()
    {
        var h = Build(
            [
                Series(1, "A", "B", 2, 0),                 // xong, 2 ván
                Series(2, "C", "D", 1, 1, done: false),    // đang đá, đã 2 ván
            ],
            m: [
                new DayMatch(100, 2400, "A", "B", true),
                new DayMatch(101, 2400, "A", "B", true),
                new DayMatch(102, 2400, "C", "D", true),
                new DayMatch(103, 2400, "C", "D", false),
            ]);

        var text = h.First(x => x.Kind == "tong-quan").Text;
        text.Should().Contain("4 ván", "hai loạt đã cho ra bốn ván, dù một loạt chưa xong");
        text.Should().Contain("chi tiết 4 ván");
    }

    /// <summary>Trung vị, không phải trung bình — một ván 90 phút kéo lệch hẳn số trung bình.</summary>
    [Fact]
    public void Thoi_luong_lay_trung_vi()
    {
        DayHighlights.MedianDuration([
            new(1, 1800, null, null, true),
            new(2, 2400, null, null, true),
            new(3, 5400, null, null, true),
        ]).Should().Be(2400, "trung bình sẽ ra 3200 vì ván 90 phút kéo lên");

        DayHighlights.MedianDuration([]).Should().BeNull();
    }
}
