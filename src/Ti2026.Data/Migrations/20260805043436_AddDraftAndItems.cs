using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftAndItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Denies",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeroDamage",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Lane",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LaneEfficiencyPct",
                table: "MatchPlayers",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LaneRole",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastHits",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NetWorth",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ObserversPlaced",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TowerDamage",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DetailSchemaVersion",
                table: "Matches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DraftEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MatchId = table.Column<long>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPick = table.Column<bool>(type: "INTEGER", nullable: false),
                    HeroId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRadiant = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DraftEvents_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemPurchases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MatchId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerSlot = table.Column<int>(type: "INTEGER", nullable: false),
                    HeroId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRadiant = table.Column<bool>(type: "INTEGER", nullable: false),
                    ItemKey = table.Column<string>(type: "TEXT", nullable: false),
                    TimeSeconds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemPurchases_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftEvents_HeroId",
                table: "DraftEvents",
                column: "HeroId");

            migrationBuilder.CreateIndex(
                name: "IX_DraftEvents_MatchId_Order",
                table: "DraftEvents",
                columns: new[] { "MatchId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemPurchases_HeroId_ItemKey",
                table: "ItemPurchases",
                columns: new[] { "HeroId", "ItemKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemPurchases_MatchId",
                table: "ItemPurchases",
                column: "MatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DraftEvents");

            migrationBuilder.DropTable(
                name: "ItemPurchases");

            migrationBuilder.DropColumn(
                name: "Denies",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "HeroDamage",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Lane",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "LaneEfficiencyPct",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "LaneRole",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "LastHits",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "NetWorth",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "ObserversPlaced",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "TowerDamage",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "DetailSchemaVersion",
                table: "Matches");
        }
    }
}
