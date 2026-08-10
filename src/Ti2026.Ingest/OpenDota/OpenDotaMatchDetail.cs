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

    /// <summary>
    /// Toàn bộ bàn draft, 24 lượt ở thể thức Captains Mode. null ở các thể thức không có draft.
    /// </summary>
    [JsonPropertyName("picks_bans")] public List<OpenDotaPickBan>? PicksBans { get; set; }

    /// <summary>Vàng dẫn trước lớn nhất bên thua từng có. OpenDota tự tính sẵn.</summary>
    [JsonPropertyName("throw")] public int? Throw { get; set; }

    [JsonPropertyName("comeback")] public int? Comeback { get; set; }
}

public class OpenDotaPickBan
{
    [JsonPropertyName("is_pick")] public bool IsPick { get; set; }
    [JsonPropertyName("hero_id")] public int HeroId { get; set; }

    /// <summary>0 = Radiant, 1 = Dire.</summary>
    [JsonPropertyName("team")] public int Team { get; set; }

    [JsonPropertyName("order")] public int Order { get; set; }
}

public class OpenDotaPurchase
{
    /// <summary>Giây kể từ tiếng còi. Âm = mua trước khai cuộc, vẫn là dữ liệu thật.</summary>
    [JsonPropertyName("time")] public int Time { get; set; }

    [JsonPropertyName("key")] public string? Key { get; set; }
}

/// <summary>
/// Một ô trong bảng benchmark: giá trị thật và phân vị của nó so với MỌI người chơi cùng hero.
///
/// raw đến khi thì là số nguyên (696 GPM) khi thì là số thực (8,879 last hit mỗi phút), nên phải
/// khai double chứ không phải int — khai int thì bộ đọc JSON ném lỗi ở đúng những chỉ số tính
/// theo phút, tức gần hết bảng.
/// </summary>
public class OpenDotaBenchmark
{
    [JsonPropertyName("raw")] public double? Raw { get; set; }

    /// <summary>0..1. Xem <see cref="OpenDotaMatchPlayer.Benchmarks"/> để biết khi nào nó vô nghĩa.</summary>
    [JsonPropertyName("pct")] public double? Pct { get; set; }
}

public class OpenDotaMatchPlayer
{
    [JsonPropertyName("account_id")] public long? AccountId { get; set; }
    [JsonPropertyName("hero_id")] public int HeroId { get; set; }

    /// <summary>Tên Steam hiển thị. null khi người đó để hồ sơ ẩn danh.</summary>
    [JsonPropertyName("personaname")] public string? PersonaName { get; set; }

    /// <summary>
    /// Nhóm nào trong ván. Cùng số = cùng nhóm. null = đi một mình.
    ///
    /// Chỉ có nghĩa TRONG một ván: party_id 0 ở ván này và party_id 0 ở ván khác không liên quan
    /// gì tới nhau. Đây là thứ duy nhất phân biệt "bạn tôi rủ đi" với "người lạ ghép ngẫu nhiên",
    /// và party_size thì không làm được — nó chỉ nói CỠ nhóm chứ không nói AI trong nhóm.
    /// </summary>
    [JsonPropertyName("party_id")] public int? PartyId { get; set; }

    /// <summary>Rank của riêng người này (10 = Herald 1 … 80 = Immortal). null khi hồ sơ ẩn.</summary>
    [JsonPropertyName("rank_tier")] public int? RankTier { get; set; }

    /// <summary>
    /// Phân vị của từng chỉ số so với mọi người chơi CÙNG HERO đó. Có ở cả 10 người, kể cả ván
    /// chưa parse — đã kiểm trên dữ liệu thật.
    ///
    /// Đây là thứ biến "600 GPM" thành "giỏi hơn 96% người chơi Centaur", tức là mốc so mà một
    /// trang theo dõi cá nhân thường không có.
    ///
    /// HAI CÁI BẪY, phải chặn ở chỗ đọc chứ không phải chỗ hiển thị:
    ///
    /// 1. RAW = 0 THÌ PHÂN VỊ LÀ RÁC. Đo thật ở ván 8937662260: hero_healing_per_min raw 0 nhưng
    ///    pct 0,93. Phần lớn người chơi hero đó cũng hồi 0 máu, nên cả khối bằng nhau đó bị xếp
    ///    chung một bậc và OpenDota trả về mép trên của khối. Tin nó thì trang sẽ viết "bạn hồi
    ///    máu tốt hơn 93% người chơi" cho một ván hồi đúng 0 máu.
    ///
    /// 2. SỐ CHẾT NGƯỢC CHIỀU. Phân vị cao của deaths_per_min nghĩa là CHẾT NHIỀU, tức tệ hơn.
    ///    Cộng thẳng vào một điểm tổng thì càng chết nhiều điểm càng cao.
    /// </summary>
    [JsonPropertyName("benchmarks")] public Dictionary<string, OpenDotaBenchmark>? Benchmarks { get; set; }
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

