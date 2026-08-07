using System.Net.Http.Json;
using System.Text.Json;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// API công khai của Valve trên www.dota2.com — nguồn CHÍNH CHỦ cho bảng đấu và lịch thi đấu.
///
/// Vì sao cần nguồn này bên cạnh OpenDota: OpenDota chỉ có trận ĐÃ ĐÁ, không có endpoint nào
/// cho trận sắp diễn ra, và giải chính TI2026 còn chưa xuất hiện trong danh mục giải của họ.
/// Valve thì công bố sẵn khung bảng đấu — Swiss, Elimination Round, Playoff — kèm giờ và kết
/// quả từng nút, cập nhật theo thời gian thực trong lúc giải diễn ra.
///
/// KHÔNG dùng chung HttpClient với OpenDotaClient: khác host, khác hạn mức, và nhịp 0,8 req/s
/// đặt ra để tôn trọng hạn mức OpenDota thì không có lý gì áp cho máy chủ của Valve.
/// </summary>
public class Dota2WebClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Toàn bộ danh mục giải. Valve BỎ QUA tham số phân trang và trả về hết trong một lần —
    /// gần 10.000 mục, khoảng vài MB. Đó là lý do chỉ gọi nó khi cần tìm lại giải, không gọi
    /// ở mỗi vòng ingest.
    /// </summary>
    public async Task<List<Dota2LeagueInfo>> GetLeagueInfoListAsync(CancellationToken ct)
    {
        using var res = await http.GetAsync(
            "webapi/IDOTA2League/GetLeagueInfoList/v001?start_at_league_id=0&itemsperpage=100", ct);
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadFromJsonAsync<Dota2LeagueInfoList>(Json, ct);
        return body?.Infos ?? [];
    }

    public async Task<Dota2LeagueData?> GetLeagueDataAsync(long leagueId, CancellationToken ct)
    {
        using var res = await http.GetAsync(
            $"webapi/IDOTA2League/GetLeagueData/v001?league_id={leagueId}", ct);
        res.EnsureSuccessStatusCode();

        return await res.Content.ReadFromJsonAsync<Dota2LeagueData>(Json, ct);
    }

    /// <summary>Duyệt phẳng cây nhóm nút, trả về (tên nhóm, nút).</summary>
    public static IEnumerable<(string? Group, Dota2Node Node)> Flatten(
        IEnumerable<Dota2NodeGroup>? groups, string? inherited = null)
    {
        foreach (var g in groups ?? [])
        {
            // Nhóm gốc có name rỗng và không chứa nút nào; tên thật nằm ở nhóm con. Giữ tên
            // gần nhất KHÁC RỖNG để nút nào cũng biết mình thuộc vòng nào.
            var name = string.IsNullOrWhiteSpace(g.Name) ? inherited : g.Name;

            foreach (var n in g.Nodes ?? []) yield return (name, n);
            foreach (var (sub, node) in Flatten(g.NodeGroups, name)) yield return (sub, node);
        }
    }
}
