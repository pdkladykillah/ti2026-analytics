using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProPubMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProPubMatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchId = table.Column<long>(type: "INTEGER", nullable: false),
                    HeroId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Won = table.Column<bool>(type: "INTEGER", nullable: false),
                    LobbyType = table.Column<int>(type: "INTEGER", nullable: true),
                    GameMode = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProPubMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProPubMatches_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProPubMatches_HeroId",
                table: "ProPubMatches",
                column: "HeroId");

            migrationBuilder.CreateIndex(
                name: "IX_ProPubMatches_PlayerId_MatchId",
                table: "ProPubMatches",
                columns: new[] { "PlayerId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProPubMatches_StartTime",
                table: "ProPubMatches",
                column: "StartTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProPubMatches");
        }
    }
}