    /// <summary>
    /// Mốc mua từng món. Chỉ có ở ván đã parse. Chứa cả món thành phẩm lẫn linh kiện, nên
    /// hỏi "khi nào lên Manta" là hỏi thẳng khoá "manta" chứ không phải cộng dồn linh kiện.
    /// </summary>
    [JsonPropertyName("purchase_log")] public List<OpenDotaPurchase>? PurchaseLog { get; set; }

    // Chỉ có ở ván đã parse — để null khi thiếu, KHÔNG quy về 0.
    [JsonPropertyName("lane_role")] public int? LaneRole { get; set; }
    [JsonPropertyName("lane")] public int? Lane { get; set; }
    [JsonPropertyName("lane_efficiency_pct")] public double? LaneEfficiencyPct { get; set; }
    [JsonPropertyName("last_hits")] public int? LastHits { get; set; }
    [JsonPropertyName("denies")] public int? Denies { get; set; }
    [JsonPropertyName("net_worth")] public int? NetWorth { get; set; }
    [JsonPropertyName("level")] public int? Level { get; set; }
    [JsonPropertyName("hero_damage")] public int? HeroDamage { get; set; }
    [JsonPropertyName("tower_damage")] public int? TowerDamage { get; set; }
    [JsonPropertyName("obs_placed")] public int? ObserversPlaced { get; set; }
    [JsonPropertyName("sen_placed")] public int? SentriesPlaced { get; set; }
    [JsonPropertyName("camps_stacked")] public int? CampsStacked { get; set; }
    [JsonPropertyName("rune_pickups")] public int? RunePickups { get; set; }
    [JsonPropertyName("buyback_count")] public int? Buybacks { get; set; }
    [JsonPropertyName("stuns")] public double? StunSeconds { get; set; }
    [JsonPropertyName("teamfight_participation")] public double? TeamfightParticipation { get; set; }
    [JsonPropertyName("tower_kills")] public int? TowerKills { get; set; }
    [JsonPropertyName("roshan_kills")] public int? RoshanKills { get; set; }
    [JsonPropertyName("courier_kills")] public int? CourierKills { get; set; }
    [JsonPropertyName("observer_kills")] public int? ObserverKills { get; set; }
    [JsonPropertyName("sentry_kills")] public int? SentryKills { get; set; }

    /// <summary>OpenDota trả 0/1 chứ không phải true/false, nên đọc thành số rồi tự quy đổi.</summary>
    [JsonPropertyName("firstblood_claimed")] public int? FirstBloodClaimed { get; set; }

    // ---------- Ba từ điển mở khoá nốt năm chỉ số fantasy còn thiếu ----------
    // Hoa sen, watcher, smoke, madstone và Tormentor đều nằm ở đây chứ không phải ở trường
    // phẳng nào cả. Không có chữ "lotus", "watcher" hay "tormentor" nào trong payload — tên
    // nội bộ lần lượt là famango, ability_lamp_use và npc_dota_miniboss. Xem FantasyFields.
    //
    // null = ván chưa được parse. Phải giữ nguyên null chứ không khởi tạo từ điển rỗng, vì
    // rỗng nghĩa là "đo được và bằng không", khác hẳn "chưa biết".
    [JsonPropertyName("item_uses")] public Dictionary<string, int>? ItemUses { get; set; }
    [JsonPropertyName("ability_uses")] public Dictionary<string, int>? AbilityUses { get; set; }
    [JsonPropertyName("killed")] public Dictionary<string, int>? Killed { get; set; }
    [JsonPropertyName("killed_by")] public Dictionary<string, int>? KilledBy { get; set; }

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
