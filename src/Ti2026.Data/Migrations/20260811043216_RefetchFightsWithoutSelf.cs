using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class RefetchFightsWithoutSelf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Không đổi cột nào, chỉ bắt đọc lại phần giao tranh.
            //
            // Cách tính chênh lệch vàng đã đổi: trước cộng cả năm người phe mình, giờ chỉ cộng
            // BỐN ĐỒNG ĐỘI. Lý do ở TrackedMatchDetailIngester.ReadFights — khi ta chết thì
            // gold_delta của chính ta đã âm sẵn, nên cộng cả ta vào là cài sẵn câu trả lời "lỗ"
            // cho mọi cái chết.
            //
            // Chỉ động tới ván đã đọc giao tranh (~841), không phải cả kho.
            migrationBuilder.Sql(
                "UPDATE TrackedPlayerMatches SET DetailFetchedAt = NULL WHERE FightsDied IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
