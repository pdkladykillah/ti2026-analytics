namespace Ti2026.Web;

public class Ti2026Options
{
    public const string SectionName = "Ti2026";

    /// <summary>
    /// Path prefix khi chạy sau reverse proxy, ví dụ "/ti2026".
    /// Caddy dùng `handle` (không cắt prefix) nên app phải tự khai qua UsePathBase.
    /// </summary>
    public string PathBase { get; set; } = "";

    /// <summary>
    /// PathBase đã chuẩn hoá: có dấu '/' ở đầu, không có '/' ở cuối, rỗng nếu không cấu hình.
    ///
    /// Cần thiết vì UsePathBase ném ArgumentException lúc startup nếu giá trị không bắt đầu
    /// bằng '/'. Gõ "ti2026" thay vì "/ti2026" trong docker-compose là lỗi rất dễ mắc, và
    /// hậu quả là container crash-loop với thông báo không nói gì về nguyên nhân.
    /// </summary>
    public string NormalizedPathBase()
    {
        var value = PathBase?.Trim() ?? "";
        if (value.Length == 0 || value == "/") return "";

        if (!value.StartsWith('/')) value = "/" + value;
        return value.TrimEnd('/');
    }

    public int IngestIntervalHours { get; set; } = 6;

    /// <summary>
    /// Bật/tắt scheduler chạy nền. Tắt thì vẫn chạy tay được qua POST api/ingest/run.
    /// Mặc định false để test và môi trường dev không tự gọi ra mạng ngoài.
    /// </summary>
    public bool IngestEnabled { get; set; }

    /// <summary>
    /// Trần số ván nạp match detail mỗi vòng. Mỗi ván một request; ở 0,8 req/giây thì 300 ván
    /// mất khoảng 6 phút.
    ///
    /// Trần này bị chặn trên bởi HẠN MỨC NGÀY của OpenDota là 2000 request. Scheduler chạy 4
    /// vòng mỗi ngày, nên 300 × 4 = 1200 request cho riêng match detail, cộng khoảng 100 cho
    /// đội/giải/hero/tuyển thủ là ~1300 — còn dư an toàn. Đặt 500 sẽ thành 2000+ và vòng cuối
    /// trong ngày luôn chết vì 429.
    /// </summary>
    public int MaxMatchDetailsPerRun { get; set; } = 300;

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
    /// <summary>
    /// Chủ động giới hạn, không đợi bị 429 rồi mới xử lý.
    ///
    /// 0.8 chứ không phải 1: hạn mức miễn phí của OpenDota là 60 request/phút, và 1 req/s
    /// bằng ĐÚNG 60/phút — ngồi ngay trên vạch nên thỉnh thoảng vẫn ăn 429 tuỳ cách nguồn
    /// tính cửa sổ thời gian. Đã gặp thật khi nạp bù: chạy êm hơn trăm ván rồi 429.
    /// 0.8 tương đương 48/phút, đủ dưới vạch, và một lượt nạp bù 1790 ván chỉ dài thêm 7 phút.
    /// </summary>
    public double RequestsPerSecond { get; set; } = 0.8;

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
