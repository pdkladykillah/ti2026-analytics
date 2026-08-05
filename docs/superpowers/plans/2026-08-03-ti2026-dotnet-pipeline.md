# Kế hoạch triển khai: ti2026-analytics sang .NET + pipeline dữ liệu thật

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Biến trang tĩnh ti2026-analytics thành một app ASP.NET Core tự cập nhật dữ liệu từ OpenDota + dltv.org, lưu lịch sử vào SQLite để phân tích form theo thời gian, deploy container riêng trên VPS đang host PhuongKhanh Travel.

**Architecture:** Một container ASP.NET Core `net10.0`. Trong cùng process: `IngestBackgroundService` chạy `PeriodicTimer` ghi vào SQLite (WAL), và Minimal API đọc từ SQLite phục vụ `wwwroot/index.html`. Ba project con theo phụ thuộc một chiều `Web → Ingest → Data`. Các endpoint giữ **đúng shape JSON hiện tại** nên frontend chỉ đổi URL.

**Tech Stack:** .NET 10 (`net10.0`, SDK 10.0.302 đã có sẵn trên máy), EF Core 10.0.9 + SQLite, Minimal API, xUnit, AngleSharp (parse HTML dltv), Docker Compose + Caddy.

**Spec:** [2026-08-03-ti2026-dotnet-pipeline-design.md](../specs/2026-08-03-ti2026-dotnet-pipeline-design.md)

---

## Bối cảnh cho người chưa biết project này

Project gốc là **một file** `index.html` 558 dòng (HTML + CSS + JS thuần, không framework, không npm) đọc 6 file JSON trong `data/`. Nó là dashboard tiếng Việt phân tích 16 đội Dota 2 dự The International 2026.

Ba điều dễ làm sai nếu không đọc trước:

1. **Không được sửa CSS hay logic render trong `index.html`.** Bảng màu "Sapphire nightfall whisper" ở đầu file có quy tắc 60/30/10 được ghi rõ trong comment, và dark mode chạy qua `[data-t=dark]`. Nhiệm vụ duy nhất ở frontend là đổi 6 lời gọi `fetch("data/x.json")` thành `fetch("api/x")`.
2. **Shape JSON phải giữ nguyên tuyệt đối.** `index.html:254-269` parse trực tiếp: `d.teams`, `x.rosters`, `x.pairs`, `pl.players`, `pl.h`, `pl.i`, `m.updatedAt`, `m.seed`. Sai một tên field là trang trắng.
3. **`m.seed` đã tồn tại** (`index.html:267`) — khi bật, UI hiện nhãn `(seed)`. Ta dùng lại cờ này cho dữ liệu mồi chưa qua ingest, không cần thêm gì ở frontend.

### Hai quyết định phát hiện trong lúc triển khai (đã áp dụng, không phải đề xuất)

**1. Mọi cột thời gian dùng `DateTime` UTC, KHÔNG dùng `DateTimeOffset`.**
SQLite không hỗ trợ `DateTimeOffset` trong `ORDER BY` — nó lưu thành TEXT kèm offset nên so
sánh chuỗi sai giữa các múi giờ, và EF Core chặn thẳng bằng `NotSupportedException`. Vì
`Match.StartTime` là trục thời gian của toàn bộ pipeline, đây là lỗi chặn ngay từ M1.
`Ti2026DbContext` có value converter buộc mọi `DateTime` ghi xuống là UTC và đọc lên có
`Kind=Utc`. Ngoại lệ duy nhất: `MetaFile.UpdatedAt` giữ `DateTimeOffset?` vì nó chỉ là DTO
parse JSON, không phải entity.

**2. Đường dẫn giải một lần qua `Ti2026Paths` singleton.**
Ban đầu `Program.cs` giải đường dẫn bằng `DirectoryResolver` còn endpoint tự nối
`ContentRootPath` + option một lần nữa. Hai cách tính cho cùng một thứ → `api/tiers` trả 404
khi chạy thật, mà test vẫn xanh vì test ghi đè bằng đường dẫn tuyệt đối và không chạm nhánh
sai. Bài học kèm theo: **test factory không được ghi đè `EditorialDirectory`**, nếu không nó
né đúng đoạn code mà production đi qua.

Đơn vị dữ liệu trong JSON hiện tại (giữ y nguyên ở API):
- `winrate`, `firstBlood`, `f10`, `winWhenFb`, `winWhenF10` — **số nguyên phần trăm** (`60` = 60%)
- `kills`, `deaths`, `assists`, `totalKills`, `killDiff` — **trung bình mỗi ván**, số thực
- `duration` — **phút**, số nguyên
- `maps` — tổng số ván trong cửa sổ

---

## Cấu trúc file

