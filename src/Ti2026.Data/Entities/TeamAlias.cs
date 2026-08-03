namespace Ti2026.Data.Entities;

/// <summary>
/// Ánh xạ tên đội giữa các nguồn. Giải quyết đúng cái ghi trong _note của teams.json:
/// OpenDota gọi PARIVISION thì dltv gọi TEAM VISION; HULIGANI là L1GA TEAM.
/// Không có bảng này thì mỗi nguồn tạo ra một đội trùng.
/// </summary>
public class TeamAlias
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }
    public required string Alias { get; set; }

    /// <summary>"opendota" | "dltv" | "editorial"</summary>
    public required string Source { get; set; }
}
