using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.Seeding;

public sealed record SeedResult(
    bool Skipped,
    int TeamsWritten,
    int PlayersWritten,
    int SnapshotsWritten = 0,
    int RostersClosed = 0);

/// <summary>
/// Nạp dữ liệu biên tập từ data/*.json vào DB, chạy lúc startup.
///
/// Mục đích: deploy lần đầu là trang đã có dữ liệu ĐẦY ĐỦ ngay giây đầu — kể cả 12 chỉ số
/// của từng đội — không phải chờ vòng ingest. Cũng là đường lùi khi nguồn ngoài chết.
///
/// Hash theo TỪNG FILE: sửa rosters.json phải nạp lại rosters, sửa teams.json phải nạp lại
/// teams. Gate chung một file là bug đã từng có ở đây (xem SeedFileKeys).
///
/// Toàn bộ chạy trong MỘT transaction: một file JSON gõ sai không được để lại trạng thái
/// nửa vời.
/// </summary>
public class EditorialSeeder(Ti2026DbContext db, string editorialDirectory)
{
    private const string TeamsFileName = "teams.json";
    private const string RostersFileName = "rosters.json";
    private const string PlayersFileName = "players.json";

    public async Task<SeedResult> SeedAsync(CancellationToken ct)
    {
        var teamsChanged = await HasChangedAsync(TeamsFileName, ct);
        var rostersChanged = await HasChangedAsync(RostersFileName, ct);

        // players.json cũng theo dõi bằng hash như hai file kia.
        //
        // Đã thử điều kiện "còn Player nào thiếu account_id thì chạy lại" và nó SAI: rosters
        // có cả HLV còn players.json chỉ có 5 tuyển thủ mỗi đội, nên luôn còn người thiếu
        // account_id và seed vĩnh viễn không bao giờ báo Skipped. Hash file trả lời đúng câu
        // hỏi "đã xử lý phiên bản này của file chưa", và nó tự chữa trên DB đang chạy: file
        // chưa từng được đánh dấu nên hash lệch, chạy một lần rồi thôi.
        var playersChanged = await HasChangedAsync(PlayersFileName, ct);

        if (!teamsChanged && !rostersChanged && !playersChanged)
            return new SeedResult(Skipped: true, 0, 0);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var teamsWritten = 0;
        var snapshotsWritten = 0;
        var playersWritten = 0;
        var rostersClosed = 0;

        if (teamsChanged)
        {
            var file = ReadJson<TeamsFile>(TeamsFileName)
                       ?? throw new InvalidOperationException($"{TeamsFileName} không parse được");

            teamsWritten = await SeedTeamsAsync(file, ct);
            snapshotsWritten = await SeedStatSnapshotsAsync(file, ct);
            await MarkAppliedAsync(TeamsFileName, ct);
        }

        // Roster phụ thuộc vào Team đã tồn tại, nên nạp lại khi teams đổi để bắt kịp đội mới
        if (rostersChanged || teamsChanged)
        {
            var file = ReadJson<RostersFile>(RostersFileName);
            if (file is not null)
            {
                (playersWritten, rostersClosed) = await SeedRostersAsync(file, ct);
                await MarkAppliedAsync(RostersFileName, ct);
            }
        }

        // Chạy sau roster vì nó bổ sung account_id cho Player mà roster vừa tạo
        await SeedPlayerAccountIdsAsync(ct);
        await MarkAppliedAsync(PlayersFileName, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new SeedResult(false, teamsWritten, playersWritten, snapshotsWritten, rostersClosed);
    }

    // ---------- Teams ----------

    private async Task<int> SeedTeamsAsync(TeamsFile file, CancellationToken ct)
    {
        var written = 0;

        // Nạp TRƯỚC vòng lặp, và gắn qua navigation property chứ không qua khoá ngoại.
        // Đội mới thêm chưa có Id (vẫn là 0) cho tới lúc SaveChanges ở cuối, nên gán TeamId
        // bằng tay sẽ nổ "FOREIGN KEY constraint failed" — im lặng ở lúc build, chỉ lộ lúc chạy.
        var claimed = await db.TeamOpenDotaIds.ToDictionaryAsync(x => x.OpenDotaTeamId, ct);

        foreach (var dto in file.Teams)
        {
            if (string.IsNullOrWhiteSpace(dto.Slug)) continue;

            var team = await db.Teams
                           .Include(t => t.Aliases).Include(t => t.OpenDotaIds)
                           .FirstOrDefaultAsync(t => t.Slug == dto.Slug, ct)
                       ?? db.Teams.Local.FirstOrDefault(t => t.Slug == dto.Slug);

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

            // Chỉ định tay ghi đè kết quả tự động. KHÔNG xoá giá trị resolver đã gán khi
            // JSON để trống — trống nghĩa là "không có ý kiến", không phải "hãy xoá đi".
            if (dto.OpenDotaTeamId is int explicitId && team.OpenDotaTeamId != explicitId)
            {
                await ReleaseIdFromOtherTeamsAsync(explicitId, team.Slug, ct);
                team.OpenDotaTeamId = explicitId;
            }

            // Id PHỤ: một roster đổi tổ chức hoặc đăng ký lại thì OpenDota sinh bản ghi mới.
            // Khai ở đây thì ingest nạp theo cả bản ghi cũ lẫn mới thay vì mất trắng một bên.
            EnsureOpenDotaIds(
                team, claimed,
                (dto.OpenDotaTeamIds ?? []).Concat(dto.OpenDotaTeamId is int e ? [e] : []));

            EnsureAlias(team, dto.Name);
            if (!string.IsNullOrWhiteSpace(dto.Short)) EnsureAlias(team, dto.Short!);

            written++;
        }

        await db.SaveChangesAsync(ct);
        return written;
    }

    /// <summary>
    /// Nạp 12 chỉ số sẵn có trong teams.json thành TeamStatSnapshot cửa sổ 180 ngày.
    ///
    /// Không có bước này thì api/teams trả stats=null cho cả 16 đội, và trang render gần như
    /// trắng: dải chỉ số nổi bật rỗng, bảng in "—" ở 12/14 cột, mọi thẻ đội ghi "Chưa có dữ
    /// liệu", tab H2H tắt vì cần ít nhất 2 đội có số. Toàn bộ số liệu vốn nằm sẵn trong file.
    ///
    /// Unique index (TeamId, CapturedOn, WindowDays) làm bước này idempotent, và SnapshotWriter
    /// của M2 sẽ ghi đè đúng hàng đó trong ngày bằng số liệu tính từ match thật.
    /// </summary>
    private async Task<int> SeedStatSnapshotsAsync(TeamsFile file, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        const int window = 180;   // teams.json là "6 tháng gần nhất"
        var written = 0;

        foreach (var dto in file.Teams)
        {
            if (dto.Stats is null || string.IsNullOrWhiteSpace(dto.Slug)) continue;

            var team = await db.Teams.FirstOrDefaultAsync(t => t.Slug == dto.Slug, ct);
            if (team is null) continue;

            var snapshot = await db.TeamStatSnapshots.FirstOrDefaultAsync(
                s => s.TeamId == team.Id && s.CapturedOn == today && s.WindowDays == window, ct);

            if (snapshot is null)
            {
                snapshot = new TeamStatSnapshot
                {
                    TeamId = team.Id, CapturedOn = today, WindowDays = window,
                    Source = "editorial",
                };
                db.TeamStatSnapshots.Add(snapshot);
            }

            var s = dto.Stats;
            snapshot.Maps = s.Maps;
            // teams.json chỉ có winrate phần trăm, không có wins/losses — suy ra từ maps
            snapshot.Wins = (int)Math.Round(s.Maps * s.Winrate / 100.0);
            snapshot.Losses = s.Maps - snapshot.Wins;
            snapshot.Winrate = s.Winrate;
            snapshot.AvgKills = s.Kills;
            snapshot.AvgDeaths = s.Deaths;
            snapshot.AvgAssists = s.Assists;
            snapshot.KillDiff = s.KillDiff;
            snapshot.TotalKills = s.TotalKills;
            snapshot.FirstBloodRate = s.FirstBlood;
            snapshot.F10Rate = s.F10;
            snapshot.WinWhenFbRate = s.WinWhenFb;
            snapshot.WinWhenF10Rate = s.WinWhenF10;
            snapshot.AvgDurationMinutes = s.Duration;

            written++;
        }

        await db.SaveChangesAsync(ct);
        return written;
    }

    // ---------- Rosters ----------

    private async Task<(int Written, int Closed)> SeedRostersAsync(
        RostersFile file, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var written = 0;
        var closed = 0;

        foreach (var (slug, rawMembers) in file.Rosters)
        {
            var team = await db.Teams.FirstOrDefaultAsync(t => t.Slug == slug, ct);
            if (team is null) continue;

            // Gộp trùng theo NickKey ngay từ đầu: một hand-edit liệt kê hai lần cùng một
            // người sẽ tạo hai hàng mở nếu không chặn ở đây.
            var members = rawMembers
                .Where(m => !string.IsNullOrWhiteSpace(m.Nick))
                .GroupBy(m => Player.MakeNickKey(m.Nick))
                .Select(g => g.First())
                .ToList();

            var incomingKeys = members.Select(m => Player.MakeNickKey(m.Nick)).ToHashSet();

            closed += await CloseDepartedAsync(team.Id, incomingKeys, today, ct);

            foreach (var m in members)
            {
                var player = await FindOrCreatePlayerAsync(m, ct);
                await UpsertRosterEntryAsync(team.Id, player, m.Role, today, ct);
                written++;
            }
        }

        await db.SaveChangesAsync(ct);
        return (written, closed);
    }

    /// <summary>
    /// Đóng bản ghi của những player không còn trong danh sách của đội.
    ///
    /// Thiếu bước này thì một player chuyển đội sẽ hiện ở CẢ HAI đội vĩnh viễn (api/rosters
    /// lọc ValidTo == null), đội cũ render 7 ô dưới tiêu đề "5 tuyển thủ + HLV", và lịch sử
    /// lưu lại điều bất khả: cùng lúc thuộc hai đội — đúng cái mà Giai đoạn 4 cần trả lời
    /// chính xác. Player nghỉ hẳn cũng sẽ nằm mãi trong đội cũ.
    /// </summary>
    private async Task<int> CloseDepartedAsync(
        int teamId, HashSet<string> incomingKeys, DateOnly today, CancellationToken ct)
    {
        var open = await db.RosterEntries
            .Include(r => r.Player)
            .Where(r => r.TeamId == teamId && r.ValidTo == null)
            .ToListAsync(ct);

        var closed = 0;
        foreach (var entry in open)
        {
            var key = entry.Player is null ? null : entry.Player.NickKey;
            if (key is null || incomingKeys.Contains(key)) continue;

            entry.ValidTo = today;   // nửa mở: không còn hiệu lực TỪ hôm nay
            closed++;
        }

        if (closed > 0) await db.SaveChangesAsync(ct);
        return closed;
    }

    private async Task<Player> FindOrCreatePlayerAsync(RosterMemberDto m, CancellationToken ct)
    {
        var key = Player.MakeNickKey(m.Nick);

        // Phải xét cả Local: thực thể vừa Add trong cùng vòng lặp chưa có trong DB, nên
        // truy vấn thuần DB sẽ không thấy và tạo bản trùng.
        var player = db.Players.Local.FirstOrDefault(p => p.NickKey == key)
                     ?? await db.Players.FirstOrDefaultAsync(p => p.NickKey == key, ct);

        if (player is null)
        {
            player = new Player
            {
                Nick = m.Nick.Trim(),
                NickKey = key,
                RealName = m.Real,
                PhotoUrl = m.Photo,
            };
            db.Players.Add(player);
            await db.SaveChangesAsync(ct);   // cần Id để tạo RosterEntry
        }
        else
        {
            player.Nick = m.Nick.Trim();
            player.RealName = m.Real ?? player.RealName;
            player.PhotoUrl = m.Photo ?? player.PhotoUrl;
        }

        return player;
    }

    private async Task UpsertRosterEntryAsync(
        int teamId, Player player, string? rawRole, DateOnly today, CancellationToken ct)
    {
        var role = string.IsNullOrWhiteSpace(rawRole) ? "CORE" : rawRole.Trim();

        var existing = db.RosterEntries.Local
                           .FirstOrDefault(r => r.TeamId == teamId
                                                && r.PlayerId == player.Id && r.ValidTo == null)
                       ?? await db.RosterEntries.FirstOrDefaultAsync(
                           r => r.TeamId == teamId && r.PlayerId == player.Id && r.ValidTo == null,
                           ct);

        if (existing is null)
        {
            db.RosterEntries.Add(new RosterEntry
            {
                TeamId = teamId, PlayerId = player.Id, Role = role, ValidFrom = today
            });
            return;
        }

        if (existing.Role == role) return;

        if (existing.ValidFrom == today)
        {
            // Sửa vai trò trong cùng ngày mới tạo: cập nhật tại chỗ. Đóng rồi mở lại sẽ sinh
            // một hàng dài bằng 0 ngày mà không truy vấn point-in-time nào trả về.
            existing.Role = role;
            return;
        }

        existing.ValidTo = today;

        // Phải flush việc đóng hàng cũ TRƯỚC khi thêm hàng mới. Unique index
        // (TeamId, PlayerId) lọc ValidTo IS NULL không cho phép hai hàng mở cùng tồn tại, và
        // nếu để cả UPDATE lẫn INSERT trong một SaveChanges thì việc nó không nổ chỉ là do
        // thứ tự lệnh nội bộ của EF đang thuận — một bảo đảm không hề được ghi ở đâu.
        await db.SaveChangesAsync(ct);

        db.RosterEntries.Add(new RosterEntry
        {
            TeamId = teamId, PlayerId = player.Id, Role = role, ValidFrom = today
        });
    }

    // ---------- Theo dõi hash từng file ----------

    private async Task<bool> HasChangedAsync(string fileName, CancellationToken ct)
    {
        var path = Path.Combine(editorialDirectory, fileName);
        if (!File.Exists(path)) return false;

        var hash = Sha256(await File.ReadAllTextAsync(path, ct));
        var state = await db.SeedStates.FirstOrDefaultAsync(s => s.Key == fileName, ct);
        return state?.Hash != hash;
    }

    private async Task MarkAppliedAsync(string fileName, CancellationToken ct)
    {
        var path = Path.Combine(editorialDirectory, fileName);
        if (!File.Exists(path)) return;

        var hash = Sha256(await File.ReadAllTextAsync(path, ct));
        var state = await db.SeedStates.FirstOrDefaultAsync(s => s.Key == fileName, ct);

        if (state is null)
        {
            db.SeedStates.Add(new SeedState
            {
                Key = fileName, Hash = hash, AppliedAt = DateTime.UtcNow
            });
        }
        else
        {
            state.Hash = hash;
            state.AppliedAt = DateTime.UtcNow;
        }
    }

    private T? ReadJson<T>(string fileName)
    {
        var path = Path.Combine(editorialDirectory, fileName);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), SeedJson.Options)
            : default;
    }

    /// <summary>
    /// Nạp account_id OpenDota cho từng Player, lấy từ players.json.
    ///
    /// ĐÂY LÀ MẮT XÍCH NỐI dữ liệu đội với dữ liệu cá nhân: MatchPlayer chỉ gắn được về Player
    /// của ta khi Player có OpenDotaAccountId. Thiếu bước này thì mọi phân tích cá nhân —
    /// kills, hỗ trợ, nhịp 10 phút đầu — đều trống rỗng dù dữ liệu đã nằm sẵn trong DB.
    ///
    /// rosters.json (nguồn tạo Player) không có account_id; players.json thì có. Khớp hai
    /// file bằng NickKey đã chuẩn hoá.
    /// </summary>
    private async Task SeedPlayerAccountIdsAsync(CancellationToken ct)
    {
        var file = ReadJson<PlayersFile>(PlayersFileName);
        if (file is null || file.Players.Count == 0) return;

        var players = await db.Players.ToListAsync(ct);
        var byKey = players.ToDictionary(p => p.NickKey);

        // Một account_id chỉ được thuộc về một Player (có unique index), nên loại trùng trước
        var taken = players.Where(p => p.OpenDotaAccountId.HasValue)
                           .Select(p => p.OpenDotaAccountId!.Value)
                           .ToHashSet();

        var linked = 0;

        foreach (var dto in file.Players)
        {
            if (dto.Id is not long accountId || string.IsNullOrWhiteSpace(dto.N)) continue;

            if (!byKey.TryGetValue(Player.MakeNickKey(dto.N), out var player)) continue;
            if (player.OpenDotaAccountId == accountId) continue;
            if (taken.Contains(accountId)) continue;

            player.OpenDotaAccountId = accountId;
            player.CountryName ??= dto.C;
            player.CountryCode ??= dto.Cc;
            taken.Add(accountId);
            linked++;
        }

        if (linked > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Gỡ OpenDotaTeamId khỏi đội khác đang giữ nó.
    ///
    /// Cần thiết vì unique index không cho hai đội cùng trỏ về một id: nếu resolver đã gán
    /// nhầm id này cho đội khác thì việc chỉ định tay sẽ nổ DbUpdateException ngay lúc
    /// startup, và app không lên được. Chỉ định tay phải luôn thắng.
    /// </summary>
    private async Task ReleaseIdFromOtherTeamsAsync(int openDotaTeamId, string keepSlug, CancellationToken ct)
    {
        var others = await db.Teams
            .Where(t => t.OpenDotaTeamId == openDotaTeamId && t.Slug != keepSlug)
            .ToListAsync(ct);

        foreach (var other in others) other.OpenDotaTeamId = null;
        if (others.Count > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Ghi nhận mọi team_id OpenDota khai trong teams.json cho đội này.
    ///
    /// Giống <see cref="ReleaseIdFromOtherTeamsAsync"/>: chỉ định tay phải luôn thắng, nên id
    /// nào đang bị đội khác giữ thì gỡ ra trước — không thì unique index nổ ngay lúc startup và
    /// app không lên được.
    /// </summary>
    private void EnsureOpenDotaIds(
        Team team, Dictionary<int, TeamOpenDotaId> claimed, IEnumerable<int> openDotaIds)
    {
        foreach (var id in openDotaIds.Distinct())
        {
            if (claimed.TryGetValue(id, out var existing))
            {
                // Đã thuộc đúng đội này rồi thì thôi. Thuộc đội khác thì gỡ ra — chỉ định tay
                // phải luôn thắng, giống ReleaseIdFromOtherTeamsAsync.
                if (ReferenceEquals(existing.Team, team) || existing.TeamId == team.Id) continue;
                db.TeamOpenDotaIds.Remove(existing);
            }

            var row = new TeamOpenDotaId
            {
                OpenDotaTeamId = id,
                Source = "editorial",
                AddedAt = DateTime.UtcNow,
            };

            team.OpenDotaIds.Add(row);      // EF tự điền TeamId lúc SaveChanges
            claimed[id] = row;
        }
    }

    /// <summary>
    /// Alias nguồn "editorial". Ingest từ OpenDota/dltv sẽ thêm alias nguồn của nó, và unique
    /// index (Alias, Source) cho phép cùng một tên tồn tại ở nhiều nguồn.
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
