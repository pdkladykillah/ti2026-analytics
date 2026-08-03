using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<Ti2026Options>(
    builder.Configuration.GetSection(Ti2026Options.SectionName));

var options = builder.Configuration.GetSection(Ti2026Options.SectionName)
    .Get<Ti2026Options>() ?? new Ti2026Options();

var dataDir = Path.IsPathRooted(options.DataDirectory)
    ? options.DataDirectory
    : Path.Combine(builder.Environment.ContentRootPath, options.DataDirectory);
Directory.CreateDirectory(dataDir);

var dbPath = Path.Combine(dataDir, "ti2026.db");
builder.Services.AddDbContext<Ti2026DbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

var app = builder.Build();

if (!string.IsNullOrEmpty(options.PathBase))
    app.UsePathBase(options.PathBase);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Ti2026DbContext>();
    db.Database.Migrate();
    // WAL: cho phép đọc song song với ghi. Cùng chế độ PhuongKhanh đang dùng.
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    app.Logger.LogInformation("SQLite tại {DbPath}", dbPath);
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();
