using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReopenUndercountedDigests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MỞ LẠI NHỮNG NGÀY ĐÃ CHỐT KHI CÒN THIẾU VÁN.
            //
            // Lỗi người dùng phát hiện sau bốn ngày: ngày 13/08 đóng băng ở "đọc được 18/29 ván"
            // trong khi bảng Matches thật ra có đủ cả 29. Bản trước chốt ngay khi Valve báo mọi
            // series đã xong — mà Valve làm tươi mỗi 15 phút còn chi tiết ván theo vòng 6 giờ,
            // nên ngày bị khoá trước khi phần ván kịp về. Đo được lúc sửa: 13/08 thiếu 11 ván,
            // 14/08 thiếu 8, 16/08 thiếu 2, còn 15/08 tình cờ đủ nên đúng.
            //
            // Đặt ClosedAt về NULL là đủ để bộ ghi tính lại ở lượt làm tươi kế tiếp; luật chốt
            // mới sẽ đòi đủ ván (hoặc quá 24 giờ) trước khi khoá lại. KHÔNG xoá dòng: số liệu
            // series trong đó vẫn đúng, chỉ phần đếm ván là thiếu.
            migrationBuilder.Sql(
                "UPDATE DailyDigests SET ClosedAt = NULL WHERE MatchesCounted < MatchesExpected");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
