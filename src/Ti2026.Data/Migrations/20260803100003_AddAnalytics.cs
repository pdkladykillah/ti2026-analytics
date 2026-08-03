using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Elo",
                table: "TeamStatSnapshots",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Predictions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamAId = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamBId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProbabilityA = table.Column<double>(type: "REAL", nullable: false),
                    EloA = table.Column<double>(type: "REAL", nullable: false),
                    EloB = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedMatchId = table.Column<long>(type: "INTEGER", nullable: true),
                    TeamAWon = table.Column<bool>(type: "INTEGER", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Predictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Predictions_Teams_TeamAId",
                        column: x => x.TeamAId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Predictions_Teams_TeamBId",
                        column: x => x.TeamBId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_CreatedAt",
                table: "Predictions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_ResolvedMatchId",
                table: "Predictions",
                column: "ResolvedMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_TeamAId",
                table: "Predictions",
                column: "TeamAId");

            migrationBuilder.CreateIndex(
                name: "IX_Predictions_TeamBId",
                table: "Predictions",
                column: "TeamBId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Predictions");

            migrationBuilder.DropColumn(
                name: "Elo",
                table: "TeamStatSnapshots");
        }
    }
}
