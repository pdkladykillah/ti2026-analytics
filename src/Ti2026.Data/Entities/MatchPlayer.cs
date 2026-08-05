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

    // ---------- Chỉ từ ván ĐÃ được OpenDota parse; chưa parse thì để null ----------
    // Cả khối dưới đây là null-able có chủ đích. Quy về 0 sẽ tạo ra những tuyển thủ "farm 0
    // lính, gây 0 sát thương" trộn lẫn với số đo thật, và trung bình sẽ sai mà nhìn vẫn hợp lý.

    /// <summary>OpenDota lane_role: 1 safe, 2 mid, 3 off, 4 jungle. 0/null = không xác định.</summary>
    public int? LaneRole { get; set; }

    /// <summary>Lane thực tế suy từ vị trí đầu trận, có thể khác LaneRole khi đội đổi lane.</summary>
    public int? Lane { get; set; }

    /// <summary>Hiệu suất lane theo phần trăm, thước đo thắng/thua lane của OpenDota.</summary>
    public double? LaneEfficiencyPct { get; set; }

    public int? LastHits { get; set; }
    public int? Denies { get; set; }
    public int? NetWorth { get; set; }
    public int? HeroDamage { get; set; }
    public int? TowerDamage { get; set; }
    public int? ObserversPlaced { get; set; }
}
