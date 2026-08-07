using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduledSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LeagueId = table.Column<long>(type: "INTEGER", nullable: false),
                    NodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupName = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActualAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ValveTeamId1 = table.Column<int>(type: "INTEGER", nullable: true),
                    ValveTeamId2 = table.Column<int>(type: "INTEGER", nullable: true),
                    TeamId1 = table.Column<int>(type: "INTEGER", nullable: true),
                    Team1Id = table.Column<int>(type: "INTEGER", nullable: true),
                    TeamId2 = table.Column<int>(type: "INTEGER", nullable: true),
                    Team2Id = table.Column<int>(type: "INTEGER", nullable: true),
                    Wins1 = table.Column<int>(type: "INTEGER", nullable: false),
                    Wins2 = table.Column<int>(type: "INTEGER", nullable: false),
                    HasStarted = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<long>(type: "INTEGER", nullable: true),
                    WinningNodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    LosingNodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    IncomingNodeId1 = table.Column<int>(type: "INTEGER", nullable: true),
                    IncomingNodeId2 = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledSeries_Teams_Team1Id",
                        column: x => x.Team1Id,
                        principalTable: "Teams",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScheduledSeries_Teams_Team2Id",
                        column: x => x.Team2Id,
                        principalTable: "Teams",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledSeries_LeagueId_NodeId",
                table: "ScheduledSeries",
                columns: new[] { "LeagueId", "NodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledSeries_Team1Id",
                table: "ScheduledSeries",
                column: "Team1Id");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledSeries_Team2Id",
                table: "ScheduledSeries",
                column: "Team2Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduledSeries");
        }
    }
}
