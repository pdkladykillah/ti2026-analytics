using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLanePhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GoldAdv10",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GoldAdv20",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GoldAdv30",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LaneEfficiency",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            // Chỉ nạp lại ván ĐÃ PARSE — hiệu suất lane và đường vàng theo phút chỉ tồn tại ở đó.
            // Đặt lại cả kho sẽ tốn 9.870 lời gọi để lấy về đúng ~840 ván có thêm dữ liệu.
            migrationBuilder.Sql(
                "UPDATE TrackedPlayerMatches SET DetailFetchedAt = NULL WHERE LaneRole IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoldAdv10",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "GoldAdv20",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "GoldAdv30",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "LaneEfficiency",
                table: "TrackedPlayerMatches");
        }
    }
}
