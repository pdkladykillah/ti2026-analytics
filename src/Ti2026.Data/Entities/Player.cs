namespace Ti2026.Data.Entities;

public class Player
{
    public int Id { get; set; }
    public long? OpenDotaAccountId { get; set; }

    /// <summary>Nick để hiển thị, giữ nguyên hoa/thường như nguồn.</summary>
    public required string Nick { get; set; }

    /// <summary>
    /// Khoá định danh: Nick đã Trim + ToUpperInvariant, có unique index.
    ///
    /// Cần thiết vì SQLite so sánh chuỗi theo collation BINARY, nên tra cứu bằng Nick
    /// nguyên bản sẽ trượt khi editor chỉ sửa hoa/thường ("ATF" -> "Atf") — hậu quả là
    /// tạo thêm một Player thứ hai cho cùng một người. Dùng NickKey thì mọi biến thể
    /// hoa/thường và khoảng trắng đầu/cuối đều trỏ về một hàng.
    /// </summary>
    public required string NickKey { get; set; }

    public static string MakeNickKey(string nick) => nick.Trim().ToUpperInvariant();
    public string? RealName { get; set; }
    public string? CountryName { get; set; }
    public string? CountryCode { get; set; }
    /// <summary>
    /// Ảnh từ players.json, trỏ dltv.org/uploads/. KHÔNG DÙNG ĐỂ HIỂN THỊ: dltv chặn hotlink
    /// theo referrer nên ảnh không bao giờ hiện trên trình duyệt, và robots.txt của họ cấm
    /// /uploads/ nên cũng không được phép cache về. Giữ lại chỉ để biết nguồn biên tập nói gì.
    /// </summary>
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Avatar công khai trên Steam, lấy qua players/{account_id} của OpenDota.
    /// Đây là ảnh DUY NHẤT hiển thị được — dùng cột này ở mọi nơi cần ảnh tuyển thủ.
    /// null thì UI hiện chữ cái đầu, không hiện ảnh vỡ.
    /// </summary>
    public string? AvatarUrl { get; set; }

    public int? PhotoMediaAssetId { get; set; }
}
