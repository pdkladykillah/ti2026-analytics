using FluentAssertions;
using Ti2026.Web;

namespace Ti2026.Tests;

public class Ti2026OptionsTests
{
    [Theory]
    [InlineData("/ti2026", "/ti2026")]
    [InlineData("ti2026", "/ti2026")]        // thiếu dấu / đầu — lỗi dễ mắc trong docker-compose
    [InlineData("/ti2026/", "/ti2026")]      // dấu / cuối làm mọi route lệch một bậc
    [InlineData("  /ti2026  ", "/ti2026")]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData(null, "")]
    public void NormalizedPathBase_chuan_hoa_moi_dang_cau_hinh_sai(string? input, string expected)
    {
        new Ti2026Options { PathBase = input! }.NormalizedPathBase().Should().Be(expected);
    }

    [Fact]
    public void Mac_dinh_khong_co_path_base()
    {
        var options = new Ti2026Options();

        options.NormalizedPathBase().Should().BeEmpty();
        options.IngestIntervalHours.Should().Be(6);
        options.SanityGate.MinTeams.Should().Be(16);
        options.SanityGate.MinPlayers.Should().Be(60);
        // 0.8 chứ không phải 1: hạn mức miễn phí của OpenDota là 60/phút, và 1 req/s bằng
        // đúng 60/phút nên thỉnh thoảng vẫn ăn 429. Đã gặp thật khi nạp bù.
        options.OpenDota.RequestsPerSecond.Should().Be(0.8);
        options.OpenDota.RequestsPerSecond.Should().BeLessThan(1,
            "ngồi ngay trên vạch giới hạn thì sớm muộn cũng vượt");
    }

    /// <summary>
    /// TẮT KHOÁ THÌ PHẢI TẮT CẢ BA THỨ CÙNG LÚC.
    ///
    /// Đây là chỗ nguy hiểm nhất của cấu hình: header xác thực, nhịp gọi và trần ván mỗi vòng
    /// đều rẽ nhánh theo cùng một câu hỏi "có khoá không". Nếu chúng tách ra tự quyết thì sẽ có
    /// lúc gửi request KHÔNG kèm khoá nhưng vẫn chạy 8 req/giây — gấp mười lần bậc miễn phí cho
    /// phép. Hậu quả không phải một vòng ingest hỏng mà là VPS bị chặn IP, mất luôn nguồn dữ liệu.
    /// </summary>
    [Fact]
    public void Tat_khoa_thi_nhip_va_tran_deu_ve_muc_mien_phi()
    {
        var off = new OpenDotaOptions { ApiKey = "co-khoa-that", UseApiKey = false };

        off.HasKey.Should().BeFalse("khoá có nhưng đang tắt");
        off.EffectiveRequestsPerSecond.Should().Be(off.RequestsPerSecond);
        off.EffectiveRequestsPerSecond.Should().BeLessThan(1,
            "chạy nhanh mà không kèm khoá là con đường thẳng tới bị chặn IP");

        var on = new OpenDotaOptions { ApiKey = "co-khoa-that", UseApiKey = true };

        on.HasKey.Should().BeTrue();
        on.EffectiveRequestsPerSecond.Should().Be(on.RequestsPerSecondWithKey);
    }

    /// <summary>
    /// Bật cờ mà KHÔNG có khoá thì vẫn phải là chế độ miễn phí — cờ không tự sinh ra khoá.
    /// </summary>
    [Fact]
    public void Bat_co_ma_khong_co_khoa_thi_van_la_che_do_mien_phi()
    {
        var o = new OpenDotaOptions { ApiKey = null, UseApiKey = true };

        o.HasKey.Should().BeFalse();
        o.EffectiveRequestsPerSecond.Should().Be(o.RequestsPerSecond);
    }

    /// <summary>
    /// Mặc định vẫn là DÙNG khoá nếu có: đổi mặc định sẽ lặng lẽ làm chậm mọi nơi triển khai
    /// đang chạy. Chỗ nào muốn tắt thì khai rõ trong docker-compose.
    /// </summary>
    [Fact]
    public void Mac_dinh_van_la_dung_khoa_neu_co()
    {
        new OpenDotaOptions().UseApiKey.Should().BeTrue();
    }
}
