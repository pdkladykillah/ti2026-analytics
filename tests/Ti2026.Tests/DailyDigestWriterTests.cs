using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ti2026.Data;
using Ti2026.Data.Entities;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Tests;

/// <summary>
/// Chốt ngày là chỗ dễ sai nhất của điểm nhấn ngày, và nó đã sai thật.
///
/// Hai nguồn chạy hai nhịp: bảng đấu của Valve làm tươi mỗi 15 phút, còn chi tiết ván đi theo
/// vòng ingest 6 giờ. Bản đầu chốt ngay khi Valve báo mọi series đã xong, nên ngày bị khoá
/// trước khi phần ván kịp về — và vì ngày đã chốt thì không tính lại nữa, con số thiếu đó
/// thành vĩnh viễn. Người dùng phát hiện sau bốn ngày: 13/08 đứng ở "18/29 ván" trong khi
/// bảng Matches có đủ cả 29.
/// </summary>
public class DailyDigestWriterTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"ti2026-digest-{Guid.NewGuid():N}.db");

    private const long League = 19719;

    /// <summary>
    /// Mốc ngày phải nằm TRONG cửa gia hạn, tính từ bây giờ chứ không viết cứng.
    ///
    /// Viết cứng "2026-08-13" thì bài kiểm đúng vào hôm viết rồi hỏng vĩnh viễn từ hôm sau: quá
    /// 24 giờ là luật gia hạn tự chốt ngày, và ca "chưa được chốt" không còn dựng lại được nữa.
    /// Đây đúng loại bài kiểm mục theo thời gian mà không ai nhận ra cho tới lúc nó đỏ.
    /// </summary>
    private static readonly DateTime DayStart = DateTime.UtcNow.AddHours(-2);

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    private static DailyDigestWriter Make(Ti2026DbContext db) =>
        new(db, NullLogger<DailyDigestWriter>.Instance);

    /// <param name="at">Giờ series diễn ra. Quyết định ngày mà digest gom nó vào.</param>
    private static void AddSeries(Ti2026DbContext db, int node, int w1, int w2, bool done, DateTime at)
        => db.ScheduledSeries.Add(new ScheduledSeries
        {
            LeagueId = League, NodeId = node, GroupName = "Swiss",
            Wins1 = w1, Wins2 = w2, IsCompleted = done, HasStarted = true,
            ScheduledAt = at, ActualAt = at,
        });

    private static void AddMatches(Ti2026DbContext db, int count, DateTime at)
    {
        for (var i = 0; i < count; i++)
            db.Matches.Add(new Match
            {
                Id = 950_000_000 + i, StartTime = at.AddMinutes(i * 45),
                DurationSeconds = 2400, LeagueId = League, RadiantWin = true,
                IngestedAt = at,
            });
    }

    /// <summary>
    /// ĐỦ SERIES NHƯNG THIẾU VÁN THÌ CHƯA ĐƯỢC CHỐT.
    ///
    /// Đây là bài khoá đúng lỗi đã xảy ra. Nếu ai đó rút gọn điều kiện chốt về lại "mọi series
    /// xong là đủ", bài này đỏ — mà không có nó thì hậu quả chỉ lộ ra sau nhiều ngày, dưới dạng
    /// một con số nhỏ hơn sự thật mà không có gì báo là sai.
    /// </summary>
    [Fact]
    public async Task Du_series_nhung_thieu_van_thi_chua_chot()
    {
        await using var db = NewDb();

        AddSeries(db, 1, 2, 0, done: true, DayStart);   // 2 ván
        AddSeries(db, 2, 2, 1, done: true, DayStart);   // 3 ván  -> tổng 5
        AddMatches(db, 3, DayStart);                    // mới đọc được 3
        await db.SaveChangesAsync();

        await Make(db).WriteAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var row = await db.DailyDigests.SingleAsync();

        row.SeriesCompleted.Should().Be(2);
        row.MatchesExpected.Should().Be(5);
        row.MatchesCounted.Should().Be(3);
        row.ClosedAt.Should().BeNull(
            "Valve báo xong không có nghĩa là chi tiết ván đã về — hai nguồn chạy hai nhịp");
    }

    /// <summary>Ván về đủ thì lượt tính sau mới chốt, và chốt đúng con số đầy đủ.</summary>
    [Fact]
    public async Task Van_ve_du_thi_luot_sau_chot()
    {
        await using var db = NewDb();

        AddSeries(db, 1, 2, 0, done: true, DayStart);
        AddSeries(db, 2, 2, 1, done: true, DayStart);
        AddMatches(db, 3, DayStart);
        await db.SaveChangesAsync();

        await Make(db).WriteAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        (await db.DailyDigests.SingleAsync()).ClosedAt.Should().BeNull();

        // Hai ván còn lại về ở vòng ingest sau.
        db.Matches.Add(new Match
        {
            Id = 951_000_001, StartTime = DayStart.AddHours(3), DurationSeconds = 2400,
            LeagueId = League, RadiantWin = true, IngestedAt = DayStart,
        });
        db.Matches.Add(new Match
        {
            Id = 951_000_002, StartTime = DayStart.AddHours(4), DurationSeconds = 2400,
            LeagueId = League, RadiantWin = false, IngestedAt = DayStart,
        });
        await db.SaveChangesAsync();

        await Make(db).WriteAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var row = await db.DailyDigests.SingleAsync();

        row.MatchesCounted.Should().Be(5);
        row.ClosedAt.Should().NotBeNull("đủ series và đủ ván thì mới là một ngày trọn vẹn");
    }

    /// <summary>
    /// NHƯNG KHÔNG CHỜ MÃI. OpenDota thỉnh thoảng không bao giờ công bố một ván nào đó, và chờ
    /// nó là chờ một thứ không tới — ngày đó sẽ bị tính lại mỗi 15 phút cho tới hết đời.
    /// </summary>
    [Fact]
    public async Task Qua_cua_gia_han_thi_chot_du_con_thieu_van()
    {
        await using var db = NewDb();

        var longAgo = DateTime.UtcNow - DailyDigestWriter.CoverageGrace - TimeSpan.FromHours(2);

        AddSeries(db, 1, 2, 0, done: true, longAgo);
        AddMatches(db, 1, longAgo);   // thiếu một ván, và sẽ không bao giờ về
        await db.SaveChangesAsync();

        await Make(db).WriteAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var row = await db.DailyDigests.SingleAsync();

        row.MatchesCounted.Should().Be(1);
        row.MatchesExpected.Should().Be(2);
        row.ClosedAt.Should().NotBeNull("quá 24 giờ thì thiếu ván nghĩa là mất hẳn, không phải đang chờ");
    }

    /// <summary>
    /// Series CHƯA xong thì không chốt, dù ván đã đủ — một ngày còn trận đang đánh chưa phải
    /// lịch sử.
    /// </summary>
    [Fact]
    public async Task Con_series_dang_danh_thi_khong_chot()
    {
        await using var db = NewDb();

        AddSeries(db, 1, 2, 0, done: true, DayStart);
        AddSeries(db, 2, 1, 1, done: false, DayStart);
        AddMatches(db, 4, DayStart);
        await db.SaveChangesAsync();

        await Make(db).WriteAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        (await db.DailyDigests.SingleAsync()).ClosedAt.Should().BeNull();
    }

    /// <summary>
    /// Ngày ĐÃ chốt thì không tính lại — nếu tính lại thì một lần nạp bù dữ liệu cũ sẽ lặng lẽ
    /// viết lại lịch sử.
    /// </summary>
    [Fact]
    public async Task Ngay_da_chot_thi_khong_tinh_lai()
    {
        await using var db = NewDb();

        AddSeries(db, 1, 2, 0, done: true, DayStart);
        AddMatches(db, 2, DayStart);
        await db.SaveChangesAsync();

        await Make(db).WriteAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var first = await db.DailyDigests.SingleAsync();
        first.ClosedAt.Should().NotBeNull();
        var stamp = first.ComputedAt;

        // Thêm một ván "mới" — ngày đã chốt thì nó không được đụng tới con số cũ nữa.
        db.Matches.Add(new Match
        {
            Id = 952_000_001, StartTime = DayStart.AddHours(5), DurationSeconds = 2400,
            LeagueId = League, RadiantWin = true, IngestedAt = DayStart,
        });
        await db.SaveChangesAsync();

        await Make(db).WriteAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var after = await db.DailyDigests.SingleAsync();

        after.MatchesCounted.Should().Be(2, "ngày đã chốt là lịch sử, không đổi theo lần nạp sau");
        after.ComputedAt.Should().Be(stamp);
    }

    /// <summary>Hai ngày khác nhau không bao giờ đè nhau — khoá duy nhất là (giải, ngày).</summary>
    [Fact]
    public async Task Hai_ngay_khac_nhau_khong_de_nhau()
    {
        await using var db = NewDb();

        AddSeries(db, 1, 2, 0, done: true, DayStart);
        AddSeries(db, 2, 2, 0, done: true, DayStart.AddDays(1));
        AddMatches(db, 2, DayStart);
        await db.SaveChangesAsync();

        await Make(db).WriteAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        var rows = await db.DailyDigests.OrderBy(d => d.Day).ToListAsync();

        rows.Should().HaveCount(2);
        rows[0].Day.Should().Be(DateOnly.FromDateTime(DayStart));
        rows[1].Day.Should().Be(DateOnly.FromDateTime(DayStart.AddDays(1)));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(p)) File.Delete(p);
        GC.SuppressFinalize(this);
    }
}
