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

    /// <summary>
    /// Khoảng hiệu lực theo quy ước NỬA MỞ: [ValidFrom, ValidTo).
    /// Tức ValidFrom tính vào, ValidTo KHÔNG tính vào.
    ///
    /// Quy ước này phải được ghi rõ vì nếu không, một lần chuyển đội sẽ tạo hai hàng cùng
    /// nhận ngày đổi: đọc theo kiểu bao gồm cả hai đầu (ValidTo >= d) thì đúng ngày đó đội
    /// có 7 người và player bị đếm hai lần trong mọi phép tổng hợp. Với nửa mở thì hàng cũ
    /// đóng ở ValidTo = ngày đổi và hàng mới mở ở ValidFrom = ngày đổi, hai khoảng rời nhau
    /// tuyệt đối.
    ///
    /// Truy vấn đúng cho một ngày d: ValidFrom &lt;= d &amp;&amp; (ValidTo == null || ValidTo &gt; d)
    /// </summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>null = đang hiệu lực. Xem quy ước nửa mở ở ValidFrom.</summary>
    public DateOnly? ValidTo { get; set; }
}