| File | Trách nhiệm |
|---|---|
| `Ti2026.slnx` | Solution |
| `Directory.Build.props` | `net10.0`, nullable, implicit usings — một chỗ duy nhất |
| **src/Ti2026.Data/** | |
| `Entities/*.cs` | 11 entity, mỗi file một entity |
| `Ti2026DbContext.cs` | DbSet + cấu hình index/unique |
| `Migrations/` | EF Core sinh ra |
| **src/Ti2026.Ingest/** | |
| `Seeding/SeedModels.cs` | DTO khớp **chính xác** 6 file JSON hiện tại |
| `Seeding/EditorialSeeder.cs` | Đọc `data/*.json` → DB, chỉ khi hash đổi |
| `Snapshots/StatCalculator.cs` | **Hàm thuần, không I/O** — phần đáng test nhất |
| `Snapshots/SnapshotWriter.cs` | Upsert `TeamStatSnapshot` |
| `SanityGate.cs` | Chặn ghi dữ liệu rỗng |
| `OpenDota/OpenDotaClient.cs` | Gọi HTTP, có rate limit |
| `OpenDota/OpenDotaIngester.cs` | Nạp match/player/hero |
| `Dltv/DltvClient.cs` | Tải HTML + ảnh, tôn trọng ETag |
| `Dltv/DltvIngester.cs` | Parse HTML bằng AngleSharp |
| `Media/MediaCache.cs` | Lưu ảnh vào `App_Data/media/` |
| `IngestOrchestrator.cs` | Chạy các ingester, ghi `IngestRun` |
| `IngestBackgroundService.cs` | `PeriodicTimer` + `try/catch` vòng ngoài |
| **src/Ti2026.Web/** | |
| `Program.cs` | DI, `UsePathBase`, migrate, map endpoints |
| `Contracts/*.cs` | DTO response — **giữ shape JSON cũ** |
| `Endpoints/DataEndpoints.cs` | `meta, teams, rosters, players, tiers` |
| `Endpoints/H2hEndpoints.cs` | `h2h` — tính từ `Match` + `SeriesId` |
| `Endpoints/TrendEndpoints.cs` | `trend` (Giai đoạn 2) |
| `Endpoints/OpsEndpoints.cs` | `health`, `ingest/run` |
| `wwwroot/index.html` | UI hiện tại, đổi 6 URL |
| `Dockerfile` | |
| **tests/Ti2026.Tests/** | xUnit, **không test nào gọi mạng thật** |
| `docker-compose.yml`, `.env.example` | Deploy |

Phụ thuộc một chiều: `Web → Ingest → Data`. `Ingest` không biết HTTP request của web; `Data` không biết OpenDota hay dltv.

---

## Các mốc

| Mốc | Nội dung | Verify được bằng |
|---|---|---|
| **M1** | Bộ xương chạy được: solution, entities, migration, seeder, API giữ shape cũ, trang chạy local | Mở `localhost:5000`, trang trông **y như bản tĩnh** |
| **M2** | Pipeline OpenDota: rate limit, StatCalculator, SnapshotWriter, SanityGate, BackgroundService | `POST api/ingest/run` → `api/health` báo `Succeeded` |
| **M3** | dltv + cache ảnh (sau khi kiểm `robots.txt`) | Ảnh phục vụ từ `media/{hash}`, không còn hotlink |
| **M4** | Giai đoạn 2: `api/trend` + tab Form có biểu đồ | Tab mới vẽ đường winrate theo ngày |
| **M5** | Deploy VPS: Dockerfile, compose, Caddy | `http://IP-VPS/ti2026` chạy, web du lịch vẫn sống |

Mỗi mốc kết thúc bằng một commit chạy được. **Không sang mốc sau khi mốc trước còn test đỏ.**

---

# M1 — Bộ xương chạy được

## Task 1: Dựng solution và Directory.Build.props

**Files:**
- Create: `Ti2026.slnx`, `Directory.Build.props`
- Create: `src/Ti2026.Data/Ti2026.Data.csproj`, `src/Ti2026.Ingest/Ti2026.Ingest.csproj`, `src/Ti2026.Web/Ti2026.Web.csproj`, `tests/Ti2026.Tests/Ti2026.Tests.csproj`

- [ ] **Step 1: Tạo solution và các project**

```bash
cd /c/Project/ti2026-analytics
dotnet new sln -n Ti2026 --format slnx
dotnet new classlib -o src/Ti2026.Data   -n Ti2026.Data   -f net10.0
dotnet new classlib -o src/Ti2026.Ingest -n Ti2026.Ingest -f net10.0
dotnet new web      -o src/Ti2026.Web    -n Ti2026.Web    -f net10.0
dotnet new xunit    -o tests/Ti2026.Tests -n Ti2026.Tests -f net10.0
dotnet sln add src/Ti2026.Data src/Ti2026.Ingest src/Ti2026.Web tests/Ti2026.Tests
```

- [ ] **Step 2: Nối phụ thuộc một chiều**

```bash
dotnet add src/Ti2026.Ingest reference src/Ti2026.Data
dotnet add src/Ti2026.Web    reference src/Ti2026.Ingest
dotnet add tests/Ti2026.Tests reference src/Ti2026.Web
```

`Web → Ingest → Data`. Test tham chiếu `Web` nên với tới cả ba.

- [ ] **Step 3: Tạo `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

`InvariantGlobalization=false` là **bắt buộc**: dữ liệu có tiếng Việt và `index.html` gọi `toLocaleString("vi-VN")`; bật invariant sẽ làm so sánh/sắp xếp chuỗi tiếng Việt sai.

- [ ] **Step 4: Thêm package**

```bash
dotnet add src/Ti2026.Data   package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.9
dotnet add src/Ti2026.Data   package Microsoft.EntityFrameworkCore.Design --version 10.0.9
dotnet add src/Ti2026.Ingest package AngleSharp
dotnet add tests/Ti2026.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Ti2026.Tests package FluentAssertions
```

Giữ EF Core ở đúng `10.0.9` như PhuongKhanh để không phải cài thêm gì trên VPS.

- [ ] **Step 5: Build để chắc chắn nền sạch**

Run: `dotnet build`
Expected: `Build succeeded`, 0 warning (vì `TreatWarningsAsErrors`)

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "chore: dung solution Ti2026 voi 4 project va Directory.Build.props"
```

---

## Task 2: Entities và DbContext

**Files:**
- Create: `src/Ti2026.Data/Entities/Team.cs`, `TeamAlias.cs`, `Player.cs`, `RosterEntry.cs`, `Hero.cs`, `Match.cs`, `TeamStatSnapshot.cs`, `TierEntry.cs`, `MediaAsset.cs`, `IngestRun.cs`, `SeedState.cs`
- Create: `src/Ti2026.Data/Ti2026DbContext.cs`

- [ ] **Step 1: Viết entity nhóm "định danh"**

`Entities/Team.cs`:
```csharp
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
    public List<TeamAlias> Aliases { get; set; } = [];
}
```

`Entities/TeamAlias.cs` — bảng giải quyết `_note` trong `teams.json` (`TEAM VISION` = `PARIVISION`, `HULIGANI` = `L1GA TEAM`):
```csharp
namespace Ti2026.Data.Entities;

public class TeamAlias
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }
    public required string Alias { get; set; }
    public required string Source { get; set; }   // "opendota" | "dltv" | "editorial"
}
```

`Entities/Player.cs`:
```csharp
namespace Ti2026.Data.Entities;

public class Player
{
    public int Id { get; set; }
    public long? OpenDotaAccountId { get; set; }
    public required string Nick { get; set; }
    public string? RealName { get; set; }
    public string? CountryName { get; set; }
    public string? CountryCode { get; set; }
    public string? PhotoUrl { get; set; }
    public int? PhotoMediaAssetId { get; set; }
}
```

`Entities/RosterEntry.cs` — có lịch sử vào/ra đội, xem lý do ở spec §5:
```csharp
namespace Ti2026.Data.Entities;

public class RosterEntry
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }
    public int PlayerId { get; set; }
    public Player? Player { get; set; }
    public required string Role { get; set; }   // CORE|MID|OFFLANE|SUPPORT|FULL SUPPORT|COACH
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }      // null = đang hiệu lực
}
```

`Role` dùng đúng 6 giá trị mà `index.html:295` đã map sẵn (`POS`/`RORD`) — đổi tên là vỡ UI đội hình.

`Entities/Hero.cs`:
```csharp
namespace Ti2026.Data.Entities;

public class Hero
{
    public int Id { get; set; }                  // OpenDota hero id, không tự tăng
    public required string Name { get; set; }
    public string? LocalizedName { get; set; }
    public string? ImageUrl { get; set; }
    public int? ImageMediaAssetId { get; set; }
}
```

- [ ] **Step 2: Viết entity nhóm "dữ liệu trận"**

`Entities/Match.cs`:
```csharp
namespace Ti2026.Data.Entities;

public class Match
{
    public long Id { get; set; }                 // OpenDota match_id, không tự tăng
    public long? SeriesId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public int DurationSeconds { get; set; }
    public long? LeagueId { get; set; }
    public string? LeagueName { get; set; }
    public int? RadiantTeamId { get; set; }      // nullable: OpenDota đôi khi thiếu ánh xạ đội
    public int? DireTeamId { get; set; }
    public bool RadiantWin { get; set; }
    public int RadiantScore { get; set; }
    public int DireScore { get; set; }
    public int? FirstBloodTimeSeconds { get; set; }
    public bool? RadiantHadFirstBlood { get; set; }
    public bool? RadiantReachedTenFirst { get; set; }
    public string? PatchVersion { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
}
```

`Entities/TeamStatSnapshot.cs` — 10 cột mirror đúng field `order` trong `h2h.json`:
```csharp
namespace Ti2026.Data.Entities;

public class TeamStatSnapshot
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }
    public DateOnly CapturedOn { get; set; }
    public int WindowDays { get; set; }          // 30 | 90 | 180

    public int Maps { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double Winrate { get; set; }          // phần trăm, 0..100
    public double AvgKills { get; set; }
    public double AvgDeaths { get; set; }
    public double AvgAssists { get; set; }
    public double KillDiff { get; set; }
    public double TotalKills { get; set; }
    public double FirstBloodRate { get; set; }
    public double F10Rate { get; set; }
    public double WinWhenFbRate { get; set; }
    public double WinWhenF10Rate { get; set; }
    public double AvgDurationMinutes { get; set; }
}
```

- [ ] **Step 3: Viết entity nhóm "vận hành"**

`Entities/TierEntry.cs`:
```csharp
namespace Ti2026.Data.Entities;

public class TierEntry
{
    public int Id { get; set; }
    public required string Patch { get; set; }
    public int HeroId { get; set; }
    public Hero? Hero { get; set; }
    public required string Tier { get; set; }     // S|A|B|C|SPEC|SIT
    public string? Note { get; set; }
    public int? Position { get; set; }            // vị trí 1..5, null = mọi vị trí
}
```

`Entities/MediaAsset.cs`:
```csharp
namespace Ti2026.Data.Entities;

public class MediaAsset
{
    public int Id { get; set; }
    public required string SourceUrl { get; set; }
    public required string LocalPath { get; set; }   // tương đối với App_Data/media
    public required string ContentHash { get; set; } // dùng làm URL: media/{ContentHash}
    public string? ETag { get; set; }
    public string? ContentType { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}
```

`Entities/IngestRun.cs`:
```csharp
namespace Ti2026.Data.Entities;

public enum IngestStatus { Running, Succeeded, Failed, Skipped }

public class IngestRun
{
    public int Id { get; set; }
    public required string Source { get; set; }    // "opendota" | "dltv" | "snapshot"
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public IngestStatus Status { get; set; }
    public int ItemsWritten { get; set; }
    public string? ErrorMessage { get; set; }
}
```

`Entities/SeedState.cs`:
```csharp
namespace Ti2026.Data.Entities;

public class SeedState
{
    public int Id { get; set; }
    public required string Key { get; set; }       // tên file, ví dụ "teams.json"
    public required string Hash { get; set; }      // SHA-256 hex
    public DateTimeOffset AppliedAt { get; set; }
}
```

- [ ] **Step 4: Viết DbContext**

`src/Ti2026.Data/Ti2026DbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Ti2026.Data.Entities;

namespace Ti2026.Data;

public class Ti2026DbContext(DbContextOptions<Ti2026DbContext> options) : DbContext(options)
{
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamAlias> TeamAliases => Set<TeamAlias>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<RosterEntry> RosterEntries => Set<RosterEntry>();
    public DbSet<Hero> Heroes => Set<Hero>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<TeamStatSnapshot> TeamStatSnapshots => Set<TeamStatSnapshot>();
    public DbSet<TierEntry> TierEntries => Set<TierEntry>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<IngestRun> IngestRuns => Set<IngestRun>();
    public DbSet<SeedState> SeedStates => Set<SeedState>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Team>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<TeamAlias>().HasIndex(x => new { x.Alias, x.Source }).IsUnique();
        b.Entity<Player>().HasIndex(x => x.OpenDotaAccountId).IsUnique()
            .HasFilter("[OpenDotaAccountId] IS NOT NULL");
        b.Entity<Hero>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Match>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Match>().HasIndex(x => x.StartTime);
        b.Entity<Match>().HasIndex(x => x.SeriesId);

        // Khoá chống nhân đôi khi ingest chạy lại trong cùng ngày
        b.Entity<TeamStatSnapshot>()
            .HasIndex(x => new { x.TeamId, x.CapturedOn, x.WindowDays }).IsUnique();

        b.Entity<TierEntry>().HasIndex(x => new { x.Patch, x.HeroId, x.Position }).IsUnique();
        b.Entity<MediaAsset>().HasIndex(x => x.SourceUrl).IsUnique();
        b.Entity<MediaAsset>().HasIndex(x => x.ContentHash).IsUnique();
        b.Entity<SeedState>().HasIndex(x => x.Key).IsUnique();
        b.Entity<IngestRun>().HasIndex(x => new { x.Source, x.StartedAt });

        b.Entity<RosterEntry>().HasIndex(x => new { x.TeamId, x.ValidTo });
        b.Entity<RosterEntry>().HasOne(x => x.Player).WithMany()
            .HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

Lưu ý cú pháp `HasFilter`: SQLite dùng ngoặc kép cho identifier. Nếu build lỗi, đổi thành `HasFilter("\"OpenDotaAccountId\" IS NOT NULL")`.

- [ ] **Step 5: Sinh migration đầu tiên**

```bash
dotnet tool install --global dotnet-ef --version 10.0.9   # nếu chưa có
dotnet ef migrations add InitialCreate \
  --project src/Ti2026.Data --startup-project src/Ti2026.Web
```
Expected: sinh ra `src/Ti2026.Data/Migrations/*_InitialCreate.cs`

Bước này cần `Program.cs` đã cấu hình `Ti2026DbContext` — làm Task 3 trước rồi quay lại nếu `dotnet ef` báo không tìm được DbContext.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(data): 11 entity + DbContext + migration InitialCreate"
```

---

## Task 3: Cấu hình app và bật SQLite WAL

**Files:**
- Modify: `src/Ti2026.Web/Program.cs`
- Create: `src/Ti2026.Web/Ti2026Options.cs`
- Modify: `src/Ti2026.Web/appsettings.json`

- [ ] **Step 1: Viết lớp options khớp env var trong spec §12**

`src/Ti2026.Web/Ti2026Options.cs`:
```csharp
namespace Ti2026.Web;

public class Ti2026Options
{
    public const string SectionName = "Ti2026";

    public string PathBase { get; set; } = "";
    public int IngestIntervalHours { get; set; } = 6;
    public string? IngestToken { get; set; }
    public string DataDirectory { get; set; } = "App_Data";
    public string EditorialDirectory { get; set; } = "data";
    public OpenDotaOptions OpenDota { get; set; } = new();
    public DltvOptions Dltv { get; set; } = new();
    public SanityGateOptions SanityGate { get; set; } = new();
}

public class OpenDotaOptions
{
    public double RequestsPerSecond { get; set; } = 1;
    public string BaseUrl { get; set; } = "https://api.opendota.com/api/";
}

public class DltvOptions
{
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
```

- [ ] **Step 2: Viết `Program.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<Ti2026Options>(
    builder.Configuration.GetSection(Ti2026Options.SectionName));

var options = builder.Configuration.GetSection(Ti2026Options.SectionName)
    .Get<Ti2026Options>() ?? new Ti2026Options();

var dataDir = Path.Combine(builder.Environment.ContentRootPath, options.DataDirectory);
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
    // WAL: cho phép đọc song song với ghi, và là chế độ PhuongKhanh đang dùng
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();
```

- [ ] **Step 3: Chạy để xác nhận DB được tạo**

Run: `dotnet run --project src/Ti2026.Web`
Expected: app lên, và có file `src/Ti2026.Web/App_Data/ti2026.db`. Ctrl+C để dừng.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(web): cau hinh SQLite WAL, UsePathBase, migrate luc startup"
```

---

## Task 4: `StatCalculator` — hàm thuần, viết test trước

Đây là task quan trọng nhất về mặt đúng/sai. Tính sai winrate thì **không có gì báo lỗi, chỉ có số sai**.

**Files:**
- Create: `src/Ti2026.Ingest/Snapshots/StatCalculator.cs`
- Test: `tests/Ti2026.Tests/StatCalculatorTests.cs`

- [ ] **Step 1: Viết test thất bại**

`tests/Ti2026.Tests/StatCalculatorTests.cs`:
```csharp
using FluentAssertions;
using Ti2026.Ingest.Snapshots;

namespace Ti2026.Tests;

public class StatCalculatorTests
{
    private static MatchOutcome M(bool won, int kills, int deaths, int assists,
        bool fb, bool f10, int durationSeconds) =>
        new(new DateOnly(2026, 7, 1), won, kills, deaths, assists, fb, f10, durationSeconds);

    [Fact]
    public void Compute_tinh_dung_winrate_va_trung_binh()
    {
        var stats = StatCalculator.Compute([
            M(won: true,  kills: 30, deaths: 20, assists: 60, fb: true,  f10: true,  durationSeconds: 2400),
            M(won: false, kills: 20, deaths: 30, assists: 40, fb: false, f10: false, durationSeconds: 3000),
        ]);

        stats.Maps.Should().Be(2);
        stats.Wins.Should().Be(1);
        stats.Losses.Should().Be(1);
        stats.Winrate.Should().Be(50);
        stats.AvgKills.Should().Be(25);
        stats.AvgDeaths.Should().Be(25);
        stats.AvgAssists.Should().Be(50);
        stats.KillDiff.Should().Be(0);
        stats.TotalKills.Should().Be(50);          // kills + deaths = tổng máu cả 2 bên
        stats.AvgDurationMinutes.Should().Be(45);  // (2400+3000)/2 = 2700s = 45 phút
    }

    [Fact]
    public void Compute_tinh_dung_ty_le_co_dieu_kien()
    {
        // 3 ván có first blood, thắng 2 → WinWhenFb = 66.67
        var stats = StatCalculator.Compute([
            M(true,  25, 20, 50, fb: true,  f10: true,  2400),
            M(true,  25, 20, 50, fb: true,  f10: false, 2400),
            M(false, 20, 25, 40, fb: true,  f10: false, 2400),
            M(false, 20, 25, 40, fb: false, f10: false, 2400),
        ]);

        stats.FirstBloodRate.Should().BeApproximately(75, 0.01);
        stats.WinWhenFbRate.Should().BeApproximately(66.67, 0.01);
        stats.F10Rate.Should().Be(25);
        stats.WinWhenF10Rate.Should().Be(100);
    }

    [Fact]
    public void Compute_khong_chia_cho_khong_khi_khong_co_van_nao()
    {
        var stats = StatCalculator.Compute([]);

        stats.Maps.Should().Be(0);
        stats.Winrate.Should().Be(0);
        stats.WinWhenFbRate.Should().Be(0);
        stats.AvgDurationMinutes.Should().Be(0);
    }

    [Fact]
    public void Compute_khong_chia_cho_khong_khi_khong_van_nao_co_first_blood()
    {
        var stats = StatCalculator.Compute([
            M(true, 25, 20, 50, fb: false, f10: false, 2400),
        ]);

        stats.FirstBloodRate.Should().Be(0);
        stats.WinWhenFbRate.Should().Be(0);   // không có mẫu → 0, không phải NaN
    }
}
```

Hai test cuối tồn tại vì `WinWhenFb` là **tỷ lệ có điều kiện**: mẫu số là số ván *có* first blood, không phải tổng số ván. Chia cho 0 ở đây sinh `NaN`, mà `NaN` serialize sang JSON sẽ làm `JSON.parse` ở browser chết — trang trắng.

- [ ] **Step 2: Chạy test để xác nhận đỏ**

Run: `dotnet test tests/Ti2026.Tests --filter StatCalculatorTests`
Expected: FAIL — `StatCalculator` chưa tồn tại (lỗi biên dịch)

- [ ] **Step 3: Viết implementation tối thiểu**

`src/Ti2026.Ingest/Snapshots/StatCalculator.cs`:
```csharp
namespace Ti2026.Ingest.Snapshots;

/// <summary>Kết quả một ván dưới góc nhìn của MỘT đội.</summary>
public readonly record struct MatchOutcome(
    DateOnly Date,
    bool Won,
    int Kills,
    int Deaths,
    int Assists,
    bool HadFirstBlood,
    bool ReachedTenFirst,
    int DurationSeconds);

public readonly record struct TeamWindowStats(
    int Maps, int Wins, int Losses, double Winrate,
    double AvgKills, double AvgDeaths, double AvgAssists,
    double KillDiff, double TotalKills,
    double FirstBloodRate, double F10Rate,
    double WinWhenFbRate, double WinWhenF10Rate,
    double AvgDurationMinutes);

public static class StatCalculator
{
    public static TeamWindowStats Compute(IReadOnlyList<MatchOutcome> matches)
    {
        if (matches.Count == 0) return default;

        var n = matches.Count;
        var wins = matches.Count(m => m.Won);
        var avgKills = matches.Average(m => (double)m.Kills);
        var avgDeaths = matches.Average(m => (double)m.Deaths);

        var withFb = matches.Where(m => m.HadFirstBlood).ToList();
        var withF10 = matches.Where(m => m.ReachedTenFirst).ToList();

        return new TeamWindowStats(
            Maps: n,
            Wins: wins,
            Losses: n - wins,
            Winrate: Pct(wins, n),
            AvgKills: avgKills,
            AvgDeaths: avgDeaths,
            AvgAssists: matches.Average(m => (double)m.Assists),
            KillDiff: avgKills - avgDeaths,
            TotalKills: avgKills + avgDeaths,
            FirstBloodRate: Pct(withFb.Count, n),
            F10Rate: Pct(withF10.Count, n),
            WinWhenFbRate: Pct(withFb.Count(m => m.Won), withFb.Count),
            WinWhenF10Rate: Pct(withF10.Count(m => m.Won), withF10.Count),
            AvgDurationMinutes: matches.Average(m => m.DurationSeconds) / 60.0);
    }

    /// <summary>Phần trăm, trả 0 khi mẫu số bằng 0 — không bao giờ trả NaN.</summary>
    private static double Pct(int numerator, int denominator) =>
        denominator == 0 ? 0 : numerator * 100.0 / denominator;
}
```

- [ ] **Step 4: Chạy test để xác nhận xanh**

Run: `dotnet test tests/Ti2026.Tests --filter StatCalculatorTests`
Expected: PASS — 4/4

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(ingest): StatCalculator + test, khong bao gio tra NaN"
```

---

## Task 5: `SanityGate` — chặn ghi dữ liệu rỗng

**Files:**
- Create: `src/Ti2026.Ingest/SanityGate.cs`
- Test: `tests/Ti2026.Tests/SanityGateTests.cs`

- [ ] **Step 1: Viết test thất bại**

```csharp
using FluentAssertions;
using Ti2026.Ingest;

namespace Ti2026.Tests;

public class SanityGateTests
{
    private static readonly SanityThresholds T = new(MinTeams: 16, MinPlayers: 60);

    [Fact]
    public void Du_nguong_thi_qua()
    {
        SanityGate.CheckTeams(16, T).Passed.Should().BeTrue();
        SanityGate.CheckPlayers(80, T).Passed.Should().BeTrue();
    }

    [Fact]
    public void Rong_thi_truot_va_neu_ro_ly_do()
    {
        var check = SanityGate.CheckTeams(0, T);

        check.Passed.Should().BeFalse();
        check.Reason.Should().Contain("0").And.Contain("16");
    }

    [Fact]
    public void Thieu_mot_chut_van_truot()
    {
        SanityGate.CheckTeams(15, T).Passed.Should().BeFalse();
        SanityGate.CheckPlayers(59, T).Passed.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Chạy để xác nhận đỏ**

Run: `dotnet test tests/Ti2026.Tests --filter SanityGateTests`
Expected: FAIL — không biên dịch được

- [ ] **Step 3: Implement**

`src/Ti2026.Ingest/SanityGate.cs`:
```csharp
namespace Ti2026.Ingest;

public sealed record SanityThresholds(int MinTeams, int MinPlayers);

public sealed record SanityCheck(bool Passed, string? Reason)
{
    public static SanityCheck Ok() => new(true, null);
    public static SanityCheck Fail(string reason) => new(false, reason);
}

/// <summary>
/// Chặn failure mode nguy hiểm nhất: ingester "thành công" nhưng trả về rỗng
/// (nguồn đổi layout, selector không match) rồi ghi đè lên dữ liệu tốt.
/// Gate áp cho TỪNG ingester, trên đúng loại dữ liệu ingester đó ghi.
/// </summary>
public static class SanityGate
{
    public static SanityCheck CheckTeams(int written, SanityThresholds t) =>
        written >= t.MinTeams
            ? SanityCheck.Ok()
            : SanityCheck.Fail($"Chỉ ghi được {written} đội, ngưỡng tối thiểu là {t.MinTeams}. Giữ nguyên dữ liệu cũ.");

    public static SanityCheck CheckPlayers(int written, SanityThresholds t) =>
        written >= t.MinPlayers
            ? SanityCheck.Ok()
            : SanityCheck.Fail($"Chỉ ghi được {written} player, ngưỡng tối thiểu là {t.MinPlayers}. Giữ nguyên dữ liệu cũ.");
}
```

- [ ] **Step 4: Chạy test để xác nhận xanh**

Run: `dotnet test tests/Ti2026.Tests --filter SanityGateTests`
Expected: PASS — 3/3

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(ingest): SanityGate chan ghi du lieu rong"
```

---

## Task 6: DTO khớp JSON hiện tại

**Files:**
- Create: `src/Ti2026.Ingest/Seeding/SeedModels.cs`
- Test: `tests/Ti2026.Tests/SeedModelsTests.cs`

DTO này dùng cho **cả hai chiều**: đọc `data/*.json` để seed, và làm response của API. Một định nghĩa duy nhất cho một shape — nếu tách đôi thì hai bên sẽ trôi khỏi nhau.

- [ ] **Step 1: Viết test đọc chính file JSON thật trong repo**

```csharp
using System.Text.Json;
using FluentAssertions;
using Ti2026.Ingest.Seeding;

namespace Ti2026.Tests;

public class SeedModelsTests
{
    private static string DataDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "data")))
            dir = Directory.GetParent(dir)?.FullName;
        Directory.Exists(Path.Combine(dir!, "data")).Should().BeTrue("phải tìm được thư mục data/ của repo");
        return Path.Combine(dir!, "data");
    }

    [Fact]
    public void Doc_duoc_teams_json_that_khong_mat_field_nao()
    {
        var json = File.ReadAllText(Path.Combine(DataDir(), "teams.json"));
        var doc = JsonSerializer.Deserialize<TeamsFile>(json, SeedJson.Options)!;

        doc.Teams.Should().HaveCount(16);
        var falcons = doc.Teams.First(t => t.Slug == "team-falcons");
        falcons.Name.Should().Be("Team Falcons");
        falcons.Short.Should().Be("Falcons");
        falcons.Region.Should().Be("Đa quốc gia");
        falcons.Stats!.Winrate.Should().Be(60);
        falcons.Stats.Kills.Should().BeApproximately(27.38, 0.001);
        falcons.Stats.KillDiff.Should().BeApproximately(1.15, 0.001);
        falcons.Logo.Should().StartWith("https://dltv.org/");
    }

    [Fact]
    public void Doc_duoc_rosters_json_va_players_json()
    {
        var rosters = JsonSerializer.Deserialize<RostersFile>(
            File.ReadAllText(Path.Combine(DataDir(), "rosters.json")), SeedJson.Options)!;
        rosters.Rosters.Should().ContainKey("team-falcons");
        rosters.Rosters["team-falcons"].Should().NotBeEmpty();

        var players = JsonSerializer.Deserialize<PlayersFile>(
            File.ReadAllText(Path.Combine(DataDir(), "players.json")), SeedJson.Options)!;
        players.Players.Should().NotBeEmpty();
        players.Players[0].N.Should().NotBeNullOrWhiteSpace();
    }
}
```

- [ ] **Step 2: Chạy để xác nhận đỏ**

Run: `dotnet test tests/Ti2026.Tests --filter SeedModelsTests`
Expected: FAIL — không biên dịch

- [ ] **Step 3: Implement DTO**

`src/Ti2026.Ingest/Seeding/SeedModels.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ti2026.Ingest.Seeding;

public static class SeedJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

// ---- teams.json ----
public class TeamsFile
{
    public string? Event { get; set; }
    public string? Source { get; set; }
    public List<TeamDto> Teams { get; set; } = [];
}

public class TeamDto
{
    public string Name { get; set; } = "";
    public string? Short { get; set; }
    public string Slug { get; set; } = "";
    public string? Region { get; set; }
    public string? Qualification { get; set; }
    public bool? SlugVerified { get; set; }
    public TeamStatsDto? Stats { get; set; }
    public string? Logo { get; set; }
}

public class TeamStatsDto
{
    public int Maps { get; set; }
    public double Winrate { get; set; }
    public double Kills { get; set; }
    public double Deaths { get; set; }
    public double Assists { get; set; }
    public double FirstBlood { get; set; }
    public double F10 { get; set; }
    public double WinWhenFb { get; set; }
    public double WinWhenF10 { get; set; }
    public double Duration { get; set; }
    public double TotalKills { get; set; }
    public double KillDiff { get; set; }
}

// ---- meta.json ----
public class MetaFile
{
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? Window { get; set; }
    public string? Source { get; set; }
    public int TeamsWithData { get; set; }
    public int TeamsTotal { get; set; }
    public bool? Seed { get; set; }
}

// ---- rosters.json ----
public class RostersFile
{
    public string? Source { get; set; }
    public string? Note { get; set; }
    public Dictionary<string, List<RosterMemberDto>> Rosters { get; set; } = [];
}

public class RosterMemberDto
{
    public string Nick { get; set; } = "";
    public string? Real { get; set; }
    public string? Role { get; set; }
    public string? Photo { get; set; }
}

// ---- players.json: field viết tắt, PHẢI giữ đúng tên ----
public class PlayersFile
{
    public string? UpdatedAt { get; set; }
    public string? Note { get; set; }
    public List<PlayerDto> Players { get; set; } = [];
    public Dictionary<string, object>? H { get; set; }
    public Dictionary<string, object>? I { get; set; }
}

public class PlayerDto
{
    public string N { get; set; } = "";        // nick
    public string? R { get; set; }             // real name
    public string? T { get; set; }             // team slug
    public string? Tn { get; set; }            // team name
    public int? P { get; set; }                // position 1..5
    public string? Pv { get; set; }            // mô tả vị trí
    public long? Id { get; set; }              // OpenDota account_id
    public string? C { get; set; }             // quốc tịch
    public string? Cc { get; set; }            // country code
    public string? Ph { get; set; }            // ảnh
}
```

`PlayerDto` dùng tên field một chữ vì `players.json` viết tắt như vậy để tiết kiệm dung lượng. **Không đổi tên** — `index.html:265` đọc `pl.players`, `pl.h`, `pl.i` trực tiếp.

- [ ] **Step 4: Chạy test để xác nhận xanh**

Run: `dotnet test tests/Ti2026.Tests --filter SeedModelsTests`
Expected: PASS — 2/2

Nếu đỏ ở `falcons.Stats.Winrate`, kiểm tra `PropertyNamingPolicy`: `winWhenF10` phải map vào `WinWhenF10`. Camel-case policy của .NET biến `WinWhenF10` → `winWhenF10`, khớp. Còn `F10` → `f10`, cũng khớp.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(ingest): DTO khop chinh xac 6 file JSON hien tai + test doc file that"
```

---

## Task 7: `EditorialSeeder` — nạp dữ liệu mồi, chỉ khi hash đổi

**Files:**
- Create: `src/Ti2026.Ingest/Seeding/EditorialSeeder.cs`
- Test: `tests/Ti2026.Tests/EditorialSeederTests.cs`

- [ ] **Step 1: Viết test thất bại**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Seeding;

namespace Ti2026.Tests;

public class EditorialSeederTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ti2026-{Guid.NewGuid():N}.db");

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    private static string DataDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "data")))
            dir = Directory.GetParent(dir)?.FullName;
        return Path.Combine(dir!, "data");
    }

    [Fact]
    public async Task Seed_nap_16_doi_va_doi_hinh_tu_file_that()
    {
        using var db = NewDb();
        var seeder = new EditorialSeeder(db, DataDir());

        var result = await seeder.SeedAsync(CancellationToken.None);

        result.TeamsWritten.Should().Be(16);
        (await db.Teams.CountAsync()).Should().Be(16);
        (await db.RosterEntries.CountAsync()).Should().BeGreaterThan(0);
        (await db.Teams.AnyAsync(t => t.Slug == "team-falcons")).Should().BeTrue();
    }

    [Fact]
    public async Task Seed_lan_hai_khong_nhan_doi_du_lieu()
    {
        using var db = NewDb();
        var seeder = new EditorialSeeder(db, DataDir());

        await seeder.SeedAsync(CancellationToken.None);
        var afterFirst = await db.Teams.CountAsync();
        var result2 = await seeder.SeedAsync(CancellationToken.None);

        result2.Skipped.Should().BeTrue("hash file không đổi thì bỏ qua");
        (await db.Teams.CountAsync()).Should().Be(afterFirst);
    }

    [Fact]
    public async Task Seed_ghi_alias_de_anh_xa_ten_giua_cac_nguon()
    {
        using var db = NewDb();
        await new EditorialSeeder(db, DataDir()).SeedAsync(CancellationToken.None);

        // teams.json ghi: TEAM VISION = PARIVISION, HULIGANI = L1GA TEAM
        var pari = await db.Teams.FirstOrDefaultAsync(t => t.Slug == "parivision");
        pari.Should().NotBeNull();
        (await db.TeamAliases.AnyAsync(a => a.TeamId == pari!.Id)).Should().BeTrue();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!,
                     Path.GetFileNameWithoutExtension(_dbPath) + "*"))
            try { File.Delete(f); } catch { /* file bị Windows giữ, bỏ qua */ }
    }
}
```

`SqliteConnection.ClearAllPools()` trong `Dispose` là cần thiết trên Windows: EF Core pool connection nên file `.db` bị giữ và `File.Delete` sẽ ném `IOException`.

- [ ] **Step 2: Chạy để xác nhận đỏ**

Run: `dotnet test tests/Ti2026.Tests --filter EditorialSeederTests`
Expected: FAIL — `EditorialSeeder` chưa tồn tại

- [ ] **Step 3: Implement**

`src/Ti2026.Ingest/Seeding/EditorialSeeder.cs`:
```csharp
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.Seeding;

