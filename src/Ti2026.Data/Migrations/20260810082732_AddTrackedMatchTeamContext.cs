using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedMatchTeamContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DetailFetchedAt",
                table: "TrackedPlayerMatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Level",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NetWorth",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ParseRequestedAt",
                table: "TrackedPlayerMatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamFarmRank",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamXpmRank",
                table: "TrackedPlayerMatches",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailFetchedAt",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "NetWorth",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "ParseRequestedAt",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "TeamFarmRank",
                table: "TrackedPlayerMatches");

            migrationBuilder.DropColumn(
                name: "TeamXpmRank",
                table: "TrackedPlayerMatches");
        }
    }
}
