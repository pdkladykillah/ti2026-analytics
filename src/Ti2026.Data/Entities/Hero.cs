namespace Ti2026.Data.Entities;

public class Hero
{
    /// <summary>OpenDota hero id — không tự tăng.</summary>
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? LocalizedName { get; set; }
    public string? ImageUrl { get; set; }
    public int? ImageMediaAssetId { get; set; }
}