public sealed record SeedResult(bool Skipped, int TeamsWritten, int PlayersWritten);

/// <summary>
/// Nạp dữ liệu biên tập từ data/*.json vào DB. Chạy lúc startup.
/// Mục đích: deploy lần đầu là trang đã có dữ liệu ngay, không phải chờ vòng ingest.
/// Chỉ nạp lại khi hash file đổi (bảng SeedState).
/// </summary>
public class EditorialSeeder(Ti2026DbContext db, string editorialDirectory)
{
    public async Task<SeedResult> SeedAsync(CancellationToken ct)
    {
        var teamsPath = Path.Combine(editorialDirectory, "teams.json");
        if (!File.Exists(teamsPath))
            return new SeedResult(Skipped: true, 0, 0);

        var teamsJson = await File.ReadAllTextAsync(teamsPath, ct);
        var hash = Sha256(teamsJson);

        var state = await db.SeedStates.FirstOrDefaultAsync(s => s.Key == "teams.json", ct);
        if (state?.Hash == hash)
            return new SeedResult(Skipped: true, 0, 0);

        var file = JsonSerializer.Deserialize<TeamsFile>(teamsJson, SeedJson.Options)
                   ?? throw new InvalidOperationException("teams.json không parse được");

        var teamsWritten = 0;
        foreach (var dto in file.Teams)
        {
            var team = await db.Teams.Include(t => t.Aliases)
                           .FirstOrDefaultAsync(t => t.Slug == dto.Slug, ct);
            if (team is null)
            {
                team = new Team { Slug = dto.Slug, Name = dto.Name };
                db.Teams.Add(team);
            }
            team.Name = dto.Name;
            team.ShortName = dto.Short;
            team.Region = dto.Region;
            team.Qualification = dto.Qualification;
            team.LogoUrl = dto.Logo;

            EnsureAlias(team, dto.Name, "editorial");
            if (!string.IsNullOrWhiteSpace(dto.Short)) EnsureAlias(team, dto.Short!, "editorial");
            teamsWritten++;
        }
        await db.SaveChangesAsync(ct);

        var playersWritten = await SeedRostersAsync(ct);

        if (state is null)
            db.SeedStates.Add(new SeedState { Key = "teams.json", Hash = hash, AppliedAt = DateTime.UtcNow });
        else
        {
            state.Hash = hash;
            state.AppliedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        return new SeedResult(Skipped: false, teamsWritten, playersWritten);
    }

    private async Task<int> SeedRostersAsync(CancellationToken ct)
    {
        var path = Path.Combine(editorialDirectory, "rosters.json");
        if (!File.Exists(path)) return 0;

        var file = JsonSerializer.Deserialize<RostersFile>(
            await File.ReadAllTextAsync(path, ct), SeedJson.Options);
        if (file is null) return 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var written = 0;

        foreach (var (slug, members) in file.Rosters)
        {
            var team = await db.Teams.FirstOrDefaultAsync(t => t.Slug == slug, ct);
            if (team is null) continue;

            foreach (var m in members)
            {
                var player = await db.Players.FirstOrDefaultAsync(p => p.Nick == m.Nick, ct);
                if (player is null)
                {
                    player = new Player { Nick = m.Nick, RealName = m.Real, PhotoUrl = m.Photo };
                    db.Players.Add(player);
                    await db.SaveChangesAsync(ct);
                }
                else
                {
                    player.RealName = m.Real ?? player.RealName;
                    player.PhotoUrl = m.Photo ?? player.PhotoUrl;
                }

                var role = m.Role ?? "CORE";
                var existing = await db.RosterEntries.FirstOrDefaultAsync(
                    r => r.TeamId == team.Id && r.PlayerId == player.Id && r.ValidTo == null, ct);

                if (existing is null)
                {
                    db.RosterEntries.Add(new RosterEntry
                    {
                        TeamId = team.Id, PlayerId = player.Id, Role = role, ValidFrom = today
                    });
                }
                else if (existing.Role != role)
                {
                    // đổi vai trò: đóng bản ghi cũ, mở bản ghi mới — giữ lịch sử
                    existing.ValidTo = today;
                    db.RosterEntries.Add(new RosterEntry
                    {
                        TeamId = team.Id, PlayerId = player.Id, Role = role, ValidFrom = today
                    });
                }
                written++;
            }
        }
        await db.SaveChangesAsync(ct);
        return written;
    }

    private static void EnsureAlias(Team team, string alias, string source)
    {
        if (team.Aliases.Any(a => a.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase)
                                  && a.Source == source)) return;
        team.Aliases.Add(new TeamAlias { Alias = alias, Source = source });
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}
```

- [ ] **Step 4: Chạy test để xác nhận xanh**

Run: `dotnet test tests/Ti2026.Tests --filter EditorialSeederTests`
Expected: PASS — 3/3

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(ingest): EditorialSeeder nap du lieu moi tu data/*.json theo hash"
```

---

## Task 8: Endpoint giữ đúng shape JSON cũ

**Files:**
- Create: `src/Ti2026.Web/Endpoints/DataEndpoints.cs`
- Modify: `src/Ti2026.Web/Program.cs`
- Test: `tests/Ti2026.Tests/ApiContractTests.cs`

Đây là task bảo vệ lời hứa quan trọng nhất của cả spec: **frontend không vỡ**.

- [ ] **Step 1: Viết test hợp đồng, dùng chính JSON hiện tại làm chuẩn vàng**

```csharp
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ti2026.Tests;

public class ApiContractTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task api_teams_tra_dung_shape_ma_index_html_dang_parse()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/teams");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());

        // index.html:255 -> d.teams.map(x => ({...x, tier: x.stats ? tier(x.stats.winrate) : null}))
        var teams = doc.RootElement.GetProperty("teams");
        teams.GetArrayLength().Should().Be(16);

        var first = teams[0];
        foreach (var field in new[] { "name", "short", "slug", "region", "logo" })
            first.TryGetProperty(field, out _).Should().BeTrue($"index.html cần field '{field}'");

        var stats = first.GetProperty("stats");
        // index.html:284 dùng đúng 12 khoá này để dựng bảng
        foreach (var key in new[] { "maps", "winrate", "kills", "deaths", "assists",
                                    "firstBlood", "f10", "winWhenFb", "winWhenF10",
                                    "duration", "totalKills", "killDiff" })
            stats.TryGetProperty(key, out _).Should().BeTrue($"index.html cần stats.{key}");
    }

    [Fact]
    public async Task api_meta_co_updatedAt_va_co_seed_khi_chua_ingest()
    {
        var client = factory.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/meta"));

        doc.RootElement.TryGetProperty("updatedAt", out _).Should().BeTrue();
        doc.RootElement.GetProperty("seed").GetBoolean().Should().BeTrue(
            "chưa có vòng ingest nào thành công thì phải bật cờ seed để UI hiện nhãn (seed)");
    }

    [Fact]
    public async Task api_rosters_tra_object_khoa_theo_slug()
    {
        var client = factory.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/rosters"));

        // index.html:258 -> x.rosters || {}
        var rosters = doc.RootElement.GetProperty("rosters");
        rosters.TryGetProperty("team-falcons", out var falcons).Should().BeTrue();
        falcons.GetArrayLength().Should().BeGreaterThan(0);
        falcons[0].TryGetProperty("nick", out _).Should().BeTrue();
        falcons[0].TryGetProperty("role", out _).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Chạy để xác nhận đỏ**

Run: `dotnet test tests/Ti2026.Tests --filter ApiContractTests`
Expected: FAIL — 404, endpoint chưa có

- [ ] **Step 3: Implement endpoint**

`src/Ti2026.Web/Endpoints/DataEndpoints.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Web.Endpoints;

public static class DataEndpoints
{
    public static void MapDataEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/meta", async (Ti2026DbContext db) =>
        {
            var lastRun = await db.IngestRuns
                .Where(r => r.Status == IngestStatus.Succeeded)
                .OrderByDescending(r => r.FinishedAt)
                .FirstOrDefaultAsync();

            var latestSnapshot = await db.TeamStatSnapshots
                .OrderByDescending(s => s.CapturedOn).FirstOrDefaultAsync();

            var teamsTotal = await db.Teams.CountAsync();
            var teamsWithData = latestSnapshot is null
                ? await db.Teams.CountAsync(t => t.LogoUrl != null)
                : await db.TeamStatSnapshots
                    .Where(s => s.CapturedOn == latestSnapshot.CapturedOn && s.WindowDays == 180)
                    .Select(s => s.TeamId).Distinct().CountAsync();

            return Results.Ok(new
            {
                updatedAt = lastRun?.FinishedAt ?? DateTime.UtcNow,
                window = "6 tháng gần nhất",
                source = lastRun is null ? "data/*.json (mồi)" : "OpenDota + dltv.org",
                teamsWithData,
                teamsTotal,
                seed = lastRun is null,      // index.html:267 hiện nhãn "(seed)"
            });
        });

