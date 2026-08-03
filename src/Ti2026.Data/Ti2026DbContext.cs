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
        b.Entity<TeamAlias>()
            .HasOne(x => x.Team).WithMany(x => x.Aliases)
            .HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Player>().HasIndex(x => x.OpenDotaAccountId).IsUnique()
            .HasFilter("\"OpenDotaAccountId\" IS NOT NULL");

        b.Entity<Hero>().Property(x => x.Id).ValueGeneratedNever();

        b.Entity<Match>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Match>().HasIndex(x => x.StartTime);
        b.Entity<Match>().HasIndex(x => x.SeriesId);

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
        b.Entity<RosterEntry>()
            .HasOne(x => x.Player).WithMany()
            .HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RosterEntry>()
            .HasOne(x => x.Team).WithMany()
            .HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
    }
}
