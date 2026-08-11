namespace Ti2026.Data.Entities;

/// <summary>
/// Tổng của CẢ 10 NGƯỜI trong một ván — cái neo để so người chơi pub với tuyển thủ chuyên nghiệp
/// mà không bị hạng đấu đánh lừa.
///
/// VÌ SAO PHẢI CÓ BẢNG NÀY. Ván pub và ván chuyên nghiệp không cùng thang đo, và chênh lệch đã
/// ĐO ĐƯỢC chứ không phải phỏng đoán:
///   • tổng số mạng mỗi 10 phút: pub 18,96 — pro 12,97, tức pub nhiều hơn 46%;
///   • sát thương trên mỗi vàng, tính trên MỌI người chơi: pub 1,416 — pro 1,110, cao hơn 28%.
///
/// Hậu quả nếu bỏ qua: đem 1,99 mạng/10 phút của người dùng so thẳng với 0,83 của một mid chuyên
/// nghiệp ra "gấp 3 lần", trong khi con số thật sau khi trừ lạm phát là 1,64 lần. Và ở trục sát
/// thương/vàng thì tệ hơn nữa — chưa chuẩn hoá thì người dùng trông như VƯỢT pro, chuẩn hoá xong
/// mới thấy anh đúng bằng pro. Sai hướng, không chỉ sai độ lớn.
///
/// VÌ SAO LƯU TỔNG CHỨ KHÔNG LƯU TRUNG VỊ. Trung vị không cộng được, nên không thể dựng lại từ
/// dữ liệu đã gộp. Tổng thì cộng được: mỗi ván cho một tỉ số "người bình thường trong ván này",
/// rồi lấy trung vị của các tỉ số đó qua nhiều ván. Vẫn bền với ván dị thường, mà lưu được.
///
/// VÌ SAO CHỈ LẤY MẪU. Neo là đặc tính của cả một HỒ ván, không phải của từng ván, nên vài trăm
/// ván là đủ và không cần lấy lại toàn bộ lịch sử — điều đó tránh đúng khoản tiền đã tiêu oan
/// bốn lần trước vì mỗi lần đổi lược đồ lại quét lại gần mười nghìn ván.
/// </summary>
public class StyleAnchor
{
    public long Id { get; set; }

    /// <summary>
    /// Hồ ván mà hàng này thuộc về. "pro" cho ván thi đấu chuyên nghiệp, "pub-{TrackedPlayerId}"
    /// cho ván xếp hạng của từng người được theo dõi.
    ///
    /// Tách theo từng người chứ không gộp một hồ "pub" chung: hai người được theo dõi ở hai mức
    /// rank khác nhau thì ván của họ cũng khác nhịp, và gộp lại là tự tạo ra đúng loại nhiễu mà
    /// bảng này sinh ra để khử.
    /// </summary>
    public required string Pool { get; set; }

    public long MatchId { get; set; }

    public DateTime StartTime { get; set; }

    public int DurationSeconds { get; set; }

    // ---------- Tổng trên cả 10 người ----------
    //
    // Mọi cột dưới đây là TỔNG, không phải trung bình: tổng thì cộng được và dựng lại được tỉ
    // số bất kỳ về sau mà không phải lấy lại dữ liệu.

    public int AllKills { get; set; }
    public int AllAssists { get; set; }

    /// <summary>Tổng số mạng chết. Về lý thuyết bằng AllKills, nhưng mạng do lính/trụ giết thì không.</summary>
    public int AllDeaths { get; set; }

    public long AllNetWorth { get; set; }
    public long AllHeroDamage { get; set; }
    public long AllDamageTaken { get; set; }
    public long AllLastHits { get; set; }
    public long AllTowerDamage { get; set; }

    /// <summary>
    /// Tổng lane_efficiency_pct của những người CÓ chỉ số đó, kèm mẫu số riêng.
    ///
    /// Phải có mẫu số riêng vì trường này chỉ xuất hiện ở ván đã parse và ngay trong ván đã parse
    /// vẫn có người thiếu. Chia cho PlayerCount thì mọi ván thiếu một người sẽ tự động bị kéo
    /// thấp xuống, và cái sai đó im lặng.
    /// </summary>
    public long AllLaneEfficiency { get; set; }

    public int LaneEfficiencyCount { get; set; }

    /// <summary>Số người thực sự có số liệu, để biết ván nào thiếu và loại ra. Bình thường là 10.</summary>
    public int PlayerCount { get; set; }

    public DateTime FetchedAt { get; set; }
}
