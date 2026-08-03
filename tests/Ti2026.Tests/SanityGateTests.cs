using FluentAssertions;
using Ti2026.Ingest;

namespace Ti2026.Tests;

public class SanityGateTests
{
    private static readonly SanityThresholds T = new(MinTeams: 16, MinPlayers: 60);

    [Fact]
    public void Du_nguong_thi_qua()
    {
        SanityGate.CheckTeams(16, T).Passed.Should().BeTrue();
        SanityGate.CheckPlayers(80, T).Passed.Should().BeTrue();
    }

    [Fact]
    public void Rong_thi_truot_va_neu_ro_ly_do()
    {
        var check = SanityGate.CheckTeams(0, T);

        check.Passed.Should().BeFalse();
        check.Reason.Should().Contain("0").And.Contain("16");
    }

    [Fact]
    public void Thieu_mot_chut_van_truot()
    {
        SanityGate.CheckTeams(15, T).Passed.Should().BeFalse();
        SanityGate.CheckPlayers(59, T).Passed.Should().BeFalse();
    }
}
