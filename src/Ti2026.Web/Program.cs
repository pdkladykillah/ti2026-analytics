using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Seeding;
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
    var seed = await new EditorialSeeder(db, paths.EditorialDirectory)
        .SeedAsync(CancellationToken.None);
    app.Logger.LogInformation(
        "Seed dữ liệu biên tập: skipped={Skipped} teams={Teams} players={Players}",
        seed.Skipped, seed.TeamsWritten, seed.PlayersWritten);
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapDataEndpoints();
app.MapH2hEndpoints();
app.MapOpsEndpoints();

app.Run();
