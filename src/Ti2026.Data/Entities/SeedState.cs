namespace Ti2026.Data.Entities;

/// <summary>
/// Hash của file JSON biên tập đã seed, để chỉ nạp lại khi file thật sự đổi.
/// </summary>
public class SeedState
{
    public int Id { get; set; }

    /// <summary>Tên file, ví dụ "teams.json".</summary>
    public required string Key { get; set; }

    /// <summary>SHA-256 dạng hex.</summary>
    public required string Hash { get; set; }

    public DateTimeOffset AppliedAt { get; set; }
}
