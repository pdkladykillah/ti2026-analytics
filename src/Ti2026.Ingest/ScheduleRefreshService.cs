using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Ingest;

/// <summary>
/// Làm tươi RIÊNG bảng đấu, nhanh hơn hẳn vòng ingest chính.
///
/// VÌ SAO TÁCH RA. Bảng đấu là thứ duy nhất trên trang có giá trị theo PHÚT: trong lúc giải đá,
/// tỷ số và trạng thái "đang diễn ra" đổi liên tục, còn nhịp 6 giờ của vòng chính nghĩa là mở
/// trang lúc 3 giờ chiều thấy tỷ số của 11 giờ trưa. Mọi nguồn khác thì 6 giờ là thừa sức —
/// hero pool của một tuyển thủ không đổi trong nửa ngày.
///
/// VÌ SAO KHÔNG TỐN HẠN MỨC. Lịch lấy từ API chính chủ của Valve (www.dota2.com), không phải
/// OpenDota. Hai nguồn dùng hai HttpClient riêng, và bộ đếm lẫn bộ giới hạn nhịp chỉ gắn vào
/// OpenDota — nên gọi lịch dày hơn không đụng một lời gọi nào trong hạn mức 2.000/ngày.
///
/// VÌ SAO VẪN PHẢI QUA IngestGate. Đây là chỗ dễ mắc nhất, và mã của dự án đã có sẵn một lời
/// cảnh báo về đúng chuyện này ở IngestPipeline: SQLite chỉ có MỘT người ghi, nên hai vòng ghi
/// chồng nhau sẽ khiến vòng sau nằm chờ hết CommandTimeout 30 giây rồi chết ở "INSERT INTO
/// IngestRuns" — với một thông báo lỗi không hề nhắc tới nguyên nhân thật. Bộ này dùng TryEnter
/// chứ không EnterAsync: nếu vòng chính đang chạy thì bỏ lượt là đúng, vì vòng chính cũng đang
/// làm tươi bảng đấu.
/// </summary>
public sealed class ScheduleRefreshService(
    IServiceScopeFactory scopeFactory,
    IngestGate gate,
    ILogger<ScheduleRefreshService> logger) : BackgroundService
{
    /// <summary>Nhịp khi giải đang diễn ra.</summary>
    public static readonly TimeSpan InSeason = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Nhịp ngoài mùa. Không tắt hẳn: khung bảng đấu của kỳ TI kế tiếp xuất hiện trước ngày khai
    /// mạc khá lâu, và ta muốn thấy nó mà không phải nhớ đi bật lại thứ gì.
    /// </summary>
    public static readonly TimeSpan OffSeason = TimeSpan.FromHours(6);

    /// <summary>
    /// Còn bao lâu quanh một trận thì tính là "đang mùa". Lấy rộng một ngày về mỗi phía: một
    /// ngày thi đấu kéo dài nhiều giờ và hay trượt lịch — hôm nay chính là ví dụ, các trận dời
    /// một tiếng so với giờ Valve công bố.
    /// </summary>
    public static readonly TimeSpan SeasonWindow = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hoãn lần đầu để không tranh chỗ với vòng ingest khởi động (nó chạy sau 30 giây và
        // cũng làm tươi bảng đấu). Chạy ngay lúc này chỉ để bị bỏ lượt vì cổng đang bận.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = OffSeason;

            // try/catch NGOÀI CÙNG là bắt buộc: exception thoát khỏi ExecuteAsync sẽ hạ cả host,
            // tức sập luôn web chứ không riêng phần làm tươi. Một lần Valve đổi payload không
            // được phép làm trang không truy cập được.
            try
            {
                interval = await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Làm tươi bảng đấu lỗi — sẽ thử lại ở lượt sau");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Trả về nhịp nên dùng cho lượt kế tiếp.</summary>
    private async Task<TimeSpan> RefreshAsync(CancellationToken ct)
    {
        using var slot = gate.TryEnter();
        if (slot is null)
        {
            logger.LogDebug("Bỏ lượt làm tươi bảng đấu: đang có vòng ingest chạy");
            return InSeason;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Ti2026DbContext>();
        var ingester = scope.ServiceProvider.GetRequiredService<TiScheduleIngester>();

        var written = await ingester.IngestAsync(ct);

        // Điểm nhấn ngày chạy NGAY SAU, cùng nhịp: nó cần đúng thứ vừa lấy về — trạng thái từng
        // loạt — và thêm một bộ hẹn giờ nữa chỉ để làm cùng việc muộn hơn vài phút là thêm một
        // thứ phải nhớ. Lỗi ở đây không được kéo theo phần làm tươi bảng đấu.
        try
        {
            await scope.ServiceProvider.GetRequiredService<DailyDigestWriter>().WriteAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Tính điểm nhấn ngày lỗi — bảng đấu vẫn đã làm tươi xong");
        }

        var season = await InSeasonAsync(db, ct);
        logger.LogInformation(
            "Làm tươi bảng đấu: {Count} nút, nhịp kế tiếp {Minutes} phút",
            written, (season ? InSeason : OffSeason).TotalMinutes);

        return season ? InSeason : OffSeason;
    }

    /// <summary>
    /// "Đang mùa" SUY TỪ DỮ LIỆU, không phải một công tắc ai đó phải nhớ bật.
    ///
    /// Một công tắc tay sẽ đúng vào ngày cài đặt rồi sai mãi mãi sau đó — không ai nhớ tắt nó
    /// sau khi giải kết thúc, và sang kỳ sau lại không ai nhớ bật. Điều kiện ở đây tự đúng: có
    /// trận nào đang diễn ra, hoặc có trận nào được xếp giờ trong vòng một ngày quanh bây giờ.
    /// </summary>
    private static async Task<bool> InSeasonAsync(Ti2026DbContext db, CancellationToken ct)
    {
        if (await db.ScheduledSeries.AnyAsync(s => s.HasStarted && !s.IsCompleted, ct))
            return true;

        var now = DateTime.UtcNow;
        var from = now - SeasonWindow;
        var to = now + SeasonWindow;

        return await db.ScheduledSeries.AnyAsync(
            s => s.ScheduledAt != null && s.ScheduledAt >= from && s.ScheduledAt <= to, ct);
    }
}

/// <summary>
/// Nhớ id giải đang theo dõi, để không tải lại danh mục giải mỗi lượt.
///
/// Danh mục của Valve là 1,9 MB thô (315 KB nén) và chứa 9.797 giải, tải về chỉ để lọc ra một
/// số nguyên. Ở nhịp 6 giờ thì không đáng bàn; ở nhịp 15 phút thì thành khoảng 30 MB mỗi ngày
/// cho một giá trị gần như không bao giờ đổi — kỳ TI mới xuất hiện mỗi năm một lần.
///
/// Singleton và có TTL, không phải nhớ vĩnh viễn: nhớ mãi thì sang TI2027 sẽ phải khởi động lại
/// container mới thấy giải mới, và đó đúng là loại việc không ai nhớ làm.
/// </summary>
public sealed class LeagueIdCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(6);

    private readonly Lock _lock = new();
    private long? _value;
    private DateTime _at;

    public bool TryGet(DateTime now, out long leagueId)
    {
        lock (_lock)
        {
            leagueId = _value ?? 0;
            return _value is not null && now - _at < Ttl;
        }
    }

    public void Set(long leagueId, DateTime now)
    {
        lock (_lock)
        {
            _value = leagueId;
            _at = now;
        }
    }
}
