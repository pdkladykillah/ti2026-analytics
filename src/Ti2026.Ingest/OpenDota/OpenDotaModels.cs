using System.Text.Json.Serialization;

namespace Ti2026.Ingest.OpenDota;

/// <summary>GET /teams — danh sách đội pro, sắp theo rating.</summary>
public class OpenDotaTeam
{
    [JsonPropertyName("team_id")] public int TeamId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("tag")] public string? Tag { get; set; }
    [JsonPropertyName("logo_url")] public string? LogoUrl { get; set; }
    [JsonPropertyName("rating")] public double? Rating { get; set; }
    [JsonPropertyName("wins")] public int? Wins { get; set; }
    [JsonPropertyName("losses")] public int? Losses { get; set; }
    [JsonPropertyName("last_match_time")] public long? LastMatchTime { get; set; }
}

/// <summary>
/// GET /teams/{team_id}/matches — một phần tử cho mỗi ván, dưới góc nhìn của đội được hỏi.
///
/// Cố ý KHÔNG có assists, first blood, timeline kill hay series_id: endpoint này không trả về
/// chúng. Đừng thêm property cho những field đó ở đây rồi tưởng là có dữ liệu — muốn có thì
/// phải gọi matches/{match_id}, một request mỗi ván (Giai đoạn 3).
/// </summary>
public class OpenDotaTeamMatch
{
    [JsonPropertyName("match_id")] public long MatchId { get; set; }

    /// <summary>true = đội được hỏi đứng phe Radiant trong ván này.</summary>
    [JsonPropertyName("radiant")] public bool? Radiant { get; set; }

    [JsonPropertyName("radiant_win")] public bool RadiantWin { get; set; }
    [JsonPropertyName("radiant_score")] public int RadiantScore { get; set; }
    [JsonPropertyName("dire_score")] public int DireScore { get; set; }
    [JsonPropertyName("duration")] public int Duration { get; set; }

    /// <summary>Unix epoch giây, UTC.</summary>
    [JsonPropertyName("start_time")] public long StartTime { get; set; }

    [JsonPropertyName("leagueid")] public long? LeagueId { get; set; }
    [JsonPropertyName("league_name")] public string? LeagueName { get; set; }
    [JsonPropertyName("opposing_team_id")] public int? OpposingTeamId { get; set; }
    [JsonPropertyName("opposing_team_name")] public string? OpposingTeamName { get; set; }
}

/// <summary>GET /heroes</summary>
public class OpenDotaHero
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("localized_name")] public string? LocalizedName { get; set; }
}
