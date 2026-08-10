using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ti2026.Data.Entities;

namespace Ti2026.Data;

public class Ti2026DbContext(DbContextOptions<Ti2026DbContext> options) : DbContext(options)
{
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamAlias> TeamAliases => Set<TeamAlias>();
    public DbSet<TeamOpenDotaId> TeamOpenDotaIds => Set<TeamOpenDotaId>();
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
    public DbSet<Prediction> Predictions => Set<Prediction>();
    public DbSet<ScheduledSeries> ScheduledSeries => Set<ScheduledSeries>();
    public DbSet<TrackedPlayer> TrackedPlayers => Set<TrackedPlayer>();
    public DbSet<TrackedPlayerMatch> TrackedPlayerMatches => Set<TrackedPlayerMatch>();
    public DbSet<TrackedMatchTeammate> TrackedMatchTeammates => Set<TrackedMatchTeammate>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<DraftEvent> DraftEvents => Set<DraftEvent>();
    public DbSet<ItemPurchase> ItemPurchases => Set<ItemPurchase>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<HeroStat> HeroStats => Set<HeroStat>();
    public DbSet<ProPubMatch> ProPubMatches => Set<ProPubMatch>();

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

        // Khoá tự nhiên của một nút bảng đấu là (giải, node_id) — Valve đánh số nút lại từ đầu
        // cho mỗi giải. Unique để nạp lại nhiều lần chỉ cập nhật chứ không nhân bản bảng đấu.
        b.Entity<ScheduledSeries>().HasIndex(x => new { x.LeagueId, x.NodeId }).IsUnique();

