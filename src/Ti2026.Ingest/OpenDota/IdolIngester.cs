using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Nạp dữ liệu của những tuyển thủ được chọn để HỌC, cùng với "cái neo" cho phép so họ với người
/// chơi pub mà không bị hạng đấu đánh lừa.
///
/// VÌ SAO PHẦN NÀY RẺ HƠN HẲN PHẦN THEO DÕI CÁ NHÂN. Ván chuyên nghiệp gần như luôn đã được parse
/// sẵn — đo thật trên 300 ván gần nhất: Malr1ne 300/300, ATF 300/300, Collapse 253/253. Nên không
/// phải xin parse, không phải chờ, không phải lấy lại ván sau khi parse xong. Một vòng đầy đủ vào
/// khoảng 1.200 lời gọi, tức chừng 0,12 đô.
///
/// BA CÁI BẪY ĐÃ ĐO ĐƯỢC VÀ ĐƯỢC CHẶN Ở ĐÂY:
///
/// 1. TRÙNG TÊN. Có bốn tài khoản mang persona "TOPSON" và ba tài khoản mang "AMMAR_THE_F". Nên
///    hạt giống khoá theo account_id chứ không bao giờ theo tên, và tên chỉ để hiển thị.
///
/// 2. ĐỒNG ĐỘI DÙNG CHUNG VÁN. Malr1ne và ATF cùng đội Falcons, nên phần lớn ván thi đấu của họ
///    LÀ CÙNG MỘT VÁN. Bảng IdolMatch phải có hai hàng (mỗi người một góc nhìn) nhưng bảng neo
///    chỉ được có MỘT hàng, nếu không thì những trận có hai người sẽ bỏ phiếu gấp đôi.
///
/// 3. LOBBY THI ĐẤU LÀ 1, KHÔNG PHẢI 2. Lọc theo 2 ("tournament") thì được đúng con số không.
/// </summary>
public class IdolIngester(
    Ti2026DbContext db,
    OpenDotaClient client,
    ILogger<IdolIngester> logger)
{
    public const string Source = "idol";

    /// <summary>
    /// Khoảng cách tối thiểu giữa hai vòng. Lối chơi của một tuyển thủ không đổi trong nửa ngày,
    /// và mỗi vòng đầy đủ tốn hơn nghìn lời gọi.
    /// </summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromHours(12);

    /// <summary>Số ván gần nhất quét cho mỗi người. Một lời gọi bất kể số này.</summary>
    public const int MatchesPerIdol = 300;

    /// <summary>
    /// Trần số chi tiết ván lấy trong MỘT vòng. Không phải giới hạn kỹ thuật mà là trần chi phí:
    /// vòng đầu tiên cần khoảng 1.200 ván nên sẽ chạy vài vòng mới xong, và đó là chủ ý — một
    /// vòng chạy quá lâu thì mọi trục trặc đều xảy ra giữa chừng.
    /// </summary>
    public const int MaxDetailsPerRun = 400;

    public const int FetchConcurrency = 8;

    /// <summary>
    /// Số ván lấy làm neo cho mỗi hồ. 150 là thoả hiệp giữa hai điều đã đo: mốc "người bình
    /// thường" đứng yên từ khoảng 40 ván trở lên (xem IdolStyle.MinPoolMatches), còn chi phí thì
    /// tuyến tính. 150 cho biên an toàn gấp gần bốn lần mà vẫn chỉ khoảng 0,015 đô mỗi người.
    /// </summary>
    public const int AnchorSampleSize = 150;

    /// <summary>
    /// Hạt giống. account_id đã tra ngược từ proPlayers và đối chiếu tên thật, KHÔNG lấy theo
    /// persona — persona trùng nhau rất nhiều và ba trong bốn người ở đây đều có bản nhái.
    ///
    /// DeclaredRole chỉ để nhóm trên giao diện. Vị trí thật luôn đo lại từ nhãn replay, và hai
    /// thứ đã lệch nhau ngay ở người đầu tiên kiểm: ATF cả đời chỉ 64% số ván có nhãn là offlane
    /// nhưng 48/51 ván gần nhất thì có.
    /// </summary>
    public static readonly (long AccountId, string Name, string Role, string Note, int Sort)[] Seed =
    [
        (94054712, "Topson", "pos2",
            "Ba lần vô địch TI. Lối mid ứng biến, hero pool rộng bất thường.", 1),
        (898455820, "Malr1ne", "pos2",
            "Mid của Team Falcons. Đổi chác rẻ nhất trong nhóm — xem trục giá mỗi pha hạ gục.", 2),
        (106573901, "No[o]ne-", "pos2",
            "Mid kỳ cựu, 88% số ván có nhãn là mid — chuyên biệt nhất nhóm mid.", 3),
        (480412663, "gpk-", "pos2",
            "Mid. Chọn theo dữ liệu chứ không theo đội: tài khoản mang tên gpk trong roster "
            + "Team Spirit chỉ có 40 ván và 0 ván thi đấu, còn đây có 300/300.", 4),
        (201358612, "Nisha", "pos2",
            "Mid của Team Liquid, nhưng 30% số ván ở vị trí khác — linh hoạt nhất nhóm.", 5),

        (302214028, "Collapse", "pos3",
            "Offlane của Team Spirit. Mở giao tranh, chịu đòn thay đội.", 6),
        (183719386, "ATF", "pos3",
            "Offlane của Team Falcons. Gánh sát thương nhiều hơn hẳn một offlaner thường.", 7),

        (321580662, "Yatoro", "pos1",
            "Carry của Team Spirit. 89% số ván có nhãn ở safelane.", 8),
        (1044002267, "Satanic", "pos1",
            "Carry. Mốc so cho những ván safelane có farm cao.", 9),

        (317880638, "Save-", "pos4",
            "63% số ván ở offlane nhưng phần lớn là support — mốc so cho vị trí 4.", 10),
        (136829091, "Whitemon", "pos5",
            "Support. Mang nhãn lane 'safe' như carry, nên chỉ tách được bằng hạng net worth.", 11),
        (73401082, "Dukalis", "pos5",
            "Hard support. 77% số ván nhãn 'safe' — cùng nhãn với Yatoro, ngược hẳn công việc.", 12),
    ];

    /// <summary>
    /// Trả về số ván ghi mới. Trả 0 và KHÔNG gọi mạng nếu chưa tới hạn chạy lại, trừ khi
    /// <paramref name="force"/>.
    /// </summary>
    public async Task<int> IngestAsync(CancellationToken ct, bool force = false)
    {
        await SeedAsync(ct);

        // CỬA CHỈ CHẶN VIỆC QUÉT LẠI DANH SÁCH, không chặn việc rút cạn tồn đọng.
        //
        // Bản đầu bọc cả hai và đó là một lỗi thật: vòng đầu tiên cần khoảng 1.200 chi tiết ván mà
        // trần mỗi vòng là 400, nên sau lần chạy đầu cửa đóng lại 12 giờ và còn 800 ván nằm chờ —
        // trong khi vòng ingest vẫn báo Succeeded. Kết quả là trang chạy trên một phần ba dữ liệu
        // suốt một ngày rưỡi mà không có gì báo là đang thiếu.
        //
        // Mốc thời gian lấy từ chính IdolPlayer.MatchesFetchedAt chứ không từ bảng IngestRuns:
        // nếu đọc IngestRuns thì mỗi vòng rút tồn đọng lại ghi một hàng Succeeded mới, và cửa sẽ
        // không bao giờ mở lại cho lần quét sau.
        var written = await ScanMatchListsAsync(ct, force);
        written += await FetchDetailsAsync(ct);
        await BuildPubAnchorsAsync(ct);
        return written;
    }

    /// <summary>Tạo hoặc cập nhật bốn hàng hạt giống. Khoá theo account_id.</summary>
    private async Task SeedAsync(CancellationToken ct)
    {
        var existing = await db.IdolPlayers.ToDictionaryAsync(x => x.AccountId, ct);

        foreach (var (accountId, name, role, note, sort) in Seed)
        {
            if (!existing.TryGetValue(accountId, out var row))
            {
                row = new IdolPlayer
                {
                    AccountId = accountId,
                    Name = name,
                    NameKey = IdolPlayer.MakeNameKey(name),
                };
                db.IdolPlayers.Add(row);
            }

            // Tên và ghi chú là do người biên tập đặt, nên hạt giống luôn thắng dữ liệu cũ trong
            // DB — sửa một dòng ở đây phải có tác dụng ngay, không phải xoá bảng đi mới đổi được.
            row.Name = name;
            row.NameKey = IdolPlayer.MakeNameKey(name);
            row.DeclaredRole = role;
            row.Note = note;
            row.SortOrder = sort;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Quét danh sách ván của từng người và tạo hàng rỗng cho ván chưa biết. Một lời gọi mỗi người.
    /// Kèm cập nhật hồ sơ (ảnh, persona) — cũng một lời gọi mỗi người.
    /// </summary>
    private async Task<int> ScanMatchListsAsync(CancellationToken ct, bool force)
    {
        var idols = await db.IdolPlayers.OrderBy(x => x.SortOrder).ToListAsync(ct);
        var written = 0;
        var cutoff = DateTime.UtcNow - MinInterval;

        foreach (var idol in idols)
        {
            ct.ThrowIfCancellationRequested();

            // GÁC THEO TỪNG NGƯỜI, không phải một cửa chung cho cả nhóm.
            //
            // Bản đầu hỏi "có ai tới hạn không" rồi quét cả bốn. Sai ở chỗ: thêm một người mới vào
            // hạt giống sẽ kéo theo việc quét lại ba người vừa quét mười phút trước, tức 6 lời gọi
            // thừa mỗi lần. Mốc thì đã có sẵn trên từng hàng nên không cần cửa chung.
            if (!force
                && idol.MatchesFetchedAt is DateTime last
                && last >= cutoff)
            {
                logger.LogInformation("Bỏ qua quét {Name}: mới quét cách đây {Hours:0.0} giờ",
                    idol.Name, (DateTime.UtcNow - last).TotalHours);
                continue;
            }

            try
            {
                var profile = await client.GetPlayerAsync(idol.AccountId, ct);
                if (profile?.Profile is { } p)
                {
                    idol.PersonaName = p.PersonaName ?? idol.PersonaName;
                    idol.AvatarUrl = p.AvatarFull ?? idol.AvatarUrl;
                }

                idol.ProfileFetchedAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                logger.LogInformation(ex, "Không lấy được hồ sơ {Name}", idol.Name);
            }

            List<OpenDotaPlayerMatch> matches;
            try
            {
                matches = await client.GetPlayerMatchesAsync(idol.AccountId, MatchesPerIdol, ct);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                logger.LogInformation(ex, "Không lấy được danh sách ván của {Name}", idol.Name);
                continue;
            }

            var known = await db.IdolMatches
                .Where(m => m.IdolPlayerId == idol.Id)
                .Select(m => m.MatchId)
                .ToListAsync(ct);

            var seen = known.ToHashSet();

            foreach (var m in matches)
            {
                // Ván chưa biết kết quả thì bỏ hẳn, không ghi Won = false: một ván đang diễn ra
                // được ghi là thua sẽ kéo tỷ lệ thắng xuống và không bao giờ tự sửa.
                if (m.Won is not bool won) continue;
                if (!seen.Add(m.MatchId)) continue;

                // Hàng RỖNG có chủ ý: danh sách ván không trả về thời lượng lẫn K/D/A (endpoint
                // chỉ trả những trường đó khi hỏi kèm project=), và mọi ván ở đây đều sẽ được lấy
                // chi tiết đầy đủ ngay sau. Hỏi thêm project= chỉ để rồi ghi đè là lời gọi thừa.
                db.IdolMatches.Add(new IdolMatch
                {
                    IdolPlayerId = idol.Id,
                    MatchId = m.MatchId,
                    HeroId = m.HeroId,
                    StartTime = DateTimeOffset.FromUnixTimeSeconds(m.StartTime).UtcDateTime,
                    Won = won,
                    IsRadiant = m.OnRadiant,
                    LobbyType = m.LobbyType,
                    GameMode = m.GameMode,
                });

                written++;
            }

            idol.MatchesFetchedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        // XOÁ BỘ THEO DÕI SAU vòng lặp, không phải trong vòng lặp.
        //
        // Đây là một lỗi thật của bản đầu. Cả bốn hàng IdolPlayer nạp về trong MỘT truy vấn nên
        // cùng được EF theo dõi; gọi Clear() ở cuối mỗi lượt sẽ tách rời cả bốn, và từ lượt thứ
        // hai trở đi thì MatchesFetchedAt, PersonaName, AvatarUrl gán vào một thực thể đã rời khỏi
        // ngữ cảnh — SaveChanges không ghi gì. Ván mới vẫn được thêm bình thường vì chúng là thực
        // thể mới, nên bề ngoài mọi thứ hoạt động; chỉ có mốc thời gian của ba người sau là vĩnh
        // viễn null, và cửa gác ở đầu hàm sẽ không bao giờ đóng với họ.
        db.ChangeTracker.Clear();
        return written;
    }

    /// <summary>
    /// Lấy chi tiết những ván chưa có, ghi cả chỉ số cá nhân lẫn hàng neo "pro".
    /// TẢI song song, GHI tuần tự — DbContext của EF Core không an toàn nhiều luồng.
    /// </summary>
    private async Task<int> FetchDetailsAsync(CancellationToken ct)
    {
        var pending = await db.IdolMatches
            .Where(m => m.DetailFetchedAt == null)
            .OrderByDescending(m => m.StartTime)
            .Take(MaxDetailsPerRun)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var accounts = await db.IdolPlayers.ToDictionaryAsync(x => x.Id, x => x.AccountId, ct);
        var loaded = new ConcurrentDictionary<long, OpenDotaMatchDetail>();

        await Parallel.ForEachAsync(
            pending,
            new ParallelOptions { MaxDegreeOfParallelism = FetchConcurrency, CancellationToken = ct },
            async (row, token) =>
            {
                try
                {
                    loaded[row.Id] = await client.GetMatchAsync(row.MatchId, token);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Không lấy được chi tiết ván {Match}", row.MatchId);
                }
            });

        // Ván nào đã có hàng neo rồi thì thôi — xem bẫy số 2 ở phần mô tả lớp. Khoá theo CẶP
        // (hồ, ván): cùng một ván không bao giờ thuộc hai hồ, nhưng khoá chỉ theo ván thì hai hồ
        // sẽ chặn nhầm lẫn nhau nếu về sau có thêm hồ thứ ba.
        var matchIds = pending.Select(m => m.MatchId).ToList();
        var anchored = (await db.StyleAnchors
                .Where(a => matchIds.Contains(a.MatchId))
                .Select(a => new { a.Pool, a.MatchId })
                .ToListAsync(ct))
            .Select(a => (a.Pool, a.MatchId))
            .ToHashSet();

        var teamNames = new Dictionary<int, (DateTime When, string Name)>();
        var done = 0;

        foreach (var row in pending)
        {
            if (!loaded.TryGetValue(row.Id, out var detail)) continue;
            if (!accounts.TryGetValue(row.IdolPlayerId, out var accountId)) continue;

            var all = detail.Players ?? [];
            var me = all.FirstOrDefault(p => p.AccountId == accountId);

            if (me is null)
            {
                // Ván có thật nhưng không tìm thấy người này. Đánh dấu đã xử lý để không hỏi mãi.
                row.DetailFetchedAt = DateTime.UtcNow;
                continue;
            }

            ReadPlayer(row, detail, me, all);

            if (AnchorPoolFor(row.LobbyType) is { } pool
                && anchored.Add((pool, row.MatchId))
                && MakeAnchor(pool, detail) is { } anchor)
                db.StyleAnchors.Add(anchor);

            // Tên đội CHỈ đọc từ ván có leagueid thật.
            //
            // Bản đầu chỉ kiểm "trường tên có rỗng không", vì tài liệu của chính nó ghi rằng ván
            // xếp hạng luôn để trống hai trường này. SAI: phòng chờ pub cũng đặt được tên, và trên
            // dữ liệu thật Topson hiện lên với đội "Sniper monkeys" — tên một nhóm pub, đứng ngay
            // cạnh Team Falcons và Team Spirit như thể ngang hàng. leagueid mới là thứ phân biệt
            // được giải đấu với phòng chờ tự đặt tên.
            var side = row.IsRadiant ? detail.RadiantName : detail.DireName;
            if (row.LeagueId is int league && league != 0
                && !string.IsNullOrWhiteSpace(side)
                && (!teamNames.TryGetValue(row.IdolPlayerId, out var cur) || row.StartTime > cur.When))
                teamNames[row.IdolPlayerId] = (row.StartTime, side);

            row.DetailFetchedAt = DateTime.UtcNow;
            done++;
        }

        // CHỈ GHI ĐÈ KHI VÁN MỚI HƠN VÁN ĐÃ SINH RA TÊN ĐANG LƯU.
        //
        // `teamNames` chỉ chứa ván của LÔ NÀY (tối đa 400 ván mỗi vòng), nên "mới nhất trong lô"
        // không phải "mới nhất của người đó". Bản trước ghi đè vô điều kiện, và kết quả là tên
        // đội bị quyết định bởi lô nào tình cờ chạy sau cùng: Satanic ra "Team Falcons" còn hai
        // đồng đội cùng ván ra "PVISION". So với mốc đã lưu thì thứ tự lô không còn ảnh hưởng gì.
        foreach (var (idolId, (when, name)) in teamNames)
        {
            var idol = await db.IdolPlayers.FirstOrDefaultAsync(x => x.Id == idolId, ct);
            if (idol is null || when <= idol.TeamNameAt) continue;

            idol.TeamName = name;
            idol.TeamNameAt = when;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        logger.LogInformation("Đọc {Done}/{Total} chi tiết ván idol", done, pending.Count);
        return done;
    }

    public const string ProPool = "pro";

    /// <summary>
    /// Hồ ván xếp hạng CỦA CÁC TUYỂN THỦ. Tách khỏi hồ pub của người dùng vì đây là pub hạng
    /// Immortal — cùng là "pub" nhưng khác thang, và gộp lại thì đúng bằng cái sai mà cả hệ neo
    /// này sinh ra để chặn.
    ///
    /// Có hồ này thì Topson mới đọc được: anh chỉ còn 22 ván thi đấu trong 12 tháng, dưới ngưỡng
    /// tối thiểu, nhưng có khoảng 228 ván xếp hạng. Với một người chơi pub muốn học thì đó còn là
    /// tài liệu sát hơn — cùng một loại ván, cùng một loại đồng đội ngẫu nhiên.
    /// </summary>
    public const string IdolPubPool = "pub-idol";

    public static string PubPool(int trackedPlayerId) => "pub-" + trackedPlayerId;

    /// <summary>
    /// Ván này neo vào hồ nào. null = không neo: Battle Cup, Turbo và chế độ vui có nhịp riêng,
    /// trộn vào sẽ làm lệch mốc "người bình thường" của cả hai hồ kia.
    /// </summary>
    public static string? AnchorPoolFor(int? lobbyType) => lobbyType switch
    {
        1 or 2 => ProPool,
        0 or 7 => IdolPubPool,
        _ => null,
    };

    /// <summary>Nhánh hiển thị của một ván idol: ván thi đấu hay ván xếp hạng.</summary>
    public static string? TrackFor(int? lobbyType) => lobbyType switch
    {
        1 or 2 => "thi-dau",
        0 or 7 => "pub",
        _ => null,
    };

    /// <summary>
    /// Dựng hàng neo cho hồ ván pub của từng người được theo dõi.
    ///
    /// CHỈ LẤY VÁN ĐÃ PARSE, vì cột hiệu suất lane chỉ tồn tại ở đó. Hệ quả phải nói ra: mẫu neo
    /// lệch về phía các ván GẦN ĐÂY, do những lần xin parse đều nhắm vào ván mới. Với mục đích ở
    /// đây thì lệch như vậy còn có lợi — ta muốn so với phong độ hiện tại, không phải với người
    /// chơi của năm 2019 — nhưng nó là lựa chọn, không phải tình cờ.
    /// </summary>
    private async Task BuildPubAnchorsAsync(CancellationToken ct)
    {
        var players = await db.TrackedPlayers.Select(p => p.Id).ToListAsync(ct);

        foreach (var playerId in players)
        {
            ct.ThrowIfCancellationRequested();

            var pool = PubPool(playerId);
            var have = await db.StyleAnchors.CountAsync(a => a.Pool == pool, ct);
            if (have >= AnchorSampleSize) continue;

            var known = (await db.StyleAnchors
                    .Where(a => a.Pool == pool)
                    .Select(a => a.MatchId)
                    .ToListAsync(ct))
                .ToHashSet();

            var candidates = await db.TrackedPlayerMatches
                .Where(m => m.TrackedPlayerId == playerId
                            && m.LaneRole != null
                            && m.DurationSeconds > 600)
                .OrderByDescending(m => m.StartTime)
                .Select(m => m.MatchId)
                .Take(AnchorSampleSize * 2)
                .ToListAsync(ct);

            var want = candidates.Where(id => !known.Contains(id))
                .Take(AnchorSampleSize - have)
                .ToList();

            if (want.Count == 0) continue;

            var loaded = new ConcurrentDictionary<long, OpenDotaMatchDetail>();

            await Parallel.ForEachAsync(
                want,
                new ParallelOptions { MaxDegreeOfParallelism = FetchConcurrency, CancellationToken = ct },
                async (matchId, token) =>
                {
                    try
                    {
                        loaded[matchId] = await client.GetMatchAsync(matchId, token);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Không lấy được ván neo {Match}", matchId);
                    }
                });

            foreach (var detail in loaded.Values)
                if (MakeAnchor(pool, detail) is { } anchor)
                    db.StyleAnchors.Add(anchor);

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            logger.LogInformation("Neo hồ {Pool}: thêm {Count} ván", pool, loaded.Count);
        }
    }

    /// <summary>
    /// Cộng tổng cả 10 người của một ván. Trả null khi ván thiếu người — một ván 9 người sẽ kéo
    /// mọi tỉ số "người bình thường" lệch đi mà không có gì báo.
    /// </summary>
    public static StyleAnchor? MakeAnchor(string pool, OpenDotaMatchDetail detail)
    {
        var all = detail.Players ?? [];
        if (all.Count < 10 || detail.Duration <= 0) return null;

        var anchor = new StyleAnchor
        {
            Pool = pool,
            MatchId = detail.MatchId,
            StartTime = DateTimeOffset.FromUnixTimeSeconds(detail.StartTime).UtcDateTime,
            DurationSeconds = detail.Duration,
            PlayerCount = all.Count,
            FetchedAt = DateTime.UtcNow,
        };

        foreach (var p in all)
        {
            anchor.AllKills += p.Kills;
            anchor.AllAssists += p.Assists;
            anchor.AllDeaths += p.Deaths;
            anchor.AllNetWorth += p.NetWorth ?? 0;
            anchor.AllHeroDamage += p.HeroDamage ?? 0;
            anchor.AllLastHits += p.LastHits ?? 0;
            anchor.AllTowerDamage += p.TowerDamage ?? 0;
            anchor.AllDamageTaken += TotalDamageTaken(p) ?? 0;

            if (p.LaneEfficiencyPct is double le && le > 0)
            {
                anchor.AllLaneEfficiency += (long)Math.Round(le);
                anchor.LaneEfficiencyCount++;
            }
        }

        return anchor;
    }

    /// <summary>
    /// Tổng sát thương phải chịu. null khi ván chưa parse — KHÔNG trả 0, vì 0 nghĩa là "không hề
    /// bị đánh" còn null nghĩa là "không đo được", và gộp hai thứ đó lại sẽ kéo mọi trung vị xuống.
    /// </summary>
    public static int? TotalDamageTaken(OpenDotaMatchPlayer p)
    {
        if (p.DamageTaken is null || p.DamageTaken.Count == 0) return null;

        var sum = 0;
        foreach (var v in p.DamageTaken.Values) sum += v;
        return sum;
    }

    private static void ReadPlayer(
        IdolMatch row, OpenDotaMatchDetail detail,
        OpenDotaMatchPlayer me, List<OpenDotaMatchPlayer> all)
    {
        row.DurationSeconds = detail.Duration;
        row.LobbyType = detail.LobbyType ?? row.LobbyType;
        row.GameMode = detail.GameMode ?? row.GameMode;
        row.LeagueId = detail.LeagueId is long lid ? (int)lid : null;
        row.PatchId = detail.Patch;

        row.Kills = me.Kills;
        row.Deaths = me.Deaths;
        row.Assists = me.Assists;
        row.LastHits = me.LastHits;
        row.Denies = me.Denies;
        row.GoldPerMin = me.GoldPerMin;
        row.XpPerMin = me.XpPerMin;
        row.NetWorth = me.NetWorth;
        row.Level = me.Level;
        row.HeroDamage = me.HeroDamage;
        row.TowerDamage = me.TowerDamage;
        row.HeroHealing = me.HeroHealing;
        row.DamageTaken = TotalDamageTaken(me);
        row.ObsPlaced = me.ObserversPlaced;
        row.SenPlaced = me.SentriesPlaced;
        row.CampsStacked = me.CampsStacked;
        row.Stuns = me.StunSeconds;
        row.TeamfightParticipation = me.TeamfightParticipation;

        row.LaneRole = me.LaneRole is int lr && lr > 0 ? lr : null;
        row.Lane = me.Lane;
        row.IsRoaming = me.IsRoaming;
        row.LaneEfficiency = me.LaneEfficiencyPct is double le ? (int)Math.Round(le) : null;

        // Mạng hạ đầu tiên. kills_log chỉ có ở ván đã parse; danh sách rỗng nghĩa là cả ván không
        // giết ai, và đó là null chứ không phải 0 — 0 sẽ đọc thành "giết ngay giây đầu".
        row.FirstKillSecond = me.KillsLog is { Count: > 0 } log ? log.Min(k => k.Time) : null;

        var meRadiant = me.PlayerSlot < 128;
        row.IsRadiant = meRadiant;

        var team = all.Where(p => (p.PlayerSlot < 128) == meRadiant).ToList();

        // Chỉ cộng khi đủ 5 người: thiếu người thì phần đóng góp đổi nghĩa, và một "23% của đội"
        // tính trên 4 người đọc như tính trên 5 người là sai lệch âm thầm.
        if (team.Count == 5)
        {
            // Hạng 1 = giàu nhất. Người thiếu net worth coi như 0 nên rơi xuống cuối, đúng chỗ:
            // ván nào thiếu số liệu thì hạng của người đó cũng không đáng tin.
            row.TeamFarmRank = team.Count(p => (p.NetWorth ?? 0) > (me.NetWorth ?? 0)) + 1;

            row.TeamKills = team.Sum(p => p.Kills);
            row.TeamDeaths = team.Sum(p => p.Deaths);
            row.TeamNetWorth = team.Sum(p => (long)(p.NetWorth ?? 0));
            row.TeamHeroDamage = team.Sum(p => (long)(p.HeroDamage ?? 0));

            var taken = team.Select(TotalDamageTaken).ToList();
            row.TeamDamageTaken = taken.All(x => x is not null)
                ? taken.Sum(x => (long)x!.Value)
                : null;
        }

        ReadGoldAdvantage(row, detail, meRadiant);
    }

    /// <summary>
    /// Chênh lệch vàng ở phút 10/20/30, ĐÃ đảo dấu cho phe Dire.
    ///
    /// radiant_gold_adv luôn là Radiant trừ Dire. Quên đảo thì gần nửa số ván đọc ngược và kết
    /// quả trung hoà về 0 mà vẫn trông hoàn toàn hợp lý — không có gì đổ vỡ để báo.
    /// </summary>
    private static void ReadGoldAdvantage(IdolMatch row, OpenDotaMatchDetail detail, bool meRadiant)
    {
        var adv = detail.RadiantGoldAdv;
        if (adv is null || adv.Count == 0) return;

        var sign = meRadiant ? 1 : -1;
        int? At(int minute) => minute < adv.Count ? adv[minute] * sign : null;

        row.GoldAdv10 = At(10);
        row.GoldAdv20 = At(20);
        row.GoldAdv30 = At(30);
    }

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or OperationCanceledException or TimeoutException
            or System.Text.Json.JsonException;
}
