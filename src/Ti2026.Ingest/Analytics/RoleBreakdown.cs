namespace Ti2026.Ingest.Analytics;

/// <summary>Một ván, rút gọn còn những gì cần để xác định vai trò.</summary>
/// <param name="HeroId">
/// Chỉ cần khi bên gọi muốn suy lane từ tiền nghiệm hero. Mặc định 0 = không suy, để mọi chỗ
/// gọi cũ không phải sửa.
/// </param>
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

    /// <param name="priors">
    /// Tiền nghiệm lane theo hero, học từ ván chuyên nghiệp. Truyền vào thì những ván CHƯA có
    /// nhãn cũng được xếp vị trí — kèm IsExact = false. Không truyền thì giữ nguyên hành vi cũ:
    /// ván chưa nhãn chỉ ra core/hỗ trợ.
    ///
    /// Đây là lựa chọn CỦA NGƯỜI DÙNG, không phải mặc định: độ chính xác đo được 65% và sai số
    /// có cấu trúc, nên nó chỉ đáng bật khi người đọc đã biết và chấp nhận điều đó.
    /// </param>
    public static List<RoleSlice> Slices(
        IReadOnlyList<RoleGame> games,
        IReadOnlyDictionary<int, RoleResolver.HeroLanePrior>? priors = null,
        int minGames = MinGamesPerRole)
    {
        var buckets = new Dictionary<string, (string Code, string Label, bool Exact, int Games, int Wins)>();

        foreach (var g in games)
        {
            RoleResolver.HeroLanePrior? prior = priors is not null
                && priors.TryGetValue(g.HeroId, out var found) ? found : null;

            var v = RoleResolver.Resolve(g.LaneRole, g.TeamFarmRank, prior);
            if (v.Code == "khong-biet") continue;

            // KHOÁ Ô GỒM CẢ ĐỘ TIN CẬY, không chỉ mã vị trí.
            //
            // Đây là một lỗi thật đã lọt lên trang: cả nhãn thật lẫn nhãn ước lượng đều trả mã
            // "pos2", nên chúng rơi chung một ô và cờ Exact bị ván cuối cùng ghi đè — 249 ván
            // biết chắc biến mất vào trong 1.853 ván phỏng đoán, đúng kiểu trộn lặng lẽ mà cả
            // thiết kế này sinh ra để chặn.
            // Khoá gom nhóm nằm TRONG, không lộ ra Code. Bản đầu nối thẳng hậu tố vào Code và
            // nó rò ra API: "core" thành "core~uoc-luong", phá cả bài kiểm lẫn mọi bên đọc mã
            // vị trí. Khoá là chuyện nội bộ của phép gom, không phải một giá trị công khai.
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
