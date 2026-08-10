namespace Ti2026.Data.Entities;

/// <summary>
/// Một người chơi được theo dõi lâu dài — chủ trang và, về sau, học trò.
///
/// VÌ SAO LÀ MỘT BẢNG chứ không phải một hằng số account_id: yêu cầu ngay từ đầu là thêm người
/// theo dõi mà không phải sửa mã. Danh sách đọc từ data/tracked-players.json, nên thêm một học
/// trò là thêm một dòng JSON rồi deploy — không đụng tới bất kỳ truy vấn nào.
///
/// KHÁC Player ở chỗ nào: Player là 80 tuyển thủ của 16 đội TI, tồn tại để phục vụ phân tích
/// giải đấu. Bảng này là người dùng của chính trang web, và vòng đời hoàn toàn khác — họ không
/// thuộc đội nào, không có roster, và dữ liệu của họ là pub chứ không phải trận chính thức.
/// Gộp hai thứ vào một bảng sẽ khiến mọi truy vấn về "tuyển thủ" phải nhớ loại trừ người dùng.
/// </summary>
public class TrackedPlayer
{
    public int Id { get; set; }

    /// <summary>
    /// account_id của Dota 2 — CHÍNH LÀ friend id hiện trong game. Đây là số 32-bit, khác với
    /// steamID64; OpenDota dùng thẳng số này ở mọi endpoint players/{id}.
    /// </summary>
    public long AccountId { get; set; }

    /// <summary>Tên do ta đặt, dùng khi OpenDota chưa trả về persona hoặc persona đổi liên tục.</summary>
    public required string DisplayName { get; set; }

    /// <summary>"chủ trang" | "học trò" | ghi chú tự do.</summary>
    public string? Note { get; set; }

    /// <summary>Chủ trang: luôn hiện đầu tiên và là người mặc định khi mở tab.</summary>
    public bool IsOwner { get; set; }

    // ---- Đồng bộ từ OpenDota ----

    public string? PersonaName { get; set; }
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// rank_tier của OpenDota: hàng chục là bậc (1 Herald … 8 Immortal), hàng đơn vị là sao.
    /// 55 = Legend 5. null = hồ sơ không công khai rank.
    /// </summary>
    public int? RankTier { get; set; }

    public int Wins { get; set; }
    public int Losses { get; set; }

    /// <summary>UTC. null = chưa đồng bộ lần nào.</summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// Lý do lần đồng bộ gần nhất không lấy được gì. Thường là hồ sơ chưa bật "Expose Public
    /// Match Data" trong Dota 2 — khi đó OpenDota trả danh sách ván RỖNG chứ không báo lỗi, và
    /// nếu không ghi lại thì trang trông y hệt như người này chưa chơi ván nào.
    /// </summary>
    public string? SyncNote { get; set; }

    public DateTime AddedAt { get; set; }
}
