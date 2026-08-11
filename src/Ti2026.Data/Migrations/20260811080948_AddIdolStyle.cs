using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdolStyle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdolPlayers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", nullable: false),
                    PersonaName = table.Column<string>(type: "TEXT", nullable: true),
                    TeamName = table.Column<string>(type: "TEXT", nullable: true),
                    AvatarUrl = table.Column<string>(type: "TEXT", nullable: true),
                    DeclaredRole = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MatchesFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdolPlayers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StyleAnchors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Pool = table.Column<string>(type: "TEXT", nullable: false),
                    MatchId = table.Column<long>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    AllKills = table.Column<int>(type: "INTEGER", nullable: false),
                    AllAssists = table.Column<int>(type: "INTEGER", nullable: false),
                    AllDeaths = table.Column<int>(type: "INTEGER", nullable: false),
                    AllNetWorth = table.Column<long>(type: "INTEGER", nullable: false),
                    AllHeroDamage = table.Column<long>(type: "INTEGER", nullable: false),
                    AllDamageTaken = table.Column<long>(type: "INTEGER", nullable: false),
                    AllLastHits = table.Column<long>(type: "INTEGER", nullable: false),
                    AllTowerDamage = table.Column<long>(type: "INTEGER", nullable: false),
                    AllLaneEfficiency = table.Column<long>(type: "INTEGER", nullable: false),
                    LaneEfficiencyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StyleAnchors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdolMatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdolPlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchId = table.Column<long>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    Won = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRadiant = table.Column<bool>(type: "INTEGER", nullable: false),
                    HeroId = table.Column<int>(type: "INTEGER", nullable: false),
                    LobbyType = table.Column<int>(type: "INTEGER", nullable: true),
                    LeagueId = table.Column<int>(type: "INTEGER", nullable: true),
                    GameMode = table.Column<int>(type: "INTEGER", nullable: true),
                    PatchId = table.Column<int>(type: "INTEGER", nullable: true),
                    Kills = table.Column<int>(type: "INTEGER", nullable: false),
                    Deaths = table.Column<int>(type: "INTEGER", nullable: false),
                    Assists = table.Column<int>(type: "INTEGER", nullable: false),
                    LastHits = table.Column<int>(type: "INTEGER", nullable: true),
                    Denies = table.Column<int>(type: "INTEGER", nullable: true),
                    GoldPerMin = table.Column<int>(type: "INTEGER", nullable: true),
                    XpPerMin = table.Column<int>(type: "INTEGER", nullable: true),
                    NetWorth = table.Column<int>(type: "INTEGER", nullable: true),
                    Level = table.Column<int>(type: "INTEGER", nullable: true),
                    HeroDamage = table.Column<int>(type: "INTEGER", nullable: true),
                    TowerDamage = table.Column<int>(type: "INTEGER", nullable: true),
                    HeroHealing = table.Column<int>(type: "INTEGER", nullable: true),
                    DamageTaken = table.Column<int>(type: "INTEGER", nullable: true),
                    ObsPlaced = table.Column<int>(type: "INTEGER", nullable: true),
                    SenPlaced = table.Column<int>(type: "INTEGER", nullable: true),
                    CampsStacked = table.Column<int>(type: "INTEGER", nullable: true),
                    Stuns = table.Column<double>(type: "REAL", nullable: true),
                    TeamfightParticipation = table.Column<double>(type: "REAL", nullable: true),
                    LaneRole = table.Column<int>(type: "INTEGER", nullable: true),
                    Lane = table.Column<int>(type: "INTEGER", nullable: true),
                    IsRoaming = table.Column<bool>(type: "INTEGER", nullable: true),
                    LaneEfficiency = table.Column<int>(type: "INTEGER", nullable: true),
                    GoldAdv10 = table.Column<int>(type: "INTEGER", nullable: true),
                    GoldAdv20 = table.Column<int>(type: "INTEGER", nullable: true),
                    GoldAdv30 = table.Column<int>(type: "INTEGER", nullable: true),
                    FirstKillSecond = table.Column<int>(type: "INTEGER", nullable: true),
                    TeamKills = table.Column<int>(type: "INTEGER", nullable: true),
                    TeamDeaths = table.Column<int>(type: "INTEGER", nullable: true),
                    TeamNetWorth = table.Column<long>(type: "INTEGER", nullable: true),
                    TeamHeroDamage = table.Column<long>(type: "INTEGER", nullable: true),
                    TeamDamageTaken = table.Column<long>(type: "INTEGER", nullable: true),
                    DetailFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdolMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdolMatches_IdolPlayers_IdolPlayerId",
                        column: x => x.IdolPlayerId,
                        principalTable: "IdolPlayers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdolMatches_IdolPlayerId_MatchId",
                table: "IdolMatches",
                columns: new[] { "IdolPlayerId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdolMatches_StartTime",
                table: "IdolMatches",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_IdolPlayers_AccountId",
                table: "IdolPlayers",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdolPlayers_NameKey",
                table: "IdolPlayers",
                column: "NameKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StyleAnchors_Pool_MatchId",
                table: "StyleAnchors",
                columns: new[] { "Pool", "MatchId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdolMatches");

            migrationBuilder.DropTable(
                name: "StyleAnchors");

            migrationBuilder.DropTable(
                name: "IdolPlayers");
        }
    }
}
