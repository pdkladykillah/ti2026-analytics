using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedPlayerHeroes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackedPlayerHeroes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackedPlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    HeroId = table.Column<int>(type: "INTEGER", nullable: false),
                    Games = table.Column<int>(type: "INTEGER", nullable: false),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false),
                    WithGames = table.Column<int>(type: "INTEGER", nullable: false),
                    WithWins = table.Column<int>(type: "INTEGER", nullable: false),
                    AgainstGames = table.Column<int>(type: "INTEGER", nullable: false),
                    AgainstWins = table.Column<int>(type: "INTEGER", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedPlayerHeroes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedPlayerHeroes_TrackedPlayers_TrackedPlayerId",
                        column: x => x.TrackedPlayerId,
                        principalTable: "TrackedPlayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedPlayerHeroes_TrackedPlayerId_HeroId",
                table: "TrackedPlayerHeroes",
                columns: new[] { "TrackedPlayerId", "HeroId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackedPlayerHeroes");
        }
    }
}
