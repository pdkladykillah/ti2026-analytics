using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Ti2026.Tests;

/// <summary>
/// Bảng màu phải đọc được — trên CẢ hai chủ đề, và cả với người mù màu đỏ-lục.
///
/// VÌ SAO LÀ TEST CHỨ KHÔNG PHẢI MỘT LẦN KIỂM BẰNG MẮT. Màu bị sửa lẻ tẻ: ai đó thấy một sắc
/// vàng đẹp hơn, đổi một dòng, và không có cách nào biết rằng dòng đó vừa đẩy chữ trên nút xuống
/// dưới ngưỡng đọc được. Sai kiểu này không đổ vỡ, không có thông báo, và chỉ người dùng có thị
/// lực kém mới gặp — tức là nhóm ít khi báo lại nhất.
///
/// Đọc THẲNG từ app.css nên không thể lệch với thứ đang chạy thật.
/// </summary>
public class ThemeContrastTests(Ti2026TestFactory factory) : IClassFixture<Ti2026TestFactory>
{
    /// <summary>Ngưỡng WCAG AA cho chữ cỡ thường.</summary>
    private const double AA = 4.5;

    // Lấy qua chính máy chủ test thay vì dò đường dẫn tệp: cùng cách StaticAssetTests đã dùng,
    // và nó kiểm luôn rằng app.css THỰC SỰ được phục vụ — một bảng màu đúng trong repo mà không
    // tới được trình duyệt thì cũng vô nghĩa.
    private async Task<string> CssAsync() => await factory.CreateClient().GetStringAsync("/app.css");

    private static Dictionary<string, string> Tokens(string block) =>
        Regex.Matches(block, @"(--[\w-]+)\s*:\s*(#[0-9a-fA-F]{3,8})\s*;")
            .GroupBy(m => m.Groups[1].Value)
            .ToDictionary(g => g.Key, g => g.Last().Groups[2].Value);

    private async Task<(Dictionary<string, string> Light, Dictionary<string, string> Dark)> LoadAsync()
    {
        var css = await CssAsync();
        var darkAt = css.IndexOf("[data-theme=\"dark\"]", StringComparison.Ordinal);
        darkAt.Should().BeGreaterThan(0, "không tìm thấy khối chủ đề tối");

        var light = Tokens(css[css.IndexOf(":root {", StringComparison.Ordinal)..darkAt]);

        // Chủ đề tối chỉ khai LẠI phần đổi, phần còn lại thừa kế từ :root — nên phải chồng lên
        // chứ không đọc riêng, nếu không mọi token không đổi sẽ thành "thiếu".
        var dark = new Dictionary<string, string>(light);
        foreach (var (k, v) in Tokens(css[darkAt..])) dark[k] = v;

        return (light, dark);
    }

    private static double[] Rgb(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        return [.. Enumerable.Range(0, 3)
            .Select(i => (double)int.Parse(h.Substring(i * 2, 2), NumberStyles.HexNumber))];
    }

