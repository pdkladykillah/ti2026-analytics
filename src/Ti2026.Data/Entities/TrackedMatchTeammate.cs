namespace Ti2026.Data.Entities;

/// <summary>
/// Một người CÙNG PHE trong một ván của người được theo dõi.
///
/// VÌ SAO LƯU RIÊNG TỪNG DÒNG thay vì gộp sẵn thành "đã chơi với A 120 ván". Gộp sẵn thì không
/// trả lời được câu hỏi thật sự đáng giá: "thắng bao nhiêu KHI CÓ A so với khi KHÔNG có A" —
/// phép so đó cần biết chính xác những ván nào có A, và cả những ván nào không.
///
/// CHỈ LƯU CÙNG PHE. Đối thủ cũng có trong payload, nhưng trong pub thì gặp lại cùng một đối thủ
/// là chuyện hiếm và ngẫu nhiên, nên mọi con số "khắc tinh" dựng trên đó sẽ là nhiễu được trình
/// bày như phát hiện. Đồng đội thì ngược lại: người ta rủ nhau chơi hàng trăm ván.
///
/// RIÊNG TƯ. Đây đều là dữ liệu công khai trên hồ sơ Dota 2, nhưng trang thì công khai còn người
/// ghép ngẫu nhiên thì không tự nguyện xuất hiện ở đây. Nên phần hiển thị chỉ nêu những người đã
/// chơi cùng đủ nhiều — xem TeammateAnalysis.MinGamesTogether. Ngưỡng đó vừa là điều kiện thống
/// kê vừa là ranh giới riêng tư, và may là cả hai đòi hỏi cùng một thứ.
/// </summary>
public class TrackedMatchTeammate
{
    public long Id { get; set; }

    public long TrackedPlayerMatchId { get; set; }
    public TrackedPlayerMatch? Match { get; set; }

    /// <summary>Steam32 id. Ván có người ẩn danh thì KHÔNG lưu dòng đó — không định danh được.</summary>
    public long AccountId { get; set; }

    /// <summary>Tên hiển thị lúc lấy dữ liệu. Người ta đổi tên, nên đây là ảnh chụp chứ không phải khoá.</summary>
    public string? PersonaName { get; set; }

    public int HeroId { get; set; }

    /// <summary>
    /// true = đi cùng nhóm với người được theo dõi, suy từ party_id trùng nhau.
    ///
    /// Đây là ranh giới giữa BẠN và NGƯỜI LẠ GHÉP TRÚNG. Không có nó thì một người ghép ngẫu
    /// nhiên trúng vài lần sẽ nằm lẫn trong danh sách bạn bè.
    /// </summary>
    public bool SameParty { get; set; }

    /// <summary>Rank của người đó lúc lấy dữ liệu. null khi hồ sơ để ẩn.</summary>
    public int? RankTier { get; set; }
}
