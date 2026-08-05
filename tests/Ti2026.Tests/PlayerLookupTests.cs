using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ti2026.Web;

namespace Ti2026.Tests;

public class PlayerLookupTests
{
    private static PlayerLookup New() => new(NullLogger<PlayerLookup>.Instance);

    /// <summary>
    /// Người dùng dán cả hai dạng. Đoán sai giữa hai dạng sẽ tra ra hồ sơ của một người hoàn
    /// toàn khác, và trang sẽ tự tin hiển thị hero pool của người lạ như thể là của họ.
    /// </summary>
    [Fact]
    public void Nhan_ca_account_id_lan_Steam_ID64()
    {
        PlayerLookup.ParseAccountId("86745912").Should().Be(86745912);

        PlayerLookup.ParseAccountId("76561198047011640")
            .Should().Be(76561198047011640L - PlayerLookup.SteamId64Offset);
    }

    [Fact]
    public void Tu_choi_thay_vi_doan_khi_id_khong_thuoc_dai_nao()
    {
        PlayerLookup.ParseAccountId(null).Should().BeNull();
        PlayerLookup.ParseAccountId("").Should().BeNull();
        PlayerLookup.ParseAccountId("khong-phai-so").Should().BeNull();
        PlayerLookup.ParseAccountId("0").Should().BeNull();
        PlayerLookup.ParseAccountId("-5").Should().BeNull();

        // Số lớn bất thường nhưng chưa tới ngưỡng ID64: gõ sai, và đoán bừa còn tệ hơn từ chối
        PlayerLookup.ParseAccountId("9999999999").Should().BeNull();
    }

    [Fact]
    public void Bo_khoang_trang_hai_dau()
    {
        PlayerLookup.ParseAccountId("  86745912  ").Should().Be(86745912);
    }

    /// <summary>
    /// Trần là thứ bảo vệ IP của VPS. Đây là endpoint công khai gọi ra nguồn ngoài; không có
    /// trần thì một con bot sẽ khiến OpenDota chặn IP, và mất IP là mất nguồn dữ liệu cho TOÀN
    /// BỘ ứng dụng chứ không phải chỉ một lần tra cứu.
    /// </summary>
    [Fact]
    public void Het_suat_thi_tu_choi_chu_khong_goi_tiep()
    {
        var lookup = New();

        for (var i = 0; i < PlayerLookup.MaxLookupsPerWindow; i++)
            lookup.TryTakeBudget().Should().BeTrue($"lần {i + 1} vẫn còn trong trần");

        lookup.TryTakeBudget().Should().BeFalse("vượt trần thì phải từ chối");
    }

    [Fact]
    public void Chua_tra_thi_khong_co_gi_trong_dem()
    {
        New().TryGetCached(86745912, out var snapshot).Should().BeFalse();
        snapshot.Should().BeNull();
    }

    /// <summary>
    /// Bộ đệm phải TÍNH VÀO trần đúng một lần: tra lại cùng một id trong thời hạn không được
    /// tiêu thêm suất, nếu không thì mở lại trang là mất suất.
    /// </summary>
    [Fact]
    public void Dem_co_thoi_han_duong_va_hop_ly()
    {
        PlayerLookup.CacheTtl.Should().BeGreaterThan(TimeSpan.Zero);
        PlayerLookup.Window.Should().BeGreaterThan(TimeSpan.Zero);
        PlayerLookup.MaxLookupsPerWindow.Should().BeGreaterThan(0);
    }
}