    private static double Linear(double c)
    {
        c /= 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(string hex)
    {
        var c = Rgb(hex).Select(Linear).ToArray();
        return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
    }

    private static double Contrast(string a, string b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    public static TheoryData<string, string, string> Pairs()
    {
        var data = new TheoryData<string, string, string>();

        foreach (var theme in new[] { "light", "dark" })
        foreach (var (fg, bg) in new[]
                 {
                     ("--ink", "--bg"), ("--ink", "--surface"),
                     ("--ink-2", "--surface"), ("--ink-2", "--surface-2"),
                     ("--muted", "--surface"),

                     // Cặp quan trọng nhất bảng này: vàng Aegis KHÔNG đủ tương phản để làm chữ
                     // trên nền sáng, nên nó chỉ được làm NỀN với chữ sẫm đặt lên.
                     ("--on-accent", "--accent"),
                     ("--accent-ink", "--accent-soft"), ("--accent-ink", "--surface"),

                     ("--pos", "--surface"), ("--neg", "--surface"), ("--warn", "--surface"),
                     ("--pos", "--pos-soft"), ("--neg", "--neg-soft"),

                     // CHỮ TRÊN NỀN --surface-2. Bộ kiểm đầu bỏ sót đúng nền này, và một
                     // vòng soi đã tìm ra bốn chỗ chữ nhỏ 11–12,5px đặt --muted lên nó:
                     // chỉ 3,84:1, dưới ngưỡng AA, và chỉ hỏng ở chủ đề SÁNG nên dễ lọt
                     // khi người sửa chỉ xem một chủ đề. Nền này có mặt ở đầu bảng, rãnh
                     // mục con, khối gập và nhiều hộp lồng — bỏ sót nó là bỏ sót nửa trang.
                     ("--ink", "--surface-2"), ("--muted", "--surface-2"),
                     ("--accent-ink", "--surface-2"),
                     ("--pastel-1-ink", "--pastel-1"), ("--pastel-2-ink", "--pastel-2"),
                     ("--pastel-3-ink", "--pastel-3"), ("--pastel-4-ink", "--pastel-4"),
                     ("--pastel-5-ink", "--pastel-5"),
                 })
            data.Add(theme, fg, bg);

        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Moi_cap_chu_nen_deu_dat_muc_AA(string theme, string fg, string bg)
    {
        var (light, dark) = await LoadAsync();
        var t = theme == "dark" ? dark : light;

        t.Should().ContainKey(fg).And.ContainKey(bg);

        Contrast(t[fg], t[bg]).Should().BeGreaterThanOrEqualTo(AA,
            $"chủ đề {theme}: {fg} trên {bg} phải đạt {AA}:1 mới đọc được");
    }

    /// <summary>
    /// Mô phỏng deuteranopia — dạng mù màu đỏ-lục phổ biến nhất, khoảng 8% nam giới.
    /// </summary>
    private static double[] Deuteranope(string hex)
    {
        var c = Rgb(hex).Select(Linear).ToArray();

        var l = 0.31399 * c[0] + 0.63951 * c[1] + 0.04649 * c[2];
        var m = 0.15537 * c[0] + 0.75789 * c[1] + 0.08670 * c[2];
        var s = 0.01775 * c[0] + 0.10944 * c[1] + 0.87262 * c[2];

        // Kênh M bị thiếu được dựng lại từ L và S — đó chính là phép chiếu làm đỏ và lục
        // sập vào nhau.
        var m2 = 0.494207 * l + 1.24827 * s;

        double[] back =
        [
            5.47221 * l - 4.6419 * m2 + 0.16963 * s,
            -1.1252 * l + 2.29317 * m2 - 0.1678 * s,
            0.02980 * l - 0.19318 * m2 + 1.16364 * s,
        ];

        return [.. back.Select(v => Math.Pow(Math.Clamp(v, 0, 1), 1 / 2.4) * 100)];
    }

    /// <summary>
    /// Thắng và thua phải còn phân biệt được sau khi mất kênh đỏ-lục.
    ///
    /// ĐÃ ĐO VÀ ĐÃ PHẢI SỬA VÌ BÀI NÀY: cặp xanh lá thuần #2a6b17 với đỏ #b3271b chỉ cách nhau
    /// ΔE 17,7 — gần như một màu. Ngả xanh sang lục-lam một chút đưa con số lên 80,7 mà mắt
    /// thường vẫn đọc ra "xanh lá". Chủ đề tối còn tệ hơn trước khi sửa: 10,7.
    ///
    /// Ngưỡng 20 là mức tối thiểu, KHÔNG phải mức đủ. Màu không bao giờ được là kênh duy nhất —
    /// mọi chỗ dùng cặp này vẫn phải kèm dấu, chữ hoặc thứ tự.
    /// </summary>
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task Thang_thua_van_tach_duoc_khi_mu_mau(string theme)
    {
        var (light, dark) = await LoadAsync();
        var t = theme == "dark" ? dark : light;

        var a = Deuteranope(t["--pos"]);
        var b = Deuteranope(t["--neg"]);
        var distance = Math.Sqrt(a.Zip(b, (x, y) => (x - y) * (x - y)).Sum());

        distance.Should().BeGreaterThanOrEqualTo(20,
            $"chủ đề {theme}: xanh thắng và đỏ thua chập vào nhau dưới mắt mù màu đỏ-lục");
    }

    /// <summary>
    /// Font phải có bộ ký tự tiếng Việt.
    ///
    /// Bản trước dùng Poppins cho toàn bộ tiêu đề của một trang tiếng Việt, mà Poppins KHÔNG có
    /// subset vietnamese — nên mọi tiêu đề có dấu đều rơi về font hệ thống, khác hẳn phần còn
    /// lại và khác nhau giữa các máy. Đây là loại lỗi không ai báo vì trang vẫn "chạy".
    /// </summary>
    [Fact]
    public async Task Khong_dung_font_thieu_dau_tieng_Viet()
    {
        var html = await factory.CreateClient().GetStringAsync("/index.html");

        foreach (var banned in new[] { "Poppins", "Fredoka", "Rubik", "Figtree" })
            html.Should().NotContain($"family={banned}",
                $"{banned} không có subset vietnamese — chữ có dấu sẽ rơi về font hệ thống");

        html.Should().Contain("family=Nunito", "font chính phải là font đã kiểm có tiếng Việt");
        html.Should().Contain("family=JetBrains+Mono", "font số phải là font đã kiểm có tiếng Việt");
    }
}
