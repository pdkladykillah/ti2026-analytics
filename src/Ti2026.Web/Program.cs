using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Analytics;
using Ti2026.Ingest;
using Ti2026.Ingest.Http;
using Ti2026.Ingest.Media;
using Ti2026.Ingest.OpenDota;
using Ti2026.Ingest.Seeding;
using Ti2026.Ingest.Snapshots;
using Ti2026.Web;
using Ti2026.Web.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<Ti2026Options>(
    builder.Configuration.GetSection(Ti2026Options.SectionName));

var options = builder.Configuration.GetSection(Ti2026Options.SectionName)
    .Get<Ti2026Options>() ?? new Ti2026Options();

// Giải đường dẫn một lần, mọi nơi dùng chung qua singleton này
var paths = Ti2026Paths.Create(builder.Environment.ContentRootPath, options);
Directory.CreateDirectory(paths.DataDirectory);
builder.Services.AddSingleton(paths);

builder.Services.AddDbContext<Ti2026DbContext>(o =>
    o.UseSqlite($"Data Source={paths.DatabasePath}"));

// ---- Ingest pipeline ----

builder.Services.AddSingleton(new SanityThresholds(
    options.SanityGate.MinTeams, options.SanityGate.MinPlayers));

// Singleton: bo dem phai song qua moi vong ingest, khong phai moi request.
builder.Services.AddSingleton<ApiCallMeter>();

builder.Services.AddHttpClient<OpenDotaClient>(c =>
    {
        c.BaseAddress = new Uri(options.OpenDota.BaseUrl);
        c.Timeout = TimeSpan.FromSeconds(30);
        c.DefaultRequestHeaders.UserAgent.ParseAdd(options.Dltv.UserAgent);

        // Bearer chứ không phải ?api_key= : query param sẽ nằm lại trong log truy cập của
        // nguồn, trong thông báo lỗi của ta, và trong mọi chuỗi URL bị in ra khi gỡ lỗi.
        if (options.OpenDota.HasKey)
        {
            c.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", options.OpenDota.ApiKey);
        }
    })
    .AddHttpMessageHandler(sp =>
        new RateLimitedHandler(
            options.OpenDota.EffectiveRequestsPerSecond,
            sp.GetRequiredService<ApiCallMeter>()));

// API chính chủ của Valve. HttpClient RIÊNG: khác host, khác hạn mức, và nhịp 0,8 req/s đặt
// ra để tôn trọng hạn mức OpenDota thì không có lý gì áp cho máy chủ Valve.
//
// Bật giải nén tự động vì danh mục giải là 1,9 MB thô nhưng chỉ 315 KB khi nén.
builder.Services.AddHttpClient<Dota2WebClient>(c =>
    {
        c.BaseAddress = new Uri("https://www.dota2.com/");
        c.Timeout = TimeSpan.FromSeconds(60);
        c.DefaultRequestHeaders.UserAgent.ParseAdd(options.Dltv.UserAgent);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                 | System.Net.DecompressionMethods.Deflate,
    });

builder.Services.AddSingleton(new MediaPaths(paths.MediaDirectory));

// HttpClient RIENG cho tai anh. Khong dung chung voi OpenDotaClient: khac host, va nhip
// 0.8 req/s dat ra de ton trong han muc cua OpenDota thi khong co ly gi ap cho Steam CDN.
builder.Services.AddHttpClient<MediaCache>(c => c.Timeout = TimeSpan.FromSeconds(20));

builder.Services.AddSingleton<IngestGate>();
builder.Services.AddSingleton<IngestStatusTracker>();
builder.Services.AddSingleton<PlayerLookup>();
builder.Services.AddScoped<TeamResolver>();
builder.Services.AddScoped<OpenDotaIngester>();
builder.Services.AddScoped<MatchDetailIngester>();
builder.Services.AddScoped<LeagueBackfillIngester>();
builder.Services.AddScoped<TiScheduleIngester>();
builder.Services.AddScoped<DailyDigestWriter>();
builder.Services.AddScoped<ProPubIngester>();
builder.Services.AddScoped<IdolIngester>();
builder.Services.AddScoped<SnapshotWriter>();
builder.Services.AddScoped<PredictionLedger>();
builder.Services.AddScoped<StaleTeamIdDetector>();
builder.Services.AddScoped<BracketTeamGapDetector>();
builder.Services.AddScoped<TrackedMatchDetailIngester>();
builder.Services.AddScoped(sp => new TrackedPlayerIngester(
    sp.GetRequiredService<Ti2026DbContext>(),
    sp.GetRequiredService<OpenDotaClient>(),
    paths.EditorialDirectory,
    sp.GetRequiredService<ILogger<TrackedPlayerIngester>>()));
builder.Services.AddScoped<IngestOrchestrator>();
builder.Services.AddScoped<IngestPipeline>();

builder.Services.AddSingleton(new IngestSchedule(
    Interval: TimeSpan.FromHours(Math.Max(options.IngestIntervalHours, 1)),
    InitialDelay: TimeSpan.FromSeconds(30),
    MaxMatchDetailsPerRun: Math.Max(
        options.OpenDota.HasKey
            ? options.MaxMatchDetailsPerRunWithKey
            : options.MaxMatchDetailsPerRun,
        1)));

// Bật scheduler chỉ khi có cấu hình rõ ràng. Test dùng WebApplicationFactory sẽ không chạy
// ingest ngoài ý muốn, và người vận hành có thể tắt hẳn để chỉ chạy tay qua api/ingest/run.
builder.Services.AddSingleton<LeagueIdCache>();

