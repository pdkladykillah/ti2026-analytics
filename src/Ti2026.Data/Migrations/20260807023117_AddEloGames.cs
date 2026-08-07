using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEloGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EloGames",
                table: "TeamStatSnapshots",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EloGames",
                table: "TeamStatSnapshots");
        }
    }
}
