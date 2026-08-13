using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Ti2026.Data;
using Ti2026.Ingest;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Tests;

/// <summary>
/// Bảng đấu chạy nhịp riêng, nhanh hơn vòng ingest chính — và cái giá của nhịp nhanh nằm ở chỗ
/// mỗi lượt trước đây tải lại toàn bộ danh mục giải của Valve.
/// </summary>
public class ScheduleRefreshTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"ti2026-sched-{Guid.NewGuid():N}.db");

    private Ti2026DbContext NewDb()
    {
        var db = new Ti2026DbContext(new DbContextOptionsBuilder<Ti2026DbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        db.Database.Migrate();
        return db;
    }

    /// <summary>Đếm riêng hai endpoint để phân biệt "tra lại danh mục" với "lấy bảng đấu".</summary>
    private sealed class WebHandler : HttpMessageHandler
    {
        private int _listCalls;
        private int _dataCalls;

        public int ListCalls => Volatile.Read(ref _listCalls);
        public int DataCalls => Volatile.Read(ref _dataCalls);

        /// <summary>Bao nhiêu lời gọi bảng đấu ĐẦU TIÊN trả về thân rỗng, mô phỏng lỗi của Valve.</summary>
        public int EmptyFirst { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            string json;

            if (path.Contains("GetLeagueInfoList"))
            {
                Interlocked.Increment(ref _listCalls);

                // tier 5 là bậc Valve chỉ dùng cho The International — xem TiScheduleIngester.
                json = """
                    {"infos":[
                      {"league_id":19719,"name":"The International 2026","tier":5,"start_timestamp":1786000000},
                      {"league_id":18000,"name":"Giải khác","tier":2,"start_timestamp":1786500000}
                    ]}
                    """;
            }
            else
            {
                var n = Interlocked.Increment(ref _dataCalls);

                // Valve thỉnh thoảng trả về đúng chữ `null` — đo được trên dữ liệu thật giữa giải.
                json = n <= EmptyFirst
                    ? "null"
                    : """
                      {"info":{"name":"The International 2026"},
                       "node_groups":[{"name":"Swiss","nodes":[
                         {"node_id":1,"name":"Match 1.A","team_id_1":0,"team_id_2":0}
                       ]}]}
                      """;
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static TiScheduleIngester Make(Ti2026DbContext db, HttpMessageHandler h, LeagueIdCache cache) =>
        new(db,
            new Dota2WebClient(new HttpClient(h) { BaseAddress = new Uri("https://x/") }),
            cache,
            NullLogger<TiScheduleIngester>.Instance);

    /// <summary>
    /// LƯỢT THỨ HAI KHÔNG ĐƯỢC TẢI LẠI DANH MỤC GIẢI.
    ///
    /// Danh mục là 1,9 MB thô (315 KB nén) và gần 10.000 mục, tải về chỉ để lọc ra một số nguyên.
    /// Ở nhịp 6 giờ cũ thì không đáng bàn; ở nhịp 15 phút để bám theo giải đang đánh thì cùng phép
    /// tính đó ra khoảng 30 MB mỗi ngày. Bài này khoá lại phần tiết kiệm đó — nếu ai bỏ bộ nhớ
    /// đệm đi, mọi thứ vẫn CHẠY ĐÚNG và không có gì báo, chỉ tốn băng thông gấp mấy chục lần.
    /// </summary>
    [Fact]
    public async Task Luot_thu_hai_khong_tai_lai_danh_muc_giai()
    {
        await using var db = NewDb();
        var handler = new WebHandler();
        var cache = new LeagueIdCache();

        await Make(db, handler, cache).IngestAsync(CancellationToken.None);
        await Make(db, handler, cache).IngestAsync(CancellationToken.None);
        await Make(db, handler, cache).IngestAsync(CancellationToken.None);

        handler.ListCalls.Should().Be(1, "id giải đã nhớ rồi thì không tra lại danh mục nữa");
        handler.DataCalls.Should().Be(3, "nhưng bảng đấu thì lượt nào cũng phải lấy mới");
    }

    /// <summary>
    /// THÂN RỖNG PHẢI THỬ LẠI, KHÔNG BỎ LƯỢT.
    ///
    /// Đo trên dữ liệu thật giữa lúc TI đang đánh: gọi thẳng 8 lần cách nhau 15 giây thì 8/8 trả đủ
    /// 27 nút, nhưng app gọi 2 lần thì 1 lần nhận về thân `null` — tức không phải Valve chặn nhịp
    /// mà là lỗi chớp nhoáng phía họ. Với nhịp làm tươi 15 phút, bỏ lượt nghĩa là bảng đấu đứng
    /// yên 15 phút giữa lúc đang có trận.
    /// </summary>
    [Fact]
    public async Task Than_rong_thi_thu_lai_chu_khong_bo_luot()
    {
        await using var db = NewDb();
        var handler = new WebHandler { EmptyFirst = 2 };

        var written = await Make(db, handler, new LeagueIdCache())
            .IngestAsync(CancellationToken.None);

        written.Should().Be(1, "lần thử thứ ba có dữ liệu thì phải ghi được nút");
        handler.DataCalls.Should().Be(3, "hai lần rỗng rồi mới tới lần có dữ liệu");
        (await db.ScheduledSeries.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// Nhưng KHÔNG thử mãi. Nếu Valve hỏng thật thì bỏ lượt và để lượt sau lo — giữ nguyên bảng
    /// đấu cũ còn hơn treo một vòng ingest đang giữ cổng ghi SQLite.
    /// </summary>
    [Fact]
    public async Task Rong_lien_tuc_thi_bo_luot_va_giu_nguyen_bang_cu()
    {
        await using var db = NewDb();
        var handler = new WebHandler { EmptyFirst = 99 };

        var written = await Make(db, handler, new LeagueIdCache())
            .IngestAsync(CancellationToken.None);

        written.Should().Be(0);
        handler.DataCalls.Should().Be(TiScheduleIngester.LoadTries, "thử đúng số lần đã khai, không hơn");
        (await db.ScheduledSeries.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// Bộ nhớ đệm phải HẾT HẠN. Nhớ vĩnh viễn thì sang kỳ TI sau phải khởi động lại container
    /// mới thấy giải mới — đúng loại việc không ai nhớ làm.
    /// </summary>
    [Fact]
    public void Nho_id_giai_co_han_dung()
    {
        var cache = new LeagueIdCache();
        var t0 = new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc);

        cache.TryGet(t0, out _).Should().BeFalse("chưa có gì để nhớ");

        cache.Set(19719, t0);

        cache.TryGet(t0 + LeagueIdCache.Ttl - TimeSpan.FromMinutes(1), out var id).Should().BeTrue();
        id.Should().Be(19719);

        cache.TryGet(t0 + LeagueIdCache.Ttl + TimeSpan.FromMinutes(1), out _).Should().BeFalse(
            "quá hạn thì phải đi tra lại, nếu không TI2027 sẽ không bao giờ xuất hiện");
    }

    /// <summary>
    /// Nhịp nhanh phải NHANH HƠN THẬT SỰ so với vòng ingest chính, nếu không cả việc tách ra là
    /// vô nghĩa. Và nhịp ngoài mùa không được tắt hẳn: khung bảng đấu kỳ sau xuất hiện trước
    /// ngày khai mạc khá lâu.
    /// </summary>
    [Fact]
    public void Nhip_trong_mua_phai_nhanh_hon_vong_ingest_chinh()
    {
        ScheduleRefreshService.InSeason.Should().BeLessThan(TimeSpan.FromHours(1));
        ScheduleRefreshService.InSeason.Should().BeLessThan(ScheduleRefreshService.OffSeason);
        ScheduleRefreshService.OffSeason.Should().BeLessThanOrEqualTo(TimeSpan.FromHours(6));
        ScheduleRefreshService.SeasonWindow.Should().BeGreaterThanOrEqualTo(TimeSpan.FromHours(12),
            "một ngày thi đấu kéo dài nhiều giờ và hay trượt lịch");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(p)) File.Delete(p);
        GC.SuppressFinalize(this);
    }
}
