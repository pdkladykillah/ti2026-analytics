namespace Ti2026.Data.Entities;

/// <summary>
/// Một ván của người được theo dõi, lưu THÔ từng ván.
///
/// Không gộp sẵn thành trung bình lúc nạp: gộp sớm thì mất khả năng hỏi lại "ba tháng gần đây",
/// "chỉ hero đi mid", "chỉ ván solo" về sau — mà đó đúng là những câu hỏi một trang theo dõi
/// bản thân sẽ phải trả lời.
///
/// Toàn bộ lấy từ một lời gọi players/{id}/matches, không cần match detail từng ván. Đó là lý
/// do chọn đúng bộ cột này: chúng là những gì endpoint đó thực sự trả về.
/// </summary>
public class TrackedPlayerMatch
{
    public long Id { get; set; }

    public int TrackedPlayerId { get; set; }
    public TrackedPlayer? TrackedPlayer { get; set; }

    public long MatchId { get; set; }

    public int HeroId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime StartTime { get; set; }

    public int DurationSeconds { get; set; }

    public bool Won { get; set; }

    /// <summary>
    /// true = phe Radiant, suy từ player_slot &lt; 128.
    ///
    /// Lưu hẳn thành cột chứ không suy lại lúc đọc: ta không lưu player_slot, nên nếu không có
    /// cột này thì phần phân tích lệch bên sân không có gì để dựa vào — và cám dỗ lúc đó là
    /// bịa ra một quy tắc trông hợp lý. Trên tài khoản thật, chênh lệch Radiant/Dire là 7,7
    /// điểm phần trăm qua gần 6.000 ván, tức hoàn toàn có thật và đáng đo.
    /// </summary>
    public bool IsRadiant { get; set; }

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }

    public int? GoldPerMin { get; set; }
    public int? XpPerMin { get; set; }
    public int? LastHits { get; set; }
    public int? Denies { get; set; }
    public int? HeroDamage { get; set; }
    public int? TowerDamage { get; set; }
    public int? HeroHealing { get; set; }

    /// <summary>
    /// lane_role của OpenDota: 1 safe, 2 mid, 3 offlane, 4 rừng. null = ván chưa được parse.
    ///
    /// Đây là NHÃN THẬT, đọc từ replay, không phải suy đoán. Nó chỉ có sau khi ván được parse —
    /// OpenDota KHÔNG tự parse ván pub (đo thật: 0/25 ván gần nhất có sẵn), phải chủ động xin.
    /// Xem <see cref="ParseRequestedAt"/>.
    /// </summary>
    public int? LaneRole { get; set; }

    // ---------- Bối cảnh cả đội, lấy từ matches/{id} ----------
    //
    // VÌ SAO CẦN. Mức farm TUYỆT ĐỐI không nói lên vai trò: đo trên tài khoản thật thì ván
    // offlane có 282 last hit còn ván safelane có 299 — gần như bằng nhau. Người chơi giỏi ở
    // offlane vẫn farm ngang carry, và một bộ luật dựa vào farm tuyệt đối sẽ xếp nhầm họ một
    // cách CÓ HỆ THỐNG.
    //
    // Thứ hạng TRONG ĐỘI thì nói được nhiều hơn: khoảng cách giữa core và support là rất lớn
    // (net worth 20k so với 8k trong ván mẫu), nên tách core/support rất chắc. Nhưng tách
    // mid/safe/off thì KHÔNG — đã đo trên 36 ván có nhãn thật và phân bố chồng nhau nặng.

    /// <summary>Net worth cuối ván.</summary>
    public int? NetWorth { get; set; }

    public int? Level { get; set; }

    /// <summary>Hạng net worth trong 5 người CÙNG ĐỘI, 1 = giàu nhất.</summary>
    public int? TeamFarmRank { get; set; }

    /// <summary>Hạng XPM trong 5 người cùng đội, 1 = cao nhất. Người đi mid thường đứng đầu.</summary>
    public int? TeamXpmRank { get; set; }

    /// <summary>UTC, lần lấy matches/{id}. null = chưa có bối cảnh đội.</summary>
    public DateTime? DetailFetchedAt { get; set; }

    /// <summary>
    /// UTC, lần đã xin OpenDota parse ván này. null = chưa xin.
    ///
    /// Replay của Valve hết hạn sau khoảng 2 tháng — đã đo: ván 61 ngày tuổi parse được, ván 70
    /// ngày thì không. Nên cột này cũng là cách để KHÔNG xin lại mãi những ván đã quá hạn.
    /// </summary>
    public DateTime? ParseRequestedAt { get; set; }

    /// <summary>7 = xếp hạng. Cần để tách ván nghiêm túc khỏi Turbo và chế độ vui.</summary>
    public int? LobbyType { get; set; }

    /// <summary>22 = All Pick xếp hạng.</summary>
    public int? GameMode { get; set; }

    /// <summary>Số người trong nhóm. 1 = đi một mình. null = OpenDota không biết.</summary>
    public int? PartySize { get; set; }

    /// <summary>
    /// Rank trung bình của ván theo ước lượng OpenDota (10 = Herald 1 … 80 = Immortal).
    /// Đây là thước đo ĐỘ KHÓ của ván, và là thứ cho phép nói "thắng ở mức nào" thay vì chỉ
    /// "thắng bao nhiêu".
    /// </summary>
    public int? AverageRank { get; set; }

    // ---------- Phân vị so với mọi người chơi cùng hero, lấy từ matches/{id} ----------
    //
    // VÌ SAO ĐÁNG LƯU. "600 GPM" không nói được gì nếu không biết 600 là nhiều hay ít TRÊN HERO
    // ĐÓ — 600 với Anti-Mage là kém, với Crystal Maiden là phi thường. Phân vị của OpenDota giải
    // đúng chuyện đó: nó so với mọi người chơi cùng hero. Có ở cả ván chưa parse, nên phủ 100%
    // lịch sử chứ không phải 6% như lane_role.
    //
    // 0..100. null nghĩa là KHÔNG ĐO ĐƯỢC, và có hai đường dẫn tới null:
    //   • ván chưa lấy chi tiết, hoặc OpenDota không trả benchmark cho chỉ số đó;
    //   • giá trị thật bằng 0 — lúc đó phân vị là rác, xem OpenDotaMatchPlayer.Benchmarks.
    // Cả hai đều phải là null chứ không phải 0: 0 nghĩa là "kém hơn tất cả", khác hẳn "chưa biết".

    public int? PctGpm { get; set; }
    public int? PctXpm { get; set; }
    public int? PctLastHits { get; set; }
    public int? PctDenies { get; set; }
    public int? PctKills { get; set; }

    /// <summary>Phân vị CAO = chết NHIỀU = tệ. Đảo chiều trước khi gộp vào bất kỳ điểm tổng nào.</summary>
    public int? PctDeaths { get; set; }

    public int? PctAssists { get; set; }
    public int? PctHeroDamage { get; set; }
    public int? PctHeroHealing { get; set; }
    public int? PctTowerDamage { get; set; }

    /// <summary>4 người cùng phe. Rỗng khi ván chưa lấy chi tiết, hoặc khi cả 4 đều ẩn danh.</summary>
    public List<TrackedMatchTeammate> Teammates { get; set; } = [];
}
