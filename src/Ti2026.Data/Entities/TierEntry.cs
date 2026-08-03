namespace Ti2026.Data.Entities;

/// <summary>
/// Tier list — dữ liệu biên tập, không scrape được. Seed từ tiers.json trong git.
/// </summary>
public class TierEntry
{
    public int Id { get; set; }
    public required string Patch { get; set; }
    public int HeroId { get; set; }
    public Hero? Hero { get; set; }

    /// <summary>S|A|B|C|SPEC|SIT</summary>
    public required string Tier { get; set; }

    public string? Note { get; set; }

    /// <summary>Vị trí 1..5; null = áp cho mọi vị trí.</summary>
    public int? Position { get; set; }
}