        api.MapGet("/teams", async (Ti2026DbContext db) =>
        {
            var teams = await db.Teams.OrderBy(t => t.Name).ToListAsync();

            var latestDate = await db.TeamStatSnapshots
                .Where(s => s.WindowDays == 180)
                .OrderByDescending(s => s.CapturedOn)
                .Select(s => (DateOnly?)s.CapturedOn)
                .FirstOrDefaultAsync();

            var snapshots = latestDate is null
                ? []
                : await db.TeamStatSnapshots
                    .Where(s => s.CapturedOn == latestDate && s.WindowDays == 180)
                    .ToDictionaryAsync(s => s.TeamId);

            return Results.Ok(new
            {
                @event = "The International 2026",
                source = "OpenDota + dltv.org",
                teams = teams.Select(t => new
                {
                    name = t.Name,
                    @short = t.ShortName,
                    slug = t.Slug,
                    region = t.Region,
                    qualification = t.Qualification,
                    logo = t.LogoUrl,
                    stats = snapshots.TryGetValue(t.Id, out var s) ? ToStatsDto(s) : null,
                }),
            });
        });

        api.MapGet("/rosters", async (Ti2026DbContext db) =>
        {
            var rows = await db.RosterEntries
                .Where(r => r.ValidTo == null)
                .Include(r => r.Team).Include(r => r.Player)
                .ToListAsync();

            var rosters = rows
                .Where(r => r.Team is not null && r.Player is not null)
                .GroupBy(r => r.Team!.Slug)
                .ToDictionary(g => g.Key, g => g.Select(r => new
                {
                    nick = r.Player!.Nick,
                    real = r.Player.RealName,
                    role = r.Role,
                    photo = r.Player.PhotoUrl,
                }).ToList());

            return Results.Ok(new { source = "dltv.org", rosters });
        });
    }

    private static object ToStatsDto(TeamStatSnapshot s) => new
    {
        maps = s.Maps,
        winrate = Math.Round(s.Winrate),
        kills = Math.Round(s.AvgKills, 2),
        deaths = Math.Round(s.AvgDeaths, 2),
        assists = Math.Round(s.AvgAssists, 2),
        firstBlood = Math.Round(s.FirstBloodRate),
        f10 = Math.Round(s.F10Rate),
        winWhenFb = Math.Round(s.WinWhenFbRate),
        winWhenF10 = Math.Round(s.WinWhenF10Rate),
        duration = Math.Round(s.AvgDurationMinutes),
        totalKills = Math.Round(s.TotalKills, 2),
        killDiff = Math.Round(s.KillDiff, 2),
    };
}
```

Làm tròn ở đây để khớp đơn vị của JSON cũ: phần trăm là số nguyên, số trung bình 2 chữ số thập phân, `duration` là phút nguyên.

- [ ] **Step 4: Nối vào `Program.cs` và seed lúc startup**

Thêm vào `Program.cs`, sau khối `db.Database.Migrate()`:
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<Ti2026DbContext>();
    var editorialDir = Path.Combine(app.Environment.ContentRootPath, options.EditorialDirectory);
    var result = await new EditorialSeeder(db, editorialDir).SeedAsync(CancellationToken.None);
    app.Logger.LogInformation("Seed dữ liệu biên tập: skipped={Skipped} teams={Teams} players={Players}",
        result.Skipped, result.TeamsWritten, result.PlayersWritten);
}

app.MapDataEndpoints();
```

Và thêm `using Ti2026.Ingest.Seeding;` + `using Ti2026.Web.Endpoints;` ở đầu file.

- [ ] **Step 5: Chạy test để xác nhận xanh**

Run: `dotnet test tests/Ti2026.Tests --filter ApiContractTests`
Expected: PASS — 3/3

