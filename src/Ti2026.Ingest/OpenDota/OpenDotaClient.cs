using System.Net.Http.Json;
using System.Text.Json;

namespace Ti2026.Ingest.OpenDota;

public class OpenDotaClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public Task<List<OpenDotaTeam>> GetTeamsAsync(CancellationToken ct) =>
        GetListAsync<OpenDotaTeam>("teams", ct);

    public Task<List<OpenDotaTeamMatch>> GetTeamMatchesAsync(int openDotaTeamId, CancellationToken ct) =>
        GetListAsync<OpenDotaTeamMatch>($"teams/{openDotaTeamId}/matches", ct);

    public Task<List<OpenDotaHero>> GetHeroesAsync(CancellationToken ct) =>
        GetListAsync<OpenDotaHero>("heroes", ct);

    public Task<List<OpenDotaHeroStat>> GetHeroStatsAsync(CancellationToken ct) =>
        GetListAsync<OpenDotaHeroStat>("heroStats", ct);

    /// <summary>Ván gần đây của một người chơi, mới nhất trước.</summary>
    public Task<List<OpenDotaPlayerMatch>> GetPlayerMatchesAsync(
        long accountId, int limit, CancellationToken ct) =>
        GetListAsync<OpenDotaPlayerMatch>("players/" + accountId + "/matches?limit=" + limit, ct);

    /// <summary>Roster của một đội theo OpenDota — dùng để nhận diện team_id lạ.</summary>
    public Task<List<OpenDotaTeamPlayer>> GetTeamPlayersAsync(int teamId, CancellationToken ct) =>
        GetListAsync<OpenDotaTeamPlayer>($"teams/{teamId}/players", ct);

    public Task<OpenDotaWinLoss?> GetPlayerWinLossAsync(long accountId, CancellationToken ct) =>
        GetOneAsync<OpenDotaWinLoss>($"players/{accountId}/wl", ct);

    /// <summary>
    /// Ván của một người, hỏi KÈM TÊN TRƯỜNG.
    ///
    /// Không có project= thì endpoint chỉ trả match_id, hero, thời gian, kết quả và K/D/A —
    /// thiếu GPM, last hit, sát thương, tức thiếu đúng phần nói lên cách chơi. Hỏi kèm thì được
    /// đủ ở 100% số ván, cùng một lời gọi, không tốn thêm gì.
    /// </summary>
    public Task<List<OpenDotaPlayerMatchRow>> GetPlayerMatchesProjectedAsync(
        long accountId, int limit, IEnumerable<string> fields, CancellationToken ct)
    {
        var project = string.Join("&", fields.Select(f => "project=" + Uri.EscapeDataString(f)));
        return GetListAsync<OpenDotaPlayerMatchRow>(
            $"players/{accountId}/matches?limit={limit}&{project}", ct);
    }

    private async Task<T?> GetOneAsync<T>(string path, CancellationToken ct)
    {
        using var res = await http.GetAsync(path, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    public Task<List<OpenDotaLeague>> GetLeaguesAsync(CancellationToken ct) =>
        GetListAsync<OpenDotaLeague>("leagues", ct);

    /// <summary>Mọi ván của một giải, kể cả ván không có đội nào trong 16 đội đang theo dõi.</summary>
    public Task<List<OpenDotaLeagueMatch>> GetLeagueMatchesAsync(long leagueId, CancellationToken ct) =>
        GetListAsync<OpenDotaLeagueMatch>($"leagues/{leagueId}/matches", ct);

    public async Task<OpenDotaMatchDetail> GetMatchAsync(long matchId, CancellationToken ct)
    {
        using var res = await http.GetAsync($"matches/{matchId}", ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<OpenDotaMatchDetail>(Json, ct)
               ?? throw new HttpRequestException($"matches/{matchId} trả về nội dung rỗng");
    }

    /// <summary>
    /// Trả danh sách rỗng khi nguồn trả mảng rỗng, và NÉM khi nguồn lỗi.
    ///
    /// Phân biệt này quan trọng: "nguồn nói không có gì" và "không gọi được nguồn" phải dẫn
    /// tới hai hành vi khác nhau ở tầng trên. Nuốt lỗi thành danh sách rỗng sẽ khiến sanity
    /// gate hiểu sai thành "nguồn đổi layout" và ghi nhầm nguyên nhân vào IngestRun.
    /// </summary>
    private async Task<List<T>> GetListAsync<T>(string path, CancellationToken ct)
    {
        using var res = await http.GetAsync(path, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<List<T>>(Json, ct) ?? [];
    }

    /// <summary>
    /// constants/items trả về đối tượng khoá theo tên item, không phải mảng — nên không dùng
    /// được <see cref="GetListAsync{T}"/>.
    /// </summary>
    /// <summary>
    /// Hồ sơ công khai của một người chơi. Ném <see cref="HttpRequestException"/> khi hồ sơ để
    /// riêng tư hoặc id không tồn tại — tầng trên phải phân biệt được hai chuyện đó với "gọi
    /// được nhưng người này chưa chơi hero nào".
    /// </summary>
    public Task<List<OpenDotaPlayerHero>> GetPlayerHeroesAsync(long accountId, CancellationToken ct) =>
        GetListAsync<OpenDotaPlayerHero>($"players/{accountId}/heroes", ct);

    public async Task<OpenDotaPlayerProfile?> GetPlayerAsync(long accountId, CancellationToken ct)
    {
        using var res = await http.GetAsync($"players/{accountId}", ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<OpenDotaPlayerProfile>(Json, ct);
    }

    public async Task<Dictionary<string, OpenDotaItem>> GetItemsAsync(CancellationToken ct)
    {
        using var res = await http.GetAsync("constants/items", ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<Dictionary<string, OpenDotaItem>>(Json, ct) ?? [];
    }
}
