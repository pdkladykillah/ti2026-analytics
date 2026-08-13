using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyDigest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyDigests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LeagueId = table.Column<long>(type: "INTEGER", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StageName = table.Column<string>(type: "TEXT", nullable: true),
                    SeriesTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesCompleted = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchesCounted = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchesExpected = table.Column<int>(type: "INTEGER", nullable: false),
                    MedianDurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ComputedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyDigests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyDigests_LeagueId_Day",
                table: "DailyDigests",
                columns: new[] { "LeagueId", "Day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyDigests");
        }
    }
}
