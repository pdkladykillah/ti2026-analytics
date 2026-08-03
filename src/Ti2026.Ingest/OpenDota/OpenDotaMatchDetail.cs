using System.Text.Json.Serialization;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// GET /matches/{match_id} — chỉ khai những trường thực sự dùng.
/// Shape đã đối chiếu với response thật (xem Fixtures/opendota-match-detail.json).
/// </summary>
public class OpenDotaMatchDetail
{
    [JsonPropertyName("match_id")] public long MatchId { get; set; }
    [JsonPropertyName("radiant_win")] public bool RadiantWin { get; set; }
    [JsonPropertyName("duration")] public int Duration { get; set; }
    [JsonPropertyName("start_time")] public long StartTime { get; set; }

    /// <summary>Giây kể từ đầu trận. null với ván không có first blood ghi nhận.</summary>
    [JsonPropertyName("first_blood_time")] public int? FirstBloodTime { get; set; }

    [JsonPropertyName("series_id")] public long? SeriesId { get; set; }
    [JsonPropertyName("patch")] public int? Patch { get; set; }
    [JsonPropertyName("radiant_team_id")] public int? RadiantTeamId { get; set; }
    [JsonPropertyName("dire_team_id")] public int? DireTeamId { get; set; }

    [JsonPropertyName("players")] public List<OpenDotaMatchPlayer> Players { get; set; } = [];
    [JsonPropertyName("objectives")] public List<OpenDotaObjective>? Objectives { get; set; }
}

public class OpenDotaMatchPlayer
{
    [JsonPropertyName("account_id")] public long? AccountId { get; set; }
    [JsonPropertyName("hero_id")] public int HeroId { get; set; }
    [JsonPropertyName("kills")] public int Kills { get; set; }
    [JsonPropertyName("deaths")] public int Deaths { get; set; }
    [JsonPropertyName("assists")] public int Assists { get; set; }
    [JsonPropertyName("gold_per_min")] public int GoldPerMin { get; set; }
    [JsonPropertyName("xp_per_min")] public int XpPerMin { get; set; }

    /// <summary>
    /// OpenDota có cả isRadiant lẫn player_slot. player_slot đáng tin hơn vì luôn có mặt:
    /// 0..127 = Radiant, 128..255 = Dire.
    /// </summary>
    [JsonPropertyName("isRadiant")] public bool? IsRadiant { get; set; }
    [JsonPropertyName("player_slot")] public int PlayerSlot { get; set; }

    /// <summary>
    /// Mốc thời gian từng mạng hạ được. CHỈ có ở ván đã được OpenDota parse —
    /// ván chưa parse thì rỗng hoặc null, và mọi chỉ số tính từ timeline phải là null.
    /// </summary>
    [JsonPropertyName("kills_log")] public List<OpenDotaKillLog>? KillsLog { get; set; }

    public bool OnRadiant => IsRadiant ?? PlayerSlot < 128;
}

public class OpenDotaKillLog
{
    [JsonPropertyName("time")] public int Time { get; set; }
    [JsonPropertyName("key")] public string? Key { get; set; }
}

public class OpenDotaObjective
{
    [JsonPropertyName("time")] public int Time { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("player_slot")] public int? PlayerSlot { get; set; }
    [JsonPropertyName("slot")] public int? Slot { get; set; }
}
