namespace Ti2026.Ingest.Analytics;

/// <summary>
/// Rút năm chỉ số fantasy mà trước đây tưởng là "OpenDota không có". Cả năm đều nằm trong
/// payload <c>matches/{id}</c>, chỉ là nằm trong ba từ điển con mà DTO chưa khai:
/// <c>item_uses</c>, <c>ability_uses</c>, <c>killed</c>.
///
/// QUY TẮC null Ở ĐÂY LÀ QUAN TRỌNG NHẤT. Từ điển null (ván OpenDota chưa parse) trả về null
/// = "chưa biết". Từ điển có mà thiếu khoá trả về 0 = "đo được, và bằng không". Gộp hai thứ
/// này lại là cách chắc chắn để biến một người hỗ trợ chưa có dữ liệu thành một người hỗ trợ
/// tệ, và trung bình sẽ sai mà nhìn vẫn hợp lý.
/// </summary>
public static class FantasyFields
{
    /// <summary>
    /// Hoa sen QUY VỀ hoa sen gốc. Đây không phải phép đếm số món trong túi: luật ghép của
    /// Dota là 3 Healing Lotus → 1 Great, 2 Great → 1 Greater (tức 6 gốc). Đếm mỗi món bằng 1
    /// thì người gom 6 bông rồi ghép lại bị tính bằng người nhặt đúng 1 bông.
    ///
    /// Tên nội bộ của hoa sen là "famango" — không phải "lotus". <c>lotus_orb</c> là món mua
    /// ở shop, hoàn toàn khác, và đó là lý do lần tìm trước đó không thấy gì.
    /// </summary>
    public const int GreatLotusInBase = 3;
    public const int GreaterLotusInBase = 6;

    public static int? Lotuses(IReadOnlyDictionary<string, int>? itemUses) =>
        itemUses is null
            ? null
            : Get(itemUses, "famango")
              + Get(itemUses, "great_famango") * GreatLotusInBase
              + Get(itemUses, "greater_famango") * GreaterLotusInBase;

    /// <summary>
    /// Watcher = cột đèn. Tên nội bộ là <c>ability_lamp_use</c>, nằm ở <c>ability_uses</c>
    /// chứ không phải <c>item_uses</c> — không có chữ "watcher" nào trong payload, nên tìm
    /// theo tên hiển thị thì không bao giờ ra.
    /// </summary>
    public static int? Watchers(IReadOnlyDictionary<string, int>? abilityUses) =>
        abilityUses is null ? null : Get(abilityUses, "ability_lamp_use");

    public static int? Smokes(IReadOnlyDictionary<string, int>? itemUses) =>
        itemUses is null ? null : Get(itemUses, "smoke_of_deceit");

    /// <summary>
    /// SỐ TÚI madstone đã dùng — KHÔNG phải số madstone nhặt được.
    ///
    /// Dọn một trại thường cho người dọn 2 madstone và đồng đội 1 (trại cổ là 3 và 2), còn
    /// túi chỉ là một trong các đường nhận. OpenDota không lộ tổng madstone của từng người ở
    /// bất kỳ đâu, nên đây là số GẦN ĐÚNG và phải được đánh dấu là gần đúng ở mọi nơi hiển
    /// thị. Một con số gần đúng trộn lẫn với số đo thật là con số nguy hiểm nhất trong bảng.
    /// </summary>
    public static int? MadstoneBundles(IReadOnlyDictionary<string, int>? itemUses) =>
        itemUses is null ? null : Get(itemUses, "madstone_bundle");

    /// <summary>Tormentor. Tên nội bộ là <c>npc_dota_miniboss</c>, lấy theo người kết liễu.</summary>
    public static int? TormentorKills(IReadOnlyDictionary<string, int>? killed) =>
        killed is null ? null : Get(killed, "npc_dota_miniboss");

    /// <summary>
    /// Roshan đếm từ <c>killed</c> thay vì trường <c>roshan_kills</c> có sẵn.
    ///
    /// Lý do: trong ván mẫu 8926048199 chỉ có ĐÚNG HAI Roshan chết (theo objectives), nhưng
    /// tổng <c>roshan_kills</c> của mười người là BA, còn tổng <c>killed[npc_dota_roshan]</c>
    /// là hai — khớp. Trường tổng hợp đếm dư, từ điển <c>killed</c> thì không.
    ///
    /// Chỉ có một ván làm bằng chứng nên chưa đủ để kết luận chắc; vì thế bên nạp còn ghi log
    /// khi hai nguồn lệch nhau, thay vì lặng lẽ chọn một bên.
    /// </summary>
    public static int? RoshanKills(IReadOnlyDictionary<string, int>? killed) =>
        killed is null ? null : Get(killed, "npc_dota_roshan");

    private static int Get(IReadOnlyDictionary<string, int> d, string key) =>
        d.TryGetValue(key, out var v) ? v : 0;
}
