namespace Ti2026.Data.Entities;

/// <summary>
/// Điểm nhấn của MỘT ngày thi đấu. Một dòng cho mỗi (giải, ngày UTC).
///
/// CÙNG KHUÔN VỚI <see cref="TeamStatSnapshot"/>: upsert trong cùng ngày, và ngày khác nhau
/// KHÔNG BAO GIỜ đè nhau. Đây là kho lịch sử, mà lịch sử là thứ không lấy lại được — bảng đấu
/// của Valve bị ghi đè ở mỗi lượt làm tươi, nên trạng thái "hôm đó nhìn thế nào" chỉ tồn tại
/// nếu ta chủ động giữ.
///
/// KHÁC api/h2h ở đúng chỗ đó. h2h cố ý KHÔNG có bảng dẫn xuất vì bảng dẫn xuất sẽ lệch khỏi
/// nguồn khi ingest chạy lại. Ở đây thì "lệch khỏi nguồn" chính là mục đích: ta muốn đóng băng
/// một thời điểm, không muốn nó tự đổi theo lần nạp sau.
/// </summary>
public class DailyDigest
{
    public int Id { get; set; }

    public long LeagueId { get; set; }

    /// <summary>
    /// Ngày theo UTC, KHÔNG theo múi giờ người xem.
    ///
    /// Một bản ghi lịch sử phải có đúng một mốc. Nếu lấy theo giờ máy người đọc thì cùng một
    /// loạt đấu sẽ rơi vào hai ngày khác nhau với hai người ở hai múi giờ, và bảng "so với ngày
    /// trước" sẽ so hai thứ khác nhau. Phần hiển thị vẫn đổi sang giờ máy như mọi chỗ khác.
    /// </summary>
    public DateOnly Day { get; set; }

    /// <summary>Tên vòng, ví dụ "Swiss". Null khi một ngày có nhiều vòng.</summary>
    public string? StageName { get; set; }

    public int SeriesTotal { get; set; }
    public int SeriesCompleted { get; set; }

    /// <summary>Số ván ĐỌC ĐƯỢC từ OpenDota. Xem <see cref="MatchesExpected"/>.</summary>
    public int MatchesCounted { get; set; }

    /// <summary>
    /// Số ván SUY RA từ tỷ số các loạt trong bảng đấu Valve.
    ///
    /// Hai con số này gần như luôn lệch nhau, và đó là thông tin chứ không phải lỗi: bảng đấu
    /// làm tươi mỗi 15 phút còn chi tiết ván đi theo vòng ingest 6 giờ. Đo lúc 04:5x ngày
    /// 13/08: bảng đấu đã ghi 2/8 loạt xong nhưng mới 4 ván nằm trong Matches. Giao diện phải
    /// nói ra độ phủ thay vì để người đọc tưởng đã tính trên tất cả.
    /// </summary>
    public int MatchesExpected { get; set; }

    public int? MedianDurationSeconds { get; set; }

    /// <summary>
    /// Khác null nghĩa là ngày đã chốt: mọi loạt xếp trong ngày đều xong. Từ lúc đó KHÔNG tính
    /// lại nữa — nếu tính lại thì một lần nạp bù dữ liệu cũ sẽ lặng lẽ viết lại lịch sử.
    /// </summary>
    public DateTime? ClosedAt { get; set; }

    public DateTime ComputedAt { get; set; }

    /// <summary>
    /// Các điểm nhấn đã tính, dạng JSON.
    ///
    /// NGOẠI LỆ CÓ CHỦ Ý trong một dự án vốn khai kiểu chặt. Danh sách điểm nhấn sẽ còn thêm
    /// bớt, mà mỗi lần thêm một loại là một migration cộng một lần quét lại toàn bộ — thứ đã
    /// tốn 2,97 đô ở phần theo dõi cá nhân. Phần TÍNH vẫn typed hoàn toàn; JSON chỉ là lớp lưu.
    /// </summary>
    public string Payload { get; set; } = "{}";
}
