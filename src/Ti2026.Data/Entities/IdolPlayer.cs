namespace Ti2026.Data.Entities;

/// <summary>
/// Một tuyển thủ được chọn để HỌC, không phải để theo dõi kết quả.
///
/// VÌ SAO KHÔNG DÙNG LẠI BẢNG Players. Bảng đó là danh sách người dự TI2026 và nó nuôi tier
/// list, fantasy, roster, dự đoán. Topson không dự TI2026 — nhét anh vào đó thì anh lập tức
/// xuất hiện trong bảng xếp hạng và đội hình fantasy như một người đang thi đấu. Danh sách
/// "người đáng học" và danh sách "người đang thi đấu" là hai câu hỏi khác nhau và sẽ tách nhau
/// ngày một xa, nên tách bảng ngay từ đầu.
///
/// Nối với Players bằng AccountId khi cần ảnh đại diện, chứ không nối bằng khoá ngoại: ba trong
/// bốn người đầu tiên tình cờ có mặt ở cả hai bảng, nhưng đó là trùng hợp chứ không phải quy tắc.
/// </summary>
public class IdolPlayer
{
    public int Id { get; set; }

    /// <summary>Steam32 id. Khoá thật của một con người ở OpenDota.</summary>
    public long AccountId { get; set; }

    /// <summary>Tên thi đấu để hiển thị.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Tên đã chuẩn hoá hoa/thường, có unique index — cùng lý do như Player.NickKey: SQLite so
    /// chuỗi theo BINARY nên "ATF" và "Atf" sẽ thành hai người nếu không có cột này.
    /// </summary>
    public required string NameKey { get; set; }

    public static string MakeNameKey(string name) => name.Trim().ToUpperInvariant();

    public string? PersonaName { get; set; }

    /// <summary>
    /// Tên phe ở ván GIẢI gần nhất của người này — không phải "đội hiện tại".
    ///
    /// Hai thứ đó khác nhau thật, và giao diện phải gọi đúng tên: Topson bán nghỉ nên ván giải gần
    /// nhất của anh đá dưới tên một stack ("Retirement home"), trong khi danh sách tuyển thủ
    /// chuyên nghiệp của OpenDota vẫn ghi affiliation cũ. Muốn affiliation thì phải gọi proPlayers
    /// và tải về cả 5.127 người chỉ để lấy một chuỗi — mà đó vẫn là một đại lượng khác.
    ///
    /// Điều kiện leagueid khác 0 là bắt buộc: phòng chờ pub cũng đặt được tên, và không lọc thì
    /// một nhóm pub tên "Sniper monkeys" sẽ hiện ngay cạnh Team Falcons như thể ngang hàng.
    /// </summary>
    public string? TeamName { get; set; }

    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Vị trí người biên tập gán, dùng để nhóm trên giao diện.
    ///
    /// KHÔNG dùng nó để lọc dữ liệu. Vị trí thật đo được từ nhãn replay của chính các ván đã
    /// nạp, và hai thứ có thể lệch nhau — đo thật thì ATF đi offlane 48/51 ván gần đây nhưng
    /// cả đời chỉ 64% số ván có nhãn là offlane. Cột này là lời giới thiệu, không phải số đo.
    /// </summary>
    public string? DeclaredRole { get; set; }

    /// <summary>Một câu vì sao người này đáng học. Hiện ở đầu thẻ.</summary>
    public string? Note { get; set; }

    /// <summary>Thứ tự hiển thị. Nhỏ hơn đứng trước.</summary>
    public int SortOrder { get; set; }

    /// <summary>UTC, lần cập nhật hồ sơ gần nhất. null = chưa từng lấy.</summary>
    public DateTime? ProfileFetchedAt { get; set; }

    /// <summary>UTC, lần quét danh sách ván gần nhất.</summary>
    public DateTime? MatchesFetchedAt { get; set; }

    public List<IdolMatch> Matches { get; set; } = [];
}
