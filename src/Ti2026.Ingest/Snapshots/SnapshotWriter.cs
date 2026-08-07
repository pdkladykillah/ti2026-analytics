using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Ingest.Snapshots;

/// <summary>
/// Tính lại chỉ số theo 3 cửa sổ và upsert vào TeamStatSnapshot.
///
/// Snapshot của các NGÀY khác nhau không bao giờ ghi đè nhau — đó là kho lịch sử, và lịch sử
/// là thứ không lấy lại được. Trong cùng một ngày thì upsert, nên chạy lại bao nhiêu lần
/// cũng an toàn.
///
/// Ngữ nghĩa merge: chỉ ghi đè một chỉ số khi giá trị mới THỰC SỰ biết. Giá trị biên tập đã
/// seed cho 5 chỉ số mà OpenDota không cung cấp sẽ được giữ nguyên, và hàng đó đánh
/// Source = "mixed" để không ai nhầm số biên tập là số đo.
/// </summary>
public class SnapshotWriter(Ti2026DbContext db)
{
    public static readonly int[] Windows = [30, 90, 180];

    /// <summary>
    /// Dưới ngần này ván với đội hình hiện tại thì KHÔNG công bố Elo, trả null.
    ///
    /// Elo khởi điểm ở 1500 và cần một số ván nhất định mới tách khỏi mốc đó. Công bố rating
    /// của một đội mới đá 8 ván là công bố con số mặc định khoác áo số đo — và tệ hơn nữa,
    /// 1500 nằm giữa bảng nên đội đó trông như "trung bình" chứ không phải "chưa biết".
    /// Trả null rồi để trang nói thẳng "chưa đủ ván" là câu trả lời thật.
    /// </summary>
    public const int MinGamesForRating = 10;

    public async Task<int> WriteAsync(DateOnly capturedOn, CancellationToken ct)
    {
        var teams = await db.Teams.ToListAsync(ct);
        if (teams.Count == 0) return 0;

        // Số người của đội hình HIỆN TẠI có mặt trong từng ván. Cả form lẫn Elo đều lọc theo
        // con số này, vì thành tích của đội hình cũ không nói gì về đội sắp ra sân ở TI2026 —
        // 12/16 đội mãi tới năm 2026 mới lần đầu đủ mặt.
        var lineups = await LineupLookup.LoadAsync(db, ct: ct);

        // Elo tính trên TOÀN BỘ lịch sử, không giới hạn theo cửa sổ: rating là thứ tích luỹ,
        // cắt cửa sổ sẽ vứt đi chính phần thông tin làm nó có ý nghĩa.
        var elo = await ComputeEloAsync(teams, lineups, capturedOn, ct);

        var written = 0;

        foreach (var window in Windows)
        {
            var since = capturedOn.AddDays(-window).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var until = capturedOn.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            var matches = await db.Matches
                .Where(m => m.StartTime >= since && m.StartTime < until
                            && m.RadiantTeamId != null && m.DireTeamId != null)
                .Include(m => m.Players)
                .ToListAsync(ct);

            if (matches.Count == 0) continue;

            foreach (var team in teams)
            {
                // Chỉ lấy ván mà CHÍNH ĐỘI NÀY ra sân đủ 5 người của đội hình hiện tại.
                //
                // Ở đây chỉ cần một bên, khác với Elo. Form là chỉ số MÔ TẢ chính đội đó chơi
                // thế nào, nên đối thủ là đội hình nào không đổi việc đây đúng là đội hình hôm
                // nay đang chơi. Elo thì ngược lại — nó là số TƯƠNG ĐỐI, cập nhật rating của X
                // dựa trên rating hiện tại của Y trong khi Y lúc ấy không phải Y là sai phép
                // tính, nên Elo bắt buộc cả hai bên.
                var outcomes = matches
                    .Where(m => m.RadiantTeamId == team.Id || m.DireTeamId == team.Id)
                    .Where(m => lineups.Kept(m.Id, team.Id, m.RadiantTeamId == team.Id) >= 5)
                    .Select(m => ToOutcome(m, team.Id))
                    .ToList();

                // Đội không có ván nào trong cửa sổ: BỎ QUA, không ghi hàng rỗng. Ghi hàng
                // toàn 0 sẽ xoá mất giá trị biên tập đang phục vụ được và làm trang tệ đi.
                if (outcomes.Count == 0) continue;

                var stats = StatCalculator.Compute(outcomes);

                var existing = await db.TeamStatSnapshots.FirstOrDefaultAsync(
                    s => s.TeamId == team.Id
                         && s.CapturedOn == capturedOn
                         && s.WindowDays == window, ct);

                if (existing is null)
                {
                    existing = new TeamStatSnapshot
                    {
                        TeamId = team.Id,
                        CapturedOn = capturedOn,
                        WindowDays = window,
                        Source = "opendota",
                    };
                    db.TeamStatSnapshots.Add(existing);
                }

                Merge(existing, stats);

                var rated = elo.TryGetValue(team.Id, out var r) ? r : default;
                existing.EloGames = rated.Games;

                // Ngưỡng chứ không phải > 0: xem ghi chú ở MinGamesForRating.
                existing.Elo = rated.Games >= MinGamesForRating ? rated.Elo : null;
                written++;
            }
        }

        await db.SaveChangesAsync(ct);
        return written;
    }

