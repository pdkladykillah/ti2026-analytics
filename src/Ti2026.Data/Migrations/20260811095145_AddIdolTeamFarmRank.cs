using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdolTeamFarmRank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TeamFarmRank",
                table: "IdolMatches",
                type: "INTEGER",
                nullable: true);

            // BẮT LẤY LẠI CHI TIẾT MỌI VÁN ĐÃ LƯU.
            //
            // Thêm cột thôi thì 1.200 ván cũ mang NULL vĩnh viễn, và vì PositionOf trả null khi
            // thiếu hạng nên toàn bộ tuyển thủ cũ sẽ biến mất khỏi mọi phép so — không lỗi, không
            // báo, chỉ là bảng trống. Đặt lại DetailFetchedAt để vòng ingest sau quét lại.
            //
            // Chi phí có thật và đã cân: khoảng 1.200 lời gọi (~0,12 đô). Rẻ hơn nhiều so với
            // phần theo dõi cá nhân vì ván chuyên nghiệp không phải xin parse.
            migrationBuilder.Sql("UPDATE IdolMatches SET DetailFetchedAt = NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeamFarmRank",
                table: "IdolMatches");
        }
    }
}
