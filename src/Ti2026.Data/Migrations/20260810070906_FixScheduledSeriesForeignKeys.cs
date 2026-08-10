using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixScheduledSeriesForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScheduledSeries_Teams_Team1Id",
                table: "ScheduledSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_ScheduledSeries_Teams_Team2Id",
                table: "ScheduledSeries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledSeries_Team1Id",
                table: "ScheduledSeries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledSeries_Team2Id",
                table: "ScheduledSeries");

            migrationBuilder.DropColumn(
                name: "Team1Id",
                table: "ScheduledSeries");

            migrationBuilder.DropColumn(
                name: "Team2Id",
                table: "ScheduledSeries");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledSeries_TeamId1",
                table: "ScheduledSeries",
                column: "TeamId1");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledSeries_TeamId2",
                table: "ScheduledSeries",
                column: "TeamId2");

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduledSeries_Teams_TeamId1",
                table: "ScheduledSeries",
                column: "TeamId1",
                principalTable: "Teams",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduledSeries_Teams_TeamId2",
                table: "ScheduledSeries",
                column: "TeamId2",
                principalTable: "Teams",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScheduledSeries_Teams_TeamId1",
                table: "ScheduledSeries");

            migrationBuilder.DropForeignKey(
                name: "FK_ScheduledSeries_Teams_TeamId2",
                table: "ScheduledSeries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledSeries_TeamId1",
                table: "ScheduledSeries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledSeries_TeamId2",
                table: "ScheduledSeries");

            migrationBuilder.AddColumn<int>(
                name: "Team1Id",
                table: "ScheduledSeries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Team2Id",
                table: "ScheduledSeries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledSeries_Team1Id",
                table: "ScheduledSeries",
                column: "Team1Id");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledSeries_Team2Id",
                table: "ScheduledSeries",
                column: "Team2Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduledSeries_Teams_Team1Id",
                table: "ScheduledSeries",
                column: "Team1Id",
                principalTable: "Teams",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduledSeries_Teams_Team2Id",
                table: "ScheduledSeries",
                column: "Team2Id",
                principalTable: "Teams",
                principalColumn: "Id");
        }
    }
}