    /// <summary>
    /// Elo từ toàn bộ trận đã biết kết quả, xếp theo thứ tự thời gian.
    /// Tính lại từ đầu mỗi vòng thay vì cập nhật tăng dần: rẻ ở quy mô này (vài nghìn trận)
    /// và loại bỏ hẳn khả năng trôi số khi có trận được nạp bổ sung vào quá khứ.
    /// </summary>
    private async Task<Dictionary<int, TeamRating>> ComputeEloAsync(
        List<Team> teams, LineupLookup lineups, DateOnly asOf, CancellationToken ct)
    {
        // Chỉ tính trận ở giải chuyên nghiệp trở lên. Hôm nay dữ liệu 100% là tier
        // "professional" nên bộ lọc này KHÔNG đổi con số nào — đó chính là bằng chứng nó
        // đúng. Nó tồn tại cho lúc vòng loại TI hoặc giải hạng thấp bắt đầu lọt vào, khi mà
        // rating sẽ lệch mà không có gì báo.
        var ratedLeagues = await db.Leagues
            .Where(l => l.Tier != null && League.RatedTiers.Contains(l.Tier))
            .Select(l => l.Id)
            .ToListAsync(ct);

        var known = await db.Leagues.AnyAsync(ct);

        // Không lấy ván sau ngày chụp: hàng snapshot của ngày 03/08 phải mang Elo CỦA ngày đó.
        // Thiếu chặn này thì mỗi lần nạp lại, mọi hàng lịch sử đều nhận Elo của hôm nay và
        // biểu đồ phong độ biến thành một đường phẳng của hiện tại.
        var until = asOf.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await db.Matches
            .Where(m => m.RadiantTeamId != null && m.DireTeamId != null && m.StartTime < until)
            // Chưa nạp được bảng League thì KHÔNG lọc, vì lọc theo danh sách rỗng sẽ vứt
            // sạch mọi trận và Elo về 1500 hết — im lặng và sai.
            .Where(m => !known || (m.LeagueId != null && ratedLeagues.Contains(m.LeagueId.Value)))
            .Select(m => new
            {
                m.Id, m.StartTime, m.RadiantTeamId, m.DireTeamId, m.RadiantWin, m.PatchVersion,
            })
            .ToListAsync(ct);

        // CẢ HAI bên phải là đội hình TI2026.
        //
        // Elo là số TƯƠNG ĐỐI: cập nhật rating của X bằng rating hiện tại của Y. Nếu Y lúc ấy
        // là một đội khác mang cùng tên thì phép tính lấy sức mạnh của đội Y hôm nay để chấm
        // một trận mà đội Y hôm nay không hề đá — sai số bơm thẳng vào rating của X mà không
        // có gì báo. Đây là chỗ duy nhất trong hệ bắt buộc cả hai bên.
        var rated = rows
            .Where(r => lineups.Kept(r.Id, r.RadiantTeamId!.Value, true) >= 5
                        && lineups.Kept(r.Id, r.DireTeamId!.Value, false) >= 5)
            .Select(r => new RatedMatch(
                r.StartTime,
                WinnerTeamId: r.RadiantWin ? r.RadiantTeamId!.Value : r.DireTeamId!.Value,
                LoserTeamId: r.RadiantWin ? r.DireTeamId!.Value : r.RadiantTeamId!.Value,
                Patch: PatchIndex.Parse(r.PatchVersion)));

        return EloEngine.Compute(rated, teams.Select(t => t.Id));
    }

