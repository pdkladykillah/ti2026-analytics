using System.Text.Json.Serialization;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// GET /heroStats. Các khoá bậc rank có dạng "1_pick".."8_pick" — tên bắt đầu bằng chữ số nên
/// không đặt được thành property C#, phải đọc qua JsonExtensionData.
/// </summary>
public class OpenDotaHeroStat
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("localized_name")] public string? LocalizedName { get; set; }

    [JsonPropertyName("pro_pick")] public int? ProPick { get; set; }
    [JsonPropertyName("pro_win")] public int? ProWin { get; set; }
    [JsonPropertyName("pro_ban")] public int? ProBan { get; set; }

    [JsonPropertyName("pub_pick")] public long? PubPick { get; set; }
    [JsonPropertyName("pub_win")] public long? PubWin { get; set; }

    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }

    /// <summary>Tổng của các bậc chỉ định, ví dụ 7 và 8 cho Divine + Immortal.</summary>
    public long SumBrackets(string suffix, params int[] brackets)
    {
        if (Extra is null) return 0;

        long total = 0;
        foreach (var b in brackets)
        {
            if (Extra.TryGetValue($"{b}_{suffix}", out var v)
                && v.ValueKind == System.Text.Json.JsonValueKind.Number)
                total += v.GetInt64();
        }

        return total;
    }
}
