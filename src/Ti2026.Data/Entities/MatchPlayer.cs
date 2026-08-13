namespace Ti2026.Data.Entities;

/// <summary>
/// Thành tích của một tuyển thủ trong một ván, lấy từ match detail của OpenDota.
///
/// Đây là thứ mở khoá 5 chỉ số mà endpoint teams/{id}/matches không cung cấp, và là nền
/// cho phân tích cá nhân: kill, hỗ trợ, nhịp độ 10 phút đầu.
/// </summary>
public class MatchPlayer
{
    public int Id { get; set; }

    public long MatchId { get; set; }
    public Match? Match { get; set; }

    /// <summary>
    /// account_id của OpenDota. Giữ nguyên kể cả khi chưa khớp được với Player nào của ta —
    /// một ván có 10 người, không phải ai cũng thuộc 16 đội đang theo dõi.
    /// </summary>
    public long? AccountId { get; set; }

    /// <summary>Khớp về Player của ta khi AccountId trùng; null nếu không thuộc 16 đội.</summary>
    public int? PlayerId { get; set; }
    public Player? Player { get; set; }

    public int HeroId { get; set; }
    public bool IsRadiant { get; set; }

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public int GoldPerMin { get; set; }
    public int XpPerMin { get; set; }

    /// <summary>
    /// Số mạng hạ được trong 10 phút đầu, tính từ kills_log.
    /// null = ván chưa được OpenDota parse nên không có timeline.
    /// </summary>
    public int? KillsFirst10Min { get; set; }

    // ---------- Chỉ từ ván ĐÃ được OpenDota parse; chưa parse thì để null ----------
    // Cả khối dưới đây là null-able có chủ đích. Quy về 0 sẽ tạo ra những tuyển thủ "farm 0
    // lính, gây 0 sát thương" trộn lẫn với số đo thật, và trung bình sẽ sai mà nhìn vẫn hợp lý.

    /// <summary>OpenDota lane_role: 1 safe, 2 mid, 3 off, 4 jungle. 0/null = không xác định.</summary>
    public int? LaneRole { get; set; }

    /// <summary>Lane thực tế suy từ vị trí đầu trận, có thể khác LaneRole khi đội đổi lane.</summary>
    public int? Lane { get; set; }

    /// <summary>Hiệu suất lane theo phần trăm, thước đo thắng/thua lane của OpenDota.</summary>
    public double? LaneEfficiencyPct { get; set; }

    public int? LastHits { get; set; }
    public int? Denies { get; set; }
    public int? NetWorth { get; set; }
    public int? HeroDamage { get; set; }
    public int? TowerDamage { get; set; }
    public int? ObserversPlaced { get; set; }

    // ---------- Chỉ số HỖ TRỢ ----------
    // Khối này tồn tại vì K/D/A và GPM không đo được công của người chơi hỗ trợ. Một người
    // stack rừng, cắm mắt và giữ địch đứng hình 40 giây có thể có bảng điểm trông rất tệ.

    public int? SentriesPlaced { get; set; }
    public int? CampsStacked { get; set; }
    public int? RunePickups { get; set; }
    public int? Buybacks { get; set; }

    /// <summary>Tổng số GIÂY giữ địch đứng hình. Là số thực, không phải số nguyên.</summary>
    public double? StunSeconds { get; set; }

    /// <summary>Tỷ lệ tham gia giao tranh, 0–1 theo cách tính của OpenDota.</summary>
    public double? TeamfightParticipation { get; set; }

    // ---------- Chỉ số fantasy ----------
    // Cả năm đều nằm sẵn trong payload matches/{id} — chỉ là DTO trước đây không khai.
    public int? TowerKills { get; set; }
    public int? RoshanKills { get; set; }
    public int? CourierKills { get; set; }
    public int? ObserverKills { get; set; }
    public int? SentryKills { get; set; }

    /// <summary>Người này có lấy first blood không. null = ván chưa parse.</summary>
    public bool? FirstBloodClaimed { get; set; }

    // ---------- Năm chỉ số fantasy từng tưởng là KHÔNG CÓ NGUỒN ----------
    // Chúng vẫn nằm trong cùng payload matches/{id}, chỉ là trong ba từ điển con mà DTO chưa
    // khai: item_uses, ability_uses, killed. Không có chữ "lotus"/"watcher"/"tormentor" nào
    // trong payload — tên nội bộ là famango, ability_lamp_use, npc_dota_miniboss — nên tìm
    // theo tên hiển thị thì mãi không ra. Cách rút: Ti2026.Ingest.Analytics.FantasyFields.

    /// <summary>Hoa sen QUY VỀ hoa sen gốc: Great tính 3, Greater tính 6 theo luật ghép.</summary>
    public int? Lotuses { get; set; }

    /// <summary>
    /// Hoa sen đếm theo MÓN: một Greater tính là 1. Giữ song song với <see cref="Lotuses"/>
    /// vì chưa biết Valve đếm kiểu nào, và hai cách chênh tới 6 lần. Có sẵn cả hai thì lúc
    /// biết chắc chỉ cần đổi một dòng trong fantasy.json, không phải nạp lại toàn bộ.
    /// </summary>
    public int? LotusItems { get; set; }

    /// <summary>Số lần chiếm watcher (ability_lamp_use).</summary>
    public int? Watchers { get; set; }

    public int? Smokes { get; set; }

    /// <summary>
    /// SỐ TÚI madstone đã dùng, KHÔNG phải số madstone nhặt. Phần lớn madstone đến từ việc dọn
    /// trại (2 cho người dọn, 1 cho đồng đội) và OpenDota không lộ tổng đó ở bất kỳ đâu. Đây là
    /// số GẦN ĐÚNG, và mọi chỗ hiển thị phải nói rõ là gần đúng.
    /// </summary>
    public int? MadstoneBundles { get; set; }

    public int? TormentorKills { get; set; }

    /// <summary>
    /// Số lần CHẾT VÌ Tormentor. Cần cho suffix "the Tormented" (+23%) — một điều kiện BẤT LỢI,
    /// nên con số này để NÉ chứ không phải để nhắm.
    /// </summary>
    public int? DeathsToTormentor { get; set; }
}
