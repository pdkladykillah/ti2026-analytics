using System.Net;
using FluentAssertions;
using Ti2026.Ingest.OpenDota;

namespace Ti2026.Tests;

/// <summary>
/// Không test nào gọi mạng thật. Bộ test phụ thuộc api.opendota.com còn sống sẽ đỏ vì lý do
/// chẳng liên quan gì đến code.
///
/// LƯU Ý: fixture trong Fixtures/ là TỰ DỰNG, chưa chụp từ API thật (sandbox chặn host).
/// Xem Fixtures/README.md để biết cách thay bằng dữ liệu thật.
/// </summary>
public class OpenDotaClientTests
{
    private sealed class StubHandler(string json, HttpStatusCode code = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private static OpenDotaClient ClientWith(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(new HttpClient(new StubHandler(json, code))
        {
            BaseAddress = new Uri("https://api.opendota.com/api/"),
        });

    internal static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public async Task Doc_duoc_danh_sach_tran_tu_fixture()
    {
        var matches = await ClientWith(Fixture("opendota-team-matches.json"))
            .GetTeamMatchesAsync(9000001, CancellationToken.None);

        matches.Should().HaveCount(4);

        var first = matches[0];
        first.MatchId.Should().Be(8500000001);
        first.Radiant.Should().BeTrue();
        first.RadiantWin.Should().BeTrue();
        first.RadiantScore.Should().Be(31);
        first.DireScore.Should().Be(18);
        first.Duration.Should().Be(2410);
        first.LeagueName.Should().Be("The International 2026");
        first.OpposingTeamId.Should().Be(9000002);
    }

    [Fact]
    public async Task Doc_duoc_danh_sach_doi_tu_fixture()
    {
        var teams = await ClientWith(Fixture("opendota-teams.json"))
            .GetTeamsAsync(CancellationToken.None);

        teams.Should().HaveCount(4);
        teams[0].TeamId.Should().Be(9000001);
        teams[0].Name.Should().Be("Alpha Legends");
        teams[0].Tag.Should().Be("ALP");
        teams[3].Tag.Should().BeNull("tag có thể vắng, không được nổ");
    }

    [Fact]
    public async Task Nguon_tra_mang_rong_thi_tra_danh_sach_rong()
    {
        var matches = await ClientWith("[]").GetTeamMatchesAsync(1, CancellationToken.None);
        matches.Should().BeEmpty();
    }

    /// <summary>
    /// "Nguồn nói không có gì" và "không gọi được nguồn" phải dẫn tới hai hành vi khác nhau.
    /// Nuốt lỗi thành danh sách rỗng sẽ khiến sanity gate ghi nhầm nguyên nhân vào IngestRun.
    /// </summary>
    [Fact]
    public async Task Nguon_loi_thi_NEM_chu_khong_tra_danh_sach_rong()
    {
        var act = () => ClientWith("upstream boom", HttpStatusCode.InternalServerError)
            .GetTeamMatchesAsync(1, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Bi_chan_403_cung_nem()
    {
        var act = () => ClientWith("forbidden", HttpStatusCode.Forbidden)
            .GetTeamsAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