Nếu `WebApplicationFactory` không tìm được `data/` (vì content root khi test là thư mục bin), sửa `Ti2026Options.EditorialDirectory` trong test bằng biến môi trường, hoặc dùng đường dẫn tuyệt đối tìm ngược lên như hàm `DataDir()` ở Task 6.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(web): endpoint meta/teams/rosters giu dung shape JSON cu + test hop dong"
```

---

## Task 9: Chuyển `index.html` vào wwwroot

**Files:**
- Create: `src/Ti2026.Web/wwwroot/index.html` (copy từ `index.html` ở gốc)
- Modify: `src/Ti2026.Web/wwwroot/index.html:254-261`

- [ ] **Step 1: Copy nguyên file**

```bash
mkdir -p src/Ti2026.Web/wwwroot
cp index.html src/Ti2026.Web/wwwroot/index.html
```

Giữ `index.html` ở gốc repo **chưa xoá** — nó là đường lùi và là bản tham chiếu để so sánh.

- [ ] **Step 2: Đổi đúng 6 lời gọi fetch, không sửa gì khác**

Trong `src/Ti2026.Web/wwwroot/index.html`, đổi:

| Dòng | Từ | Thành |
|---|---|---|
| 254 | `fetch("data/teams.json")` | `fetch("api/teams")` |
| 257 | `fetch("data/meta.json")` | `fetch("api/meta")` |
| 258 | `fetch("data/rosters.json")` | `fetch("api/rosters")` |
| 259 | `fetch("data/h2h.json")` | `fetch("api/h2h")` |
| 260 | `fetch("data/tiers.json")` | `fetch("api/tiers")` |
| 261 | `fetch("data/players.json")` | `fetch("api/players")` |

Đường dẫn **tương đối** (không có `/` đầu) để chạy đúng cả khi có path prefix `/ti2026`.

Đổi thêm thông báo lỗi ở dòng 269 cho khớp thực tế:
```js
$("#err").innerHTML='<p style="color:var(--rd)">Không tải được api/teams — kiểm tra log server hoặc api/health.</p>';
```

- [ ] **Step 3: Chạy app và mở trang**

Run: `dotnet run --project src/Ti2026.Web`
Mở: `http://localhost:5000`
Expected: trang hiện 16 đội với nhãn `(seed)` ở dòng cập nhật. Bảng chỉ số hiện `—` ở các cột số vì chưa có snapshot nào (đúng như thiết kế — M2 sẽ điền).

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(web): chuyen index.html vao wwwroot, doi 6 fetch sang api/*"
```

---

## Task 10: Hoàn tất M1 — các endpoint còn lại

**Files:**
- Create: `src/Ti2026.Web/Endpoints/H2hEndpoints.cs`, `src/Ti2026.Web/Endpoints/OpsEndpoints.cs`
- Modify: `src/Ti2026.Web/Endpoints/DataEndpoints.cs` (thêm `players`, `tiers`)

- [ ] **Step 1: Thêm `api/players` và `api/tiers`**

Hai endpoint này ở M1 đọc trực tiếp từ file biên tập (chưa qua DB), vì `tiers.json` là dữ liệu biên tập thuần và `players.json` chứa `h`/`i` là bảng tra cứu phụ. Thêm vào `DataEndpoints.cs`:

```csharp
api.MapGet("/players", (IWebHostEnvironment env, IOptions<Ti2026Options> opt) =>
    ServeEditorialFile(env, opt.Value, "players.json"));

api.MapGet("/tiers", (IWebHostEnvironment env, IOptions<Ti2026Options> opt) =>
    ServeEditorialFile(env, opt.Value, "tiers.json"));
```

```csharp
private static IResult ServeEditorialFile(IWebHostEnvironment env, Ti2026Options opt, string fileName)
{
    var path = Path.Combine(env.ContentRootPath, opt.EditorialDirectory, fileName);
    return File.Exists(path)
        ? Results.Text(File.ReadAllText(path), "application/json")
        : Results.NotFound();
}
```

Thêm `using Microsoft.Extensions.Options;` ở đầu file.

Ghi rõ để không ai hiểu lầm: đây là **giải pháp tạm của M1**. `tiers` chuyển sang đọc bảng `TierEntry` ở M3, `players` chuyển sang DB khi Giai đoạn 3 làm phân tích cá nhân.

- [ ] **Step 2: Thêm `api/h2h` tính từ Match**

`src/Ti2026.Web/Endpoints/H2hEndpoints.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;

namespace Ti2026.Web.Endpoints;

public static class H2hEndpoints
{
    private static readonly string[] Order =
    [
        "winrate", "kills", "deaths", "killDiff", "totalKills",
        "firstBlood", "f10", "winWhenFb", "winWhenF10", "duration"
    ];

    public static void MapH2hEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/h2h", async (Ti2026DbContext db) =>
        {
            var teams = await db.Teams.ToDictionaryAsync(t => t.Id, t => t.Slug);

            var matches = await db.Matches
                .Where(m => m.RadiantTeamId != null && m.DireTeamId != null)
                .OrderByDescending(m => m.StartTime)
                .ToListAsync();

            var pairs = new Dictionary<string, object>();
            foreach (var group in matches.GroupBy(m => PairKey(teams, m.RadiantTeamId!.Value, m.DireTeamId!.Value)))
            {
                if (group.Key is null) continue;
                pairs[group.Key] = new
                {
                    n = group.Count(),
                    series = group.Select(m => new object[]
                    {
                        m.StartTime.ToString("yyyy-MM-dd"),
                        m.LeagueName ?? "",
                        m.RadiantScore,
                        m.DireScore,
                    }).ToList(),
                };
            }

            return Results.Ok(new
            {
                updatedAt = DateTime.UtcNow,
                source = "OpenDota",
                window = "6 tháng gần nhất",
                order = Order,
                pairs,
            });
        });
    }

    /// <summary>Khoá cặp đấu: hai slug sắp xếp theo alphabet, nối bằng '|' — đúng như h2h.json.</summary>
    private static string? PairKey(Dictionary<int, string> teams, int a, int b)
    {
        if (!teams.TryGetValue(a, out var sa) || !teams.TryGetValue(b, out var sb)) return null;
        return string.CompareOrdinal(sa, sb) <= 0 ? $"{sa}|{sb}" : $"{sb}|{sa}";
    }
}
```

Khoá cặp phải khớp `h2h.json` hiện tại: `"betboom-team|parivision"` — hai slug sắp theo alphabet, nối bằng `|`.

- [ ] **Step 3: Thêm `api/health`**

`src/Ti2026.Web/Endpoints/OpsEndpoints.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;

namespace Ti2026.Web.Endpoints;

public static class OpsEndpoints
{
    public static void MapOpsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (Ti2026DbContext db) =>
        {
            var runs = await db.IngestRuns
                .OrderByDescending(r => r.StartedAt).Take(6).ToListAsync();

            return Results.Ok(new
            {
                status = "ok",
                teams = await db.Teams.CountAsync(),
                matches = await db.Matches.CountAsync(),
                snapshots = await db.TeamStatSnapshots.CountAsync(),
                recentRuns = runs.Select(r => new
                {
                    r.Source,
                    status = r.Status.ToString(),
                    r.StartedAt,
                    r.FinishedAt,
                    r.ItemsWritten,
                    r.ErrorMessage,
                }),
            });
        });
    }
}
```

- [ ] **Step 4: Nối vào `Program.cs`**

```csharp
app.MapDataEndpoints();
app.MapH2hEndpoints();
app.MapOpsEndpoints();
```

- [ ] **Step 5: Chạy toàn bộ test**

Run: `dotnet test`
Expected: tất cả PASS

- [ ] **Step 6: Kiểm tra bằng tay**

```bash
dotnet run --project src/Ti2026.Web
# terminal khác:
curl -s localhost:5000/api/health | head -20
curl -s localhost:5000/api/teams | head -5
```
Expected: `health` trả `teams: 16`, `matches: 0`, `snapshots: 0`, `recentRuns: []`

- [ ] **Step 7: Commit — kết thúc M1**

```bash
git add -A && git commit -m "feat(web): api h2h + health, hoan tat M1 bo xuong chay duoc"
```

**Điều kiện xong M1:** `dotnet test` xanh toàn bộ; mở `localhost:5000` thấy trang y như bản tĩnh, có nhãn `(seed)`, 16 đội hiện đúng tên/vùng/logo/đội hình.

---

# M2 — Pipeline OpenDota

## Task 11: `RateLimitedHandler` — tự giới hạn tốc độ

**Files:**
- Create: `src/Ti2026.Ingest/Http/RateLimitedHandler.cs`
- Test: `tests/Ti2026.Tests/RateLimitedHandlerTests.cs`

Chủ động giới hạn ~1 req/s, **không** đợi bị 429 rồi mới xử lý — bị OpenDota chặn IP thì cả VPS mất quyền truy cập.

- [ ] **Step 1: Viết test thất bại**

```csharp
using System.Net;
using FluentAssertions;
using Ti2026.Ingest.Http;

namespace Ti2026.Tests;

public class RateLimitedHandlerTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task Gian_cach_toi_thieu_giua_hai_request()
    {
        var inner = new CountingHandler();
        var handler = new RateLimitedHandler(requestsPerSecond: 20) { InnerHandler = inner };
        using var client = new HttpClient(handler);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await client.GetAsync("https://example.test/a");
        await client.GetAsync("https://example.test/b");
        await client.GetAsync("https://example.test/c");
        sw.Stop();

        inner.Calls.Should().Be(3);
        // 20 req/s → giãn cách 50ms → 3 request mất ít nhất 100ms
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(90);
    }
}
```

Test dùng 20 req/s thay vì 1 req/s để chạy trong 100ms chứ không phải 2 giây — bộ test chậm là bộ test không ai chạy.

- [ ] **Step 2: Chạy để xác nhận đỏ**

Run: `dotnet test tests/Ti2026.Tests --filter RateLimitedHandlerTests`
Expected: FAIL — không biên dịch

- [ ] **Step 3: Implement**

```csharp
namespace Ti2026.Ingest.Http;

/// <summary>
/// Tự giãn cách request để không bao giờ vượt giới hạn của nguồn.
/// Chủ động chậm còn hơn bị chặn IP.
/// </summary>
public sealed class RateLimitedHandler(double requestsPerSecond) : DelegatingHandler
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minInterval =
        TimeSpan.FromSeconds(1.0 / Math.Max(requestsPerSecond, 0.01));
    private DateTimeOffset _lastSent = DateTimeOffset.MinValue;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var wait = _lastSent + _minInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastSent = DateTime.UtcNow;
        }
        finally { _gate.Release(); }

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
            await Task.Delay(retryAfter, ct);
        }
        return response;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _gate.Dispose();
        base.Dispose(disposing);
    }
}
```

- [ ] **Step 4: Chạy test để xác nhận xanh**

Run: `dotnet test tests/Ti2026.Tests --filter RateLimitedHandlerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(ingest): RateLimitedHandler tu gian cach request"
```

---

## Task 12: `OpenDotaClient` — test bằng fixture, không gọi mạng

**Files:**
- Create: `src/Ti2026.Ingest/OpenDota/OpenDotaClient.cs`, `src/Ti2026.Ingest/OpenDota/OpenDotaModels.cs`
- Create: `tests/Ti2026.Tests/Fixtures/opendota-team-matches.json`
- Test: `tests/Ti2026.Tests/OpenDotaClientTests.cs`

- [ ] **Step 1: Lấy fixture thật MỘT LẦN rồi lưu vào repo**

```bash
mkdir -p tests/Ti2026.Tests/Fixtures
curl -s "https://api.opendota.com/api/teams/8291895/matches" \
  | head -c 60000 > tests/Ti2026.Tests/Fixtures/opendota-team-matches.json
```

Kiểm tra file có JSON hợp lệ trước khi đi tiếp. Nếu OpenDota đổi shape sau này, đây là chỗ duy nhất phải cập nhật.

Đặt trong `Ti2026.Tests.csproj` để fixture được copy ra thư mục output:
```xml
<ItemGroup>
  <None Include="Fixtures/**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 2: Viết test dùng handler giả**

```csharp
using System.Net;
using FluentAssertions;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Tests;

public class OpenDotaClientTests
{
    private sealed class StubHandler(string json, HttpStatusCode code = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
    }

    private static OpenDotaClient ClientWith(string json, HttpStatusCode code = HttpStatusCode.OK)
    {
        var http = new HttpClient(new StubHandler(json, code))
        {
            BaseAddress = new Uri("https://api.opendota.com/api/")
        };
        return new OpenDotaClient(http);
    }

    [Fact]
    public async Task Doc_duoc_danh_sach_tran_tu_fixture_that()
    {
        var json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "opendota-team-matches.json"));

        var matches = await ClientWith(json).GetTeamMatchesAsync(8291895, CancellationToken.None);

        matches.Should().NotBeEmpty();
        matches[0].MatchId.Should().BeGreaterThan(0);
        matches[0].Duration.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Tra_danh_sach_rong_khi_nguon_tra_mang_rong()
    {
        var matches = await ClientWith("[]").GetTeamMatchesAsync(1, CancellationToken.None);
        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task Nem_loi_ro_rang_khi_nguon_tra_500()
    {
        var client = ClientWith("upstream boom", HttpStatusCode.InternalServerError);

        var act = () => client.GetTeamMatchesAsync(1, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
```

- [ ] **Step 3: Chạy để xác nhận đỏ**

Run: `dotnet test tests/Ti2026.Tests --filter OpenDotaClientTests`
Expected: FAIL — không biên dịch

- [ ] **Step 4: Implement model và client**

`src/Ti2026.Ingest/OpenDota/OpenDotaModels.cs`:
```csharp
using System.Text.Json.Serialization;

namespace Ti2026.Ingest.OpenDota;

public class OpenDotaTeamMatch
{
    [JsonPropertyName("match_id")]      public long MatchId { get; set; }
    [JsonPropertyName("radiant")]       public bool? Radiant { get; set; }
    [JsonPropertyName("radiant_win")]   public bool RadiantWin { get; set; }
    [JsonPropertyName("duration")]      public int Duration { get; set; }
    [JsonPropertyName("start_time")]    public long StartTime { get; set; }
    [JsonPropertyName("leagueid")]      public long? LeagueId { get; set; }
    [JsonPropertyName("league_name")]   public string? LeagueName { get; set; }
    [JsonPropertyName("radiant_score")] public int RadiantScore { get; set; }
    [JsonPropertyName("dire_score")]    public int DireScore { get; set; }
    [JsonPropertyName("opposing_team_id")] public int? OpposingTeamId { get; set; }
    [JsonPropertyName("opposing_team_name")] public string? OpposingTeamName { get; set; }
}

public class OpenDotaHero
{
    [JsonPropertyName("id")]             public int Id { get; set; }
    [JsonPropertyName("name")]           public string Name { get; set; } = "";
    [JsonPropertyName("localized_name")] public string? LocalizedName { get; set; }
}
```

`src/Ti2026.Ingest/OpenDota/OpenDotaClient.cs`:
```csharp
using System.Net.Http.Json;

namespace Ti2026.Ingest.OpenDota;

public class OpenDotaClient(HttpClient http)
{
    public async Task<List<OpenDotaTeamMatch>> GetTeamMatchesAsync(int teamId, CancellationToken ct)
    {
        var res = await http.GetAsync($"teams/{teamId}/matches", ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<List<OpenDotaTeamMatch>>(cancellationToken: ct) ?? [];
    }

    public async Task<List<OpenDotaHero>> GetHeroesAsync(CancellationToken ct)
    {
        var res = await http.GetAsync("heroes", ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<List<OpenDotaHero>>(cancellationToken: ct) ?? [];
    }
}
```

- [ ] **Step 5: Chạy test để xác nhận xanh**

Run: `dotnet test tests/Ti2026.Tests --filter OpenDotaClientTests`
Expected: PASS — 3/3

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(ingest): OpenDotaClient + fixture test khong goi mang that"
```

---

## Task 13: `SnapshotWriter` — upsert, chạy lại không nhân đôi

**Files:**
- Create: `src/Ti2026.Ingest/Snapshots/SnapshotWriter.cs`
- Test: `tests/Ti2026.Tests/SnapshotIdempotencyTests.cs`

- [ ] **Step 1: Viết test thất bại**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.Snapshots;

namespace Ti2026.Tests;

public class SnapshotIdempotencyTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ti2026-snap-{Guid.NewGuid():N}.db");

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    private static async Task<Team> SeedTeamWithMatches(Ti2026DbContext db)
    {
        var team = new Team { Slug = "test-team", Name = "Test Team" };
        var foe = new Team { Slug = "foe", Name = "Foe" };
        db.Teams.AddRange(team, foe);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        for (var i = 0; i < 4; i++)
            db.Matches.Add(new Match
            {
                Id = 1000 + i,
                StartTime = now.AddDays(-i - 1),
                DurationSeconds = 2400,
                RadiantTeamId = team.Id,
                DireTeamId = foe.Id,
                RadiantWin = i % 2 == 0,
                RadiantScore = 25,
                DireScore = 20,
                RadiantHadFirstBlood = true,
                RadiantReachedTenFirst = true,
                IngestedAt = now,
            });
        await db.SaveChangesAsync();
        return team;
    }

    [Fact]
    public async Task Chay_hai_lan_cung_ngay_khong_nhan_doi_dong()
    {
        using var db = NewDb();
        await SeedTeamWithMatches(db);
        var writer = new SnapshotWriter(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var first = await writer.WriteAsync(today, CancellationToken.None);
        var countAfterFirst = await db.TeamStatSnapshots.CountAsync();
        var second = await writer.WriteAsync(today, CancellationToken.None);

        countAfterFirst.Should().BeGreaterThan(0);
        (await db.TeamStatSnapshots.CountAsync()).Should().Be(countAfterFirst,
            "unique index (TeamId, CapturedOn, WindowDays) + upsert phải chặn nhân đôi");
        second.Should().Be(first);
    }

    [Fact]
    public async Task Ghi_snapshot_cho_ca_ba_cua_so()
    {
        using var db = NewDb();
        var team = await SeedTeamWithMatches(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await new SnapshotWriter(db).WriteAsync(today, CancellationToken.None);

        var windows = await db.TeamStatSnapshots
            .Where(s => s.TeamId == team.Id).Select(s => s.WindowDays).OrderBy(w => w).ToListAsync();
        windows.Should().Equal([30, 90, 180]);
    }

    [Fact]
    public async Task Tinh_dung_winrate_tu_goc_nhin_moi_doi()
    {
        using var db = NewDb();
        var team = await SeedTeamWithMatches(db);   // 4 ván, team ở Radiant, thắng 2
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await new SnapshotWriter(db).WriteAsync(today, CancellationToken.None);

        var snap = await db.TeamStatSnapshots
            .FirstAsync(s => s.TeamId == team.Id && s.WindowDays == 30);
        snap.Maps.Should().Be(4);
        snap.Wins.Should().Be(2);
        snap.Winrate.Should().Be(50);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!,
                     Path.GetFileNameWithoutExtension(_dbPath) + "*"))
            try { File.Delete(f); } catch { }
    }
}
```

- [ ] **Step 2: Chạy để xác nhận đỏ**

Run: `dotnet test tests/Ti2026.Tests --filter SnapshotIdempotencyTests`
Expected: FAIL — không biên dịch

- [ ] **Step 3: Implement**

`src/Ti2026.Ingest/Snapshots/SnapshotWriter.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.Snapshots;

/// <summary>
/// Tính lại chỉ số theo 3 cửa sổ và upsert vào TeamStatSnapshot.
/// Snapshot của các ngày khác nhau KHÔNG bao giờ ghi đè nhau — đó là kho lịch sử.
/// Trong cùng một ngày thì upsert, nên chạy lại bao nhiêu lần cũng an toàn.
/// </summary>
public class SnapshotWriter(Ti2026DbContext db)
{
    public static readonly int[] Windows = [30, 90, 180];

    public async Task<int> WriteAsync(DateOnly capturedOn, CancellationToken ct)
    {
        var teams = await db.Teams.ToListAsync(ct);
        var written = 0;

        foreach (var window in Windows)
        {
            var since = capturedOn.AddDays(-window).ToDateTime(TimeOnly.MinValue);
            var sinceOffset = new DateTimeOffset(since, TimeSpan.Zero);

            var matches = await db.Matches
                .Where(m => m.StartTime >= sinceOffset
                            && m.RadiantTeamId != null && m.DireTeamId != null)
                .ToListAsync(ct);

            foreach (var team in teams)
            {
                var outcomes = matches
                    .Where(m => m.RadiantTeamId == team.Id || m.DireTeamId == team.Id)
                    .Select(m => ToOutcome(m, team.Id))
                    .ToList();

                if (outcomes.Count == 0) continue;

                var stats = StatCalculator.Compute(outcomes);

                var existing = await db.TeamStatSnapshots.FirstOrDefaultAsync(
                    s => s.TeamId == team.Id && s.CapturedOn == capturedOn && s.WindowDays == window, ct);

                if (existing is null)
                {
                    existing = new TeamStatSnapshot
                    {
                        TeamId = team.Id, CapturedOn = capturedOn, WindowDays = window
                    };
                    db.TeamStatSnapshots.Add(existing);
                }

                Apply(existing, stats);
                written++;
            }
        }

        await db.SaveChangesAsync(ct);
        return written;
    }

    private static MatchOutcome ToOutcome(Match m, int teamId)
    {
        var isRadiant = m.RadiantTeamId == teamId;
        var won = isRadiant ? m.RadiantWin : !m.RadiantWin;
        var kills = isRadiant ? m.RadiantScore : m.DireScore;
        var deaths = isRadiant ? m.DireScore : m.RadiantScore;

        var hadFb = m.RadiantHadFirstBlood is null
            ? false
            : isRadiant ? m.RadiantHadFirstBlood.Value : !m.RadiantHadFirstBlood.Value;
        var f10 = m.RadiantReachedTenFirst is null
            ? false
            : isRadiant ? m.RadiantReachedTenFirst.Value : !m.RadiantReachedTenFirst.Value;

        return new MatchOutcome(
            Date: DateOnly.FromDateTime(m.StartTime.UtcDateTime),
            Won: won,
            Kills: kills,
            Deaths: deaths,
            Assists: 0,                 // OpenDota team-matches không trả assists; Giai đoạn 3 lấy từ match detail
            HadFirstBlood: hadFb,
            ReachedTenFirst: f10,
            DurationSeconds: m.DurationSeconds);
    }

    private static void Apply(TeamStatSnapshot s, TeamWindowStats st)
    {
        s.Maps = st.Maps;
        s.Wins = st.Wins;
        s.Losses = st.Losses;
        s.Winrate = st.Winrate;
        s.AvgKills = st.AvgKills;
        s.AvgDeaths = st.AvgDeaths;
        s.AvgAssists = st.AvgAssists;
        s.KillDiff = st.KillDiff;
        s.TotalKills = st.TotalKills;
        s.FirstBloodRate = st.FirstBloodRate;
        s.F10Rate = st.F10Rate;
        s.WinWhenFbRate = st.WinWhenFbRate;
        s.WinWhenF10Rate = st.WinWhenF10Rate;
        s.AvgDurationMinutes = st.AvgDurationMinutes;
    }
}
```

`Assists: 0` là **giới hạn đã biết của M2**, không phải bug: endpoint `teams/{id}/matches` của OpenDota không trả assists. Cột `assists` trên UI sẽ hiện `0` cho tới khi Giai đoạn 3 nạp match detail. Ghi lại điều này trong commit message để không ai đi tìm lỗi ở chỗ khác.

- [ ] **Step 4: Chạy test để xác nhận xanh**

Run: `dotnet test tests/Ti2026.Tests --filter SnapshotIdempotencyTests`
Expected: PASS — 3/3

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(ingest): SnapshotWriter upsert 3 cua so, chay lai khong nhan doi

Gioi han da biet: assists = 0 vi OpenDota teams/{id}/matches khong tra assists.
Giai doan 3 se nap tu match detail."
```

---

## Task 14: `IngestOrchestrator` + `IngestBackgroundService`

**Files:**
- Create: `src/Ti2026.Ingest/IngestOrchestrator.cs`, `src/Ti2026.Ingest/IngestBackgroundService.cs`
- Test: `tests/Ti2026.Tests/IngestOrchestratorTests.cs`

- [ ] **Step 1: Viết test — trượt sanity gate thì giữ nguyên dữ liệu cũ**

Đây là test quan trọng nhất cả bộ.

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest;

namespace Ti2026.Tests;

public class IngestOrchestratorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ti2026-orch-{Guid.NewGuid():N}.db");

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    [Fact]
    public async Task Truot_sanity_gate_thi_danh_Failed_va_giu_nguyen_du_lieu_cu()
    {
        using var db = NewDb();
        db.Teams.Add(new Team { Slug = "old-team", Name = "Dữ liệu cũ tốt" });
        await db.SaveChangesAsync();

        var orchestrator = new IngestOrchestrator(db, new SanityThresholds(16, 60),
            NullLogger<IngestOrchestrator>.Instance);

        // ingester giả trả về 0 đội — mô phỏng nguồn đổi layout
        var run = await orchestrator.RunSourceAsync("dltv",
            _ => Task.FromResult(0),
            SanityKind.Teams,
            CancellationToken.None);

        run.Status.Should().Be(IngestStatus.Failed);
        run.ErrorMessage.Should().Contain("0").And.Contain("16");
        (await db.Teams.CountAsync()).Should().Be(1, "dữ liệu cũ phải còn nguyên");
        (await db.Teams.AnyAsync(t => t.Slug == "old-team")).Should().BeTrue();
    }

    [Fact]
    public async Task Dat_nguong_thi_danh_Succeeded()
    {
        using var db = NewDb();
        var orchestrator = new IngestOrchestrator(db, new SanityThresholds(1, 1),
            NullLogger<IngestOrchestrator>.Instance);

        var run = await orchestrator.RunSourceAsync("opendota",
            _ => Task.FromResult(5), SanityKind.Teams, CancellationToken.None);

        run.Status.Should().Be(IngestStatus.Succeeded);
        run.ItemsWritten.Should().Be(5);
        run.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Ingester_nem_exception_thi_ghi_Failed_chu_khong_lam_sap()
    {
        using var db = NewDb();
        var orchestrator = new IngestOrchestrator(db, new SanityThresholds(1, 1),
            NullLogger<IngestOrchestrator>.Instance);

        var run = await orchestrator.RunSourceAsync("dltv",
            _ => throw new HttpRequestException("dltv sập"), SanityKind.Teams, CancellationToken.None);

        run.Status.Should().Be(IngestStatus.Failed);
        run.ErrorMessage.Should().Contain("dltv sập");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!,
                     Path.GetFileNameWithoutExtension(_dbPath) + "*"))
            try { File.Delete(f); } catch { }
    }
}
```

- [ ] **Step 2: Chạy để xác nhận đỏ**

Run: `dotnet test tests/Ti2026.Tests --filter IngestOrchestratorTests`
Expected: FAIL — không biên dịch

- [ ] **Step 3: Implement orchestrator**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest;

public enum SanityKind { Teams, Players, None }

/// <summary>
/// Chạy từng nguồn trong transaction riêng, áp sanity gate, ghi IngestRun.
/// Một nguồn fail KHÔNG ảnh hưởng nguồn khác.
/// </summary>
public class IngestOrchestrator(
    Ti2026DbContext db,
    SanityThresholds thresholds,
    ILogger<IngestOrchestrator> logger)
{
    public async Task<IngestRun> RunSourceAsync(
        string source,
        Func<CancellationToken, Task<int>> ingest,
        SanityKind sanityKind,
        CancellationToken ct)
    {
        var run = new IngestRun
        {
            Source = source, StartedAt = DateTime.UtcNow, Status = IngestStatus.Running
        };
        db.IngestRuns.Add(run);
        await db.SaveChangesAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var written = await ingest(ct);

            var check = sanityKind switch
            {
                SanityKind.Teams => SanityGate.CheckTeams(written, thresholds),
                SanityKind.Players => SanityGate.CheckPlayers(written, thresholds),
                _ => SanityCheck.Ok(),
            };

            if (!check.Passed)
            {
                await tx.RollbackAsync(ct);
                await FinishAsync(run, IngestStatus.Failed, 0, check.Reason, ct);
                logger.LogWarning("Ingest {Source} trượt sanity gate: {Reason}", source, check.Reason);
                return run;
            }

            await tx.CommitAsync(ct);
            await FinishAsync(run, IngestStatus.Succeeded, written, null, ct);
            logger.LogInformation("Ingest {Source} xong, ghi {Written} bản ghi", source, written);
            return run;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            await FinishAsync(run, IngestStatus.Failed, 0, ex.ToString(), ct);
            logger.LogError(ex, "Ingest {Source} lỗi", source);
            return run;
        }
    }

    private async Task FinishAsync(IngestRun run, IngestStatus status, int written,
        string? error, CancellationToken ct)
    {
        run.Status = status;
        run.ItemsWritten = written;
        run.ErrorMessage = error;
        run.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
```

`IngestRun` được ghi **ngoài** transaction đang rollback vì nó đã `SaveChanges` trước khi mở transaction, và `FinishAsync` chạy sau `RollbackAsync`. Nếu ghi trong transaction thì rollback sẽ xoá luôn bản ghi lỗi — mất đúng cái thông tin cần để chẩn đoán.

- [ ] **Step 4: Implement BackgroundService**

`src/Ti2026.Ingest/IngestBackgroundService.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ti2026.Ingest;

public class IngestBackgroundService(
    IServiceScopeFactory scopeFactory,
    IngestSchedule schedule,
    ILogger<IngestBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hoãn vòng đầu để restart liên tục không thành spam nguồn dữ liệu
        try { await Task.Delay(schedule.InitialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(schedule.Interval);
        do
        {
            // try/catch NGOÀI CÙNG là bắt buộc: exception thoát khỏi ExecuteAsync
            // sẽ hạ cả host — sập luôn web, không chỉ ingest.
            try
            {
                using var scope = scopeFactory.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<IngestPipeline>();
                await pipeline.RunAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Vòng ingest lỗi không mong đợi — sẽ thử lại ở vòng sau");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}

public sealed record IngestSchedule(TimeSpan Interval, TimeSpan InitialDelay);
```

- [ ] **Step 5: Implement `IngestPipeline` gộp các nguồn**

`src/Ti2026.Ingest/IngestPipeline.cs`:
```csharp
using Microsoft.Extensions.Logging;
using Ti2026.Ingest.OpenDota;
using Ti2026.Ingest.Snapshots;

namespace Ti2026.Ingest;

public class IngestPipeline(
    IngestOrchestrator orchestrator,
    OpenDotaIngester openDota,
    SnapshotWriter snapshots,
    ILogger<IngestPipeline> logger)
{
    public async Task RunAllAsync(CancellationToken ct)
    {
        logger.LogInformation("Bắt đầu vòng ingest");

        await orchestrator.RunSourceAsync("opendota",
            c => openDota.IngestAsync(c), SanityKind.None, ct);

        await orchestrator.RunSourceAsync("snapshot",
            c => snapshots.WriteAsync(DateOnly.FromDateTime(DateTime.UtcNow), c),
            SanityKind.None, ct);

        logger.LogInformation("Kết thúc vòng ingest");
    }
}
```

`SanityKind.None` cho `opendota` và `snapshot` vì cả hai **thêm** dữ liệu chứ không thay thế toàn bộ như dltv. Gate `Teams`/`Players` áp cho `dltv` ở M3 — đó là nguồn có nguy cơ trả rỗng do đổi layout.

- [ ] **Step 6: Đăng ký DI trong `Program.cs`**

```csharp
builder.Services.AddScoped(sp => new SanityThresholds(
    options.SanityGate.MinTeams, options.SanityGate.MinPlayers));
builder.Services.AddScoped<IngestOrchestrator>();
builder.Services.AddScoped<SnapshotWriter>();
builder.Services.AddScoped<OpenDotaIngester>();
builder.Services.AddScoped<IngestPipeline>();

builder.Services.AddHttpClient<OpenDotaClient>(c =>
{
    c.BaseAddress = new Uri(options.OpenDota.BaseUrl);
    c.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler(() => new RateLimitedHandler(options.OpenDota.RequestsPerSecond));

builder.Services.AddSingleton(new IngestSchedule(
    Interval: TimeSpan.FromHours(options.IngestIntervalHours),
    InitialDelay: TimeSpan.FromSeconds(30)));
builder.Services.AddHostedService<IngestBackgroundService>();
```

- [ ] **Step 7: Chạy toàn bộ test**

Run: `dotnet test`
Expected: tất cả PASS

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "feat(ingest): Orchestrator + BackgroundService, try/catch vong ngoai chan sap host"
```

---

## Task 15: `POST api/ingest/run` có bảo vệ token

**Files:**
- Modify: `src/Ti2026.Web/Endpoints/OpsEndpoints.cs`
- Test: `tests/Ti2026.Tests/IngestEndpointSecurityTests.cs`

Để hở endpoint này thì bất kỳ ai cũng ép VPS spam OpenDota tới mức bị chặn IP.

- [ ] **Step 1: Viết test bảo mật trước**

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ti2026.Tests;

public class IngestEndpointSecurityTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Khong_co_token_thi_tu_choi()
    {
        var res = await factory.CreateClient().PostAsync("/api/ingest/run", null);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_sai_thi_tu_choi()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Ingest-Token", "sai-be-bet");

        var res = await client.PostAsync("/api/ingest/run", null);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 2: Chạy để xác nhận đỏ**

Run: `dotnet test tests/Ti2026.Tests --filter IngestEndpointSecurityTests`
Expected: FAIL — 404 vì endpoint chưa có

- [ ] **Step 3: Implement**

Thêm vào `OpsEndpoints.cs`:
```csharp
app.MapPost("/api/ingest/run", async (
    HttpContext ctx,
    IOptions<Ti2026Options> opt,
    IngestPipeline pipeline,
    CancellationToken ct) =>
{
    var expected = opt.Value.IngestToken;
    if (string.IsNullOrWhiteSpace(expected))
        return Results.Problem("Chưa cấu hình Ti2026__IngestToken", statusCode: 503);

    var provided = ctx.Request.Headers["X-Ingest-Token"].ToString();
    if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
        return Results.Unauthorized();

    await pipeline.RunAllAsync(ct);
    return Results.Accepted();
});
```

Thêm `using System.Security.Cryptography;`, `using System.Text;`, `using Microsoft.Extensions.Options;`, `using Ti2026.Ingest;`.

`FixedTimeEquals` thay vì `==` để không rò rỉ độ dài/nội dung token qua thời gian so sánh. Lưu ý: `FixedTimeEquals` trả `false` ngay khi độ dài khác nhau, nên test "không có token" (chuỗi rỗng) cũng trượt đúng.

- [ ] **Step 4: Chạy test để xác nhận xanh**

Run: `dotnet test tests/Ti2026.Tests --filter IngestEndpointSecurityTests`
Expected: PASS — 2/2

- [ ] **Step 5: Kiểm tra bằng tay end-to-end**

```bash
Ti2026__IngestToken=test123 dotnet run --project src/Ti2026.Web
# terminal khác:
curl -s -X POST -H "X-Ingest-Token: test123" localhost:5000/api/ingest/run -i | head -3
curl -s localhost:5000/api/health
```
Expected: `202 Accepted`, rồi `health` có `recentRuns` với `source: "opendota"` và `source: "snapshot"`

- [ ] **Step 6: Commit — kết thúc M2**

```bash
git add -A && git commit -m "feat(web): POST api/ingest/run co token, hoan tat M2 pipeline OpenDota"
```

**Điều kiện xong M2:** `POST api/ingest/run` chạy được; `api/health` báo `Succeeded`; `api/teams` trả stats thật thay vì `null`; nhãn `(seed)` biến mất khỏi UI.

---

# M3 — dltv.org + cache ảnh

## Task 16: Kiểm tra `robots.txt` TRƯỚC KHI viết scraper

**Files:**
- Create: `docs/superpowers/notes/dltv-robots-check.md`

Đây là task đầu tiên của M3 và **không được bỏ qua**. Spec §15 điểm 1 ghi rõ đây là giả định chưa kiểm chứng.

- [ ] **Step 1: Đọc robots.txt**

```bash
curl -s https://dltv.org/robots.txt
```

- [ ] **Step 2: Ghi lại kết quả và quyết định**

Tạo `docs/superpowers/notes/dltv-robots-check.md` với: nội dung robots.txt nguyên văn, ngày kiểm tra, các path dự định lấy, và kết luận cho từng path là được phép hay không.

- [ ] **Step 3: Quyết định phạm vi M3 theo kết quả**

| Kết quả | Hành động |
|---|---|
| Cho phép các path cần lấy | Làm tiếp Task 17 như kế hoạch |
| Cấm một phần | Chỉ lấy phần được phép; ảnh/logo lấy từ nguồn khác hoặc giữ hotlink; ghi rõ trong note |
| Cấm toàn bộ / có ToS cấm crawl | **Dừng M3.** Đặt `Ti2026__Dltv__Enabled=false`, giữ ảnh + tier list ở dạng biên tập trong `data/*.json`. Báo cho chủ project và chuyển sang M4 |

- [ ] **Step 4: Commit note**

```bash
git add docs/superpowers/notes/dltv-robots-check.md
git commit -m "docs: ket qua kiem tra robots.txt cua dltv.org va pham vi scrape"
```

---

## Task 17: `MediaCache` — tải ảnh về VPS

**Files:**
- Create: `src/Ti2026.Ingest/Media/MediaCache.cs`
- Modify: `src/Ti2026.Web/Endpoints/MediaEndpoints.cs`
- Test: `tests/Ti2026.Tests/MediaCacheTests.cs`

- [ ] **Step 1: Viết test thất bại**

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Ingest.Media;

namespace Ti2026.Tests;

public class MediaCacheTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ti2026-media-{Guid.NewGuid():N}.db");
    private readonly string _mediaDir = Path.Combine(Path.GetTempPath(), $"ti2026-media-{Guid.NewGuid():N}");

    private sealed class ImageHandler(byte[] body, string etag) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            if (r.Headers.IfNoneMatch.Any(t => t.Tag == $"\"{etag}\""))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));

            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            };
            res.Content.Headers.ContentType = new("image/png");
            res.Headers.ETag = new(  $"\"{etag}\"");
            return Task.FromResult(res);
        }
    }

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    [Fact]
    public async Task Tai_anh_ve_dia_va_ghi_MediaAsset()
    {
        using var db = NewDb();
        var handler = new ImageHandler([1, 2, 3, 4], "abc");
        var cache = new MediaCache(db, new HttpClient(handler), _mediaDir);

        var asset = await cache.EnsureAsync("https://dltv.org/x.png", CancellationToken.None);

        asset.Should().NotBeNull();
        File.Exists(Path.Combine(_mediaDir, asset!.LocalPath)).Should().BeTrue();
        asset.ContentType.Should().Be("image/png");
        (await db.MediaAssets.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Lan_hai_dung_ETag_va_khong_tai_lai_noi_dung()
    {
        using var db = NewDb();
        var handler = new ImageHandler([1, 2, 3, 4], "abc");
        var cache = new MediaCache(db, new HttpClient(handler), _mediaDir);

        var first = await cache.EnsureAsync("https://dltv.org/x.png", CancellationToken.None);
        var second = await cache.EnsureAsync("https://dltv.org/x.png", CancellationToken.None);

        second!.ContentHash.Should().Be(first!.ContentHash);
        (await db.MediaAssets.CountAsync()).Should().Be(1, "không tạo bản ghi trùng");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_mediaDir, recursive: true); } catch { }
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!,
                     Path.GetFileNameWithoutExtension(_dbPath) + "*"))
            try { File.Delete(f); } catch { }
    }
}
```

- [ ] **Step 2: Chạy để xác nhận đỏ**

Run: `dotnet test tests/Ti2026.Tests --filter MediaCacheTests`
Expected: FAIL — không biên dịch

- [ ] **Step 3: Implement**

`src/Ti2026.Ingest/Media/MediaCache.cs`:
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.Media;

/// <summary>
/// Tải ảnh về VPS thay vì hotlink. Tôn trọng ETag để không tải lại thứ chưa đổi —
/// vừa bớt tốn bandwidth của nguồn, vừa hết cảnh ảnh chết khi họ đổi đường dẫn.
/// </summary>
public class MediaCache(Ti2026DbContext db, HttpClient http, string mediaDirectory)
{
    public async Task<MediaAsset?> EnsureAsync(string sourceUrl, CancellationToken ct)
    {
        Directory.CreateDirectory(mediaDirectory);

        var existing = await db.MediaAssets.FirstOrDefaultAsync(a => a.SourceUrl == sourceUrl, ct);

        using var req = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
        if (existing?.ETag is not null)
            req.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(existing.ETag));

        using var res = await http.SendAsync(req, ct);

        if (res.StatusCode == HttpStatusCode.NotModified && existing is not null)
            return existing;

        if (!res.IsSuccessStatusCode)
            return existing;      // giữ bản cũ, không xoá thứ đang dùng được

        var bytes = await res.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length == 0) return existing;

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..32];
        var ext = GuessExtension(res.Content.Headers.ContentType?.MediaType);
        var localPath = $"{hash}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(mediaDirectory, localPath), bytes, ct);

        if (existing is null)
        {
            existing = new MediaAsset
            {
                SourceUrl = sourceUrl, LocalPath = localPath, ContentHash = hash
            };
            db.MediaAssets.Add(existing);
        }
        existing.LocalPath = localPath;
        existing.ContentHash = hash;
        existing.ETag = res.Headers.ETag?.ToString();
        existing.ContentType = res.Content.Headers.ContentType?.MediaType;
        existing.FetchedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return existing;
    }

    private static string GuessExtension(string? mediaType) => mediaType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".bin",
    };
}
```

- [ ] **Step 4: Thêm endpoint phục vụ ảnh**

`src/Ti2026.Web/Endpoints/MediaEndpoints.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ti2026.Data;

namespace Ti2026.Web.Endpoints;

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/media/{hash}", async (
            string hash, Ti2026DbContext db,
            IWebHostEnvironment env, IOptions<Ti2026Options> opt, CancellationToken ct) =>
        {
            var asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.ContentHash == hash, ct);
            if (asset is null) return Results.NotFound();

            var path = Path.Combine(env.ContentRootPath, opt.Value.DataDirectory, "media", asset.LocalPath);
            if (!File.Exists(path)) return Results.NotFound();

            // Nội dung khoá theo hash nên không bao giờ đổi → cache vĩnh viễn
            return Results.File(path, asset.ContentType ?? "application/octet-stream",
                enableRangeProcessing: true);
        }).CacheOutput(p => p.Expire(TimeSpan.FromDays(365)));
    }
}
```

Thêm `builder.Services.AddOutputCache();` và `app.UseOutputCache();` vào `Program.cs`, và `app.MapMediaEndpoints();`.

- [ ] **Step 5: Chạy test để xác nhận xanh**

Run: `dotnet test tests/Ti2026.Tests --filter MediaCacheTests`
Expected: PASS — 2/2

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(ingest): MediaCache tai anh ve VPS ton trong ETag + endpoint media/{hash}"
```

---

## Task 18: `DltvIngester` — parse HTML phòng vệ

**Files:**
- Create: `src/Ti2026.Ingest/Dltv/DltvIngester.cs`
- Create: `tests/Ti2026.Tests/Fixtures/dltv-team-page.html`
- Test: `tests/Ti2026.Tests/DltvIngesterTests.cs`

**Chỉ làm task này nếu Task 16 kết luận được phép scrape.**

- [ ] **Step 1: Lưu fixture HTML thật một lần**

```bash
curl -s -A "ti2026-analytics/1.0" https://dltv.org/teams/team-falcons \
  > tests/Ti2026.Tests/Fixtures/dltv-team-page.html
```

- [ ] **Step 2: Viết test — trong đó có test HTML rỗng phải trượt gate**

```csharp
using FluentAssertions;
using Ti2026.Ingest.Dltv;

namespace Ti2026.Tests;

public class DltvIngesterTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public async Task Parse_duoc_trang_that()
    {
        var parsed = await DltvParser.ParseTeamPageAsync(Fixture("dltv-team-page.html"));

        parsed.Should().NotBeNull();
        parsed!.Players.Should().NotBeEmpty("trang đội thật phải có ít nhất một player");
    }

    [Fact]
    public async Task HTML_rong_thi_tra_ket_qua_rong_chu_khong_nem()
    {
        var parsed = await DltvParser.ParseTeamPageAsync("<html><body></body></html>");

        parsed.Should().NotBeNull();
        parsed!.Players.Should().BeEmpty(
            "layout đổi thì trả rỗng để sanity gate chặn, không được ném để rồi bị bắt và bỏ qua");
    }
}
```

- [ ] **Step 3: Chạy để xác nhận đỏ, rồi implement parser bằng AngleSharp**

Implement `DltvParser.ParseTeamPageAsync` trả `DltvTeamPage` với danh sách player + URL ảnh, dùng `AngleSharp.HtmlParser`. Selector cụ thể phụ thuộc HTML thật trong fixture — đọc fixture rồi viết selector khớp, đừng đoán.

Nguyên tắc phòng vệ: mọi selector không match thì trả về danh sách rỗng, **không ném exception**. Lý do là sanity gate ở tầng trên mới là chỗ quyết định có commit hay không; parser ném exception sẽ bị `catch` ở orchestrator và cũng thành `Failed`, nhưng thông báo lỗi sẽ là stack trace vô nghĩa thay vì "chỉ ghi được 0 đội, ngưỡng 16".

- [ ] **Step 4: Nối vào pipeline với sanity gate `Teams`**

Trong `IngestPipeline.RunAllAsync`, thêm trước bước snapshot:
```csharp
if (dltvEnabled)
    await orchestrator.RunSourceAsync("dltv", c => dltv.IngestAsync(c), SanityKind.Teams, ct);
```

- [ ] **Step 5: Chạy toàn bộ test rồi commit**

```bash
dotnet test
git add -A && git commit -m "feat(ingest): DltvIngester parse phong ve, tra rong thay vi nem"
```

---

# M4 — Giai đoạn 2: form theo thời gian

## Task 19: `api/trend`

**Files:**
- Create: `src/Ti2026.Web/Endpoints/TrendEndpoints.cs`
- Test: `tests/Ti2026.Tests/TrendEndpointTests.cs`

- [ ] **Step 1: Viết test thất bại**

```csharp
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ti2026.Tests;

public class TrendEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Tra_mang_phang_de_nhet_thang_vao_chart()
    {
        using var doc = JsonDocument.Parse(
            await factory.CreateClient().GetStringAsync("/api/trend?window=30"));

        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Window_khong_hop_le_tra_400()
    {
        var res = await factory.CreateClient().GetAsync("/api/trend?window=7");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Khong_truyen_team_thi_tra_tat_ca_doi()
    {
        var res = await factory.CreateClient().GetAsync("/api/trend");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 2: Chạy để xác nhận đỏ, rồi implement**

```csharp
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;

namespace Ti2026.Web.Endpoints;

public static class TrendEndpoints
{
    private static readonly int[] AllowedWindows = [30, 90, 180];

    public static void MapTrendEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/trend", async (
            Ti2026DbContext db, string? team, int window = 30,
            DateOnly? from = null, DateOnly? to = null) =>
        {
            if (!AllowedWindows.Contains(window))
                return Results.BadRequest(new { error = "window chỉ nhận 30, 90 hoặc 180" });

            var until = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var since = from ?? until.AddDays(-90);

            var q = db.TeamStatSnapshots
                .Where(s => s.WindowDays == window && s.CapturedOn >= since && s.CapturedOn <= until);

            if (!string.IsNullOrWhiteSpace(team))
                q = q.Where(s => s.Team!.Slug == team);

            var rows = await q
                .OrderBy(s => s.CapturedOn)
                .Select(s => new
                {
                    teamSlug = s.Team!.Slug,
                    date = s.CapturedOn,
                    maps = s.Maps,
                    winrate = Math.Round(s.Winrate, 1),
                    kills = Math.Round(s.AvgKills, 2),
                    deaths = Math.Round(s.AvgDeaths, 2),
                    killDiff = Math.Round(s.KillDiff, 2),
                    totalKills = Math.Round(s.TotalKills, 2),
                    firstBlood = Math.Round(s.FirstBloodRate, 1),
                    f10 = Math.Round(s.F10Rate, 1),
                    duration = Math.Round(s.AvgDurationMinutes, 1),
                })
                .ToListAsync();

            return Results.Ok(rows);
        });
    }
}
```

Ngày không có snapshot thì **khuyết dòng**, không trả `0` — số 0 giả sẽ vẽ thành cú sụt phong độ không tồn tại. Đây là quyết định đã ghi trong spec §7, phía client phải xử lý điểm khuyết (đường ngắt hoặc nối bỏ qua).

- [ ] **Step 3: Nối vào `Program.cs`, chạy test, commit**

```bash
dotnet test tests/Ti2026.Tests --filter TrendEndpointTests
git add -A && git commit -m "feat(web): api/trend tra mang phang cho bieu do form"
```

---

## Task 20: Tab "Form" trên UI

**Files:**
- Modify: `src/Ti2026.Web/wwwroot/index.html`

- [ ] **Step 1: Thêm nút tab vào `nav`**

Tìm khối `<nav>` và thêm một `<button>` theo **đúng** mẫu các nút hiện có (class `on` cho tab đang mở). Không tạo class mới.

- [ ] **Step 2: Thêm khối view**

Thêm `<div class="v" id="v-form">` theo đúng mẫu các view khác (`.v` ẩn, `.v.on` hiện).

- [ ] **Step 3: Vẽ biểu đồ bằng SVG thuần, không thêm thư viện**

Project hiện tại **không có npm, không có CDN nào**. Thêm Chart.js sẽ phá vỡ tính chất một-file và thêm phụ thuộc mạng ngoài. Vẽ đường bằng SVG inline:

- Dùng biến CSS sẵn có cho màu: `var(--bl)` cho đường chính, `var(--mu)` cho trục, `var(--gr)` cho lưới
- Không hardcode mã màu hex — dark mode sẽ sai
- Điểm khuyết (ngày không có snapshot) thì ngắt đường bằng cách bắt đầu một `<path>` mới, không nối thẳng qua

- [ ] **Step 4: Kiểm tra bằng mắt cả hai theme**

Run: `dotnet run --project src/Ti2026.Web`, mở `localhost:5000`, bấm tab Form, rồi bấm nút đổi theme.
Expected: biểu đồ đọc được ở cả sáng và tối; không có màu nào bị chìm vào nền.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(web): tab Form ve bieu do winrate theo thoi gian bang SVG thuan"
```

---

# M5 — Deploy lên VPS

## Task 21: Dockerfile

**Files:**
- Create: `src/Ti2026.Web/Dockerfile`, `.dockerignore`

- [ ] **Step 1: Viết Dockerfile multi-stage**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Ti2026.Data/Ti2026.Data.csproj      src/Ti2026.Data/
COPY src/Ti2026.Ingest/Ti2026.Ingest.csproj  src/Ti2026.Ingest/
COPY src/Ti2026.Web/Ti2026.Web.csproj        src/Ti2026.Web/
RUN dotnet restore src/Ti2026.Web/Ti2026.Web.csproj
COPY . .
RUN dotnet publish src/Ti2026.Web/Ti2026.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
# data/ là nguồn seed dữ liệu biên tập — phải có trong image
COPY data ./data
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ti2026.Web.dll"]
```

Copy `.csproj` trước rồi `restore` trước khi copy toàn bộ source — Docker cache lớp restore, nên sửa code không phải restore lại.

`COPY data ./data` là **bắt buộc**: `EditorialSeeder` đọc từ đó. Thiếu dòng này thì container lên với DB rỗng và trang trắng.

- [ ] **Step 2: Viết `.dockerignore`**

```
**/bin
**/obj
**/App_Data
.git
docs
```

Loại `App_Data` để không copy DB máy dev vào image.

- [ ] **Step 3: Build thử image**

Run: `docker build -f src/Ti2026.Web/Dockerfile -t ti2026-web:test .`
Expected: build thành công

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "chore: Dockerfile multi-stage + dockerignore"
```