        // KHAI KHOÁ NGOẠI TƯỜNG MINH. Quy ước của EF tìm cột tên "Team1Id" cho navigation
        // "Team1"; tên của ta là "TeamId1" nên nó KHÔNG khớp, và EF lặng lẽ tạo thêm một cột
        // bóng Team1Id rồi để .Include đọc cột đó. Hậu quả: ingest ghi đúng vào TeamId1, còn
        // trang đọc Team1 thì luôn rỗng — bảng đấu hiện "#9247354" thay vì "Team Falcons", và
        // vì đó đúng bằng cách hiển thị dành cho đội chưa ánh xạ được nên nhìn như tính năng.
        b.Entity<ScheduledSeries>()
            .HasOne(x => x.Team1).WithMany().HasForeignKey(x => x.TeamId1);
        b.Entity<ScheduledSeries>()
            .HasOne(x => x.Team2).WithMany().HasForeignKey(x => x.TeamId2);
        b.Entity<TeamAlias>()
            .HasOne(x => x.Team).WithMany(x => x.Aliases)
            .HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);

        // Một team_id của OpenDota chỉ được thuộc về ĐÚNG MỘT đội của ta. Không có ràng buộc
        // này thì một lần thêm nhầm sẽ quy toàn bộ ván của một đội cho hai đội cùng lúc, và
        // mọi con số suy ra đều sai mà không có gì đổ vỡ để báo.
        b.Entity<TeamOpenDotaId>().HasIndex(x => x.OpenDotaTeamId).IsUnique();
        b.Entity<TeamOpenDotaId>()
            .HasOne(x => x.Team).WithMany(x => x.OpenDotaIds)
            .HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<TrackedPlayer>().HasIndex(x => x.AccountId).IsUnique();

        // Một người + một ván là duy nhất. Chốt này biến việc nạp lại thành vô hại thay vì
        // nhân bản lịch sử mỗi vòng.
        b.Entity<TrackedPlayerMatch>().HasIndex(x => new { x.TrackedPlayerId, x.MatchId }).IsUnique();
        b.Entity<TrackedPlayerMatch>().HasIndex(x => x.StartTime);
        b.Entity<TrackedPlayerMatch>().HasIndex(x => x.HeroId);
        b.Entity<TrackedPlayerMatch>()
            .HasOne(x => x.TrackedPlayer).WithMany()
            .HasForeignKey(x => x.TrackedPlayerId).OnDelete(DeleteBehavior.Cascade);

        // Một người trong một ván chỉ xuất hiện một lần. Không có chốt này thì mỗi lần lấy lại
        // chi tiết ván (chuyện vẫn xảy ra sau khi xin parse) sẽ nhân đôi số ván đã chơi cùng.
        b.Entity<TrackedMatchTeammate>()
            .HasIndex(x => new { x.TrackedPlayerMatchId, x.AccountId }).IsUnique();
        b.Entity<TrackedMatchTeammate>().HasIndex(x => x.AccountId);
        b.Entity<TrackedMatchTeammate>()
            .HasOne(x => x.Match).WithMany(x => x.Teammates)
            .HasForeignKey(x => x.TrackedPlayerMatchId).OnDelete(DeleteBehavior.Cascade);

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

        // Thứ tự là duy nhất trong một trận — chốt này biến việc nạp lại thành vô hại thay vì
        // nhân đôi bàn draft mỗi lần chạy.
        b.Entity<DraftEvent>().HasIndex(x => new { x.MatchId, x.Order }).IsUnique();
        b.Entity<DraftEvent>().HasIndex(x => x.HeroId);
        b.Entity<DraftEvent>()
            .HasOne(x => x.Match).WithMany()
            .HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);

        // Không đặt khoá duy nhất: một người mua tango ba lần ở cùng một giây là chuyện thật.
        // Chống trùng bằng cách xoá sạch theo trận rồi ghi lại, không bằng ràng buộc.
        b.Entity<ItemPurchase>().HasIndex(x => new { x.HeroId, x.ItemKey });
        b.Entity<ItemPurchase>().HasIndex(x => x.MatchId);
        b.Entity<ItemPurchase>()
            .HasOne(x => x.Match).WithMany()
            .HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);

        // Một người + một ván là duy nhất. Chốt này biến việc chạy lại job thành vô hại.
        b.Entity<ProPubMatch>().HasIndex(x => new { x.PlayerId, x.MatchId }).IsUnique();
        b.Entity<ProPubMatch>().HasIndex(x => x.StartTime);
        b.Entity<ProPubMatch>().HasIndex(x => x.HeroId);
        b.Entity<ProPubMatch>()
            .HasOne(x => x.Player).WithMany()
            .HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Cascade);

        // Khoá chính là hero id của OpenDota, không tự tăng. Ba tỷ lệ là thuộc tính tính toán
        // nên không lưu: lưu cả tử số, mẫu số lẫn thương số là ba chỗ có thể lệch nhau.
        b.Entity<HeroStat>().HasKey(x => x.HeroId);
        b.Entity<HeroStat>().Property(x => x.HeroId).ValueGeneratedNever();
        b.Entity<HeroStat>().Ignore(x => x.PubWinrate);
        b.Entity<HeroStat>().Ignore(x => x.HighWinrate);
        b.Entity<HeroStat>().Ignore(x => x.ProWinrate);

        // Khoá chính là chuỗi vì đó là thứ ItemPurchase lưu; thêm một id số chỉ tạo thêm một
        // bước tra cứu mà không giải quyết gì.
        b.Entity<Item>().HasKey(x => x.Key);
        b.Entity<Item>().Ignore(x => x.IsConsumable);

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

        b.Entity<League>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<League>().HasIndex(x => x.Tier);

        b.Entity<Prediction>().HasIndex(x => x.CreatedAt);
        b.Entity<Prediction>().HasIndex(x => x.ResolvedMatchId);
        b.Entity<Prediction>()
            .HasOne(x => x.TeamA).WithMany()
            .HasForeignKey(x => x.TeamAId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Prediction>()
            .HasOne(x => x.TeamB).WithMany()
            .HasForeignKey(x => x.TeamBId).OnDelete(DeleteBehavior.Restrict);

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
