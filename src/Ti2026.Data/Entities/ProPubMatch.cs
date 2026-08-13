namespace Ti2026.Data.Entities;

/// <summary>
/// Một ván xếp hạng thường (pub) mà một tuyển thủ chuyên nghiệp vừa chơi.
///
/// VÌ SAO ĐÁNG LƯU: trận chính thức là chỉ báo TRỄ — đội chỉ mang ra trận thứ họ đã tin. Còn
/// những gì pro luyện trong pub là chỉ báo SỚM, và mẫu lớn hơn nhiều lần vì họ chơi pub hằng
/// ngày còn giải thì vài tuần một lần.
///
/// LƯU THÔ từng ván, không gộp sẵn: gộp ở lúc nạp thì mất khả năng hỏi "hai tuần gần đây" hay
/// "chỉ người đi mid" về sau.
///
/// GIỚI HẠN phải nói ra: pub của pro có nhiễu thật — chơi lệch vai, chơi thử, chơi cho vui,
/// và đôi khi là người khác mượn tài khoản. Đừng đọc nó như ý định thi đấu.
/// </summary>
public class ProPubMatch
{
    public long Id { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    /// <summary>match_id của OpenDota. Cùng một ván có thể xuất hiện cho nhiều tuyển thủ.</summary>
    public long MatchId { get; set; }

    public int HeroId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime StartTime { get; set; }

    public bool Won { get; set; }

    /// <summary>
    /// lobby_type của OpenDota: 7 = xếp hạng thường. Giữ lại để lọc được về sau — trận đấu
    /// giải cũng lọt vào danh sách này và không được tính lẫn vào "luyện tập".
    /// </summary>
    public int? LobbyType { get; set; }

    /// <summary>Chế độ chơi; 22 là All Pick xếp hạng. Turbo và các chế độ vui khác cần loại.</summary>
    public int? GameMode { get; set; }
}
