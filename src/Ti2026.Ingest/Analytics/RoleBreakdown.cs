namespace Ti2026.Ingest.Analytics;

/// <summary>Một ván, rút gọn còn những gì cần để xác định vai trò.</summary>
/// <param name="HeroId">Giữ lại để các phần khác dùng; phần chia vai trò không đọc tới.</param>
public readonly record struct RoleGame(
    DateTime StartTime, bool Won, int? LaneRole, int? TeamFarmRank, int HeroId = 0);

/// <param name="Exact">true = nhãn thật từ replay. false = chỉ suy được core hay hỗ trợ.</param>
public readonly record struct RoleSlice(
    string Code, string Label, int Games, int Wins, double Winrate, bool Exact);

/// <summary>Một năm, và tỷ trọng từng lane trong những ván CÓ NHÃN THẬT của năm đó.</summary>
/// <param name="Labelled">Số ván có nhãn thật. Nhỏ thì mọi tỷ lệ bên dưới đều mỏng.</param>
public readonly record struct RoleEra(
    int Year, int Games, int Labelled, int Safe, int Mid, int Off, int Jungle);

/// <summary>
/// Tách lịch sử theo VAI TRÒ, và tách theo thời gian để thấy vai trò dịch chuyển.
///
/// Dựa hoàn toàn vào <see cref="RoleResolver"/> nên thừa hưởng đúng nguyên tắc của nó: ván đã
/// parse thì có vị trí thật; ván chưa parse thì chỉ dám nói core hay hỗ trợ; không bao giờ đoán
/// lane từ chỉ số.
///
/// VÌ SAO PHẦN "DỊCH CHUYỂN THEO NĂM" CHỈ DÙNG VÁN CÓ NHÃN THẬT. Nó hỏi "trước kia đi mid, giờ
/// đi lung tung phải không" — mà mid/safe/off thì chỉ nhãn thật mới phân biệt được. Dùng phần
/// suy luận vào đây sẽ cho ra một biểu đồ đầy đặn, mượt mà và hoàn toàn bịa.
///
/// Đổi lại, số ván có nhãn mỗi năm rất mỏng, nên <see cref="RoleEra.Labelled"/> phải đi kèm ra
/// tận giao diện: một năm có 3 ván có nhãn mà vẽ thành "100% mid" thì con số đúng nhưng câu
/// chuyện sai.
/// </summary>
public static class RoleBreakdown
{
    /// <summary>Dưới ngần này ván thì một vai trò không đáng đứng riêng thành mục.</summary>
    public const int MinGamesPerRole = 15;

    /// <summary>Một năm cần ngần này ván có nhãn thì tỷ trọng lane của năm đó mới đáng đọc.</summary>
    public const int MinLabelledPerYear = 10;

    public static List<RoleSlice> Slices(
        IReadOnlyList<RoleGame> games, int minGames = MinGamesPerRole)
    {
        var buckets = new Dictionary<string, (string Code, string Label, bool Exact, int Games, int Wins)>();

        foreach (var g in games)
        {
            var v = RoleResolver.Resolve(g.LaneRole, g.TeamFarmRank);
            if (v.Code == "khong-biet") continue;

            // Khoá ô gồm cả độ tin cậy, và nằm TRONG chứ không lộ ra Code. Hai tầng hiện có
            // (pos1..pos5 từ replay, core/support suy từ đội hình) vốn đã khác mã nên chốt này
            // là phòng thủ — nhưng nó đã cứu một lần rồi: khi còn tầng "ước lượng" dùng CHUNG mã
            // pos2, hai tầng rơi chung một ô và 249 ván biết chắc tan vào 1.853 ván phỏng đoán.
            var key = v.Code + "|" + v.IsExact;

            var cur = buckets.GetValueOrDefault(key);
            buckets[key] = (v.Code, v.Label, v.IsExact, cur.Games + 1, cur.Wins + (g.Won ? 1 : 0));
        }

        return buckets
            .Where(b => b.Value.Games >= minGames)
            .Select(b => new RoleSlice(
                b.Value.Code, b.Value.Label, b.Value.Games, b.Value.Wins,
                Math.Round(b.Value.Wins * 100.0 / b.Value.Games, 1), b.Value.Exact))
            .OrderByDescending(s => s.Exact).ThenByDescending(s => s.Games)
            .ToList();
    }

    /// <summary>
    /// Tỷ trọng lane theo từng năm — chỉ đếm ván CÓ NHÃN THẬT.
    ///
    /// <see cref="RoleEra.Games"/> là tổng số ván của năm, để đặt cạnh số ván có nhãn: thấy ngay
    /// tỷ lệ này dựng trên bao nhiêu phần trăm dữ liệu của năm đó.
    /// </summary>
    public static List<RoleEra> Eras(IReadOnlyList<RoleGame> games)
    {
        return games
            .GroupBy(g => g.StartTime.Year)
            .OrderBy(g => g.Key)
            .Select(g => new RoleEra(
                g.Key,
                g.Count(),
                g.Count(x => x.LaneRole is >= 1 and <= 4),
                g.Count(x => x.LaneRole == 1),
                g.Count(x => x.LaneRole == 2),
                g.Count(x => x.LaneRole == 3),
                g.Count(x => x.LaneRole == 4)))
            .ToList();
    }

    /// <summary>
    /// Năm đầu và năm cuối ĐỦ DÀY để so, cùng tỷ trọng mid của mỗi năm.
    ///
    /// Trả null khi không có hai năm nào đủ dày. Đây là chỗ dễ sai nhất của cả phần này: so năm
    /// 2015 có 4 ván có nhãn với năm 2026 có 300 ván rồi tuyên bố "bạn đã đổi vai trò" là kết
    /// luận rút từ 4 điểm dữ liệu.
    /// </summary>
    public static (RoleEra First, RoleEra Last)? Shift(IReadOnlyList<RoleEra> eras)
    {
        var solid = eras.Where(e => e.Labelled >= MinLabelledPerYear).ToList();
        return solid.Count < 2 ? null : (solid[0], solid[^1]);
    }
}
