using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// Phần dùng chung giữa api/idols và api/versus: đổi hàng neo trong DB thành <see cref="StylePool"/>,
/// và dựng mốc "người bình thường ở ván chuyên nghiệp".
///
/// Tách ra thành một chỗ vì đây đúng là loại mã sẽ bị cài lần thứ hai. Hai endpoint cùng cần một
/// phép đổi tầm thường, mà mỗi bản sao là một cơ hội để hai trang cùng nói về "mức thường" theo hai
/// nghĩa khác nhau — kiểu lệch không có gì báo, vì cả hai bên đều chạy được và đều ra số trông hợp lý.
/// Dự án đã có một lần suýt như vậy với RoleResolver.
/// </summary>
public static class StyleSupport
{
    /// <summary>
    /// Hồ ván chuyên nghiệp. Chung cho cả tuyển thủ được theo dõi lẫn 16 đội dự giải: cả hai đều
    /// đá ván chuyên nghiệp, nên mốc "người bình thường" của họ là cùng một thứ.
    /// </summary>
    public const string ProPool = "pro";

    public static StylePool ToPool(StyleAnchor a) => new(
        a.AllKills, a.AllAssists, a.AllDeaths, a.AllNetWorth, a.AllHeroDamage,
        a.AllLastHits, a.AllTowerDamage, a.AllLaneEfficiency, a.LaneEfficiencyCount,
        a.PlayerCount, a.DurationSeconds);

    /// <summary>
    /// Mốc chuẩn hoá của hồ ván chuyên nghiệp. Trả về từ điển RỖNG nếu chưa đủ ván neo — bên gọi
    /// phải xử lý được ca đó, vì <see cref="IdolStyle.Signature"/> khi thiếu mốc vẫn trả về giá trị
    /// thô với Index null, chứ không ném.
    /// </summary>
    public static async Task<Dictionary<string, double>> ProNormalizerAsync(
        Ti2026DbContext db, CancellationToken ct = default)
    {
        var anchors = await db.StyleAnchors
            .Where(a => a.Pool == ProPool)
            .ToListAsync(ct);

        return IdolStyle.Normalizer(anchors.Select(ToPool).ToList());
    }
}
