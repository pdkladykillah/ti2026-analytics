using System.Text.Json;
using FluentAssertions;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Tests;

/// <summary>
/// Fixture ở đây là DỮ LIỆU THẬT chụp từ OpenDota (match 8926048199), khác với fixture tự
/// dựng của các test khác. Quy sai phe first blood sẽ làm lệch chỉ số của CẢ HAI đội trong
/// ván đó, nên phần này phải test trên shape thật.
/// </summary>
public class MatchDetailAnalyzerTests
{
    private static OpenDotaMatchDetail RealMatch() =>
        JsonSerializer.Deserialize<OpenDotaMatchDetail>(
            OpenDotaClientTests.Fixture("opendota-match-detail.json"))!;

    [Fact]
    public void Doc_duoc_match_detail_that()
    {
        var m = RealMatch();

        m.MatchId.Should().Be(8926048199);
        m.FirstBloodTime.Should().Be(26);
        m.SeriesId.Should().Be(1126807);
        m.Duration.Should().Be(2893);
        m.Players.Should().HaveCount(10);
        m.Objectives.Should().NotBeNullOrEmpty();

        var p = m.Players[0];
        p.AccountId.Should().Be(10366616);
        p.Assists.Should().Be(21);
        p.GoldPerMin.Should().Be(420);
        p.KillsLog.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Xac_dinh_dung_phe_lay_first_blood_tu_objectives()
    {
        var facts = MatchDetailAnalyzer.Analyze(RealMatch());

        // objective that: {"type":"CHAT_MESSAGE_FIRSTBLOOD","player_slot":1}
        // slot 1 < 128 -> Radiant
        facts.RadiantHadFirstBlood.Should().BeTrue();
        facts.FirstBloodTimeSeconds.Should().Be(26);
    }

    [Fact]
    public void Xac_dinh_duoc_phe_dat_10_mang_truoc()
    {
        var facts = MatchDetailAnalyzer.Analyze(RealMatch());

        // Radiant thắng 33-24 nên gần như chắc chắn đạt 10 mạng trước; điều quan trọng là
        // giá trị PHẢI được xác định, không phải null.
        facts.RadiantReachedTenFirst.Should().NotBeNull();
    }

    [Fact]
    public void Player_slot_duoi_128_la_Radiant()
    {
        MatchDetailAnalyzer.SlotIsRadiant(0).Should().BeTrue();
        MatchDetailAnalyzer.SlotIsRadiant(4).Should().BeTrue();
        MatchDetailAnalyzer.SlotIsRadiant(128).Should().BeFalse();
        MatchDetailAnalyzer.SlotIsRadiant(132).Should().BeFalse();
    }

    /// <summary>
    /// Ván chưa được OpenDota parse thì không có kills_log và objectives. Phải trả null,
    /// không được suy ra "Dire lấy first blood" từ việc thiếu dữ liệu.
    /// </summary>
    [Fact]
    public void Van_chua_parse_thi_tra_null_chu_khong_suy_dien()
    {
        var m = new OpenDotaMatchDetail
        {
            MatchId = 1,
            FirstBloodTime = 42,          // biết LÚC NÀO
            Objectives = null,            // nhưng không biết AI
            Players =
            [
                new() { PlayerSlot = 0, Kills = 5, KillsLog = null },
                new() { PlayerSlot = 128, Kills = 3, KillsLog = null },
            ],
        };

        var facts = MatchDetailAnalyzer.Analyze(m);

        facts.RadiantHadFirstBlood.Should().BeNull("biết thời điểm không có nghĩa là biết phe");
        facts.RadiantReachedTenFirst.Should().BeNull();
        facts.FirstBloodTimeSeconds.Should().Be(42);
    }

    [Fact]
    public void Khong_phe_nao_dat_10_mang_thi_tra_null()
    {
        var m = new OpenDotaMatchDetail
        {
            Players =
            [
                new() { PlayerSlot = 0, KillsLog = Kills(60, 120, 180) },
                new() { PlayerSlot = 128, KillsLog = Kills(90, 150) },
            ],
        };

        MatchDetailAnalyzer.Analyze(m).RadiantReachedTenFirst.Should().BeNull(
            "trận kết thúc trước khi phe nào đạt 10 mạng — không có câu trả lời, khác với \"Dire trước\"");
    }

    [Fact]
    public void Dire_dat_10_mang_truoc_thi_tra_false()
    {
        var m = new OpenDotaMatchDetail
        {
            Players =
            [
                new() { PlayerSlot = 0, KillsLog = Kills(500, 600, 700) },
                new() { PlayerSlot = 128, KillsLog = Kills(10, 20, 30, 40, 50, 60, 70, 80, 90, 100) },
            ],
        };

        MatchDetailAnalyzer.Analyze(m).RadiantReachedTenFirst.Should().BeFalse();
    }

    [Fact]
    public void Dem_dung_so_mang_trong_10_phut_dau()
    {
        var p = new OpenDotaMatchPlayer { KillsLog = Kills(60, 300, 599, 600, 601, 1200) };

        MatchDetailAnalyzer.KillsWithinMinutes(p, 10).Should().Be(4, "mốc 600 giây tính vào");
        MatchDetailAnalyzer.KillsWithinMinutes(new OpenDotaMatchPlayer(), 10).Should().BeNull(
            "không có kills_log thì không biết, không phải bằng 0");
    }

    private static List<OpenDotaKillLog> Kills(params int[] times) =>
        times.Select(t => new OpenDotaKillLog { Time = t }).ToList();
}
