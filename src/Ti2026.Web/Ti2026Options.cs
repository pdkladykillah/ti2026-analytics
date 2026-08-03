namespace Ti2026.Web;

public class Ti2026Options
{
    public const string SectionName = "Ti2026";

    /// <summary>
    /// Path prefix khi chạy sau reverse proxy, ví dụ "/ti2026".
    /// Caddy dùng `handle` (không cắt prefix) nên app phải tự khai qua UsePathBase.
    /// </summary>
    public string PathBase { get; set; } = "";

    public int IngestIntervalHours { get; set; } = 6;

    /// <summary>Bắt buộc ở Production — bảo vệ POST api/ingest/run.</summary>
    public string? IngestToken { get; set; }

    /// <summary>Nơi chứa SQLite + ảnh cache. Là volume trong Docker.</summary>
    public string DataDirectory { get; set; } = "App_Data";

    /// <summary>Nơi chứa 6 file JSON biên tập dùng làm nguồn seed.</summary>
    public string EditorialDirectory { get; set; } = "data";

    public OpenDotaOptions OpenDota { get; set; } = new();
    public DltvOptions Dltv { get; set; } = new();
    public SanityGateOptions SanityGate { get; set; } = new();
}

public class OpenDotaOptions
{
    /// <summary>Chủ động giới hạn, không đợi bị 429 rồi mới xử lý.</summary>
    public double RequestsPerSecond { get; set; } = 1;

    public string BaseUrl { get; set; } = "https://api.opendota.com/api/";
}

public class DltvOptions
{
    /// <summary>Tắt nhanh phần scrape nếu robots.txt hoặc ToS không cho phép.</summary>
    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://dltv.org/";

    public string UserAgent { get; set; } =
        "ti2026-analytics/1.0 (+https://github.com/pdkladykillah/ti2026-analytics)";
}

public class SanityGateOptions
{
    public int MinTeams { get; set; } = 16;
    public int MinPlayers { get; set; } = 60;
}