if (options.IngestEnabled)
{
    builder.Services.AddHostedService<IngestBackgroundService>();

    // Bảng đấu có nhịp RIÊNG, nhanh hơn hẳn vòng chính. Nó là thứ duy nhất trên trang có giá
    // trị theo phút, và nó lấy từ API của Valve chứ không phải OpenDota nên không đụng hạn mức.
    // Vẫn đi qua chung IngestGate — xem ghi chú ở ScheduleRefreshService.
    builder.Services.AddHostedService<ScheduleRefreshService>();
}

var app = builder.Build();

var pathBase = options.NormalizedPathBase();
if (pathBase.Length > 0)
{
    app.UsePathBase(pathBase);
    app.Logger.LogInformation("Chạy sau reverse proxy với path base {PathBase}", pathBase);
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Ti2026DbContext>();
    db.Database.Migrate();
    // WAL: cho phép đọc song song với ghi. Cùng chế độ PhuongKhanh đang dùng.
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

    app.Logger.LogInformation("SQLite: {DbPath} · dữ liệu biên tập: {EditorialDir}",
        paths.DatabasePath, paths.EditorialDirectory);

    // Seed để deploy lần đầu là trang đã có dữ liệu ngay, không phải chờ vòng ingest.
    //
    // try/catch ở đây là CÓ CHỦ Ý và không được bỏ: file biên tập được sửa tay (đó là quy
    // trình đã chọn), nên một dấu phẩy thừa trong teams.json là chuyện sẽ xảy ra. Không bắt
    // thì exception hạ cả host — site không phục vụ gì cả, dù SQLite vẫn đang giữ nguyên bộ
    // dữ liệu tốt của lần trước. Đúng nghịch đảo của nguyên tắc "dữ liệu hơi lỗi thời tốt
    // hơn dữ liệu rỗng". Migration lỗi thì vẫn để chết như thiết kế.
    try
    {
        var seed = await new EditorialSeeder(db, paths.EditorialDirectory)
            .SeedAsync(CancellationToken.None);

        app.Logger.LogInformation(
            "Seed biên tập: skipped={Skipped} teams={Teams} snapshots={Snapshots} " +
            "players={Players} rostersClosed={Closed}",
            seed.Skipped, seed.TeamsWritten, seed.SnapshotsWritten,
            seed.PlayersWritten, seed.RostersClosed);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex,
            "Seed dữ liệu biên tập THẤT BẠI — tiếp tục chạy với dữ liệu đang có trong DB. " +
            "Kiểm tra cú pháp các file trong {EditorialDir}", paths.EditorialDirectory);
    }
}

app.UseDefaultFiles();

// BẮT TRÌNH DUYỆT KIỂM LẠI index.html, app.js, app.css MỖI LẦN MỞ TRANG.
//
// Vì sao cần, và đây là sự cố đã xảy ra thật: ba tệp này tham chiếu nhau bằng đường dẫn KHÔNG
// có phiên bản ("app.js"), và trước đây không gửi header cache nào cả. Thiếu Cache-Control thì
// trình duyệt tự suy ra thời hạn theo kinh nghiệm và giữ bản cũ hàng giờ.
//
// Hậu quả không phải "thấy bản cũ" — mà là TRANG HỎNG HẲN. Một lần triển khai đổi cả HTML lẫn
// JS: HTML mới tách thành sáu khung mục con, JS mới ghi vào sáu khung đó. Người dùng nhận HTML
// mới (vì họ vừa tải lại trang) nhưng JS CŨ từ bộ nhớ đệm, và JS cũ ghi vào #profile-body — một
// thẻ vừa bị bỏ đi. Nó ném lỗi ngay dòng đầu và cả trang hồ sơ trắng trơn, trông y hệt như mất
// dữ liệu.
//
// "no-cache" KHÔNG phải là cấm lưu đệm: trình duyệt vẫn giữ tệp, chỉ bắt buộc hỏi lại server
// trước khi dùng. Đã có ETag nên câu hỏi đó trả về 304 rỗng — vài trăm byte, không phải vài
// chục KB. Đổi lại là bảo đảm một lần triển khai luôn tới được người dùng.
//
// Ảnh trong /media thì ngược lại, vẫn cache một năm: tên tệp là hash NỘI DUNG nên ảnh đổi thì
// tên đổi, không bao giờ có chuyện tên cũ trỏ vào nội dung mới.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers.CacheControl = "no-cache",
});

// Ảnh đã tải về, phục vụ từ chính máy mình tại /media.
//
// Dùng static file middleware thay vì viết endpoint riêng: được content-type đúng, header cache
// và hỗ trợ range miễn phí. Tên tệp là hash NỘI DUNG nên bảo trình duyệt cache thật lâu là an
// toàn — ảnh đổi thì tên đổi, không có cảnh cache giữ ảnh cũ.
Directory.CreateDirectory(paths.MediaDirectory);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(paths.MediaDirectory),
    RequestPath = "/media",
    OnPrepareResponse = ctx =>
        ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable",
});

app.MapDataEndpoints();
app.MapH2hEndpoints();
app.MapVersusEndpoints();
app.MapTrendEndpoints();
app.MapInsightEndpoints();
app.MapScheduleEndpoints();
app.MapDigestEndpoints();
app.MapProfileEndpoints();
app.MapMatchHistoryEndpoints();
app.MapMatchBoardEndpoints();
app.MapAnalyticsEndpoints();
app.MapLearnEndpoints();
app.MapIdolEndpoints();
app.MapTierListEndpoints();
app.MapFantasyEndpoints();
app.MapOpsEndpoints();

app.Run();
