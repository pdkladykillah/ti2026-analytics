using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// URL ảnh phải dựng LÚC ĐỌC, không lưu xuống DB.
///
/// Bài học thật: bản đầu lưu URL với host cdn.cloudflare.steamstatic.com. Host đó trả 200 khi
/// thử từ VPS nhưng ảnh KHÔNG hiện trên trình duyệt người dùng, trong khi logo đội — nằm trên
/// steamcdn-a.akamaihd.net vì OpenDota trả về thế — thì hiện bình thường. Vì URL đã bị lưu, sửa
/// host đòi phải nạp lại cả bảng mới có tác dụng.
/// </summary>
public class DotaImagesTests
{
    [Fact]
    public void Cat_tien_to_npc_dota_hero_khoi_ten()
    {
        DotaImages.Hero("npc_dota_hero_lone_druid")
            .Should().Be($"{DotaImages.Host}/dota_react/heroes/lone_druid.png");

        DotaImages.Hero("npc_dota_hero_antimage")
            .Should().EndWith("/heroes/antimage.png");
    }

    [Fact]
    public void Ten_khong_co_tien_to_thi_dung_nguyen()
    {
        DotaImages.Hero("puck").Should().EndWith("/heroes/puck.png");
    }

    /// <summary>
    /// Thiếu tên thì trả null để UI hiện chữ cái thay thế. Trả một URL đoán bừa sẽ cho ra biểu
    /// tượng ảnh vỡ, và ba mươi biểu tượng ảnh vỡ xếp thành cột trông như trang bị hỏng.
    /// </summary>
    [Fact]
    public void Thieu_ten_thi_tra_null_chu_khong_doan_URL()
    {
        DotaImages.Hero(null).Should().BeNull();
        DotaImages.Hero("").Should().BeNull();
        DotaImages.Hero("   ").Should().BeNull();

        DotaImages.Item(null).Should().BeNull();
        DotaImages.Item("").Should().BeNull();
    }

    [Fact]
    public void Item_dung_dung_duong_dan()
    {
        DotaImages.Item("black_king_bar")
            .Should().Be($"{DotaImages.Host}/dota_react/items/black_king_bar.png");
    }

    /// <summary>
    /// Chốt host. Đây là host DUY NHẤT đã chứng minh chạy được ở phía người dùng — logo đội nằm
    /// trên đó và hiện bình thường. Đổi host là thay đổi có hậu quả thấy được ngay trên trang,
    /// nên phải là quyết định có ý thức chứ không phải sửa nhầm.
    /// </summary>
    [Fact]
    public void Dung_host_da_chung_minh_chay_duoc_o_phia_nguoi_dung()
    {
        DotaImages.Host.Should().Be("https://steamcdn-a.akamaihd.net/apps/dota2/images");
        DotaImages.Host.Should().NotContain("cloudflare",
            "host cloudflare trả 200 từ VPS nhưng ảnh không hiện trên trình duyệt người dùng");
    }
}
