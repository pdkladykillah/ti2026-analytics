namespace Ti2026.Data.Entities;

/// <summary>
/// Bảng item từ constants/items của OpenDota. Có hai việc: cho TÊN đúng, và cho GIÁ để lọc
/// linh kiện khỏi bảng mốc lên đồ.
///
/// Vì sao lọc theo giá chứ không theo <see cref="Quality"/>: trường qual của OpenDota gắn nhãn
/// "component" cho cả Blink Dagger (2250 vàng, món chủ lực của nửa số hero) lẫn Circlet
/// (155 vàng). Lọc theo qual sẽ vứt đúng những món người ta muốn xem. Giá thì không nhập nhằng.
/// </summary>
public class Item
{
    /// <summary>Khoá kỹ thuật, ví dụ "black_king_bar". Trùng với ItemPurchase.ItemKey.</summary>
    public string Key { get; set; } = "";

    /// <summary>dname của OpenDota — "Ring of Basilius", "Aghanim's Scepter".</summary>
    public string? Name { get; set; }

    /// <summary>Tổng giá. null với vài mục không mua được trực tiếp.</summary>
    public int? Cost { get; set; }

    /// <summary>
    /// qual của OpenDota: component, consumable, rare, epic, artifact, common, secret_shop —
    /// hoặc null (272 mục không có). Chỉ dùng để loại đồ tiêu hao; xem ghi chú ở đầu lớp.
    /// </summary>
    public string? Quality { get; set; }

    public bool IsConsumable =>
        Quality is not null && Quality.Contains("consumable", StringComparison.OrdinalIgnoreCase);
}
