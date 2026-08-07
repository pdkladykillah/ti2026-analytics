namespace Ti2026.Data.Entities;

/// <summary>
/// Một team_id của OpenDota thuộc về đội này. MỘT đội có thể có NHIỀU.
///
/// Vì sao không phải một cột duy nhất trên Team. Trong Dota, "đội" là một tổ chức còn team_id
/// là một bản ghi; khi một roster đổi tổ chức hoặc đăng ký lại, OpenDota sinh bản ghi MỚI và
/// bản ghi cũ ngừng nhận ván. Chuyện này xảy ra thật với 4/16 đội TI2026:
///
///   PariVision  9572001 chết 25/06  ->  9824702 "PVISION"     (đá và VÔ ĐỊCH EWC 2026)
///   L1GA        9303383 chết 09/05  ->  10182299 "L1 TEAM"
///   1win        vẫn sống 10182357   +   10150413 "Iron Wing"  (tên cũ của cùng roster)
///   LGD         vẫn sống 10150538   +   10144195 "ex-HEROIC"
///
/// Với một cột duy nhất, hai trường hợp đầu làm hệ thống LẶNG LẼ ngừng nhận ván của đội đó:
/// TeamResolver chỉ phân giải đội có OpenDotaTeamId là null, nên đã gán một lần thì không bao
/// giờ kiểm lại, và vòng ingest vẫn báo "Succeeded" trong lúc mất trắng dữ liệu.
/// </summary>
public class TeamOpenDotaId
{
    public int Id { get; set; }

    public int TeamId { get; set; }
    public Team? Team { get; set; }

    public int OpenDotaTeamId { get; set; }

    /// <summary>
    /// Do đâu mà biết: "editorial" (khai trong teams.json), "resolver" (khớp tên/tag),
    /// "roster" (bộ dò thấy đủ 5 người của đội hình trên một bên chưa nhận diện được).
    /// Giữ lại để về sau còn phân biệt được cái nào do máy tự thêm.
    /// </summary>
    public required string Source { get; set; }

    /// <summary>UTC, lúc id này được ghi nhận.</summary>
    public DateTime AddedAt { get; set; }
}
