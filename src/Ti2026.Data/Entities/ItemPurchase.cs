namespace Ti2026.Data.Entities;

/// <summary>
/// Một lần mua đồ, kèm mốc giây kể từ tiếng còi khai cuộc.
///
/// Lưu THÔ, không lọc và không gộp sẵn. Lọc lúc nạp thì cần một danh sách "món đáng quan
/// tâm" — danh sách đó sẽ lỗi thời ngay bản sau, và lúc muốn hỏi câu khác thì dữ liệu đã mất
/// rồi. Nạp lại 1790 ván tốn nửa tiếng, nên thà tốn vài chục MB đĩa còn hơn.
///
/// Thời gian có thể ÂM: đồ mua trước khi khai cuộc. Đó là dữ liệu thật, không phải rác.
/// </summary>
public class ItemPurchase
{
    public long Id { get; set; }

    public long MatchId { get; set; }
    public Match? Match { get; set; }

    /// <summary>Của OpenDota: 0..127 Radiant, 128..255 Dire. Dùng để gom theo từng người.</summary>
    public int PlayerSlot { get; set; }

    public int HeroId { get; set; }
    public bool IsRadiant { get; set; }

    /// <summary>Khoá kỹ thuật của item, ví dụ "black_king_bar". Không phải tên hiển thị.</summary>
    public string ItemKey { get; set; } = "";

    public int TimeSeconds { get; set; }
}
