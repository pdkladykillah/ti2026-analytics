using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHeroStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HeroStats",
                columns: table => new
                {
                    HeroId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProPick = table.Column<int>(type: "INTEGER", nullable: false),
                    ProWin = table.Column<int>(type: "INTEGER", nullable: false),
                    ProBan = table.Column<int>(type: "INTEGER", nullable: false),
                    PubPick = table.Column<long>(type: "INTEGER", nullable: false),
                    PubWin = table.Column<long>(type: "INTEGER", nullable: false),
                    HighPick = table.Column<long>(type: "INTEGER", nullable: false),
                    HighWin = table.Column<long>(type: "INTEGER", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeroStats", x => x.HeroId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HeroStats");
        }
    }
}
