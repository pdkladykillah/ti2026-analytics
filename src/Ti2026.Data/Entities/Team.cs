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

    /// <summary>
    /// team_id của OpenDota, phân giải bằng cách đối chiếu tên/tag qua TeamAlias.
    /// null = chưa phân giải được, và ingest sẽ bỏ qua đội này thay vì đoán.
    /// </summary>
    public int? OpenDotaTeamId { get; set; }
    public List<TeamAlias> Aliases { get; set; } = [];
}
