namespace Ti2026.Data.Entities;

/// <summary>
/// Thành tích của một người được theo dõi với MỘT hero, ở cả ba tư cách: tự chơi, cùng phe,
/// và đối đầu.
///
/// VÌ SAO KHÔNG TỰ ĐẾM TỪ BẢNG VÁN. Phần "tự chơi" thì đếm được — ta đã lưu hero của từng ván.
/// Nhưng phần ĐỐI ĐẦU thì cần biết cả năm hero phe địch của mọi ván, tức phải lấy lại chi tiết
/// gần mười nghìn ván. OpenDota đã đếm sẵn cả ba và trả về trong MỘT lời gọi cho mỗi người.
///
/// Đổi lại, những con số này là của TOÀN BỘ lịch sử theo OpenDota, không cắt theo khoảng thời
/// gian nào — nên chúng không khớp tuyệt đối với các bảng khác trên trang, vốn tính trên phần
/// đã lưu. Chênh lệch đó phải nói ra chứ không được lặng lẽ trộn hai nguồn.
/// </summary>
public class TrackedPlayerHero
{
    public long Id { get; set; }

    public int TrackedPlayerId { get; set; }
    public TrackedPlayer? TrackedPlayer { get; set; }

    public int HeroId { get; set; }

    /// <summary>Số ván tự cầm hero này.</summary>
    public int Games { get; set; }
    public int Wins { get; set; }

    /// <summary>Số ván có hero này ở CÙNG phe (do đồng đội cầm).</summary>
    public int WithGames { get; set; }
    public int WithWins { get; set; }

    /// <summary>Số ván ĐỐI ĐẦU hero này — mẫu số của câu "hero nào khắc chế tôi".</summary>
    public int AgainstGames { get; set; }
    public int AgainstWins { get; set; }

    /// <summary>UTC, lần cập nhật gần nhất.</summary>
    public DateTime FetchedAt { get; set; }
}