    private static MatchOutcome ToOutcome(Match m, int teamId)
    {
        var isRadiant = m.RadiantTeamId == teamId;

        return new MatchOutcome(
            Date: DateOnly.FromDateTime(m.StartTime),
            Won: isRadiant ? m.RadiantWin : !m.RadiantWin,
            Kills: isRadiant ? m.RadiantScore : m.DireScore,
            Deaths: isRadiant ? m.DireScore : m.RadiantScore,

            // Ba giá trị dưới đây chỉ có sau khi MatchDetailIngester nạp matches/{id}.
            // Ván chưa có detail thì giữ null — KHÔNG quy về 0/false.
            Assists: TeamAssists(m, isRadiant),
            HadFirstBlood: Flip(m.RadiantHadFirstBlood, isRadiant),
            ReachedTenFirst: Flip(m.RadiantReachedTenFirst, isRadiant),

            DurationSeconds: m.DurationSeconds);
    }

    /// <summary>Đổi góc nhìn Radiant sang góc nhìn đội đang xét, giữ null là null.</summary>
    private static bool? Flip(bool? radiantValue, bool isRadiant) =>
        radiantValue is null ? null : isRadiant ? radiantValue : !radiantValue;

    /// <summary>
    /// Tổng assists của 5 người thuộc phe đang xét.
    ///
    /// Trả null khi ván chưa nạp detail. Cũng trả null khi phe đó không đủ 5 người trong dữ
    /// liệu: tổng của 3 người rồi đem so với tổng của 5 người ở ván khác là số liệu sai mà
    /// nhìn vẫn hợp lý.
    /// </summary>
    private static int? TeamAssists(Match m, bool isRadiant)
    {
        if (m.DetailsIngestedAt is null || m.Players.Count == 0) return null;

        var side = m.Players.Where(p => p.IsRadiant == isRadiant).ToList();
        return side.Count == 5 ? side.Sum(p => p.Assists) : null;
    }

    /// <summary>
    /// Ghi các chỉ số tính được; với chỉ số chưa biết thì GIỮ giá trị đang có thay vì ghi null.
    /// Nếu có giữ lại giá trị cũ nào thì hàng được đánh "mixed".
    /// </summary>
    private static void Merge(TeamStatSnapshot s, TeamWindowStats st)
    {
        s.Maps = st.Maps;
        s.Wins = st.Wins;
        s.Losses = st.Losses;
        s.Winrate = st.Winrate;
        s.AvgKills = st.AvgKills;
        s.AvgDeaths = st.AvgDeaths;
        s.KillDiff = st.KillDiff;
        s.TotalKills = st.TotalKills;
        s.AvgDurationMinutes = st.AvgDurationMinutes;

        var keptAny = false;

        s.AvgAssists = Take(st.AvgAssists, s.AvgAssists, ref keptAny);
        s.FirstBloodRate = Take(st.FirstBloodRate, s.FirstBloodRate, ref keptAny);
        s.F10Rate = Take(st.F10Rate, s.F10Rate, ref keptAny);
        s.WinWhenFbRate = Take(st.WinWhenFbRate, s.WinWhenFbRate, ref keptAny);
        s.WinWhenF10Rate = Take(st.WinWhenF10Rate, s.WinWhenF10Rate, ref keptAny);

        s.Source = keptAny ? "mixed" : "opendota";
    }

    private static double? Take(double? fresh, double? existing, ref bool keptAny)
    {
        if (fresh.HasValue) return fresh;
        if (existing.HasValue) keptAny = true;
        return existing;
    }
}
