using System.Text.Json.Serialization;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Một mục trong GetLeagueInfoList của Valve.
///
/// <see cref="Tier"/> = 5 là The International, và CHỈ The International: kiểm trên toàn bộ
/// 9.797 giải Valve biết thì tier 5 khớp đúng 9 giải, đúng bằng số kỳ TI từ 2018 tới 2026,
/// không lẫn một giải nào khác. Nhờ vậy không phải viết cứng id hay tên năm — cách tìm này
/// còn đúng cho TI2027 mà không ai phải sửa gì.
/// </summary>
public class Dota2LeagueInfo
{
    [JsonPropertyName("league_id")] public long LeagueId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("tier")] public int Tier { get; set; }
    [JsonPropertyName("start_timestamp")] public long? StartTimestamp { get; set; }
    [JsonPropertyName("end_timestamp")] public long? EndTimestamp { get; set; }
}

public class Dota2LeagueInfoList
{
    [JsonPropertyName("infos")] public List<Dota2LeagueInfo> Infos { get; set; } = [];
}

/// <summary>
/// Một NÚT trong bảng đấu.
///
/// team_id = 0 nghĩa là CHƯA BIẾT ĐỘI NÀO VÀO, không phải "đội có id 0". Đó là trạng thái
/// bình thường của mọi nút loại trực tiếp trước khi vòng trước kết thúc, và quy nó về null
/// là việc bắt buộc — để 0 lọt vào bước ánh xạ đội sẽ tạo ra những cặp đấu ma.
/// </summary>
public class Dota2Node
{
    [JsonPropertyName("node_id")] public int NodeId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("team_id_1")] public int? TeamId1 { get; set; }
    [JsonPropertyName("team_id_2")] public int? TeamId2 { get; set; }
    [JsonPropertyName("team_1_wins")] public int Team1Wins { get; set; }
    [JsonPropertyName("team_2_wins")] public int Team2Wins { get; set; }
    [JsonPropertyName("scheduled_time")] public long? ScheduledTime { get; set; }
    [JsonPropertyName("actual_time")] public long? ActualTime { get; set; }
    [JsonPropertyName("has_started")] public bool HasStarted { get; set; }
    [JsonPropertyName("is_completed")] public bool IsCompleted { get; set; }
    [JsonPropertyName("series_id")] public long? SeriesId { get; set; }
    [JsonPropertyName("winning_node_id")] public int? WinningNodeId { get; set; }
    [JsonPropertyName("losing_node_id")] public int? LosingNodeId { get; set; }
    [JsonPropertyName("incoming_node_id_1")] public int? IncomingNodeId1 { get; set; }
    [JsonPropertyName("incoming_node_id_2")] public int? IncomingNodeId2 { get; set; }
}

/// <summary>Nhóm nút, LỒNG NHAU: nhóm gốc không có nút nào, chỉ chứa nhóm con.</summary>
public class Dota2NodeGroup
{
    [JsonPropertyName("node_group_id")] public int NodeGroupId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("nodes")] public List<Dota2Node>? Nodes { get; set; }
    [JsonPropertyName("node_groups")] public List<Dota2NodeGroup>? NodeGroups { get; set; }
}

public class Dota2LeagueDataInfo
{
    [JsonPropertyName("league_id")] public long LeagueId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("start_timestamp")] public long? StartTimestamp { get; set; }
    [JsonPropertyName("end_timestamp")] public long? EndTimestamp { get; set; }
}

public class Dota2LeagueData
{
    [JsonPropertyName("info")] public Dota2LeagueDataInfo? Info { get; set; }
    [JsonPropertyName("node_groups")] public List<Dota2NodeGroup>? NodeGroups { get; set; }
}
