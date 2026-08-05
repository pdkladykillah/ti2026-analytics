using System.Text.Json.Serialization;

namespace Ti2026.Ingest.OpenDota;

/// <summary>GET /players/{account_id}/matches — danh sách ván gần đây của một người.</summary>
public class OpenDotaPlayerMatch
{
    [JsonPropertyName("match_id")] public long MatchId { get; set; }
    [JsonPropertyName("hero_id")] public int HeroId { get; set; }
    [JsonPropertyName("start_time")] public long StartTime { get; set; }
    [JsonPropertyName("radiant_win")] public bool? RadiantWin { get; set; }
    [JsonPropertyName("player_slot")] public int PlayerSlot { get; set; }
    [JsonPropertyName("lobby_type")] public int? LobbyType { get; set; }
    [JsonPropertyName("game_mode")] public int? GameMode { get; set; }

    public bool OnRadiant => PlayerSlot < 128;

    /// <summary>null khi OpenDota chưa biết kết quả — khi đó ván chưa dùng được.</summary>
    public bool? Won => RadiantWin is bool rw ? rw == OnRadiant : null;
}
