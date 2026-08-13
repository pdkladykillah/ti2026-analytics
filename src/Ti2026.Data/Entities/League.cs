namespace Ti2026.Data.Entities;

/// <summary>
/// Giải đấu, kèm hạng do OpenDota phân loại.
///
/// VÌ SAO CẦN: Elo hiện tính trên MỌI trận với cùng hệ số K. Dữ liệu hôm nay toàn bộ là
/// tier "professional" (DreamLeague, PGL Wallachia, BLAST SLAM, ESL One…) nên chưa có vấn
/// đề. Nhưng OpenDota còn các hạng "amateur", "excluded", "null" — và khi vòng loại TI bắt
/// đầu hoặc một đội đánh giải hạng thấp, chúng sẽ lọt vào và kéo rating sai mà không có gì báo.
///
/// Bảng này để lọc trước khi tính, chứ không phải để hiển thị.
/// </summary>
public class League
{
    /// <summary>leagueid của OpenDota — không tự tăng.</summary>
    public long Id { get; set; }

    public string? Name { get; set; }

    /// <summary>"premium" | "professional" | "amateur" | "excluded" | null</summary>
    public string? Tier { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Hạng được tính vào Elo. Chỉ nhận giải chuyên nghiệp trở lên: rating đo sức mạnh thi
    /// đấu đỉnh cao, và một trận thắng ở giải phong trào không phải bằng chứng cho điều đó.
    /// </summary>
    public static readonly string[] RatedTiers = ["premium", "professional"];

    public static bool IsRated(string? tier) =>
        tier is not null && RatedTiers.Contains(tier, StringComparer.OrdinalIgnoreCase);
}
