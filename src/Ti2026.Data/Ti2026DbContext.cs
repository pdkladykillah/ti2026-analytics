using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
    public DbSet<MatchPlayer> MatchPlayers => Set<MatchPlayer>();
    public DbSet<TeamStatSnapshot> TeamStatSnapshots => Set<TeamStatSnapshot>();
    public DbSet<TierEntry> TierEntries => Set<TierEntry>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<IngestRun> IngestRuns => Set<IngestRun>();
    public DbSet<SeedState> SeedStates => Set<SeedState>();

    /// <summary>
    /// Mọi DateTime ghi xuống đều chuyển sang UTC, mọi DateTime đọc lên đều được gắn
    /// Kind=Utc. Không có converter này thì SQLite trả về Kind=Unspecified, và một lời gọi
    /// ToLocalTime() ở đâu đó sẽ lệch giờ theo múi giờ của máy chủ mà không ai phát hiện.
    ///
    /// Toàn bộ entity dùng DateTime (không phải DateTimeOffset) vì SQLite không hỗ trợ
    /// DateTimeOffset trong ORDER BY — xem ghi chú ở Match.StartTime.
    /// </summary>
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        toDb => toDb.Kind == DateTimeKind.Utc ? toDb : toDb.ToUniversalTime(),
        fromDb => DateTime.SpecifyKind(fromDb, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter = new(
        toDb => toDb.HasValue
            ? (toDb.Value.Kind == DateTimeKind.Utc ? toDb : toDb.Value.ToUniversalTime())
            : toDb,
        fromDb => fromDb.HasValue
            ? DateTime.SpecifyKind(fromDb.Value, DateTimeKind.Utc)
            : fromDb);

    protected override void OnModelCreating(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                if (prop.ClrType == typeof(DateTime))
                    prop.SetValueConverter(UtcConverter);
                else if (prop.ClrType == typeof(DateTime?))
                    prop.SetValueConverter(NullableUtcConverter);
            }
        }

        b.Entity<Team>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<Team>().HasIndex(x => x.OpenDotaTeamId).IsUnique()
            .HasFilter("\"OpenDotaTeamId\" IS NOT NULL");

        b.Entity<TeamAlias>().HasIndex(x => new { x.Alias, x.Source }).IsUnique();
        b.Entity<TeamAlias>()
            .HasOne(x => x.Team).WithMany(x => x.Aliases)
            .HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Player>().HasIndex(x => x.OpenDotaAccountId).IsUnique()
            .HasFilter("\"OpenDotaAccountId\" IS NOT NULL");
        b.Entity<Player>().HasIndex(x => x.NickKey).IsUnique();

        b.Entity<Hero>().Property(x => x.Id).ValueGeneratedNever();

        b.Entity<Match>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Match>().HasIndex(x => x.StartTime);
        b.Entity<Match>().HasIndex(x => x.SeriesId);
        // Ingester quét cột này mỗi vòng để tìm ván chưa có detail
        b.Entity<Match>().HasIndex(x => x.DetailsIngestedAt);

        b.Entity<MatchPlayer>()
            .HasIndex(x => new { x.MatchId, x.AccountId }).IsUnique();
        b.Entity<MatchPlayer>()
            .HasOne(x => x.Match).WithMany(x => x.Players)
            .HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MatchPlayer>()
            .HasOne(x => x.Player).WithMany()
            .HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<MatchPlayer>().HasIndex(x => x.PlayerId);

        // Khoá chống nhân đôi khi ingest chạy lại trong cùng ngày
        b.Entity<TeamStatSnapshot>()
            .HasIndex(x => new { x.TeamId, x.CapturedOn, x.WindowDays }).IsUnique();
        b.Entity<TeamStatSnapshot>()
            .HasOne(x => x.Team).WithMany()
            .HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<TierEntry>().HasIndex(x => new { x.Patch, x.HeroId, x.Position }).IsUnique();

        b.Entity<MediaAsset>().HasIndex(x => x.SourceUrl).IsUnique();
        b.Entity<MediaAsset>().HasIndex(x => x.ContentHash).IsUnique();

        b.Entity<SeedState>().HasIndex(x => x.Key).IsUnique();

        b.Entity<IngestRun>().HasIndex(x => new { x.Source, x.StartedAt });

        b.Entity<RosterEntry>().HasIndex(x => new { x.TeamId, x.ValidTo });

        // Một player chỉ được có ĐÚNG MỘT bản ghi đang hiệu lực trong một đội.
        // Index này là lưới an toàn ở tầng DB: kể cả khi logic seeder sai thì cũng không
        // thể tạo được hai hàng mở, thay vì âm thầm hiện 7 người trên UI đội hình.
        b.Entity<RosterEntry>()
            .HasIndex(x => new { x.TeamId, x.PlayerId }).IsUnique()
            .HasFilter("\"ValidTo\" IS NULL");
        b.Entity<RosterEntry>()
            .HasOne(x => x.Player).WithMany()
            .HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RosterEntry>()
            .HasOne(x => x.Team).WithMany()
            .HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
    }
}
