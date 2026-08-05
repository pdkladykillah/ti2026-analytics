using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
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

builder.Services.AddHttpClient<OpenDotaClient>(c =>
    {
        c.BaseAddress = new Uri(options.OpenDota.BaseUrl);
        c.Timeout = TimeSpan.FromSeconds(30);
        c.DefaultRequestHeaders.UserAgent.ParseAdd(options.Dltv.UserAgent);
    })
    .AddHttpMessageHandler(() => new RateLimitedHandler(options.OpenDota.RequestsPerSecond));

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
builder.Services.AddScoped<ProPubIngester>();
builder.Services.AddScoped<SnapshotWriter>();
builder.Services.AddScoped<IngestOrchestrator>();
builder.Services.AddScoped<IngestPipeline>();

builder.Services.AddSingleton(new IngestSchedule(
    Interval: TimeSpan.FromHours(Math.Max(options.IngestIntervalHours, 1)),
    InitialDelay: TimeSpan.FromSeconds(30),
    MaxMatchDetailsPerRun: Math.Max(options.MaxMatchDetailsPerRun, 1)));

// Bật scheduler chỉ khi có cấu hình rõ ràng. Test dùng WebApplicationFactory sẽ không chạy
// ingest ngoài ý muốn, và người vận hành có thể tắt hẳn để chỉ chạy tay qua api/ingest/run.
if (options.IngestEnabled)
    builder.Services.AddHostedService<IngestBackgroundService>();

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
app.UseStaticFiles();

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
app.MapTrendEndpoints();
app.MapAnalyticsEndpoints();
app.MapLearnEndpoints();
app.MapTierListEndpoints();
app.MapOpsEndpoints();

app.Run();
