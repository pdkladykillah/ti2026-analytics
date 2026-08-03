using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.Seeding;

public sealed record SeedResult(bool Skipped, int TeamsWritten, int PlayersWritten);

/// <summary>
/// Nạp dữ liệu biên tập từ data/*.json vào DB, chạy lúc startup.
///
/// Mục đích: deploy lần đầu là trang đã có dữ liệu ngay giây đầu, không phải chờ vòng
/// ingest đầu tiên. Cũng là đường lùi khi nguồn ngoài chết.
///
/// Chỉ nạp lại khi hash file đổi (bảng SeedState) — nên restart app liên tục không
/// gây ghi DB vô ích.
/// </summary>
public class EditorialSeeder(Ti2026DbContext db, string editorialDirectory)
{
    public async Task<SeedResult> SeedAsync(CancellationToken ct)
    {
        var teamsPath = Path.Combine(editorialDirectory, "teams.json");
        if (!File.Exists(teamsPath))
            return new SeedResult(Skipped: true, 0, 0);

        var teamsJson = await File.ReadAllTextAsync(teamsPath, ct);
        var hash = Sha256(teamsJson);

        var state = await db.SeedStates.FirstOrDefaultAsync(s => s.Key == "teams.json", ct);
        if (state?.Hash == hash)
            return new SeedResult(Skipped: true, 0, 0);

        var file = JsonSerializer.Deserialize<TeamsFile>(teamsJson, SeedJson.Options)
                   ?? throw new InvalidOperationException("teams.json không parse được");

        var teamsWritten = await SeedTeamsAsync(file, ct);
        var playersWritten = await SeedRostersAsync(ct);

        if (state is null)
        {
            db.SeedStates.Add(new SeedState
            {
                Key = "teams.json", Hash = hash, AppliedAt = DateTime.UtcNow
            });
        }
        else
        {
            state.Hash = hash;
            state.AppliedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        return new SeedResult(Skipped: false, teamsWritten, playersWritten);
    }

    private async Task<int> SeedTeamsAsync(TeamsFile file, CancellationToken ct)
    {
        var written = 0;

        foreach (var dto in file.Teams)
        {
            var team = await db.Teams.Include(t => t.Aliases)
                .FirstOrDefaultAsync(t => t.Slug == dto.Slug, ct);

            if (team is null)
            {
                team = new Team { Slug = dto.Slug, Name = dto.Name };
                db.Teams.Add(team);
            }

            team.Name = dto.Name;
            team.ShortName = dto.Short;
            team.Region = dto.Region;
            team.Qualification = dto.Qualification;
            team.LogoUrl = dto.Logo;

            EnsureAlias(team, dto.Name);
            if (!string.IsNullOrWhiteSpace(dto.Short)) EnsureAlias(team, dto.Short!);

            written++;
        }

        await db.SaveChangesAsync(ct);
        return written;
    }

    private async Task<int> SeedRostersAsync(CancellationToken ct)
    {
        var path = Path.Combine(editorialDirectory, "rosters.json");
        if (!File.Exists(path)) return 0;

        var file = JsonSerializer.Deserialize<RostersFile>(
            await File.ReadAllTextAsync(path, ct), SeedJson.Options);
        if (file is null) return 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var written = 0;

        foreach (var (slug, members) in file.Rosters)
        {
            var team = await db.Teams.FirstOrDefaultAsync(t => t.Slug == slug, ct);
            if (team is null) continue;

            foreach (var m in members)
            {
                if (string.IsNullOrWhiteSpace(m.Nick)) continue;

                var player = await db.Players.FirstOrDefaultAsync(p => p.Nick == m.Nick, ct);
                if (player is null)
                {
                    player = new Player
                    {
                        Nick = m.Nick, RealName = m.Real, PhotoUrl = m.Photo
                    };
                    db.Players.Add(player);
                    await db.SaveChangesAsync(ct);   // cần Id ngay để tạo RosterEntry
                }
                else
                {
                    player.RealName = m.Real ?? player.RealName;
                    player.PhotoUrl = m.Photo ?? player.PhotoUrl;
                }

                var role = string.IsNullOrWhiteSpace(m.Role) ? "CORE" : m.Role!;

                var existing = await db.RosterEntries.FirstOrDefaultAsync(
                    r => r.TeamId == team.Id && r.PlayerId == player.Id && r.ValidTo == null, ct);

                if (existing is null)
                {
                    db.RosterEntries.Add(new RosterEntry
                    {
                        TeamId = team.Id, PlayerId = player.Id, Role = role, ValidFrom = today
                    });
                }
                else if (existing.Role != role)
                {
                    // Đổi vai trò: đóng bản ghi cũ, mở bản ghi mới — giữ lịch sử thay vì ghi đè
                    existing.ValidTo = today;
                    db.RosterEntries.Add(new RosterEntry
                    {
                        TeamId = team.Id, PlayerId = player.Id, Role = role, ValidFrom = today
                    });
                }

                written++;
            }
        }

        await db.SaveChangesAsync(ct);
        return written;
    }

    /// <summary>
    /// Alias nguồn "editorial". Ingest từ OpenDota/dltv sẽ thêm alias nguồn của nó,
    /// và unique index (Alias, Source) cho phép cùng một tên tồn tại ở nhiều nguồn.
    /// </summary>
    private static void EnsureAlias(Team team, string alias)
    {
        const string source = "editorial";
        if (team.Aliases.Any(a =>
                a.Source == source && a.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)))
            return;

        team.Aliases.Add(new TeamAlias { Alias = alias, Source = source });
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
