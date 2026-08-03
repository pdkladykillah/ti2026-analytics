namespace Ti2026.Data.Entities;

public class Player
{
    public int Id { get; set; }
    public long? OpenDotaAccountId { get; set; }
    public required string Nick { get; set; }
    public string? RealName { get; set; }
    public string? CountryName { get; set; }
    public string? CountryCode { get; set; }
    public string? PhotoUrl { get; set; }
    public int? PhotoMediaAssetId { get; set; }
}
