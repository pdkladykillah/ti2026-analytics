using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Lấy BỐI CẢNH CẢ ĐỘI cho từng ván của người được theo dõi, và xin OpenDota parse replay để
/// có vai trò THẬT thay vì suy đoán.
///
/// VÌ SAO PHẢI LÀM. Câu hỏi "tôi chơi vị trí nào" không trả lời được bằng chỉ số của riêng
/// người đó. Đo trên tài khoản thật:
///
///   lane thật    last hits   GPM    XPM
///   safe (pos1)     299      543    678
///   mid  (pos2)     345      667    915
///   off  (pos3)     282      533    707
///
/// Farm ba lane gần như bằng nhau. Một người chơi offlane tốt vẫn farm ngang carry, nên mọi
/// quy tắc dựa vào mức farm tuyệt đối sẽ xếp nhầm họ MỘT CÁCH CÓ HỆ THỐNG — sai đều một chiều,
/// loại sai khó thấy nhất.
///
/// HAI NGUỒN, HAI MỨC TIN CẬY:
///
/// 1. PARSE (nhãn thật). OpenDota không tự parse ván pub — đo thật: 0/25 ván gần nhất có sẵn.
///    Phải chủ động POST /request/{id}. Xong trong khoảng 30 giây. Nhưng replay hết hạn: ván
///    61 ngày tuổi parse được, ván 70 ngày thì không.
///
/// 2. THỨ HẠNG TRONG ĐỘI (suy luận). Có cho mọi ván. Tách core/support rất chắc — chênh lệch
///    net worth giữa hai nhóm là 20k so với 8k. Nhưng KHÔNG tách được mid/safe/off: đã kiểm
///    trên 36 ván có nhãn thật, phân bố thứ hạng của ba lane chồng lên nhau nặng.
///
/// Nên: ván trong cửa sổ replay thì có vai trò thật; ván cũ hơn thì chỉ nói được core hay
/// support, và phải nói rõ là suy luận.
/// </summary>
public class TrackedMatchDetailIngester(
    Ti2026DbContext db, OpenDotaClient client, ILogger<TrackedMatchDetailIngester> logger)
{
    /// <summary>
    /// Chỉ xin parse ván trẻ hơn ngần này ngày. Đo thật: 61 ngày còn parse được, 70 ngày thì
    /// không. Lấy 60 cho chắc — xin ván đã quá hạn chỉ tốn lời gọi mà không bao giờ có kết quả.
    /// </summary>
    public const int ParseWindowDays = 60;

    /// <summary>Trần mỗi vòng, để một lần nạp bù không chiếm hết nhịp gọi của phần khác.</summary>
    public const int MaxDetailsPerRun = 400;

    /// <summary>Trần yêu cầu parse mỗi vòng — chúng vào hàng đợi chung của OpenDota.</summary>
    public const int MaxParseRequestsPerRun = 120;

    public async Task<int> IngestAsync(CancellationToken ct)
    {
        var players = await db.TrackedPlayers.ToDictionaryAsync(p => p.Id, p => p.AccountId, ct);
        if (players.Count == 0) return 0;

        var touched = await FetchDetailsAsync(players, ct);
        touched += await RequestParsesAsync(ct);

        await db.SaveChangesAsync(ct);
        return touched;
    }

    /// <summary>
    /// Lấy matches/{id} để biết cả 10 người, rồi tính thứ hạng trong đội.
    ///
    /// Ưu tiên ván MỚI trước: ván mới vừa là thứ người dùng nhìn nhiều nhất, vừa là thứ còn kịp
    /// xin parse trước khi replay hết hạn.
    /// </summary>
    private async Task<int> FetchDetailsAsync(Dictionary<int, long> players, CancellationToken ct)
    {
        var pending = await db.TrackedPlayerMatches
            .Where(m => m.DetailFetchedAt == null)
            .OrderByDescending(m => m.StartTime)
            .Take(MaxDetailsPerRun)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var done = 0;

        foreach (var row in pending)
        {
            if (!players.TryGetValue(row.TrackedPlayerId, out var accountId)) continue;

            try
            {
                var detail = await client.GetMatchAsync(row.MatchId, ct);
                var all = detail.Players ?? [];

                var me = all.FirstOrDefault(p => p.AccountId == accountId);
                if (me is null)
                {
                    // Ván có thật nhưng không tìm thấy người này: đánh dấu đã xử lý để không
                    // hỏi lại mãi. Xảy ra khi tài khoản ẩn danh trong ván đó.
                    row.DetailFetchedAt = DateTime.UtcNow;
                    continue;
                }

                // isRadiant của OpenDota chỉ có ở ván đã parse; player_slot thì luôn có.
                var meRadiant = me.PlayerSlot < 128;
                var team = all.Where(p => (p.PlayerSlot < 128) == meRadiant).ToList();

                row.NetWorth = me.NetWorth;
                row.Level = me.Level;
                row.LaneRole = me.LaneRole is int lr && lr > 0 ? lr : row.LaneRole;

                // Chỉ xếp hạng khi đủ 5 người: thiếu người thì thứ hạng đổi nghĩa, và một hạng
                // "2/3" đọc như "2/5" là sai lệch âm thầm.
                if (team.Count == 5)
                {
                    row.TeamFarmRank = Rank(team, me, p => p.NetWorth ?? p.GoldPerMin);
                    row.TeamXpmRank = Rank(team, me, p => p.XpPerMin);
                }

                row.DetailFetchedAt = DateTime.UtcNow;
                done++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Không lấy được chi tiết ván {Match}", row.MatchId);
            }
        }

        logger.LogInformation("Lấy bối cảnh đội cho {Done}/{Total} ván", done, pending.Count);
        return done;
    }

    /// <summary>
    /// Xin parse những ván còn trong cửa sổ replay và chưa có vai trò thật.
    ///
    /// Chỉ ĐẶT HÀNG, không chờ: parse xong sau khoảng 30 giây, và vòng ingest sau sẽ đọc được
    /// lane_role khi lấy lại chi tiết. Chờ tại chỗ sẽ kéo dài vòng nạp lên hàng giờ mà chẳng
    /// được gì thêm.
    /// </summary>
    private async Task<int> RequestParsesAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-ParseWindowDays);

        var candidates = await db.TrackedPlayerMatches
            .Where(m => m.LaneRole == null
                        && m.ParseRequestedAt == null
                        && m.StartTime >= cutoff)
            .OrderByDescending(m => m.StartTime)
            .Take(MaxParseRequestsPerRun)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        var asked = 0;

        foreach (var row in candidates)
        {
            try
            {
                await client.RequestParseAsync(row.MatchId, ct);

                // Đánh dấu dù kết quả thế nào: replay có thể đã hỏng, và xin lại mỗi vòng cho
                // cùng một ván là cách đốt hạn mức mà không bao giờ có kết quả.
                row.ParseRequestedAt = DateTime.UtcNow;
                asked++;
            }
            catch (Exception ex)
            {
                row.ParseRequestedAt = DateTime.UtcNow;
                logger.LogWarning(ex, "Không xin được parse cho ván {Match}", row.MatchId);
            }
        }

        // Ván đã xin parse thì phải lấy lại chi tiết ở vòng sau mới đọc được lane_role.
        foreach (var row in candidates) row.DetailFetchedAt = null;

        logger.LogInformation("Đã xin parse {Asked} ván trong cửa sổ {Days} ngày", asked, ParseWindowDays);
        return asked;
    }

    private static int Rank<T>(List<T> team, T me, Func<T, int> by) where T : class =>
        team.OrderByDescending(by).ToList().IndexOf(me) + 1;
}
