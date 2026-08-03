namespace Ti2026.Ingest;

public sealed record SanityThresholds(int MinTeams, int MinPlayers);

public sealed record SanityCheck(bool Passed, string? Reason)
{
    public static SanityCheck Ok() => new(true, null);
    public static SanityCheck Fail(string reason) => new(false, reason);
}

/// <summary>
/// Chặn failure mode nguy hiểm nhất của cả pipeline: ingester "thành công" nhưng trả về
/// rỗng (nguồn đổi layout, selector không match) rồi ghi con số rỗng đó đè lên dữ liệu tốt.
/// Kiểu lỗi này không có exception nào, không có log nào — chỉ có một trang trắng.
///
/// Gate áp cho TỪNG ingester, trên đúng loại dữ liệu ingester đó ghi. Nếu áp một ngưỡng
/// chung cho cả vòng thì một nguồn lỗi sẽ kéo các nguồn đang tốt bị rollback theo.
///
/// Nguyên tắc: dữ liệu hơi lỗi thời tốt hơn dữ liệu rỗng, luôn luôn.
/// </summary>
public static class SanityGate
{
    public static SanityCheck CheckTeams(int written, SanityThresholds t) =>
        written >= t.MinTeams
            ? SanityCheck.Ok()
            : SanityCheck.Fail(
                $"Chỉ ghi được {written} đội, ngưỡng tối thiểu là {t.MinTeams}. Giữ nguyên dữ liệu cũ.");

    public static SanityCheck CheckPlayers(int written, SanityThresholds t) =>
        written >= t.MinPlayers
            ? SanityCheck.Ok()
            : SanityCheck.Fail(
                $"Chỉ ghi được {written} player, ngưỡng tối thiểu là {t.MinPlayers}. Giữ nguyên dữ liệu cũ.");
}
