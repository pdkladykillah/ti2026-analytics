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
    /// lane_role của OpenDota: 1 an toàn, 2 mid, 3 offlane, 4 rừng. null/0 = không suy ra được.
    ///
    /// Đây là suy đoán từ vị trí đầu trận, KHÔNG phải vai trò khai báo — nên nó sai ở những ván
    /// đổi lane. Mọi chỗ hiển thị phải nói rõ là suy đoán.
    /// </summary>
    public int? LaneRole { get; set; }

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
}
