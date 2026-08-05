using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Buybacks",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CampsStacked",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RunePickups",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SentriesPlaced",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StunSeconds",
                table: "MatchPlayers",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TeamfightParticipation",
                table: "MatchPlayers",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ComebackGold",
                table: "Matches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirstRoshanSeconds",
                table: "Matches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThrowGold",
                table: "Matches",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Buybacks",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "CampsStacked",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "RunePickups",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "SentriesPlaced",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "StunSeconds",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "TeamfightParticipation",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "ComebackGold",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "FirstRoshanSeconds",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ThrowGold",
                table: "Matches");
        }
    }
}
