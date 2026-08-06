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
    /// Hoa sen đếm theo MÓN, không quy về bông gốc — một Greater tính là 1.
    ///
    /// Tồn tại song song với <see cref="Lotuses"/> vì chưa ai biết Valve đếm kiểu nào, và hai
    /// cách chênh nhau tới 6 lần trên một chỉ số 176 điểm. Dự án gốc — nơi bảng hệ số của ta
    /// lấy về — đếm theo MÓN; số trung bình của họ cho người hỗ trợ là 380 điểm, còn cách quy
    /// về bông gốc của ta ra khoảng 1250, tức lệch đúng cỡ hệ số ghép.
    ///
    /// Nạp cả hai để lúc TI bắt đầu chỉ cần đổi MỘT dòng "field" trong fantasy.json là xong,
    /// không phải nạp lại 1798 ván.
    /// </summary>
    public static int? LotusItems(IReadOnlyDictionary<string, int>? itemUses) =>
        itemUses is null
            ? null
            : Get(itemUses, "famango")
              + Get(itemUses, "great_famango")
              + Get(itemUses, "greater_famango");

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
    /// CHẾT VÌ Tormentor — lấy từ <c>killed_by</c>, không phải <c>killed</c>.
    ///
    /// Cần cho suffix "the Tormented" (+23%), một trong hai suffix cao điểm nhất. Nhưng đó là
    /// điều kiện BẤT LỢI: nó ăn khi có người trong đội hình chết vì Tormentor, nên biết xác
    /// suất là để NÉ chứ không phải để nhắm.
    /// </summary>
    public static int? DeathsToTormentor(IReadOnlyDictionary<string, int>? killedBy) =>
        killedBy is null ? null : Get(killedBy, "npc_dota_miniboss");

    /// <summary>
    /// Roshan đếm từ <c>killed</c>, KHÔNG dùng trường <c>roshan_kills</c> có sẵn — trường đó
    /// đếm dư.
    ///
    /// Đã kiểm ba chiều trên hai ván thật, lấy objectives làm trọng tài:
    ///
    ///   ván 8926048199 — objectives 2 Roshan · killed 2 · roshan_kills 3
    ///   ván 8784047386 — objectives 4 Roshan · killed 4 · roshan_kills 5
    ///
    /// Và trong 30 ván nạp gần nhất, 19 ván có hai nguồn lệch nhau — TẤT CẢ đều lệch cùng một
    /// chiều, trường tổng hợp cao hơn, phần lớn là kiểu "roshan_kills=1 nhưng killed=0".
    ///
    /// Đây không phải chuyện nhỏ: Roshan là 1172 điểm, nên mỗi con Roshan ma cộng thẳng 1172
    /// điểm vào một ván cho người không hề kết liễu nó.
    /// </summary>
    public static int? RoshanKills(IReadOnlyDictionary<string, int>? killed) =>
        killed is null ? null : Get(killed, "npc_dota_roshan");

    private static int Get(IReadOnlyDictionary<string, int> d, string key) =>
        d.TryGetValue(key, out var v) ? v : 0;
}
