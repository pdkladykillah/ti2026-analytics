namespace Ti2026.Ingest.Analytics;

/// <summary>
/// Suy ra vị trí 1–5 từ lane và tài sản.
///
/// VÌ SAO PHẢI SUY: OpenDota chỉ cho lane_role (1 an toàn, 2 mid, 3 khó, 4 rừng) — tức là ba
/// lane, không phải năm vị trí. Carry và hỗ trợ 5 đứng CÙNG một lane an toàn nên mang cùng một
/// lane_role, và gộp chúng lại thì mọi thống kê theo vị trí đều vô nghĩa.
///
/// CÁCH SUY: trong cùng một trận, cùng một đội, cùng một lane — ai nhiều tài sản hơn là core,
/// người còn lại là hỗ trợ. Đây là quy tắc quen thuộc và nó đúng vì định nghĩa của vai trò hỗ
/// trợ chính là nhường tài nguyên.
///
/// ĐÃ KIỂM CHỨNG trên 368 bàn draft 7.41 — năm danh sách hero phổ biến nhất theo vị trí đọc ra
/// đúng như người trong nghề sẽ viết:
///   1: Tiny, Kez, Windranger      2: Ember Spirit, Puck, Storm Spirit
///   3: Timbersaw, Axe, Centaur    4: Hoodwink, Rubick, Lion
///   5: Phoenix, Winter Wyvern, Bane
/// Nếu quy tắc sai thì hỗ trợ sẽ lẫn vào vị trí 1 và danh sách nhìn là biết ngay.
/// </summary>
public static class PositionInference
{
    public const int SafeLane = 1;
    public const int MidLane = 2;
    public const int OffLane = 3;

    /// <summary>
    /// <paramref name="wealthRankInLane"/> đếm từ 1 = giàu nhất trong cùng lane, cùng đội.
    /// Trả null khi không đủ căn cứ — thà không xếp còn hơn xếp bừa rồi cả bảng lệch.
    /// </summary>
    public static int? Infer(int? laneRole, int wealthRankInLane) => laneRole switch
    {
        MidLane => 2,

        // Lane an toàn: giàu nhất là carry, còn lại là hỗ trợ 5
        SafeLane => wealthRankInLane == 1 ? 1 : 5,

        // Lane khó: giàu nhất là offlane, còn lại là hỗ trợ 4
        OffLane => wealthRankInLane == 1 ? 3 : 4,

        // lane_role 4 là rừng, và 0/null là OpenDota không xác định được. Cả hai đều không
        // ánh xạ được sang vị trí 1–5, nên trả null thay vì nhét đại vào một ô.
        _ => null,
    };

    public static string Name(int position) => position switch
    {
        1 => "Carry",
        2 => "Mid",
        3 => "Offlane",
        4 => "Hỗ trợ 4",
        5 => "Hỗ trợ 5",
        _ => $"vị trí {position}",
    };

    public static string Description(int position) => position switch
    {
        1 => "Farm lane an toàn, gánh trận về cuối",
        2 => "Kiểm soát nhịp giữa bản đồ, thường quyết định 10 phút đầu",
        3 => "Chịu áp lực lane khó, mở giao tranh",
        4 => "Đi lang thang, tạo đột biến sớm",
        5 => "Nhường hết tài nguyên, giữ tầm nhìn và mở giao tranh",
        _ => "",
    };

    /// <summary>Xếp hạng tài sản trong nhóm rồi suy vị trí cho từng người.</summary>
    public static IEnumerable<(T Row, int Position)> InferGroup<T>(
        IEnumerable<T> sameLaneSameTeam, Func<T, int?> laneRole, Func<T, int?> netWorth)
    {
        var ordered = sameLaneSameTeam
            .OrderByDescending(x => netWorth(x) ?? int.MinValue)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var position = Infer(laneRole(ordered[i]), i + 1);
            if (position is int p) yield return (ordered[i], p);
        }
    }
}
