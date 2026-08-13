namespace Ti2026.Data.Entities;

/// <summary>
/// Một ván của người đáng học, lưu thô từng ván.
///
/// KHÁC HẲN ván của người dùng ở một điểm quyết định: ván chuyên nghiệp gần như LUÔN đã được
/// parse. Đo thật trên 300 ván gần nhất: Malr1ne 300/300, ATF 300/300, Collapse 253/253,
/// Topson 22/22 ván thi đấu. Nên lane_role, lane_efficiency_pct, teamfight_participation và
/// mốc vàng theo phút đều có sẵn — những thứ chỉ phủ được 6% lịch sử pub của người dùng.
///
/// BỘ CỘT NÀY CỐ Ý RỘNG HƠN MỨC CẦN NGAY. Mỗi lần thêm cột là một lần phải quét lại toàn bộ
/// ván đã lưu, và bốn lần như vậy ở phần theo dõi cá nhân đã tốn 2,97 đô. Ở đây mỗi lần quét
/// lại chỉ khoảng 1.200 lời gọi (~0,12 đô) nên rẻ hơn nhiều, nhưng thói quen thì vẫn nên giữ:
/// đọc hết payload một lần, lưu những gì có thể sẽ hỏi tới.
/// </summary>
public class IdolMatch
{
    public long Id { get; set; }

    public int IdolPlayerId { get; set; }
    public IdolPlayer? IdolPlayer { get; set; }

    public long MatchId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime StartTime { get; set; }

    public int DurationSeconds { get; set; }

    public bool Won { get; set; }

    public bool IsRadiant { get; set; }

    public int HeroId { get; set; }

    /// <summary>
    /// lobby_type. Đo thật: ván thi đấu hiện lên là 1, KHÔNG phải 2 như tên gọi "tournament"
    /// gợi ý. Đây là ranh giới quan trọng nhất của bảng — Topson gần như chỉ còn ván xếp hạng
    /// (218/300) trong khi ba người kia gần như chỉ có ván thi đấu, và trộn hai loại vào một
    /// phép so là đúng loại nhiễu đã lật kết luận nhiều lần ở dự án này.
    /// </summary>
    public int? LobbyType { get; set; }

    /// <summary>Khác 0 nghĩa là ván giải chính thức. Chỉ có ở matches/{id}, không có ở danh sách ván.</summary>
    public int? LeagueId { get; set; }

    public int? GameMode { get; set; }

    /// <summary>Bản game, để không trộn hai meta cách nhau nửa năm.</summary>
    public int? PatchId { get; set; }

    // ---------- Chỉ số cá nhân ----------

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }

    public int? LastHits { get; set; }
    public int? Denies { get; set; }
    public int? GoldPerMin { get; set; }
    public int? XpPerMin { get; set; }
    public int? NetWorth { get; set; }
    public int? Level { get; set; }

    public int? HeroDamage { get; set; }
    public int? TowerDamage { get; set; }
    public int? HeroHealing { get; set; }

    /// <summary>Tổng sát thương phải chịu, cộng từ mọi nguồn trong damage_taken.</summary>
    public int? DamageTaken { get; set; }

    public int? ObsPlaced { get; set; }
    public int? SenPlaced { get; set; }
    public int? CampsStacked { get; set; }

    /// <summary>Tổng thời gian khống chế gây ra, tính bằng giây. Số thực ở nguồn.</summary>
    public double? Stuns { get; set; }

    /// <summary>
    /// teamfight_participation của OpenDota, 0..1: phần các pha giao tranh có mặt.
    ///
    /// Đây là thứ gần nhất với "người này có mặt lúc cần không" mà không phải tự dựng lại từ
    /// bảng teamfights. Đo thật trên bốn người: 0,63–0,71 — dải hẹp, nên nó phân biệt yếu giữa
    /// các tuyển thủ và phải đọc như bối cảnh chứ không phải như trục xếp hạng.
    /// </summary>
    public double? TeamfightParticipation { get; set; }

    // ---------- Giai đoạn lane ----------

    /// <summary>1 safe, 2 mid, 3 offlane, 4 rừng. Nhãn thật từ replay.</summary>
    public int? LaneRole { get; set; }

    public int? Lane { get; set; }

    /// <summary>true = rời lane đi quấy sớm. Phân biệt support lang thang với support đứng lane.</summary>
    public bool? IsRoaming { get; set; }

    public int? LaneEfficiency { get; set; }

    /// <summary>
    /// Hạng net worth trong 5 người CÙNG ĐỘI, 1 = giàu nhất.
    ///
    /// VÌ SAO BẮT BUỘC PHẢI CÓ. lane_role nói NGƯỜI NÀY ĐỨNG Ở ĐÂU, không nói họ LÀM GÌ.
    /// Hard support đứng ở safelane nên cũng mang nhãn "safe"; soft support đứng offlane nên
    /// mang nhãn "off". Đo trên chính bộ idol này: Yatoro (carry) 89% safe và Dukalis (hard
    /// support) 77% safe — cùng một nhãn cho hai công việc ngược nhau.
    ///
    /// Ghép nhãn lane với hạng net worth mới ra vị trí thật. Đó là lý do cột này tồn tại, và
    /// là lý do đáng bỏ tiền lấy lại toàn bộ ván đã lưu để có nó.
    /// </summary>
    public int? TeamFarmRank { get; set; }

    /// <summary>
    /// Chênh lệch vàng hai phe ở phút 10, theo góc nhìn PHE NÀY. Dương = đang dẫn.
    ///
    /// radiant_gold_adv ở nguồn luôn là Radiant trừ Dire, nên PHẢI đảo dấu khi ở phe Dire. Quên
    /// bước đó thì gần nửa số ván đọc ngược và kết quả trung hoà về 0 mà vẫn trông hợp lý.
    /// </summary>
    public int? GoldAdv10 { get; set; }

    public int? GoldAdv20 { get; set; }
    public int? GoldAdv30 { get; set; }

    /// <summary>Giây tới pha kill đầu tiên của người này. null = cả ván không giết ai.</summary>
    public int? FirstKillSecond { get; set; }

    // ---------- Tổng của 5 người CÙNG PHE ----------
    //
    // Để tính phần đóng góp trong đội: sát thương của một offlaner chỉ có nghĩa khi biết bốn
    // người kia làm được bao nhiêu. Đo thật, cùng ở offlane: Collapse chiếm 17,4% sát thương
    // của đội còn ATF chiếm 23,6% — chênh 6 điểm giữa hai người cùng vị trí, tức đây là trục
    // phân biệt được lối chơi thật chứ không chỉ phân biệt vị trí.

    public int? TeamKills { get; set; }
    public int? TeamDeaths { get; set; }
    public long? TeamNetWorth { get; set; }
    public long? TeamHeroDamage { get; set; }
    public long? TeamDamageTaken { get; set; }

    /// <summary>
    /// UTC, lần đã đọc matches/{id} cho ván này. null = mới chỉ biết ván tồn tại từ danh sách ván,
    /// chưa có chỉ số nào.
    ///
    /// Cùng khuôn với TrackedPlayerMatch.DetailFetchedAt và cùng lý do: ván tải hỏng phải để null
    /// để vòng sau thử lại, chứ không được đánh dấu đã xong — đánh dấu sớm thì một trục trặc mạng
    /// thoáng qua biến thành mất dữ liệu vĩnh viễn.
    /// </summary>
    public DateTime? DetailFetchedAt { get; set; }
}
