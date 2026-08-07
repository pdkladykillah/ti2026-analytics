namespace Ti2026.Data.Entities;

/// <summary>
/// Xương sống của phần phân tích form. Một dòng cho mỗi (đội, ngày, cửa sổ).
/// Snapshot của các ngày khác nhau KHÔNG bao giờ ghi đè nhau — đó là kho lịch sử,
/// và lịch sử là thứ không lấy lại được. Trong cùng một ngày thì upsert, nên chạy
/// ingest nhiều lần trong ngày cũng không nhân đôi dữ liệu.
///
/// 10 chỉ số dưới đây mirror đúng field "order" trong h2h.json hiện tại, nên UI H2H
/// chạy được không cần sửa logic.
/// </summary>
public class TeamStatSnapshot
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }
    public DateOnly CapturedOn { get; set; }

    /// <summary>30 | 90 | 180 ngày.</summary>
    public int WindowDays { get; set; }

    public int Maps { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }

    /// <summary>Phần trăm, 0..100.</summary>
    public double Winrate { get; set; }

    public double AvgKills { get; set; }
    public double AvgDeaths { get; set; }
    public double KillDiff { get; set; }
    public double TotalKills { get; set; }

    /// <summary>Phút — khớp đơn vị của field "duration" trong teams.json.</summary>
    public double AvgDurationMinutes { get; set; }

    // ---- Năm chỉ số dưới đây NULLABLE có chủ ý ----
    //
    // Endpoint teams/{id}/matches của OpenDota không trả về assists, first blood, hay mốc
    // đội nào đạt 10 kill trước. Chúng chỉ có trong match detail (matches/{id}) — một request
    // cho mỗi trận, tức việc của Giai đoạn 3.
    //
    // null = CHƯA BIẾT, khác hoàn toàn với 0 = ĐÃ ĐO VÀ BẰNG KHÔNG. Nếu để không nullable thì
    // ingest sẽ ghi 0 và trang hiển thị "first blood 0%" cho cả 16 đội, trông y như số thật.
    // API trả null, UI hiện "—".

    /// <summary>
    /// Elo tại thời điểm chụp. Lưu vào snapshot thay vì bảng riêng để có LỊCH SỬ RATING
    /// miễn phí — biểu đồ phong độ vẽ được đường Elo theo ngày mà không thêm hạ tầng nào.
    ///
    /// null khi đội chưa đá đủ <see cref="Ti2026.Ingest"/> SnapshotWriter.MinGamesForRating ván
    /// mà CẢ HAI bên đều là đội hình TI2026 — xem <see cref="EloGames"/>.
    /// </summary>
    public double? Elo { get; set; }

    /// <summary>
    /// Số ván đã dùng để tính <see cref="Elo"/>: những ván mà cả hai bên đều ra sân đủ 5 người
    /// của đội hình hiện tại.
    ///
    /// Phải công bố con số này. Không có nó thì một Elo dựng trên 11 ván trông y hệt một Elo
    /// dựng trên 102 ván, và người đọc không có cách nào biết cái nào đáng tin. Khác
    /// <see cref="Maps"/>: Maps đếm ván trong cửa sổ mà CHÍNH đội này đủ đội hình, không đòi
    /// hỏi gì ở đối thủ.
    ///
    /// null = hàng ghi từ trước khi có luật lọc đội hình.
    /// </summary>
    public int? EloGames { get; set; }

    public double? AvgAssists { get; set; }
    public double? FirstBloodRate { get; set; }
    public double? F10Rate { get; set; }
    public double? WinWhenFbRate { get; set; }
    public double? WinWhenF10Rate { get; set; }

    /// <summary>
    /// Nguồn của hàng này: "editorial" (seed từ teams.json), "opendota" (tính từ match thật),
    /// hoặc "mixed" (ingest cập nhật phần tính được, giữ lại phần biên tập chưa tính được).
    /// Có cột này để không bao giờ nhầm số biên tập là số đo.
    /// </summary>
    public required string Source { get; set; }
}
