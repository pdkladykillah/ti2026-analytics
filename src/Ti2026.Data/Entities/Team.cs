namespace Ti2026.Data.Entities;

public class Team
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public string? ShortName { get; set; }
    public string? Region { get; set; }
    public string? Qualification { get; set; }
    public string? LogoUrl { get; set; }
    public int? LogoMediaAssetId { get; set; }
    public List<TeamAlias> Aliases { get; set; } = [];
}
