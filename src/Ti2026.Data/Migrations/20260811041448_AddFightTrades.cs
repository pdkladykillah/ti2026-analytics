using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFightTrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FightSwingDied",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FightSwingSurvived",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FightsDied",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FightsDiedAhead",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FightsSurvived",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TradeFoeDeaths",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TradeFoeGold",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TradeMyGold",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            // Nạp lại CHỈ những ván đã parse, không phải toàn bộ.
            //
            // Dữ liệu giao tranh chỉ tồn tại ở ván đã parse, và lane_role khác null chính là dấu
            // hiệu của việc đó. Đặt lại tất cả sẽ tốn 9.870 lời gọi để lấy về đúng ~750 ván có
            // thêm dữ liệu — mười ba lần chi phí cho cùng một kết quả.
            //
            // Ván parse SAU này thì tự có: bộ nạp vốn đã đặt lại DetailFetchedAt = null ngay sau
            // khi xin parse, nên vòng kế tiếp lấy lại chi tiết và đọc luôn phần giao tranh.
            migrationBuilder.Sql(
                "UPDATE TrackedPlayerMatches SET DetailFetchedAt = NULL WHERE LaneRole IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FightSwingDied",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "FightSwingSurvived",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "FightsDied",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "FightsDiedAhead",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "FightsSurvived",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "TradeFoeDeaths",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "TradeFoeGold",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "TradeMyGold",
                table: "TrackedPlayerMatches");
        }
    }
}
