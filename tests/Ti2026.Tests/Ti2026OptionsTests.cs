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
        options.OpenDota.RequestsPerSecond.Should().Be(1);
    }
}
