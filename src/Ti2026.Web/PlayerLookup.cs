using System.Collections.Concurrent;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Web;

public record PlayerHeroStat(int HeroId, int Games, int Wins);

public record PlayerSnapshot(
    long AccountId, string? Name, string? Avatar, int? RankTier, List<PlayerHeroStat> Heroes);

/// <summary>
/// Tra hồ sơ công khai của một người chơi theo id họ TỰ NHẬP, có bộ nhớ đệm và có trần.
///
/// VÌ SAO PHẢI CÓ TRẦN: đây là endpoint công khai gọi ra nguồn ngoài. Không giới hạn thì một
/// người bấm liên tục — hoặc một con bot — sẽ khiến OpenDota chặn IP của VPS, và mất IP là mất
/// luôn nguồn dữ liệu cho TOÀN BỘ ứng dụng, không phải chỉ một lần tra cứu thất bại.
///
/// KHÔNG lưu xuống DB. Dữ liệu người chơi chỉ nằm trong bộ nhớ đệm có thời hạn: vừa là tôn
/// trọng người dùng, vừa là để không phải trả lời câu "ứng dụng này giữ gì của tôi".
/// </summary>
/// <remarks>
/// Là SINGLETON vì bộ đệm và trần phải dùng chung cho toàn ứng dụng — mỗi request một bộ đếm
/// thì trần chẳng chặn được gì. Nhưng cố tình KHÔNG giữ <see cref="OpenDotaClient"/> làm phụ
/// thuộc: typed client là transient, giữ nó trong singleton sẽ ghim một HttpMessageHandler
/// sống mãi và mất khả năng luân chuyển kết nối. Client được truyền theo từng lần gọi.
/// </remarks>
public class PlayerLookup(ILogger<PlayerLookup> logger)
{
    /// <summary>Hồ sơ gần như không đổi trong vài phút, và mỗi lần tra là hai request thật.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    /// <summary>Trần số lần tra MỚI trong một cửa sổ, tính trên toàn ứng dụng.</summary>
    public const int MaxLookupsPerWindow = 20;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>Steam ID64 = account_id + hằng số này. Người dùng thường dán ID64.</summary>
    public const long SteamId64Offset = 76561197960265728L;

    private readonly ConcurrentDictionary<long, (DateTimeOffset At, PlayerSnapshot Snapshot)> _cache = new();
    private readonly object _budgetGate = new();
    private DateTimeOffset _windowStart = DateTimeOffset.UtcNow;
    private int _usedInWindow;

    /// <summary>
    /// Nhận cả account_id (dạng 86745912) lẫn Steam ID64 (dạng 76561198047011640) và đưa về
    /// account_id. Không đoán nếu số không thuộc dải nào — trả null để tầng trên nói rõ.
    /// </summary>
    public static long? ParseAccountId(string? raw)
    {
        if (!long.TryParse(raw?.Trim(), out var value) || value <= 0) return null;

        if (value > SteamId64Offset) return value - SteamId64Offset;

        // account_id thật của Dota nằm dưới ngưỡng này rất xa; số lớn bất thường mà không phải
        // ID64 thì là gõ sai, và đoán bừa sẽ tra ra một người hoàn toàn khác.
        return value < 2_000_000_000 ? value : null;
    }

    public bool TryGetCached(long accountId, out PlayerSnapshot? snapshot)
    {
        snapshot = null;
        if (!_cache.TryGetValue(accountId, out var entry)) return false;
        if (DateTimeOffset.UtcNow - entry.At > CacheTtl)
        {
            _cache.TryRemove(accountId, out _);
            return false;
        }

        snapshot = entry.Snapshot;
        return true;
    }

    /// <summary>Còn suất tra mới trong cửa sổ hiện tại không.</summary>
    public bool TryTakeBudget()
    {
        lock (_budgetGate)
        {
            if (DateTimeOffset.UtcNow - _windowStart > Window)
            {
                _windowStart = DateTimeOffset.UtcNow;
                _usedInWindow = 0;
            }

            if (_usedInWindow >= MaxLookupsPerWindow) return false;

            _usedInWindow++;
            return true;
        }
    }

    /// <summary>
    /// Trả null khi hồ sơ để riêng tư, id không tồn tại, hoặc nguồn đang lỗi — ba trường hợp
    /// đó với người dùng là cùng một câu trả lời, nên không cần phân biệt ra ngoài.
    /// </summary>
    public async Task<PlayerSnapshot?> FetchAsync(
        OpenDotaClient client, long accountId, CancellationToken ct)
    {
        try
        {
            var profile = await client.GetPlayerAsync(accountId, ct);
            var heroes = await client.GetPlayerHeroesAsync(accountId, ct);

            var stats = heroes
                .Where(h => h.Games > 0)
                .Select(h => new PlayerHeroStat(
                    int.TryParse(h.HeroId, out var id) ? id : 0, h.Games, h.Win))
                .Where(h => h.HeroId > 0)
                .ToList();

            // OpenDota vẫn trả 200 với hồ sơ để riêng tư, chỉ là profile rỗng và không có hero
            // nào. Coi đó là "không tra được" chứ không phải "người này chơi 0 ván".
            if (stats.Count == 0 && profile?.Profile?.AccountId is null) return null;

            var snapshot = new PlayerSnapshot(
                accountId,
                profile?.Profile?.PersonaName,
                profile?.Profile?.AvatarFull,
                profile?.RankTier,
                stats);

            _cache[accountId] = (DateTimeOffset.UtcNow, snapshot);
            return snapshot;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                        or TimeoutException)
        {
            logger.LogInformation(ex, "Không tra được hồ sơ {AccountId}", accountId);
            return null;
        }
    }
}
