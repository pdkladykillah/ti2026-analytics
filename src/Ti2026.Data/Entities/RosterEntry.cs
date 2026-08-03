namespace Ti2026.Data.Entities;

/// <summary>
/// Player thuộc đội nào, từ ngày nào đến ngày nào.
/// Có lịch sử vào/ra vì roster đổi giữa giải: nếu chỉ lưu quan hệ phẳng thì hôm một
/// player chuyển đội, toàn bộ lịch sử bị tính lại như thể anh ta luôn ở đội mới —
/// dữ liệu form quá khứ lặng lẽ sai đi mà không có lỗi nào báo.
/// </summary>
public class RosterEntry
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }
    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    /// <summary>
    /// CORE|MID|OFFLANE|SUPPORT|FULL SUPPORT|COACH — đúng 6 giá trị mà index.html
    /// đã map sẵn trong POS/RORD. Đổi tên là vỡ UI đội hình.
    /// </summary>
    public required string Role { get; set; }

    public DateOnly ValidFrom { get; set; }

    /// <summary>null = đang hiệu lực.</summary>
    public DateOnly? ValidTo { get; set; }
}
