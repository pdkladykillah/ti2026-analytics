using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Tính và lưu điểm nhấn của từng ngày thi đấu.
///
/// CHẠY CHUNG NHỊP VỚI BỘ LÀM TƯƠI BẢNG ĐẤU, không có lịch riêng: nó cần đúng thứ bộ kia vừa
/// lấy về — trạng thái từng series — và thêm một bộ hẹn giờ nữa chỉ để làm cùng một việc muộn hơn
/// vài phút là thêm một thứ phải nhớ.
///
/// KHÔNG BAO GIỜ TÍNH LẠI NGÀY ĐÃ CHỐT. Một lần nạp bù dữ liệu cũ sẽ lặng lẽ viết lại lịch sử —
/// và lịch sử ở đây là toàn bộ lý do bảng này tồn tại.
/// </summary>
public class DailyDigestWriter(Ti2026DbContext db, ILogger<DailyDigestWriter> logger)
{
    /// <summary>Cửa sổ winrate dùng để nhận ra kết quả ngược kèo. 180 ngày là cửa sổ chuẩn của trang.</summary>
    public const int WinrateWindowDays = 180;

    /// <summary>
    /// camelCase để payload giống mọi endpoint khác của trang — giao diện không phải nhớ rằng
    /// riêng chỗ này khoá viết hoa. Và đọc thì không phân biệt hoa thường, để bản ghi cũ viết
    /// bằng quy ước trước vẫn đọc được thay vì lặng lẽ ra danh sách rỗng.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Trả về số ngày đã ghi hoặc cập nhật.</summary>
    public async Task<int> WriteAsync(CancellationToken ct)
    {
        var rows = await db.ScheduledSeries
            .Include(s => s.Team1)
            .Include(s => s.Team2)
            .ToListAsync(ct);

        if (rows.Count == 0) return 0;

        var leagueId = rows[0].LeagueId;

        // NGÀY LẤY THEO GIỜ ĐÁ THẬT khi đã đánh, chỉ dùng giờ xếp lịch khi chưa.
        //
        // Không phải chi tiết vụn: hôm nay các trận trượt một tiếng so với giờ Valve công bố, và
        // một series xếp 23:30 mà đánh lúc 00:20 thuộc về ngày HÔM SAU. Lấy theo giờ xếp lịch thì
        // digest của hai ngày cùng sai, mà không có gì báo.
        var byDay = rows
            .Select(s => new { S = s, At = s.ActualAt ?? s.ScheduledAt })
            .Where(x => x.At is not null)
            .GroupBy(x => DateOnly.FromDateTime(x.At!.Value))
            .ToList();

        if (byDay.Count == 0) return 0;

        var existing = await db.DailyDigests
            .Where(d => d.LeagueId == leagueId)
            .ToDictionaryAsync(d => d.Day, ct);

        var winrates = await WinratesAsync(ct);
        var heroNames = await db.Heroes
            .ToDictionaryAsync(h => h.Id, h => h.LocalizedName ?? h.Name, ct);

        var touched = 0;

        foreach (var group in byDay.OrderBy(g => g.Key))
        {
            var day = group.Key;

            if (existing.TryGetValue(day, out var row) && row.ClosedAt is not null) continue;

            var series = group.Select(x => ToSeries(x.S, winrates)).ToList();
            var (matches, drafts) = await DayDataAsync(leagueId, day, ct);

            var yesterday = BannedOn(existing, day.AddDays(-1));
            var highlights = DayHighlights.Build(series, matches, drafts, heroNames, yesterday);

            var completed = series.Count(s => s.Completed);
            var closed = completed == series.Count && series.Count > 0;

            row ??= new DailyDigest { LeagueId = leagueId, Day = day };
            if (row.Id == 0) db.DailyDigests.Add(row);

            row.StageName = group.Select(x => x.S.GroupName).Distinct().Count() == 1
                ? group.First().S.GroupName
                : null;
            row.SeriesTotal = series.Count;
            row.SeriesCompleted = completed;
            row.MatchesCounted = matches.Count;
            // Mọi series, không chỉ series đã xong — series đang đánh dở vẫn có ván đã kết thúc. Xem
            // ghi chú ở DayHighlights.Build: cộng riêng series đã xong cho ra "đọc được 9/7 ván".
            row.MatchesExpected = series.Sum(s => s.Wins1 + s.Wins2);
            row.MedianDurationSeconds = DayHighlights.MedianDuration(matches);
            row.ComputedAt = DateTime.UtcNow;
            row.Payload = JsonSerializer.Serialize(highlights, Json);

            // Chốt ngày ở đây, và chỉ một lần. Từ lúc này nó là lịch sử.
            if (closed && row.ClosedAt is null) row.ClosedAt = DateTime.UtcNow;

            existing[day] = row;
            touched++;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Điểm nhấn ngày: cập nhật {Count} ngày", touched);
        return touched;
    }

    private static DaySeries ToSeries(ScheduledSeries s, IReadOnlyDictionary<int, double> winrates) => new(
        s.NodeId, s.Name, s.GroupName,
        s.Team1?.Name, s.Team2?.Name, s.Team1?.Slug, s.Team2?.Slug,
        s.Wins1, s.Wins2, s.IsCompleted, s.ActualAt ?? s.ScheduledAt,
        s.TeamId1 is int a && winrates.TryGetValue(a, out var wa) ? wa : null,
        s.TeamId2 is int b && winrates.TryGetValue(b, out var wb) ? wb : null);

    /// <summary>
    /// Winrate mới nhất trong cửa sổ 180 ngày, theo đội.
    ///
    /// Lọc theo <c>Maps</c> chứ không theo <c>Winrate != null</c>: cột đó không nhận null, nên
    /// một đội chưa đánh ván nào vẫn có winrate 0,0 — và 0,0 đọc vào phép so "ngược kèo" sẽ biến
    /// mọi trận của đội đó thành bất ngờ lớn nhất trong ngày.
    /// </summary>
    private async Task<Dictionary<int, double>> WinratesAsync(CancellationToken ct)
    {
        var snaps = await db.TeamStatSnapshots
            .Where(s => s.WindowDays == WinrateWindowDays && s.Maps >= MinMapsForWinrate)
            .ToListAsync(ct);

        return snaps
            .GroupBy(s => s.TeamId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CapturedOn).First().Winrate);
    }

