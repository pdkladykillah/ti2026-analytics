using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFantasyStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CourierKills",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FirstBloodClaimed",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ObserverKills",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoshanKills",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SentryKills",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TowerKills",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CourierKills",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "FirstBloodClaimed",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "ObserverKills",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "RoshanKills",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "SentryKills",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "TowerKills",
                table: "MatchPlayers");
        }
    }
}
