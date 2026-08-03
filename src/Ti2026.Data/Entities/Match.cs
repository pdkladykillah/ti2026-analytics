namespace Ti2026.Data.Entities;

public class Match
{
    /// <summary>OpenDota match_id — không tự tăng.</summary>
    public long Id { get; set; }

    /// <summary>Để gom các game thành series Bo3/Bo5. API H2H tính từ đây.</summary>
    public long? SeriesId { get; set; }

    public DateTimeOffset StartTime { get; set; }
    public int DurationSeconds { get; set; }
    public long? LeagueId { get; set; }
    public string? LeagueName { get; set; }

    /// <summary>Nullable: OpenDota đôi khi thiếu ánh xạ đội cho một số trận.</summary>
    public int? RadiantTeamId { get; set; }
    public int? DireTeamId { get; set; }

    public bool RadiantWin { get; set; }
    public int RadiantScore { get; set; }
    public int DireScore { get; set; }
    public int? FirstBloodTimeSeconds { get; set; }
    public bool? RadiantHadFirstBlood { get; set; }
    public bool? RadiantReachedTenFirst { get; set; }
    public string? PatchVersion { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
}
