using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarksAndTeammates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PctAssists",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PctDeaths",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PctDenies",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PctGpm",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PctHeroDamage",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PctHeroHealing",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PctKills",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PctLastHits",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PctTowerDamage",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PctXpm",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrackedMatchTeammates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackedPlayerMatchId = table.Column<long>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<long>(type: "INTEGER", nullable: false),
                    PersonaName = table.Column<string>(type: "TEXT", nullable: true),
                    HeroId = table.Column<int>(type: "INTEGER", nullable: false),
                    SameParty = table.Column<bool>(type: "INTEGER", nullable: false),
                    RankTier = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedMatchTeammates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedMatchTeammates_TrackedPlayerMatches_TrackedPlayerMatchId",
                        column: x => x.TrackedPlayerMatchId,
                        principalTable: "TrackedPlayerMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedMatchTeammates_AccountId",
                table: "TrackedMatchTeammates",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedMatchTeammates_TrackedPlayerMatchId_AccountId",
                table: "TrackedMatchTeammates",
                columns: new[] { "TrackedPlayerMatchId", "AccountId" },
                unique: true);

            // Bắt nạp lại chi tiết những ván ĐÃ lấy trước khi có hai thứ trên.
            //
            // DetailFetchedAt nghĩa là "đã lấy đủ bối cảnh ván này". Sau lần này thì định nghĩa
            // "đủ" đã đổi: giờ gồm cả phân vị lẫn danh sách đồng đội. Không đặt lại thì những ván
            // lấy trước đây sẽ vĩnh viễn không có hai phần đó, mà cũng không có gì báo — trang
            // vẫn chạy, chỉ là bảng đồng đội thiếu mất phần lịch sử cũ và không ai biết vì sao.
            migrationBuilder.Sql(
                "UPDATE TrackedPlayerMatches SET DetailFetchedAt = NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackedMatchTeammates");

            migrationBuilder.DropColumn(
                name: "PctAssists",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "PctDeaths",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "PctDenies",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "PctGpm",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "PctHeroDamage",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "PctHeroHealing",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "PctKills",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "PctLastHits",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "PctTowerDamage",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "PctXpm",
                table: "TrackedPlayerMatches");
        }
    }
}
