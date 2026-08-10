using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackedPlayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<long>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    IsOwner = table.Column<bool>(type: "INTEGER", nullable: false),
                    PersonaName = table.Column<string>(type: "TEXT", nullable: true),
                    AvatarUrl = table.Column<string>(type: "TEXT", nullable: true),
                    RankTier = table.Column<int>(type: "INTEGER", nullable: true),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false),
                    Losses = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SyncNote = table.Column<string>(type: "TEXT", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedPlayers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackedPlayerMatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrackedPlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchId = table.Column<long>(type: "INTEGER", nullable: false),
                    HeroId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    Won = table.Column<bool>(type: "INTEGER", nullable: false),
                    Kills = table.Column<int>(type: "INTEGER", nullable: false),
                    Deaths = table.Column<int>(type: "INTEGER", nullable: false),
                    Assists = table.Column<int>(type: "INTEGER", nullable: false),
                    GoldPerMin = table.Column<int>(type: "INTEGER", nullable: true),
                    XpPerMin = table.Column<int>(type: "INTEGER", nullable: true),
                    LastHits = table.Column<int>(type: "INTEGER", nullable: true),
                    Denies = table.Column<int>(type: "INTEGER", nullable: true),
                    HeroDamage = table.Column<int>(type: "INTEGER", nullable: true),
                    TowerDamage = table.Column<int>(type: "INTEGER", nullable: true),
                    HeroHealing = table.Column<int>(type: "INTEGER", nullable: true),
                    LaneRole = table.Column<int>(type: "INTEGER", nullable: true),
                    LobbyType = table.Column<int>(type: "INTEGER", nullable: true),
                    GameMode = table.Column<int>(type: "INTEGER", nullable: true),
                    PartySize = table.Column<int>(type: "INTEGER", nullable: true),
                    AverageRank = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedPlayerMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedPlayerMatches_TrackedPlayers_TrackedPlayerId",
                        column: x => x.TrackedPlayerId,
                        principalTable: "TrackedPlayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedPlayerMatches_HeroId",
                table: "TrackedPlayerMatches",
                column: "HeroId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedPlayerMatches_StartTime",
                table: "TrackedPlayerMatches",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedPlayerMatches_TrackedPlayerId_MatchId",
                table: "TrackedPlayerMatches",
                columns: new[] { "TrackedPlayerId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedPlayers_AccountId",
                table: "TrackedPlayers",
                column: "AccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackedPlayerMatches");

            migrationBuilder.DropTable(
                name: "TrackedPlayers");
        }
    }
}
