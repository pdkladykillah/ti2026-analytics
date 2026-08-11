using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;

namespace Ti2026.Ingest.OpenDota;

/// <summary>
/// Lấy BỐI CẢNH CẢ ĐỘI cho từng ván của người được theo dõi, và xin OpenDota parse replay để
/// có vai trò THẬT thay vì suy đoán.
///
/// VÌ SAO PHẢI LÀM. Câu hỏi "tôi chơi vị trí nào" không trả lời được bằng chỉ số của riêng
/// người đó. Đo trên tài khoản thật:
///
///   lane thật    last hits   GPM    XPM
///   safe (pos1)     299      543    678
///   mid  (pos2)     345      667    915
///   off  (pos3)     282      533    707
///
/// Farm ba lane gần như bằng nhau. Một người chơi offlane tốt vẫn farm ngang carry, nên mọi
/// quy tắc dựa vào mức farm tuyệt đối sẽ xếp nhầm họ MỘT CÁCH CÓ HỆ THỐNG — sai đều một chiều,
/// loại sai khó thấy nhất.
///
/// HAI NGUỒN, HAI MỨC TIN CẬY:
///
/// 1. PARSE (nhãn thật). OpenDota không tự parse ván pub — đo thật: 0/25 ván gần nhất có sẵn.
///    Phải chủ động POST /request/{id}. Xong trong khoảng 30 giây. Nhưng replay hết hạn: ván
///    61 ngày tuổi parse được, ván 70 ngày thì không.
///
/// 2. THỨ HẠNG TRONG ĐỘI (suy luận). Có cho mọi ván. Tách core/support rất chắc — chênh lệch
///    net worth giữa hai nhóm là 20k so với 8k. Nhưng KHÔNG tách được mid/safe/off: đã kiểm
///    trên 36 ván có nhãn thật, phân bố thứ hạng của ba lane chồng lên nhau nặng.
///
/// Nên: ván trong cửa sổ replay thì có vai trò thật; ván cũ hơn thì chỉ nói được core hay
/// support, và phải nói rõ là suy luận.
/// </summary>
public class TrackedMatchDetailIngester(
    Ti2026DbContext db, OpenDotaClient client, ILogger<TrackedMatchDetailIngester> logger)
{
    /// <summary>
    /// Chỉ xin parse ván trẻ hơn ngần này ngày. Đo thật: 61 ngày còn parse được, 70 ngày thì
    /// không. Lấy 60 cho chắc — xin ván đã quá hạn chỉ tốn lời gọi mà không bao giờ có kết quả.
    /// </summary>
    public const int ParseWindowDays = 60;

    /// <summary>
    /// Trần mỗi vòng. 1500 để nạp bù cả lịch sử 5.877 ván xong trong khoảng 4 vòng (một ngày)
    /// thay vì 15 vòng (bốn ngày) — nhưng vẫn đủ thấp để không chiếm hết nhịp gọi của phần phân
    /// tích giải, nhất là trong những ngày TI đang diễn ra.
    /// </summary>
    public const int MaxDetailsPerRun = 1500;

    /// <summary>Trần yêu cầu parse mỗi vòng — chúng vào hàng đợi chung của OpenDota.</summary>
    public const int MaxParseRequestsPerRun = 120;

    /// <summary>
    /// Số lời gọi cùng lúc lúc tải chi tiết ván.
    ///
    /// VÌ SAO CẦN. Vòng lặp tuần tự có thông lượng bằng 1 chia cho ĐỘ TRỄ, không phải bằng hạn
    /// mức. Đo trên production: hạn mức cho phép 8 lời gọi/giây nhưng thực tế chỉ đạt 0,6 —
    /// tức mỗi lời gọi mất khoảng 1,6 giây và suốt thời gian đó không có gì khác chạy. Nạp bù
    /// 5.877 ván ở nhịp đó mất gần ba giờ, trong khi hạn mức thừa sức làm trong mười hai phút.
    ///
    /// VÌ SAO KHÔNG CAO HƠN. RateLimitedHandler vẫn là trần thật — nó giãn cách mọi lời gọi bất
    /// kể có bao nhiêu luồng, nên tăng số này chỉ giúp LẤP ĐẦY hạn mức chứ không vượt được. 8 là
    /// đủ để lấp đầy ở độ trễ đo được, và giữ thấp thì lúc nguồn chậm cũng không dồn ứ.
    ///
    /// VÀ CHỈ SONG SONG PHẦN TẢI. DbContext của EF Core không an toàn nhiều luồng, nên toàn bộ
    /// phần ghi vẫn chạy tuần tự sau khi tải xong.
    /// </summary>
    public const int FetchConcurrency = 8;

    public async Task<int> IngestAsync(CancellationToken ct)
    {
        var players = await db.TrackedPlayers.ToDictionaryAsync(p => p.Id, p => p.AccountId, ct);
        if (players.Count == 0) return 0;

        var touched = await FetchDetailsAsync(players, ct);

        // GHI XUỐNG TRƯỚC khi sang bước xin parse. Bước sau hỏi DB "ván nào còn thiếu lane_role",
        // và câu hỏi đó chạy thành SQL nên nó nhìn thấy trạng thái ĐÃ LƯU, không thấy những gì
        // vừa gán trong bộ nhớ. Thiếu dòng này thì mọi ván vừa đọc được nhãn vai trò ở bước trên
        // vẫn bị đem đi xin parse, rồi bị đặt lại DetailFetchedAt = null — tốn thêm một lời gọi
        // cho mỗi ván, và trong lúc đó cột "đã lấy chi tiết" nói sai về chính ván vừa lấy xong.
        await db.SaveChangesAsync(ct);

        touched += await RequestParsesAsync(ct);

        await db.SaveChangesAsync(ct);
        return touched;
    }

    /// <summary>
    /// Lấy matches/{id} để biết cả 10 người, rồi tính thứ hạng trong đội.
    ///
    /// Ưu tiên ván MỚI trước: ván mới vừa là thứ người dùng nhìn nhiều nhất, vừa là thứ còn kịp
    /// xin parse trước khi replay hết hạn.
    /// </summary>
    private async Task<int> FetchDetailsAsync(Dictionary<int, long> players, CancellationToken ct)
    {
        var pending = await db.TrackedPlayerMatches
            .Where(m => m.DetailFetchedAt == null)
            .OrderByDescending(m => m.StartTime)
            .Take(MaxDetailsPerRun)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        // Nạp trước đồng đội đã lưu của đúng những ván sắp xử lý. Một ván ĐƯỢC lấy lại nhiều lần
        // (sau khi xin parse thì DetailFetchedAt bị đặt lại null có chủ ý), nên nếu cứ thêm mới
        // thì số ván đã chơi cùng mỗi người sẽ tăng dần mà không ai thấy sai — bảng vẫn có thứ
        // hạng hợp lý, chỉ là mọi con số đều phóng đại.
        var ids = pending.Select(m => m.Id).ToList();
        var savedMates = (await db.TrackedMatchTeammates
                .Where(t => ids.Contains(t.TrackedPlayerMatchId))
                .ToListAsync(ct))
            .ToDictionary(t => (t.TrackedPlayerMatchId, t.AccountId));

        // TẢI song song, GHI tuần tự. DbContext của EF Core không an toàn nhiều luồng, nên bước
        // dưới chỉ thu về dữ liệu thô; mọi thao tác chạm vào thực thể đều nằm ở vòng lặp sau.
        var loaded = new System.Collections.Concurrent.ConcurrentDictionary<long, OpenDotaMatchDetail>();

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

        var done = 0;

        foreach (var row in pending)
        {
            if (!players.TryGetValue(row.TrackedPlayerId, out var accountId)) continue;

            // Ván tải hỏng thì để nguyên DetailFetchedAt = null, để vòng sau thử lại. Đánh dấu
            // đã xử lý ở đây sẽ biến một trục trặc mạng thoáng qua thành mất dữ liệu vĩnh viễn.
            if (!loaded.TryGetValue(row.Id, out var detail)) continue;

            try
            {
                var all = detail.Players ?? [];

                var me = all.FirstOrDefault(p => p.AccountId == accountId);
                if (me is null)
                {
                    // Ván có thật nhưng không tìm thấy người này: đánh dấu đã xử lý để không
                    // hỏi lại mãi. Xảy ra khi tài khoản ẩn danh trong ván đó.
                    row.DetailFetchedAt = DateTime.UtcNow;
                    continue;
                }

                // isRadiant của OpenDota chỉ có ở ván đã parse; player_slot thì luôn có.
                var meRadiant = me.PlayerSlot < 128;
                var team = all.Where(p => (p.PlayerSlot < 128) == meRadiant).ToList();

                row.NetWorth = me.NetWorth;
                row.Level = me.Level;
                row.LaneRole = me.LaneRole is int lr && lr > 0 ? lr : row.LaneRole;

                // Chỉ xếp hạng khi đủ 5 người: thiếu người thì thứ hạng đổi nghĩa, và một hạng
                // "2/3" đọc như "2/5" là sai lệch âm thầm.
                if (team.Count == 5)
                {
                    row.TeamFarmRank = Rank(team, me, p => p.NetWorth ?? p.GoldPerMin);
                    row.TeamXpmRank = Rank(team, me, p => p.XpPerMin);
                }

                ReadBenchmarks(row, me);
                ReadTeamEconomy(row, me, all);
                ReadFights(row, detail, me, all);
                ReadLanePhase(row, detail, me);
                SaveTeammates(row, me, team, savedMates);

                row.DetailFetchedAt = DateTime.UtcNow;
                done++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Không đọc được chi tiết ván {Match}", row.MatchId);
            }
        }

        logger.LogInformation(
            "Lấy bối cảnh đội cho {Done}/{Total} ván, {Failed} ván tải hỏng sẽ thử lại vòng sau",
            done, pending.Count, pending.Count - loaded.Count);

        return done;
    }

    /// <summary>
    /// Xin parse những ván còn trong cửa sổ replay và chưa có vai trò thật.
    ///
    /// Chỉ ĐẶT HÀNG, không chờ: parse xong sau khoảng 30 giây, và vòng ingest sau sẽ đọc được
    /// lane_role khi lấy lại chi tiết. Chờ tại chỗ sẽ kéo dài vòng nạp lên hàng giờ mà chẳng
    /// được gì thêm.
    /// </summary>
    private async Task<int> RequestParsesAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-ParseWindowDays);

        var candidates = await db.TrackedPlayerMatches
            .Where(m => m.LaneRole == null
                        && m.ParseRequestedAt == null
                        && m.StartTime >= cutoff)
            .OrderByDescending(m => m.StartTime)
            .Take(MaxParseRequestsPerRun)
            .ToListAsync(ct);

        if (candidates.Count == 0) return 0;

        var asked = 0;

        foreach (var row in candidates)
        {
            try
            {
                await client.RequestParseAsync(row.MatchId, ct);

                // Đánh dấu dù kết quả thế nào: replay có thể đã hỏng, và xin lại mỗi vòng cho
                // cùng một ván là cách đốt hạn mức mà không bao giờ có kết quả.
                row.ParseRequestedAt = DateTime.UtcNow;
                asked++;
            }
            catch (Exception ex)
            {
                row.ParseRequestedAt = DateTime.UtcNow;
                logger.LogWarning(ex, "Không xin được parse cho ván {Match}", row.MatchId);
            }
        }

        // Ván đã xin parse thì phải lấy lại chi tiết ở vòng sau mới đọc được lane_role.
        foreach (var row in candidates) row.DetailFetchedAt = null;

        logger.LogInformation("Đã xin parse {Asked} ván trong cửa sổ {Days} ngày", asked, ParseWindowDays);
        return asked;
    }

    private static int Rank<T>(List<T> team, T me, Func<T, int> by) where T : class =>
        team.OrderByDescending(by).ToList().IndexOf(me) + 1;

    /// <summary>
    /// Chép bảng phân vị của OpenDota vào hàng, sau khi đã lọc những ô vô nghĩa.
    ///
    /// Đây là phần biến con số thô thành thứ đọc được: "696 GPM" không nói được gì nếu không
    /// biết trên hero đó 696 là nhiều hay ít, còn "cao hơn 96% người chơi Centaur" thì có.
    /// </summary>
    private static void ReadBenchmarks(Data.Entities.TrackedPlayerMatch row, OpenDotaMatchPlayer me)
    {
        var b = me.Benchmarks;
        if (b is null || b.Count == 0) return;

        row.PctGpm = Pct(b, "gold_per_min");
        row.PctXpm = Pct(b, "xp_per_min");
        row.PctLastHits = Pct(b, "last_hits_per_min");
        row.PctDenies = Pct(b, "denies_per_min");
        row.PctKills = Pct(b, "kills_per_min");
        row.PctDeaths = Pct(b, "deaths_per_min");
        row.PctAssists = Pct(b, "assists_per_min");
        row.PctHeroDamage = Pct(b, "hero_damage_per_min");
        row.PctHeroHealing = Pct(b, "hero_healing_per_min");
        row.PctTowerDamage = Pct(b, "tower_damage");
    }

    /// <summary>
    /// Một ô phân vị, hoặc null nếu ô đó không đáng tin.
    ///
    /// PHÉP KIỂM MÂU THUẪN. Không thể vừa đạt giá trị THẤP NHẤT có thể (raw = 0) vừa đứng trên
    /// quá nửa số người chơi — trừ khi quá nửa số người chơi cũng bằng 0, và khi đó chỉ số không
    /// phân biệt được ai với ai nên phân vị chỉ là vị trí ngẫu nhiên trong một khối bằng nhau
    /// khổng lồ.
    ///
    /// Đo thật ở ván 8937662260: hero_healing_per_min raw 0, pct 0,93 — Centaur không có kỹ năng
    /// hồi máu nên gần như ai chơi cũng hồi 0, và OpenDota trả về mép trên của khối đó. Không
    /// chặn thì trang sẽ viết "hồi máu tốt hơn 93% người chơi" cho một ván hồi đúng 0 máu.
    ///
    /// Vì sao ngưỡng đặt ở 0,5 chứ không phải chặn mọi raw = 0: với SỐ CHẾT, raw = 0 nghĩa là
    /// không chết lần nào — thành tích thật và hiếm, nên khối bằng nhau nhỏ, phân vị nằm thấp và
    /// hoàn toàn có nghĩa. Chặn tất cả raw = 0 sẽ vứt đi đúng những ván chơi hay nhất.
    /// </summary>
    public static int? Pct(Dictionary<string, OpenDotaBenchmark>? b, string key)
    {
        if (b is null || !b.TryGetValue(key, out var cell)) return null;
        if (cell.Pct is not double p || p < 0 || p > 1) return null;
        if (cell.Raw is not double raw) return null;
        if (raw <= 0 && p > 0.5) return null;

        return (int)Math.Round(p * 100);
    }

    /// <summary>
    /// Kinh tế của cả hai phe, và mức farm của riêng 4 ĐỒNG ĐỘI.
    ///
    /// Đây là dữ liệu để trả lời "cái chết của tôi có tạo ra khoảng trống không" mà không phải
    /// mượn chỉ số hỗ trợ làm proxy — hỗ trợ chỉ ghi nhận việc có mặt lúc hạ gục, còn một cái
    /// chết mua thời gian cho đồng đội đi farm thì không để lại dấu vết nào trong đó.
    ///
    /// Chỉ ghi khi ĐỦ 5 người mỗi phe. Thiếu người thì tổng kinh tế đổi nghĩa, và một tổng của
    /// 4 người đọc như tổng của 5 là sai lệch âm thầm — đúng loại đã phải chặn ở thứ hạng farm.
    /// </summary>
    private static void ReadTeamEconomy(
        Data.Entities.TrackedPlayerMatch row,
        OpenDotaMatchPlayer me,
        List<OpenDotaMatchPlayer> all)
    {
        var meRadiant = me.PlayerSlot < 128;
        var team = all.Where(p => (p.PlayerSlot < 128) == meRadiant).ToList();
        var foes = all.Where(p => (p.PlayerSlot < 128) != meRadiant).ToList();

        if (team.Count != 5 || foes.Count != 5) return;
        if (team.Any(p => p.NetWorth is null) || foes.Any(p => p.NetWorth is null)) return;

        row.TeamNetWorth = team.Sum(p => p.NetWorth!.Value);
        row.EnemyNetWorth = foes.Sum(p => p.NetWorth!.Value);

        var mates = team.Where(p => !ReferenceEquals(p, me)).ToList();
        row.MatesPctGpm = MedianPct(mates, "gold_per_min");
        row.MatesPctXpm = MedianPct(mates, "xp_per_min");
    }

    /// <summary>
    /// Đọc từng PHA GIAO TRANH: cái chết của người này đổi được gì cho đội.
    ///
    /// VÌ SAO CẦN TẦNG NÀY khi đã có kinh tế cả ván. Con số cả ván không phân biệt được một cái
    /// chết vô ích với một cái chết kéo 2-3 người địch đi xa để đồng đội dọn phần còn lại. Pha
    /// giao tranh thì phân biệt được: cộng vàng cộng thêm của 5 người mỗi phe TRONG pha đó là
    /// biết pha đó ai lời.
    ///
    /// VÀ TIỀN THƯỞNG ĐÃ TỰ TÍNH ĐỘ GIÀU. Bounty của Dota tỉ lệ với net worth nạn nhân, nên
    /// "tôi chết rẻ, đổi lại đội giết được đứa giàu nhất bên kia" nằm sẵn trong chênh lệch vàng
    /// — không phải ước lượng thêm.
    ///
    /// Riêng phần so độ giàu thì lấy VÀNG THEO PHÚT tại đúng phút xảy ra pha, không lấy net
    /// worth cuối ván: cuối ván là con số sau khi mọi chuyện đã xảy ra, còn thứ quyết định một
    /// cuộc đổi chác lời hay lỗ là độ giàu ngay lúc đó.
    /// </summary>
    private static void ReadFights(
        Data.Entities.TrackedPlayerMatch row,
        OpenDotaMatchDetail detail,
        OpenDotaMatchPlayer me,
        List<OpenDotaMatchPlayer> all)
    {
        var fights = detail.Teamfights;

        // null = ván chưa parse. Giữ nguyên null ở mọi cột thay vì ghi 0: 0 pha giao tranh là
        // một khẳng định về ván, còn "chưa parse" thì không biết gì cả.
        if (fights is null || fights.Count == 0 || all.Count != 10) return;

        var meIndex = all.IndexOf(me);
        if (meIndex < 0) return;

        var meRadiant = me.PlayerSlot < 128;
        var mates = Enumerable.Range(0, 10).Where(i => (all[i].PlayerSlot < 128) == meRadiant).ToList();
        var foes = Enumerable.Range(0, 10).Where(i => (all[i].PlayerSlot < 128) != meRadiant).ToList();
        if (mates.Count != 5 || foes.Count != 5) return;

        int died = 0, diedAhead = 0, swingDied = 0, survived = 0, swingSurvived = 0;
        int myGold = 0, foeGold = 0, foeDeaths = 0;

        foreach (var f in fights)
        {
            if (f.Players.Count != 10) continue;

            // CỘNG VÀNG CỦA 4 ĐỒNG ĐỘI, KHÔNG TÍNH CHÍNH MÌNH.
            //
            // Đây không phải tinh chỉnh mà là điều kiện để phép đo có nghĩa. Khi ta chết,
            // gold_delta của chính ta đã âm sẵn — nên một pha có ta chết bắt đầu bằng một khoản
            // trừ ĐƯƠNG NHIÊN cho phe mình. Cộng cả ta vào rồi hỏi "pha này đội có lời không"
            // là hỏi một câu đã bị cài sẵn câu trả lời, và mọi cái chết đều sẽ trông như lỗ.
            //
            // Câu hỏi thật là: bốn người kia có kiếm được nhiều hơn cái giá ta trả không. Nên
            // vế của ta bị loại khỏi tử số, còn cái giá ta trả thì vẫn nằm ở vế địch (tiền
            // thưởng chúng nhận được khi giết ta).
            var teamGold = mates.Where(i => i != meIndex).Sum(i => f.Players[i].GoldDelta ?? 0);
            var enemyGold = foes.Sum(i => f.Players[i].GoldDelta ?? 0);
            var swing = teamGold - enemyGold;

            if ((f.Players[meIndex].Deaths ?? 0) > 0)
            {
                died++;
                swingDied += swing;
                if (swing > 0) diedAhead++;

                // Độ giàu tại phút xảy ra pha. Chỉ cộng khi ĐỌC ĐƯỢC cả hai vế, nếu không thì
                // tử số và mẫu số lệch nhau và tỷ lệ mất nghĩa.
                var mine = GoldAt(all[meIndex], f.Start);
                if (mine is int g)
                {
                    foreach (var i in foes.Where(i => (f.Players[i].Deaths ?? 0) > 0))
                    {
                        if (GoldAt(all[i], f.Start) is not int fg) continue;
                        myGold += g;
                        foeGold += fg;
                        foeDeaths++;
                    }
                }
            }
            else
            {
                survived++;
                swingSurvived += swing;
            }
        }

        row.FightsDied = died;
        row.FightsDiedAhead = diedAhead;
        row.FightSwingDied = swingDied;
        row.FightsSurvived = survived;
        row.FightSwingSurvived = swingSurvived;
        row.TradeMyGold = myGold;
        row.TradeFoeGold = foeGold;
        row.TradeFoeDeaths = foeDeaths;
    }

    /// <summary>
    /// Giai đoạn lane: hiệu suất lane, và chênh lệch vàng hai phe ở phút 10, 20, 30.
    ///
    /// PHẦN NÀY SẠCH HƠN MỌI CHỈ SỐ KHÁC TRÊN TRANG, và lý do đáng ghi lại: mọi thứ khác đo lúc
    /// ván đã kết thúc nên so giữa thắng và thua luôn vướng vòng nhân quả — thắng thì chỉ số nào
    /// cũng đẹp. Mốc phút 10 thì đo TRƯỚC KHI ván ngã ngũ, nên chênh lệch giữa ván thắng và ván
    /// thua ở đó nói được điều thật: thua từ lane, hay thắng lane rồi mất về sau.
    ///
    /// DẤU PHẢI ĐẢO KHI Ở PHE DIRE. radiant_gold_adv là Radiant trừ Dire; quên đảo thì mọi ván
    /// Dire đọc ngược hoàn toàn — và vì gần nửa số ván là Dire, kết quả sẽ trung hoà về 0 mà
    /// trông vẫn hợp lý.
    /// </summary>
    private static void ReadLanePhase(
        Data.Entities.TrackedPlayerMatch row, OpenDotaMatchDetail detail, OpenDotaMatchPlayer me)
    {
        if (me.LaneEfficiencyPct is double eff)
            row.LaneEfficiency = (int)Math.Round(eff);

        var adv = detail.RadiantGoldAdv;
        if (adv is null || adv.Count == 0) return;

        var sign = me.PlayerSlot < 128 ? 1 : -1;

        int? At(int minute) => minute < adv.Count ? adv[minute] * sign : null;

        row.GoldAdv10 = At(10);
        row.GoldAdv20 = At(20);
        row.GoldAdv30 = At(30);
    }

    /// <summary>Vàng tích luỹ của một người tại giây <paramref name="seconds"/>, hoặc null.</summary>
    private static int? GoldAt(OpenDotaMatchPlayer p, int seconds)
    {
        var t = p.GoldPerMinute;
        if (t is null || t.Count == 0) return null;

        // Pha xảy ra trước khai cuộc có start âm — kẹp về phút 0 chứ không bỏ, vì đó vẫn là một
        // pha giao tranh thật (tranh rune, chặn creep) và độ giàu lúc đó đúng bằng vàng khởi đầu.
        var minute = Math.Clamp(seconds / 60, 0, t.Count - 1);
        return t[minute];
    }

    /// <summary>
    /// Trung vị phân vị của một chỉ số trên nhóm người truyền vào.
    ///
    /// Trung vị chứ không trung bình, và bỏ qua người thiếu phân vị thay vì coi họ bằng 0: một
    /// đồng đội ẩn danh hay một ô benchmark trống không có nghĩa là người đó farm kém nhất trận.
    /// </summary>
    private static int? MedianPct(List<OpenDotaMatchPlayer> group, string key)
    {
        var values = group.Select(p => Pct(p.Benchmarks, key)).OfType<int>().OrderBy(x => x).ToList();
        if (values.Count == 0) return null;

        return values.Count % 2 == 1
            ? values[values.Count / 2]
            : (int)Math.Round((values[values.Count / 2 - 1] + values[values.Count / 2]) / 2.0);
    }

    /// <summary>
    /// Ghi lại 4 người cùng phe, kèm việc họ có ĐI CÙNG NHÓM hay chỉ ghép trúng.
    ///
    /// party_id là thứ duy nhất tách được bạn bè khỏi người lạ: party_size chỉ nói CỠ nhóm chứ
    /// không nói AI trong nhóm, nên một ván 5 người vẫn có thể gồm hai nhóm 3 và 2.
    /// </summary>
    private static void SaveTeammates(
        Data.Entities.TrackedPlayerMatch row,
        OpenDotaMatchPlayer me,
        List<OpenDotaMatchPlayer> team,
        Dictionary<(long, long), Data.Entities.TrackedMatchTeammate> saved)
    {
        foreach (var p in team)
        {
            // Người ẩn danh không định danh được nên không lưu: gộp mọi người ẩn danh lại thành
            // một "đồng đội" duy nhất sẽ tạo ra một cái tên chơi cùng hàng nghìn ván.
            if (p.AccountId is not long acc || acc <= 0 || acc == me.AccountId) continue;

            // party_id null nghĩa là đi một mình, và hai người CÙNG null không phải cùng nhóm.
            var sameParty = me.PartyId is int mine && p.PartyId == mine;

            if (!saved.TryGetValue((row.Id, acc), out var mate))
            {
                mate = new Data.Entities.TrackedMatchTeammate
                {
                    TrackedPlayerMatchId = row.Id,
                    AccountId = acc,
                };
                saved[(row.Id, acc)] = mate;
                row.Teammates.Add(mate);
            }

            mate.PersonaName = p.PersonaName ?? mate.PersonaName;
            mate.HeroId = p.HeroId;
            mate.SameParty = sameParty;
            mate.RankTier = p.RankTier ?? mate.RankTier;
        }
    }
}