    /// <summary>Dưới ngần này ván thì winrate chưa nói được gì, đừng đem đi so.</summary>
    public const int MinMapsForWinrate = 10;

    private async Task<(List<DayMatch> Matches, List<DayDraft> Drafts)> DayDataAsync(
        long leagueId, DateOnly day, CancellationToken ct)
    {
        var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = from.AddDays(1);

        var matches = await db.Matches
            .Where(m => m.LeagueId == leagueId && m.StartTime >= from && m.StartTime < to)
            .Select(m => new
            {
                m.Id, m.DurationSeconds, m.RadiantWin,
                Rad = m.RadiantTeamId, Dire = m.DireTeamId,
            })
            .ToListAsync(ct);

        if (matches.Count == 0) return ([], []);

        var names = await db.Teams.ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var ids = matches.Select(m => m.Id).ToList();

        var drafts = await db.DraftEvents
            .Where(d => ids.Contains(d.MatchId))
            .Select(d => new { d.MatchId, d.HeroId, d.IsPick, d.IsRadiant })
            .ToListAsync(ct);

        var radiantWon = matches.ToDictionary(m => m.Id, m => m.RadiantWin);

        return (
            matches.Select(m => new DayMatch(
                m.Id, m.DurationSeconds,
                m.Rad is int r ? names.GetValueOrDefault(r) : null,
                m.Dire is int d ? names.GetValueOrDefault(d) : null,
                m.RadiantWin)).ToList(),

            drafts.Select(d => new DayDraft(
                d.MatchId, d.HeroId, d.IsPick,
                radiantWon.GetValueOrDefault(d.MatchId) == d.IsRadiant)).ToList());
    }

    /// <summary>Nhóm hero bị cấm nhiều của một ngày đã lưu, đọc lại từ payload.</summary>
    private static HashSet<int>? BannedOn(IReadOnlyDictionary<DateOnly, DailyDigest> all, DateOnly day)
    {
        if (!all.TryGetValue(day, out var row)) return null;

        try
        {
            var items = JsonSerializer.Deserialize<List<Highlight>>(row.Payload, Json) ?? [];

            return items
                .Where(h => h.Kind == "cam-nhieu" && h.Data is JsonElement)
                .Select(h => ((JsonElement)h.Data!).GetProperty("heroId").GetInt32())
                .ToHashSet();
        }
        catch
        {
            // Payload là JSON tự do nên hình dạng có thể đổi giữa các bản. Không đọc được thì
            // coi như chưa có ngày trước — mất một dòng so sánh, không được phép làm hỏng cả digest.
            return null;
        }
    }
}
