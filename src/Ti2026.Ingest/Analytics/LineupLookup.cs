using Microsoft.EntityFrameworkCore;
using Ti2026.Data;

namespace Ti2026.Ingest.Analytics;

/// <summary>
/// Tra cứu "ván này mỗi bên còn mấy người của đội hình TI2026".
///
/// Tính lúc ĐỌC chứ không lưu sẵn vào Match: roster còn có thể đổi trước giờ khai mạc, mà lưu
/// sẵn thì mọi ván cũ giữ nguyên con số của roster hôm nạp và lặng lẽ sai đi.
///
/// Dùng chung cho api/h2h, api/predict và SnapshotWriter (form + Elo). Cả ba cùng trả lời
/// những câu hỏi về CÙNG một thứ — đội này mạnh yếu ra sao — nên nếu mỗi chỗ tự lọc một kiểu
/// thì trang H2H nói 20 ván còn trang dự đoán nói 71 ván, và người đọc không có cách nào biết
/// bên nào đúng.
/// </summary>
public sealed class LineupLookup
{
    private readonly Dictionary<int, HashSet<int>> _roster;
    private readonly Dictionary<long, List<(int PlayerId, bool IsRadiant)>> _byMatch;

    private LineupLookup(
        Dictionary<int, HashSet<int>> roster,
        Dictionary<long, List<(int, bool)>> byMatch)
    {
        _roster = roster;
        _byMatch = byMatch;
    }

    /// <param name="matchIds">
    /// null = nạp mọi ván có đủ hai đội. Truyền danh sách khi chỉ cần vài ván, để không kéo
    /// 13,6 nghìn hàng cho một cặp đấu có 71 ván.
    /// </param>
    public static async Task<LineupLookup> LoadAsync(
        Ti2026DbContext db, IReadOnlyCollection<long>? matchIds = null,
        CancellationToken ct = default)
    {
        // Bỏ HLV: HLV không ra trận nên không bao giờ xuất hiện trong MatchPlayers, tính vào mẫu
        // số thì mọi đội mãi mãi chỉ đạt 5/6.
        var roster = (await db.RosterEntries
                .Where(r => r.ValidTo == null && r.Role != "COACH")
                .Select(r => new { r.TeamId, r.PlayerId })
                .ToListAsync(ct))
            .GroupBy(r => r.TeamId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PlayerId).ToHashSet());

        // Chỉ lấy hàng đã khớp được về Player của ta — hàng không khớp thì chắc chắn không
        // thuộc đội hình nào đang theo dõi.
        //
        // KHÔNG giới hạn ở ván có đủ hai đội. Bản trước có giới hạn đó, và hệ quả là câu "đội
        // hình này đánh cùng nhau N ván" đếm hụt: Liquid hiện ra 165 trong khi năm người đó đã đánh
        // cùng nhau 195 ván — 30 ván kia gặp đối thủ ngoài 16 đội nên không lọt vào bộ lọc.
        // Mọi phép tính mức ĐỘI vẫn tự lọc "đủ hai đội" ở chỗ của nó, nên nới ở đây không làm
        // ván một chiều lọt vào Elo hay đối đầu.
        var q = db.MatchPlayers.Where(p => p.PlayerId != null);

        if (matchIds is not null) q = q.Where(p => matchIds.Contains(p.MatchId));

        var rows = await q
            .Select(p => new { p.MatchId, PlayerId = p.PlayerId!.Value, p.IsRadiant })
            .ToListAsync(ct);

        return new LineupLookup(roster, rows
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.PlayerId, x.IsRadiant)).ToList()));
    }

    /// <summary>
    /// Số người của đội hình hiện tại mà <paramref name="teamId"/> cho ra trận ở ván này.
    /// Ván chưa có match detail thì không có hàng nào, trả 0 — và 0 bị loại khỏi mọi nhận định,
    /// đúng ý: không biết ai ra trận thì không dùng ván đó để kết luận.
    /// </summary>
    public int Kept(long matchId, int teamId, bool radiantSide) =>
        !_byMatch.TryGetValue(matchId, out var rows) || !_roster.TryGetValue(teamId, out var five)
            ? 0
            : rows.Count(p => p.IsRadiant == radiantSide && five.Contains(p.PlayerId));
}