---

## Task 22: docker-compose và Caddy

**Files:**
- Create: `docker-compose.yml`, `.env.example`
- Modify: `.gitignore`
- Modify (repo khác): `../PhuongKhanhTravel/Caddyfile`, `../PhuongKhanhTravel/docker-compose.yml`

- [ ] **Step 1: Viết `docker-compose.yml`**

```yaml
services:
  ti2026:
    build:
      context: .
      dockerfile: src/Ti2026.Web/Dockerfile
    container_name: ti2026-web
    restart: unless-stopped
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Ti2026__PathBase=/ti2026
      - Ti2026__IngestIntervalHours=6
      - Ti2026__IngestToken=${TI2026_INGEST_TOKEN}
    volumes:
      - ti2026_data:/app/App_Data
    expose:
      - "8080"
    networks:
      - edge

volumes:
  ti2026_data:

networks:
  edge:
    external: true
```

Service tên `ti2026` **không** phải `web`: compose của PhuongKhanh đã có service `web`, để chung network `edge` là xung đột alias.

- [ ] **Step 2: Tạo `.env.example` và cập nhật `.gitignore`**

`.env.example`:
```
# Token bảo vệ POST api/ingest/run. Sinh bằng: openssl rand -hex 32
TI2026_INGEST_TOKEN=
```

