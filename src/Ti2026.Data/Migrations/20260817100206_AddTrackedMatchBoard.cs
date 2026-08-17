using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedMatchBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackedMatchBoards",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MatchId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerSlot = table.Column<int>(type: "INTEGER", nullable: false),
                    IsRadiant = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<long>(type: "INTEGER", nullable: true),
                    PersonaName = table.Column<string>(type: "TEXT", nullable: true),
                    HeroId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kills = table.Column<int>(type: "INTEGER", nullable: false),
                    Deaths = table.Column<int>(type: "INTEGER", nullable: false),
                    Assists = table.Column<int>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: true),
                    NetWorth = table.Column<int>(type: "INTEGER", nullable: true),
                    LastHits = table.Column<int>(type: "INTEGER", nullable: true),
                    Denies = table.Column<int>(type: "INTEGER", nullable: true),
                    GoldPerMin = table.Column<int>(type: "INTEGER", nullable: false),
                    XpPerMin = table.Column<int>(type: "INTEGER", nullable: false),
                    HeroDamage = table.Column<int>(type: "INTEGER", nullable: true),
                    TowerDamage = table.Column<int>(type: "INTEGER", nullable: true),
                    HeroHealing = table.Column<int>(type: "INTEGER", nullable: true),
                    PartyId = table.Column<int>(type: "INTEGER", nullable: true),
                    RankTier = table.Column<int>(type: "INTEGER", nullable: true),
                    Item0 = table.Column<int>(type: "INTEGER", nullable: true),
                    Item1 = table.Column<int>(type: "INTEGER", nullable: true),
                    Item2 = table.Column<int>(type: "INTEGER", nullable: true),
                    Item3 = table.Column<int>(type: "INTEGER", nullable: true),
                    Item4 = table.Column<int>(type: "INTEGER", nullable: true),
                    Item5 = table.Column<int>(type: "INTEGER", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedMatchBoards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedMatchBoards_MatchId_PlayerSlot",
                table: "TrackedMatchBoards",
                columns: new[] { "MatchId", "PlayerSlot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackedMatchBoards");
        }
    }
}
