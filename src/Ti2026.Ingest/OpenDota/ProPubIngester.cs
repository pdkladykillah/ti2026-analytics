using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Nạp các ván xếp hạng thường mà tuyển thủ chuyên nghiệp vừa chơi.
///
/// VÌ SAO ĐÁNG LÀM: trận chính thức là chỉ báo TRỄ — đội chỉ mang ra trận thứ họ đã tin. Thứ pro
/// luyện trong pub là chỉ báo SỚM, và mẫu lớn hơn nhiều vì họ chơi pub hằng ngày còn giải thì
/// vài tuần một lần.
///
/// CHẠY THEO KHUNG GIỜ, KHÔNG theo lượt tải trang. Đây là ràng buộc quan trọng nhất của cả lớp
/// này: 96 request mỗi lần làm mới, mà nếu buộc vào lượt truy cập thì chi phí phụ thuộc lưu
/// lượng — một ngày đông khách là một ngày bị OpenDota chặn IP. Chạy theo lịch thì nó là chi
/// phí CỐ ĐỊNH, dự đoán được, và không ai bấm F5 làm nó tăng lên.
/// </summary>
public class ProPubIngester(
    Ti2026DbContext db,
    OpenDotaClient client,
    ILogger<ProPubIngester> logger)
{
    public const string Source = "pro-pub";

    /// <summary>
    /// Khoảng cách tối thiểu giữa hai lần chạy. Vòng ingest chính chạy mỗi 6 giờ, nhưng job này
    /// không cần nhịp đó: hero pool của một tuyển thủ không đổi trong nửa ngày, và mỗi lần chạy
    /// tốn 96 request.
    /// </summary>
    /// <remarks>
    /// 24 giờ, không phải 12. Đây là nguồn TỐN NHẤT của cả trang: 80 tuyển thủ × 1 lời gọi mỗi
    /// vòng, tức 160 lời gọi mỗi ngày ở nhịp 12 giờ — bằng 45% toàn bộ chi phí API, chỉ để dò
    /// xu hướng hero. Mà hero pool của một tuyển thủ không đổi trong một ngày, nên 12 giờ vốn
    /// đã thừa: giãn lên 24 giờ cắt đúng một nửa khoản lớn nhất mà không mất tín hiệu nào.
    /// </remarks>
    public static readonly TimeSpan MinInterval = TimeSpan.FromHours(24);

    /// <summary>Số ván gần nhất lấy cho mỗi người. Một request bất kể số này.</summary>
    public const int MatchesPerPlayer = 20;

    /// <summary>lobby_type = 7 là xếp hạng thường. Đây là thứ duy nhất tính là "luyện tập".</summary>
    public const int RankedLobbyType = 7;

    /// <summary>
    /// Trả về số ván ghi mới. Trả 0 và KHÔNG gọi mạng nếu chưa tới hạn chạy lại.
    /// </summary>
    public async Task<int> IngestAsync(CancellationToken ct)
    {
        var lastRun = await db.IngestRuns
            .Where(r => r.Source == Source && r.Status == IngestStatus.Succeeded)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => (DateTime?)r.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (lastRun is DateTime last && DateTime.UtcNow - last < MinInterval)
        {
            logger.LogInformation(
                "Bỏ qua vòng pub của pro: lần chạy trước cách đây {Hours:0.0} giờ, tối thiểu {Min} giờ",
                (DateTime.UtcNow - last).TotalHours, MinInterval.TotalHours);
            return 0;
        }

        var players = await db.Players
            .Where(p => p.OpenDotaAccountId != null)
            .Select(p => new { p.Id, p.Nick, AccountId = p.OpenDotaAccountId!.Value })
            .ToListAsync(ct);

        if (players.Count == 0) return 0;

        var written = 0;
        var failuresInARow = 0;

        foreach (var player in players)
        {
            ct.ThrowIfCancellationRequested();

            List<OpenDotaPlayerMatch> matches;
            try
            {
                matches = await client.GetPlayerMatchesAsync(
                    player.AccountId, MatchesPerPlayer, ct);
                failuresInARow = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                             or TimeoutException or System.Text.Json.JsonException)
            {
                failuresInARow++;
                logger.LogInformation(ex, "Không lấy được ván pub của {Nick}", player.Nick);

                // Cùng lý do như MatchDetailIngester: ba lỗi liền nhau gần như luôn là hết hạn
                // mức, và cố thêm 90 lần nữa chỉ làm tăng nguy cơ bị chặn IP.
                if (failuresInARow >= MatchDetailIngester.MaxConsecutiveFailures)
                {
                    logger.LogWarning("Dừng vòng pub sau {Count} lỗi liên tiếp", failuresInARow);
                    break;
                }

                continue;
            }

            var existing = await db.ProPubMatches
                .Where(x => x.PlayerId == player.Id)
                .Select(x => x.MatchId)
                .ToListAsync(ct);

            var known = existing.ToHashSet();

            foreach (var m in matches)
            {
                // Ván chưa biết kết quả thì bỏ qua HẲN, không ghi với Won = false: một ván đang
                // diễn ra được ghi là thua sẽ kéo winrate xuống và không bao giờ tự sửa.
                if (m.Won is not bool won) continue;
                if (known.Contains(m.MatchId)) continue;

                db.ProPubMatches.Add(new ProPubMatch
                {
                    PlayerId = player.Id,
                    MatchId = m.MatchId,
                    HeroId = m.HeroId,
                    StartTime = DateTimeOffset.FromUnixTimeSeconds(m.StartTime).UtcDateTime,
                    Won = won,
                    LobbyType = m.LobbyType,
                    GameMode = m.GameMode,
                });

                known.Add(m.MatchId);
                written++;
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }

        logger.LogInformation("Nạp {Written} ván pub từ {Players} tuyển thủ", written, players.Count);
        return written;
    }
}
