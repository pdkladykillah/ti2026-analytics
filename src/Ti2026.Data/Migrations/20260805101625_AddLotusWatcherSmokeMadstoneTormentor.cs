using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLotusWatcherSmokeMadstoneTormentor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Lotuses",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MadstoneBundles",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Smokes",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TormentorKills",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Watchers",
                table: "MatchPlayers",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Lotuses",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "MadstoneBundles",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Smokes",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "TormentorKills",
                table: "MatchPlayers");

            migrationBuilder.DropColumn(
                name: "Watchers",
                table: "MatchPlayers");
        }
    }
}
