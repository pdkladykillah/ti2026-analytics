namespace Ti2026.Data.Entities;

public class Match
{
    /// <summary>OpenDota match_id — không tự tăng.</summary>
    public long Id { get; set; }

    /// <summary>Để gom các game thành series Bo3/Bo5. API H2H tính từ đây.</summary>
    public long? SeriesId { get; set; }

    /// <summary>
    /// UTC. Dùng DateTime chứ không DateTimeOffset vì SQLite không hỗ trợ DateTimeOffset
    /// trong ORDER BY — nó lưu thành TEXT kèm offset nên so sánh chuỗi sẽ sai giữa các múi
    /// giờ. Toàn bộ pipeline sắp xếp và lọc theo cột này nên nó phải so sánh được.
    /// DbContext có converter buộc mọi DateTime đọc/ghi đều là UTC.
    /// </summary>
    public DateTime StartTime { get; set; }

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

    /// <summary>UTC.</summary>
    public DateTime IngestedAt { get; set; }
}
