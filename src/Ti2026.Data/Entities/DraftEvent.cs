namespace Ti2026.Data.Entities;

/// <summary>
/// Một lượt cấm hoặc chọn trong bàn draft, giữ nguyên THỨ TỰ.
///
/// Thứ tự mới là phần đắt giá. Winrate nói hero đó thắng bao nhiêu; lượt cấm đầu tiên nói
/// người trong nghề SỢ hero đó đến mức nào — và họ ra quyết định đó trước khi trận bắt đầu,
/// không phải sau khi đã biết kết quả. Một hero winrate 48% mà bị cấm ngay lượt đầu ở phần
/// lớn số trận là hero mạnh, chỉ là không ai được chơi nó.
/// </summary>
public class DraftEvent
{
    public long Id { get; set; }

    public long MatchId { get; set; }
    public Match? Match { get; set; }

    /// <summary>0..23. Duy nhất trong một trận — dùng làm chốt chống nạp trùng.</summary>
    public int Order { get; set; }

    public bool IsPick { get; set; }
    public int HeroId { get; set; }

    /// <summary>OpenDota trả team 0 = Radiant, 1 = Dire.</summary>
    public bool IsRadiant { get; set; }
}
