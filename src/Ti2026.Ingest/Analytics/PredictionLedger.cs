using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.Analytics;

/// <summary>
/// Sổ theo dõi dự đoán: ghi lại mô hình nói gì TRƯỚC khi trận diễn ra, rồi đối chiếu kết quả.
///
/// VÌ SAO PHẢI GHI TRƯỚC. Hiệu chuẩn hồi tố — chấm lại trên dữ liệu đã có — luôn dễ hơn thực
/// tế, vì tham số đã được chọn khi đã nhìn thấy chính những trận đó. Bài kiểm thật là nói
/// trước rồi chờ. Và nó CHỈ làm được nếu bắt đầu ghi từ bây giờ: giải đấu xong rồi thì không
/// còn cách nào dựng lại "lúc đó mô hình nói gì", vì Elo đã thay đổi theo chính các trận ấy.
///
/// CÁCH GHI. Mỗi vòng nạp, chụp lại dự đoán cho mọi cặp trong 16 đội. Không chờ ai bấm xem —
/// chỉ ghi khi có người mở trang thì mẫu vừa thưa vừa lệch, và những cặp không ai quan tâm sẽ
/// không bao giờ được kiểm.
///
/// Bỏ qua cặp nào Elo KHÔNG ĐỔI so với bản ghi chưa chấm gần nhất: mô hình chưa nói gì mới thì
/// không cần một dòng mới.
/// </summary>
public class PredictionLedger(Ti2026DbContext db, ILogger<PredictionLedger> logger)
{
    /// <summary>
    /// Chụp dự đoán hiện tại cho mọi cặp đội. Trả về số dòng mới ghi.
    /// </summary>
    public async Task<int> SnapshotAsync(
        IReadOnlyDictionary<int, double> eloByTeam, DateTime now, CancellationToken ct)
    {
        var teamIds = eloByTeam.Keys.OrderBy(x => x).ToList();
        if (teamIds.Count < 2) return 0;

        // Bản ghi CHƯA CHẤM gần nhất của từng cặp — để biết mô hình đã đổi ý chưa
        var open = await db.Predictions
            .Where(p => p.ResolvedMatchId == null)
            .GroupBy(p => new { p.TeamAId, p.TeamBId })
            .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
            .ToListAsync(ct);

        var latestOpen = open.ToDictionary(p => (p.TeamAId, p.TeamBId));
        var added = 0;

        for (var i = 0; i < teamIds.Count; i++)
        for (var j = i + 1; j < teamIds.Count; j++)
        {
            var (aId, bId) = (teamIds[i], teamIds[j]);
            var (eloA, eloB) = (eloByTeam[aId], eloByTeam[bId]);

            if (latestOpen.TryGetValue((aId, bId), out var prev)
                && Math.Abs(prev.EloA - eloA) < 0.01 && Math.Abs(prev.EloB - eloB) < 0.01)
            {
                continue; // mô hình chưa nói gì mới về cặp này
            }

            db.Predictions.Add(new Prediction
            {
                TeamAId = aId,
                TeamBId = bId,
                EloA = eloA,
                EloB = eloB,
                ProbabilityA = Math.Round(
                    EloEngine.ExpectedScore(eloA, eloB, EloEngine.DefaultProbabilityScale) * 100, 2),
                CreatedAt = now,
            });

            added++;
        }

        if (added > 0) logger.LogInformation("Ghi {Count} dự đoán mới vào sổ theo dõi", added);
        return added;
    }

    /// <summary>
    /// Chấm những dự đoán đã có kết quả. Trả về số dòng vừa chấm.
    ///
    /// Mỗi dự đoán chấm theo ván ĐẦU TIÊN giữa hai đội sau thời điểm ghi. Cố tình không chấm
    /// mọi ván của một series: ba ván trong cùng một Bo3 không độc lập với nhau, gộp cả ba vào
    /// mẫu sẽ làm mẫu trông lớn gấp ba và đường hiệu chuẩn trông chắc hơn thực tế.
    /// </summary>
    public async Task<int> ResolveAsync(DateTime now, CancellationToken ct)
    {
        var open = await db.Predictions.Where(p => p.ResolvedMatchId == null).ToListAsync(ct);
        if (open.Count == 0) return 0;

        var earliest = open.Min(p => p.CreatedAt);

        var candidates = await db.Matches
            .Where(m => m.StartTime > earliest
                        && m.RadiantTeamId != null && m.DireTeamId != null)
            .Select(m => new { m.Id, m.StartTime, m.RadiantTeamId, m.DireTeamId, m.RadiantWin })
            .OrderBy(m => m.StartTime)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        // Một ván chỉ được chấm cho MỘT dự đoán. Không khoá lại thì hai bản ghi cùng cặp
        // (ghi ở hai thời điểm khác nhau) sẽ cùng ăn một ván và mẫu bị đếm đôi.
        var used = new HashSet<long>(
            await db.Predictions.Where(p => p.ResolvedMatchId != null)
                .Select(p => p.ResolvedMatchId!.Value).ToListAsync(ct));

        var resolved = 0;

        foreach (var p in open.OrderBy(x => x.CreatedAt))
        {
            var match = candidates.FirstOrDefault(m =>
                m.StartTime > p.CreatedAt
                && !used.Contains(m.Id)
                && ((m.RadiantTeamId == p.TeamAId && m.DireTeamId == p.TeamBId)
                    || (m.RadiantTeamId == p.TeamBId && m.DireTeamId == p.TeamAId)));

            if (match is null) continue;

            p.ResolvedMatchId = match.Id;
            p.TeamAWon = (match.RadiantTeamId == p.TeamAId) == match.RadiantWin;
            p.ResolvedAt = now;
            used.Add(match.Id);
            resolved++;
        }

        if (resolved > 0) logger.LogInformation("Chấm được {Count} dự đoán", resolved);
        return resolved;
    }
}
