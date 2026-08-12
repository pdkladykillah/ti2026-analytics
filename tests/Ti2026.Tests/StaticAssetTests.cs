using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Ti2026.Tests;

/// <summary>
/// Bảo vệ trang khỏi một sự cố đã xảy ra thật: HTML mới gặp JavaScript CŨ trong bộ nhớ đệm.
///
/// index.html, app.js, app.css tham chiếu nhau bằng đường dẫn KHÔNG có phiên bản. Nếu server
/// không bảo trình duyệt kiểm lại thì trình duyệt tự suy ra thời hạn và giữ bản cũ hàng giờ.
///
/// Hậu quả không phải "thấy bản cũ" mà là TRANG HỎNG HẲN: một lần triển khai tách trang hồ sơ
/// thành sáu khung mục con: HTML mới có sáu thẻ mới, JS mới ghi vào chúng. Người dùng nhận HTML
/// mới nhưng JS cũ từ đệm, và JS cũ ghi vào #profile-body — thẻ vừa bị bỏ. Nó ném lỗi ngay dòng
/// đầu, cả trang trắng trơn, và trông y hệt như mất dữ liệu.
/// </summary>
public class StaticAssetTests(Ti2026TestFactory factory) : IClassFixture<Ti2026TestFactory>
{
    [Theory]
    [InlineData("/index.html")]
    [InlineData("/app.js")]
    [InlineData("/app.css")]
    public async Task Vo_trang_phai_bat_trinh_duyet_kiem_lai_moi_lan(string path)
    {
        var res = await factory.CreateClient().GetAsync(path);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var cache = res.Headers.CacheControl;
        cache.Should().NotBeNull($"{path} không có Cache-Control thì trình duyệt tự suy ra thời "
            + "hạn và giữ bản cũ — HTML mới gặp JS cũ là trang hỏng hẳn");

        cache!.NoCache.Should().BeTrue($"{path} tham chiếu không có phiên bản nên bắt buộc phải "
            + "hỏi lại server trước khi dùng");

        // ETag phải còn, nếu không thì mỗi lần hỏi lại là tải nguyên tệp thay vì nhận 304 rỗng.
        res.Headers.ETag.Should().NotBeNull("thiếu ETag thì no-cache thành tải lại toàn bộ");
    }

