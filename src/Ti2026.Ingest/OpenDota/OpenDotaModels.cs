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

/// <summary>
/// GET /leagues/{id}/matches — MỌI ván của một giải, kể cả ván giữa hai đội ngoài 16 đội.
///
/// Khác <see cref="OpenDotaTeamMatch"/> ở chỗ nó khai thẳng cả hai phe (radiant_team_id và
/// dire_team_id) thay vì "đội được hỏi" và "đối thủ", nên dùng được cho ván không có đội nào
/// của ta. Đây là nguồn duy nhất lấy được những ván đó — nạp theo từng đội thì vĩnh viễn không
/// thấy chúng.
/// </summary>
public class OpenDotaLeagueMatch
{
    [JsonPropertyName("match_id")] public long MatchId { get; set; }
    [JsonPropertyName("radiant_win")] public bool RadiantWin { get; set; }
    [JsonPropertyName("radiant_score")] public int RadiantScore { get; set; }
    [JsonPropertyName("dire_score")] public int DireScore { get; set; }
    [JsonPropertyName("duration")] public int Duration { get; set; }

    /// <summary>Unix epoch giây, UTC.</summary>
    [JsonPropertyName("start_time")] public long StartTime { get; set; }

    [JsonPropertyName("leagueid")] public long? LeagueId { get; set; }
    [JsonPropertyName("series_id")] public long? SeriesId { get; set; }

    [JsonPropertyName("radiant_team_id")] public int? RadiantTeamId { get; set; }
    [JsonPropertyName("dire_team_id")] public int? DireTeamId { get; set; }
}

/// <summary>
/// GET /teams/{id}/players — roster theo OpenDota.
///
/// Dùng để TRẢ LỜI câu "team_id lạ này là đội nào của ta", bằng cách so account_id với đội hình
/// đang hiệu lực. So theo tên là vô dụng ở đây: tên chính là thứ đã đổi.
/// </summary>
public class OpenDotaTeamPlayer
{
    [JsonPropertyName("account_id")] public long? AccountId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("games_played")] public int GamesPlayed { get; set; }
    [JsonPropertyName("is_current_team_member")] public bool? IsCurrentTeamMember { get; set; }
}

/// <summary>GET /leagues — dùng để lọc trận nào được tính vào Elo.</summary>
public class OpenDotaLeague
{
    [JsonPropertyName("leagueid")] public long LeagueId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>"premium" | "professional" | "amateur" | "excluded" | null</summary>
    [JsonPropertyName("tier")] public string? Tier { get; set; }
}

/// <summary>GET /heroes</summary>
public class OpenDotaHero
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("localized_name")] public string? LocalizedName { get; set; }
}

/// <summary>
/// players/{id}/heroes — thành tích của MỘT người chơi trên từng hero.
///
/// hero_id là SỐ. Bản đầu tôi khai là chuỗi theo phỏng đoán và endpoint trả 500 ngay lần gọi
/// thật đầu tiên; shape đã đối chiếu với response thật:
/// {"hero_id":11,"last_played":1782853806,"games":167,"win":115,...}
/// </summary>
public class OpenDotaPlayerHero
{
    [JsonPropertyName("hero_id")] public int HeroId { get; set; }
    [JsonPropertyName("games")] public int Games { get; set; }
    [JsonPropertyName("win")] public int Win { get; set; }
    [JsonPropertyName("last_played")] public long? LastPlayed { get; set; }
}

/// <summary>players/{id} — chỉ lấy phần hiển thị, không lấy gì thêm.</summary>
public class OpenDotaPlayerProfile
{
    [JsonPropertyName("profile")] public OpenDotaProfile? Profile { get; set; }
    [JsonPropertyName("rank_tier")] public int? RankTier { get; set; }
}

public class OpenDotaProfile
{
    [JsonPropertyName("account_id")] public long? AccountId { get; set; }
    [JsonPropertyName("personaname")] public string? PersonaName { get; set; }
    [JsonPropertyName("avatarfull")] public string? AvatarFull { get; set; }
}

/// <summary>
/// Một mục trong constants/items. Endpoint đó trả về ĐỐI TƯỢNG khoá theo tên item, không phải
/// mảng — nên phải đọc thành Dictionary chứ không phải List.
/// </summary>
public class OpenDotaItem
{
    [JsonPropertyName("dname")] public string? DisplayName { get; set; }
    [JsonPropertyName("cost")] public int? Cost { get; set; }
    [JsonPropertyName("qual")] public string? Quality { get; set; }
}
