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

    /// <summary>Số ván có hero này CÙNG PHE, do đồng đội cầm.</summary>
    [JsonPropertyName("with_games")] public int WithGames { get; set; }
    [JsonPropertyName("with_win")] public int WithWin { get; set; }

    /// <summary>
    /// Số ván ĐỐI ĐẦU hero này. Đây là mẫu số của câu "hero nào khắc chế tôi" — thiếu nó thì
    /// hero phổ biến sẽ luôn đứng đầu bảng chỉ vì gặp nhiều, chứ không phải vì khắc chế.
    /// </summary>
    [JsonPropertyName("against_games")] public int AgainstGames { get; set; }
    [JsonPropertyName("against_win")] public int AgainstWin { get; set; }
}

/// <summary>players/{id}/wl — tổng thắng thua toàn bộ lịch sử.</summary>
public class OpenDotaWinLoss
{
    [JsonPropertyName("win")] public int Win { get; set; }
    [JsonPropertyName("lose")] public int Lose { get; set; }
}

/// <summary>
/// Một ván trong players/{id}/matches.
///
/// Phần lớn các trường CHỈ có khi hỏi kèm project= — không hỏi thì endpoint chỉ trả match_id,
/// hero_id, thời gian, kết quả và K/D/A. Riêng lane_role thì có hỏi cũng gần như không có: nó
/// đến từ replay đã phân tích, và trên tài khoản thật chỉ 6% số ván có.
/// </summary>
public class OpenDotaPlayerMatchRow
{
    [JsonPropertyName("match_id")] public long MatchId { get; set; }
    [JsonPropertyName("hero_id")] public int HeroId { get; set; }
    [JsonPropertyName("start_time")] public long StartTime { get; set; }
    [JsonPropertyName("duration")] public int Duration { get; set; }

    /// <summary>&lt; 128 là phe Radiant. Kết hợp với radiant_win mới biết người này thắng hay thua.</summary>
    [JsonPropertyName("player_slot")] public int PlayerSlot { get; set; }
    [JsonPropertyName("radiant_win")] public bool RadiantWin { get; set; }

    [JsonPropertyName("kills")] public int? Kills { get; set; }
    [JsonPropertyName("deaths")] public int? Deaths { get; set; }
    [JsonPropertyName("assists")] public int? Assists { get; set; }
    [JsonPropertyName("gold_per_min")] public int? GoldPerMin { get; set; }
    [JsonPropertyName("xp_per_min")] public int? XpPerMin { get; set; }
    [JsonPropertyName("last_hits")] public int? LastHits { get; set; }
    [JsonPropertyName("denies")] public int? Denies { get; set; }
    [JsonPropertyName("hero_damage")] public int? HeroDamage { get; set; }
    [JsonPropertyName("tower_damage")] public int? TowerDamage { get; set; }
    [JsonPropertyName("hero_healing")] public int? HeroHealing { get; set; }
    [JsonPropertyName("lane_role")] public int? LaneRole { get; set; }
    [JsonPropertyName("lobby_type")] public int? LobbyType { get; set; }
    [JsonPropertyName("game_mode")] public int? GameMode { get; set; }
    [JsonPropertyName("party_size")] public int? PartySize { get; set; }
    [JsonPropertyName("average_rank")] public int? AverageRank { get; set; }
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
    /// <summary>id SỐ của vật phẩm. Đây là thứ nối được item_0..item_5 trong bảng điểm với
    /// danh mục vốn khoá theo TÊN — thiếu nó thì sáu ô đồ không tra ra được gì.</summary>
    [JsonPropertyName("id")] public int? Id { get; set; }

    [JsonPropertyName("dname")] public string? DisplayName { get; set; }
    [JsonPropertyName("cost")] public int? Cost { get; set; }
    [JsonPropertyName("qual")] public string? Quality { get; set; }
}