Thêm vào `.gitignore`:
```
.env
bin/
obj/
App_Data/
```

`.env` **phải** bị ignore — token lọt vào git là mất tác dụng bảo vệ.

- [ ] **Step 3: Tạo network dùng chung trên VPS**

```bash
docker network create edge
```

Chạy một lần. Nếu đã tồn tại, Docker báo lỗi vô hại.

- [ ] **Step 4: Sửa `Caddyfile` bên PhuongKhanhTravel**

```caddyfile
:80 {
	handle /ti2026/* {
		reverse_proxy ti2026-web:8080
	}
	reverse_proxy web:8080
}
```

Dùng `handle` **không phải** `handle_path`: prefix `/ti2026` không bị cắt, app tự xử lý qua `UsePathBase`. Cắt prefix thì mọi URL app sinh ra sẽ trỏ về `/` và đâm vào web du lịch.

- [ ] **Step 5: Thêm network cho caddy bên PhuongKhanhTravel**

```yaml
  caddy:
    networks:
      - default
      - edge

networks:
  edge:
    external: true
```

Phải khai cả `default` — bỏ đi thì caddy mất khả năng gọi `web:8080` của PhuongKhanh.

- [ ] **Step 6: Deploy và kiểm tra CẢ HAI site**

```bash
# trên VPS
cd ~/ti2026-analytics && docker compose up -d --build
cd ~/PhuongKhanhTravel && docker compose up -d   # restart caddy

curl -s -o /dev/null -w "%{http_code}\n" http://localhost/ti2026/
curl -s -o /dev/null -w "%{http_code}\n" http://localhost/
curl -s http://localhost/ti2026/api/health
```
Expected: cả hai trả `200`. **Kiểm tra web du lịch trước khi coi là xong** — đây là site đang kinh doanh thật.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "chore: docker-compose + env example, deploy container rieng tren VPS"
```

---

## Task 23: Dọn dẹp và tài liệu

**Files:**
- Create: `README.md`
- Modify: `index.html` (ở gốc — thêm ghi chú đây là bản tĩnh cũ)

- [ ] **Step 1: Viết README**

Nội dung tối thiểu: project là gì, cách chạy local (`dotnet run --project src/Ti2026.Web`), cách chạy test (`dotnet test`), cách deploy (`docker compose up -d --build`), cách chạy ingest tay (`curl -X POST -H "X-Ingest-Token: ..."`), và ghi rõ 4 giai đoạn với trạng thái hiện tại của từng giai đoạn.

- [ ] **Step 2: Quyết định số phận `index.html` và `data/` ở gốc**

`data/` **phải giữ** — nó là nguồn seed và Dockerfile copy vào image.

`index.html` ở gốc: giữ lại, thêm comment ở đầu file ghi rõ đây là bản tĩnh trước khi chuyển sang .NET, bản đang chạy là `src/Ti2026.Web/wwwroot/index.html`. Giữ hai bản trong thời gian đầu là đường lùi rẻ; xoá khi đã tin API chạy ổn định.

- [ ] **Step 3: Chạy toàn bộ verification lần cuối**

```bash
dotnet build
dotnet test
docker build -f src/Ti2026.Web/Dockerfile -t ti2026-web:test .
```
Expected: cả ba thành công, 0 warning

- [ ] **Step 4: Commit và mở PR**

```bash
git add -A && git commit -m "docs: README huong dan chay, test, deploy"
git push -u origin feature/dotnet-pipeline
gh pr create --title "Chuyen sang .NET + pipeline du lieu that (Giai doan 1+2)" --body "..."
```

---

## Tự soát kế hoạch

**Spec coverage** — đối chiếu từng mục spec với task:

| Spec | Task |
|---|---|
| §3 Kiến trúc Hướng A | 1, 3, 14 |
| §4 Cấu trúc project | 1 |
| §5 Mô hình dữ liệu, 11 bảng | 2 |
| §5 `TeamAlias` giải quyết `_note` | 2, 7 |
| §5 `RosterEntry` có lịch sử | 2, 7 |
| §5 H2H tính từ Match, không bảng riêng | 10 |
| §5 Snapshot upsert không nhân đôi | 2 (unique index), 13 |
| §6 Vòng ingest 5 bước | 14 |
| §6 Nhịp 6h, hoãn vòng đầu 30s | 14 |
| §6 robots.txt trước khi scrape | 16 |
| §6 Rate limit 1 req/s | 11 |
| §6 Tải ảnh về, tôn trọng ETag | 17 |
| §7 8 endpoint | 8, 10, 15, 19 |
| §7 `api/trend` mảng phẳng, khuyết dòng | 19 |
| §7 Token bảo vệ ingest/run | 15 |
| §8 Đổi 6 fetch, đường dẫn tương đối | 9 |
| §8 Tab Form dùng biến CSS sẵn có | 20 |
| §9 Network `edge`, `handle` không `handle_path` | 22 |
| §9 `.env` vào `.gitignore` | 22 |
| §10 Sanity gate từng ingester | 5, 14, 18 |
| §10 `try/catch` vòng ngoài BackgroundService | 14 |
| §10 Seed lúc DB trống | 7, 8 |
| §10 `api/health` | 10 |
| §11 5 loại test | 4, 5, 8, 13, 14, 17 |
| §12 7 env var | 3, 22 |
| §14 Điều kiện xong | 10, 15, 20, 22 |

Không còn mục spec nào thiếu task.

**Placeholder scan:** không có "TBD"/"TODO". Task 18 Step 3 cố ý không viết selector cụ thể vì selector phụ thuộc HTML thật trong fixture — kèm chỉ dẫn rõ ràng là đọc fixture rồi viết, không đoán. Task 20 Step 3 cho nguyên tắc vẽ SVG thay vì code đầy đủ vì layout phụ thuộc dữ liệu thật có được sau M2.

**Type consistency đã kiểm:**
- `MatchOutcome` có 8 tham số ở Task 4, dùng đúng 8 ở Task 13 (`Assists` là tham số thứ 5)
- `TeamWindowStats` field khớp giữa Task 4 và `Apply()` ở Task 13
- `SanityThresholds(MinTeams, MinPlayers)` khớp Task 5 / 14
- `SanityCheck.Passed`/`.Reason` khớp Task 5 / 14
- `SeedResult(Skipped, TeamsWritten, PlayersWritten)` khớp Task 7 / 8
- `IngestStatus` enum khớp Task 2 / 10 / 14
- `SnapshotWriter.WriteAsync(DateOnly, CancellationToken)` khớp Task 13 / 14
- `MediaCache.EnsureAsync(string, CancellationToken)` khớp Task 17
- `TeamStatSnapshot.AvgDurationMinutes` (phút, không phải giây) dùng thống nhất ở Task 2 / 8 / 13 / 19

---

## Ghi chú triển khai M2 (cập nhật 2026-08-03)

**Sandbox chặn `api.opendota.com` và `docs.opendota.com`.** `example.com` truy cập được, nên là
chặn theo host chứ không phải mất mạng. Hệ quả:

1. Fixture trong `tests/Ti2026.Tests/Fixtures/` là **tự dựng**, chưa chụp từ API thật. Cách thay
   bằng dữ liệu thật: `tests/Ti2026.Tests/Fixtures/README.md`.
2. Chưa xác minh được `/teams/{id}/matches` thực sự thiếu `assists` / first blood / `series_id`.
   Toàn bộ thiết kế "chưa biết ≠ bằng không" dựa trên giả định đó. **Bước đầu tiên khi có mạng:
   chụp fixture và kiểm bằng mắt.**

**Thay đổi so với kế hoạch gốc:**

| Kế hoạch gốc | Thực tế | Lý do |
|---|---|---|
| 5 chỉ số là `double` | `double?` | OpenDota không cung cấp; `0` sẽ hiển thị như số thật |
| Snapshot ghi đè toàn bộ | Merge, không ghi null lên giá trị đã có | Giữ số biên tập cho 5 cột chưa tính được |
| Không có cột nguồn | `TeamStatSnapshot.Source` | Không bao giờ nhầm số biên tập là số đo |
| `GetTeamMatchesAsync(teamId)` dùng id nào không rõ | `Team.OpenDotaTeamId` + `TeamResolver` | Kế hoạch gốc mâu thuẫn: Task 12 dùng OpenDota team id, Task 14 nói dùng account_id |
| Ingest luôn chạy | `Ti2026__IngestEnabled`, mặc định `false` | Test và dev không được tự gọi ra mạng ngoài |

**`TeamResolver` không đoán.** Không khớp chắc chắn tên/tag thì để `OpenDotaTeamId = null` và ghi
log cảnh báo. Đoán sai sẽ gán toàn bộ ván của một đội cho đội khác — sai lặng lẽ, rất khó phát
hiện về sau, và làm hỏng cả dữ liệu lịch sử đã tích luỹ.

---

## Hiệu chuẩn ngoài mẫu + yếu tố bản game (cập nhật 2026-08-05)

Câu hỏi đặt ra: *"7.41e là bản vá thứ 4 của 7.41. Dữ liệu bản trước vẫn nên có sức nặng nào
đó — có nên đưa bản game vào mô hình không?"*

Đã dựng cơ chế và **đo** thay vì đoán.

**Cơ chế:** `EloOptions.PatchRegression`. Tại mỗi ranh giới bản chính, mọi rating bị kéo về
1500 theo `r ← 1500 + (r − 1500) × (1 − reg)`. Chọn cách này chứ không nhân trọng số cho trận
cũ theo khoảng cách tới bản hiện tại, vì cách sau **không có nhân quả**: nó đánh giá quá khứ
bằng thông tin của tương lai, và mọi hiệu chuẩn hồi tố sau đó đều vô nghĩa.

**Kỷ luật đo:** chia theo thời gian, 70% trận cũ nhất chọn tham số, 30% mới nhất chấm điểm.
Đây không phải hình thức — lưới quét chọn thang **700** trên tập huấn luyện, nhưng trên tập
kiểm định 700 lại gần như tệ nhất. Nếu chọn và báo cáo trên cùng một tập thì đã đổi sang 700.

**Kết quả** (1782 trận, mốc chia 2026-02-13, 515 dự đoán kiểm định):

| Thang | 400 | 500 | 600 | 700 | 800 |
|---|---|---|---|---|---|
| Brier kiểm định | 0.2350 | 0.2351 | **0.2358** | 0.2368 | 0.2377 |

Ghép cặp 400 vs 600: chênh 0.0009, sai số chuẩn 0.0017, **t = −0.52** → không phân biệt được.

| `PatchRegression` | 0 | 0.15 | 0.30 |
|---|---|---|---|
| Brier kiểm định | **0.2358** | 0.2360 | 0.2362 |

Ghép cặp 0 vs 0.30: **t = −0.51** → không có tác dụng đo được.

Thử cả biến thể mạnh nhất — vứt hẳn mọi trận trước 7.41 — so trên **cùng 310 dự đoán**:
chỉ 7.41 cho 0.2338, cả lịch sử cho 0.2311. Dữ liệu bản cũ không những không cần giảm sức
nặng, bỏ đi còn hơi tệ hơn.

**Vì sao:** Elo vốn đã tự quên. K = 24 với hàng trăm ván mỗi đội nghĩa là một trận từ hai năm
trước đã bị ghi đè nhiều lần. Một hệ số quên gắn thêm chỉ lặp lại việc mô hình đang làm sẵn.

**Quyết định:** giữ `ProbabilityScale = 600`, `PatchRegression = 0`. Cơ chế ở lại, có test,
lộ ra ở `api/calibration` để bật khi dữ liệu nói khác.

Giữ 600 chứ không đổi sang 400 vì hai lẽ: (1) đổi *vì* tập kiểm định nói thế là đốt mất chính
tập kiểm định đó; (2) trên nửa dữ liệu mới mô hình đang nghiêng về **dè dặt** (nói 63.5% thì
thực tế 72.2%), mà sai theo hướng dè dặt an toàn hơn hẳn sai theo hướng tự tin.

**Giới hạn phải nhớ:** chỉ số bản game của OpenDota chỉ có bản chính — 7.41a…7.41e đều là 60.
Mô hình phân biệt được 7.40 với 7.41, **không** tách được các bản vá chữ cái.

**Ngưỡng báo động lại tham số:** `MinBrierGainToRetune = 0.002`, đặt theo sai số chuẩn đo
được (0.0017). Trên lưới 30 tổ hợp luôn có một ô nhỉnh hơn ở chữ số thứ tư; hô hoán vì 0.0002
là báo động giả, và một cảnh báo kêu suốt thì chẳng khác gì không có cảnh báo.
