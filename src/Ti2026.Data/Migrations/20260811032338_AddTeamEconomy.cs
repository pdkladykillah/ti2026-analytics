using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamEconomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EnemyNetWorth",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MatesPctGpm",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MatesPctXpm",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamNetWorth",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            // Bắt nạp lại chi tiết: định nghĩa "đã lấy đủ" vừa đổi lần nữa, giờ gồm cả kinh tế
            // hai phe. Cùng lý do với lần thêm phân vị và đồng đội — không đặt lại thì những ván
            // lấy trước đây vĩnh viễn thiếu bốn cột này mà không có gì báo.
            migrationBuilder.Sql("UPDATE TrackedPlayerMatches SET DetailFetchedAt = NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnemyNetWorth",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "MatesPctGpm",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "MatesPctXpm",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "TeamNetWorth",
                table: "TrackedPlayerMatches");
        }
    }
}
