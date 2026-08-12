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

    /// <summary>
    /// Trần mỗi vòng khi CÓ API key. Cao hơn nhiều vì trần ngày biến mất và nhịp nhanh gấp
    /// mười: 2000 ván ở 8 req/giây mất khoảng bốn phút — vẫn NGẮN HƠN một vòng 300 ván ở nhịp
    /// miễn phí, nên transaction không hề mở lâu hơn hiện nay.
    /// </summary>
    public int MaxMatchDetailsPerRunWithKey { get; set; } = 2000;

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

    /// <summary>
    /// Nhịp khi CÓ API key. Bậc trả tiền cho 3000 request/phút (tức 50/giây) và không giới
    /// hạn tổng, nhưng ta chỉ dùng 8/giây — bằng 1/6 mức cho phép.
    ///
    /// Không chạy sát trần vì không có lý do: 8/giây đã nạp xong 1798 ván trong khoảng bốn
    /// phút, và chỗ dư đó là biên an toàn cho những lúc nguồn chậm. Ép lên 50/giây chỉ để
    /// tiết kiệm ba phút là đánh đổi tồi.
    /// </summary>
    public double RequestsPerSecondWithKey { get; set; } = 8;

    /// <summary>
    /// API key của OpenDota. Gỡ trần 3000 request/ngày và nâng nhịp lên 3000/phút.
    ///
    /// KHÔNG BAO GIỜ đặt giá trị thật vào đây hay vào appsettings trong git — nạp qua biến
    /// môi trường <c>Ti2026__OpenDota__ApiKey</c>, giống Ti2026__IngestToken.
    ///
    /// Gửi bằng header Authorization chứ không phải query param: query param sẽ nằm lại trong
    /// log truy cập, trong thông báo lỗi và trong mọi chuỗi URL bị in ra.
    /// </summary>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.opendota.com/api/";

    /// <summary>
    /// Giá mỗi request ở bậc trả tiền của OpenDota: $0,01 cho 100 lần gọi.
    /// Chỉ dùng để ƯỚC TÍNH và hiển thị — không phải hoá đơn thật.
    /// </summary>
    public double UsdPerCall { get; set; } = 0.0001;

    /// <summary>
    /// Trên mức này thì một đợt nạp phải được người vận hành duyệt trước.
    ///
    /// Con số là quyết định của chủ dự án, không phải kết quả đo: vận hành thường ngày chỉ
    /// khoảng 250 request (~2,5 xu) nên xin duyệt từng lần chỉ làm chậm việc, còn nâng schema
    /// thì nạp lại toàn bộ và mới là chỗ cần dừng lại hỏi.
    ///
    /// Để ở đây thay vì chỉ nằm trong đầu người làm: một ngưỡng không hiện ra ở đâu là một
    /// ngưỡng sẽ bị quên đúng lúc nó quan trọng nhất.
    /// </summary>
    public double ApprovalThresholdUsd { get; set; } = 1.0;

    /// <summary>
    /// Có DÙNG khoá hay không — tách khỏi việc CÓ khoá hay không.
    ///
    /// Trạng thái thường của trang tốn khoảng 355 lời gọi mỗi ngày (đo được: 36 mỗi vòng ingest
    /// × 4 vòng, cộng 160 cho pro-pub và 51 cho idol), tức chỉ 18% của bậc miễn phí 2.000/ngày.
    /// Trả tiền cho lượng đó là trả tiền cho thứ vốn miễn phí.
    ///
    /// Nhưng khoá vẫn phải nằm sẵn trong .env, vì lúc NẠP BÙ thì nó đáng: không khoá thì trần
    /// mỗi vòng tụt từ 2.000 xuống 300 ván và nhịp chậm gấp mười, nên một đợt 1.200 ván mất hơn
    /// 25 phút và chiếm nửa hạn mức ngày thay vì 4 phút. Bật lại bằng đúng một biến môi trường
    /// rồi dựng lại container.
    ///
    /// Đặt mặc định true để không đổi hành vi của bất kỳ nơi triển khai nào đang chạy — chỗ nào
    /// muốn tắt thì khai rõ trong docker-compose, và khai rõ như vậy thì đọc file là thấy.
    /// </summary>
    public bool UseApiKey { get; set; } = true;

    /// <summary>
    /// MỘT quyết định, ba hệ quả. Cả header xác thực, nhịp gọi, lẫn trần ván mỗi vòng đều rẽ
    /// nhánh theo đúng thuộc tính này.
    ///
    /// Đây là chỗ NGUY HIỂM nhất của cả lớp: nếu ba thứ đó tách ra tự quyết, sẽ có lúc ta gửi
    /// request không kèm khoá nhưng vẫn chạy 8 req/giây — tức gấp mười lần bậc miễn phí cho
    /// phép — và hậu quả không phải một vòng ingest hỏng mà là VPS bị chặn IP, mất luôn nguồn
    /// dữ liệu. Vì thế mọi nơi phải hỏi thuộc tính này, không ai được tự kiểm ApiKey.
    /// </summary>
    public bool HasKey => UseApiKey && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Nhịp thực tế: có key thì nhanh, không thì giữ mức tôn trọng bậc miễn phí.</summary>
    public double EffectiveRequestsPerSecond => HasKey ? RequestsPerSecondWithKey : RequestsPerSecond;
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
