namespace Ti2026.Data.Entities;

/// <summary>
/// Thành tích của một tuyển thủ trong một ván, lấy từ match detail của OpenDota.
///
/// Đây là thứ mở khoá 5 chỉ số mà endpoint teams/{id}/matches không cung cấp, và là nền
/// cho phân tích cá nhân: hạ gục, hỗ trợ, nhịp độ 10 phút đầu.
/// </summary>
public class MatchPlayer
{
    public int Id { get; set; }

    public long MatchId { get; set; }
    public Match? Match { get; set; }

    /// <summary>
    /// account_id của OpenDota. Giữ nguyên kể cả khi chưa khớp được với Player nào của ta —
    /// một ván có 10 người, không phải ai cũng thuộc 16 đội đang theo dõi.
    /// </summary>
    public long? AccountId { get; set; }

    /// <summary>Khớp về Player của ta khi AccountId trùng; null nếu không thuộc 16 đội.</summary>
    public int? PlayerId { get; set; }
    public Player? Player { get; set; }

    public int HeroId { get; set; }
    public bool IsRadiant { get; set; }

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int GoldPerMin { get; set; }
    public int XpPerMin { get; set; }

    /// <summary>
    /// Số mạng hạ được trong 10 phút đầu, tính từ kills_log.
    /// null = ván chưa được OpenDota parse nên không có timeline.
    /// </summary>
    public int? KillsFirst10Min { get; set; }
}