    /// <summary>
    /// Mọi id mà app.js ghi vào phải TỒN TẠI trong index.html.
    ///
    /// Đây là bài kiểm chặn chính cái lỗi đã gây trắng trang: app.js gọi
    /// $('#profile-body').innerHTML sau khi index.html đã bỏ thẻ đó đi. JavaScript không báo lỗi
    /// lúc dịch — nó chỉ ném đúng lúc người dùng mở tab, và ném trước khi kịp vẽ được gì.
    /// </summary>
    [Fact]
    public async Task Moi_id_ma_app_js_ghi_vao_deu_phai_co_trong_index_html()
    {
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/index.html");
        var js = await client.GetStringAsync("/app.js");

        // Một id hợp lệ nếu nó có trong index.html HOẶC do chính app.js dựng ra rồi mới truy vấn
        // lại — lối thứ hai rất phổ biến ở đây (insight-body, calc-player, pf-radar…).
        //
        // Suy ra từ mã thay vì giữ một danh sách miễn trừ viết tay: danh sách viết tay sẽ mục
        // dần, và mỗi lần ai đó thêm một khung dựng bằng JS thì test đỏ oan rồi bị nới ra cho
        // qua — đúng con đường biến một bài kiểm thành vô dụng.
        var declared = Regex.Matches(html + js, "id=\"([\\w-]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        // Chỉ xét $('#...') với id viết thẳng — phần dựng id động thì test này không với tới,
        // và nói ra giới hạn đó ở đây còn hơn để người sau tưởng nó phủ hết.
        var used = Regex.Matches(js, @"\$\('#([\w-]+)'\)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        used.Should().NotBeEmpty("nếu không khớp được gì thì bài kiểm này đang tự lừa mình");

        var missing = used.Where(id => !declared.Contains(id)).ToList();

        missing.Should().BeEmpty(
            "app.js ghi vào những id này nhưng index.html không có: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Sáu khung mục con của trang hồ sơ phải còn đủ, và phải mang data-sec để setupSubTabs()
    /// tự sinh nút — cơ chế đó dò theo thuộc tính chứ không theo tên, nên mất thuộc tính là mất
    /// mục con mà không có gì báo.
    /// </summary>
    [Fact]
    public async Task Trang_ho_so_phai_giu_du_sau_muc_con()
    {
        var html = await factory.CreateClient().GetStringAsync("/index.html");

        var profile = html[html.IndexOf("id=\"view-profile\"", StringComparison.Ordinal)..];
        profile = profile[..profile.IndexOf("</section>", StringComparison.Ordinal)];

        foreach (var sec in new[]
                 {
                     "Tổng quan", "Vai trò", "Giai đoạn lane", "Hero", "Nhịp chơi",
                     "Đồng đội", "Theo thời gian", "Nhận định",
                 })
            profile.Should().Contain($"data-sec=\"{sec}\"", $"mục con '{sec}' đã biến mất");

        foreach (var id in new[]
                 {
                     "head", "overview", "roles", "lane", "heroes", "habits",
                     "mates", "months", "insights",
                 })
            profile.Should().Contain($"id=\"profile-{id}\"", $"khung profile-{id} đã biến mất");
    }

    /// <summary>
    /// Mọi khu vực dài phải chia mục con — KHÔNG được có khu vực nào chỉ có đúng 1 data-sec.
    ///
    /// setupSubTabs() chỉ sinh nút khi thấy từ 2 mục trở lên, nên một khu vực có đúng 1 mục sẽ
    /// im lặng không có mục con nào — trông y hệt như cố ý. Tab Đội hình từng là khu vực DUY
    /// NHẤT không có data-sec, và hậu quả là bảng chỉ số cùng 16 thẻ đội xếp chồng thành một
    /// trang dài hơn 10.000px, trong khi 8 tab kia đều đã chia mục.
    /// </summary>
    [Fact]
    public async Task Khong_khu_vuc_nao_co_dung_mot_muc_con()
    {
        var html = await factory.CreateClient().GetStringAsync("/index.html");

        foreach (var view in Regex.Matches(html, "id=\"view-(\\w+)\"").Select(m => m.Groups[1].Value))
        {
            var at = html.IndexOf($"id=\"view-{view}\"", StringComparison.Ordinal);
            var body = html[at..];
            var end = body.IndexOf("</section>", StringComparison.Ordinal);
            if (end > 0) body = body[..end];

            var count = Regex.Matches(body, "data-sec=\"").Count;

            count.Should().NotBe(1,
                $"khu vực '{view}' có đúng 1 data-sec nên setupSubTabs() bỏ qua — hoặc thêm mục "
                + "thứ hai, hoặc bỏ hẳn data-sec đi cho rõ ý định");
        }

        // Và tab Đội hình thì phải có mục con thật, đây là chỗ đã sửa.
        var teams = html[html.IndexOf("id=\"view-teams\"", StringComparison.Ordinal)..];
        teams = teams[..teams.IndexOf("</section>", StringComparison.Ordinal)];
        Regex.Matches(teams, "data-sec=\"").Count.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Tab học lối chơi phải có đủ nút điều hướng, bốn mục con và bốn khung nội dung.
    ///
    /// Nút nav là thứ dễ quên nhất: thiếu section thì JS ném lỗi ngay và thấy liền, còn thiếu nút
    /// thì cả tab tồn tại đầy đủ trong HTML mà không ai vào được — không lỗi, không dấu hiệu.
    /// </summary>
    [Fact]
    public async Task Tab_hoc_loi_choi_phai_co_nut_nav_va_du_bon_muc()
    {
        var html = await factory.CreateClient().GetStringAsync("/index.html");

        html.Should().Contain("data-view=\"idols\"",
            "thiếu nút nav thì cả tab có trong HTML mà không có đường vào");

        var view = html[html.IndexOf("id=\"view-idols\"", StringComparison.Ordinal)..];
        view = view[..view.IndexOf("</section>", StringComparison.Ordinal)];

        foreach (var sec in new[] { "Bốn người", "Chữ ký lối chơi", "So với tôi", "Hero pool" })
            view.Should().Contain($"data-sec=\"{sec}\"", $"mục con '{sec}' đã biến mất");

        foreach (var id in new[] { "cards", "signature", "me", "heroes", "person" })
            view.Should().Contain($"id=\"idol-{id}\"", $"khung idol-{id} đã biến mất");
    }
}
