using System.Text.Json;
using FluentAssertions;
using Ti2026.Ingest.Seeding;

namespace Ti2026.Tests;

/// <summary>
/// Test đọc CHÍNH 6 file JSON thật trong repo, không dùng fixture bịa ra. Nếu shape DTO
/// lệch khỏi dữ liệu thật thì phải đỏ ở đây, không phải đỏ trên trang của người dùng.
/// </summary>
public class SeedModelsTests
{
    internal static string DataDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "data")))
            dir = Directory.GetParent(dir)?.FullName;

        dir.Should().NotBeNull("phải tìm được thư mục data/ của repo khi đi ngược lên từ bin/");
        return Path.Combine(dir!, "data");
    }

    private static T Read<T>(string fileName) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(DataDir(), fileName)),
            SeedJson.Options)!;

    [Fact]
    public void Doc_duoc_teams_json_that_khong_mat_field_nao()
    {
        var doc = Read<TeamsFile>("teams.json");

        doc.Teams.Should().HaveCount(16);

        var falcons = doc.Teams.First(t => t.Slug == "team-falcons");
        falcons.Name.Should().Be("Team Falcons");
        falcons.Short.Should().Be("Falcons");
        falcons.Region.Should().Be("Đa quốc gia");
        falcons.Logo.Should().StartWith("https://dltv.org/");

        falcons.Stats.Should().NotBeNull();
        falcons.Stats!.Maps.Should().Be(159);
        falcons.Stats.Winrate.Should().Be(60);
        falcons.Stats.Kills.Should().BeApproximately(27.38, 0.001);
        falcons.Stats.KillDiff.Should().BeApproximately(1.15, 0.001);
        falcons.Stats.WinWhenF10.Should().Be(78);
        falcons.Stats.F10.Should().Be(50);
        falcons.Stats.Duration.Should().Be(44);
    }

    [Fact]
    public void Doc_duoc_rosters_json_that()
    {
        var doc = Read<RostersFile>("rosters.json");

        doc.Rosters.Should().ContainKey("team-falcons");
        var falcons = doc.Rosters["team-falcons"];
        falcons.Should().NotBeEmpty();
        falcons[0].Nick.Should().NotBeNullOrWhiteSpace();

        // Vai trò phải nằm trong đúng 6 giá trị mà index.html:295-296 map sẵn
        var validRoles = new[] { "CORE", "MID", "OFFLANE", "SUPPORT", "FULL SUPPORT", "COACH" };
        doc.Rosters.SelectMany(r => r.Value)
            .Where(m => m.Role is not null)
            .Select(m => m.Role!)
            .Distinct()
            .Should().BeSubsetOf(validRoles);
    }

    [Fact]
    public void Doc_duoc_players_json_that_voi_field_viet_tat()
    {
        var doc = Read<PlayersFile>("players.json");

        doc.Players.Should().NotBeEmpty();

        var nightfall = doc.Players.First(p => p.N == "NIGHTFALL");
        nightfall.R.Should().Be("Egor Grigorenko");
        nightfall.T.Should().Be("aurora");
        nightfall.P.Should().Be(1);
        nightfall.Id.Should().Be(124801257);
        nightfall.Cc.Should().Be("ru");
    }

    [Fact]
    public void Doc_duoc_meta_json_that()
    {
        var doc = Read<MetaFile>("meta.json");

        doc.UpdatedAt.Should().NotBeNull();
        doc.TeamsTotal.Should().Be(16);
        doc.TeamsWithData.Should().Be(16);
    }
}
