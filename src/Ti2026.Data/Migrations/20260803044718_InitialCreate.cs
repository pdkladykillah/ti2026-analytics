using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Heroes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LocalizedName = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ImageMediaAssetId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Heroes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemsWritten = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<long>(type: "INTEGER", nullable: true),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    LeagueId = table.Column<long>(type: "INTEGER", nullable: true),
                    LeagueName = table.Column<string>(type: "TEXT", nullable: true),
                    RadiantTeamId = table.Column<int>(type: "INTEGER", nullable: true),
                    DireTeamId = table.Column<int>(type: "INTEGER", nullable: true),
                    RadiantWin = table.Column<bool>(type: "INTEGER", nullable: false),
                    RadiantScore = table.Column<int>(type: "INTEGER", nullable: false),
                    DireScore = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstBloodTimeSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    RadiantHadFirstBlood = table.Column<bool>(type: "INTEGER", nullable: true),
                    RadiantReachedTenFirst = table.Column<bool>(type: "INTEGER", nullable: true),
                    PatchVersion = table.Column<string>(type: "TEXT", nullable: true),
                    IngestedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    ETag = table.Column<string>(type: "TEXT", nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OpenDotaAccountId = table.Column<long>(type: "INTEGER", nullable: true),
                    Nick = table.Column<string>(type: "TEXT", nullable: false),
                    NickKey = table.Column<string>(type: "TEXT", nullable: false),
                    RealName = table.Column<string>(type: "TEXT", nullable: true),
                    CountryName = table.Column<string>(type: "TEXT", nullable: true),
                    CountryCode = table.Column<string>(type: "TEXT", nullable: true),
                    PhotoUrl = table.Column<string>(type: "TEXT", nullable: true),
                    PhotoMediaAssetId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeedStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeedStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slug = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ShortName = table.Column<string>(type: "TEXT", nullable: true),
                    Region = table.Column<string>(type: "TEXT", nullable: true),
                    Qualification = table.Column<string>(type: "TEXT", nullable: true),
                    LogoUrl = table.Column<string>(type: "TEXT", nullable: true),
                    LogoMediaAssetId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TierEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Patch = table.Column<string>(type: "TEXT", nullable: false),
                    HeroId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tier = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Position = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TierEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TierEntries_Heroes_HeroId",
                        column: x => x.HeroId,
                        principalTable: "Heroes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RosterEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RosterEntries_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RosterEntries_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamAliases_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamStatSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    WindowDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Maps = table.Column<int>(type: "INTEGER", nullable: false),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false),
                    Losses = table.Column<int>(type: "INTEGER", nullable: false),
                    Winrate = table.Column<double>(type: "REAL", nullable: false),
                    AvgKills = table.Column<double>(type: "REAL", nullable: false),
                    AvgDeaths = table.Column<double>(type: "REAL", nullable: false),
                    AvgAssists = table.Column<double>(type: "REAL", nullable: false),
                    KillDiff = table.Column<double>(type: "REAL", nullable: false),
                    TotalKills = table.Column<double>(type: "REAL", nullable: false),
                    FirstBloodRate = table.Column<double>(type: "REAL", nullable: false),
                    F10Rate = table.Column<double>(type: "REAL", nullable: false),
                    WinWhenFbRate = table.Column<double>(type: "REAL", nullable: false),
                    WinWhenF10Rate = table.Column<double>(type: "REAL", nullable: false),
                    AvgDurationMinutes = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamStatSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamStatSnapshots_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngestRuns_Source_StartedAt",
                table: "IngestRuns",
                columns: new[] { "Source", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_SeriesId",
                table: "Matches",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_StartTime",
                table: "Matches",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_ContentHash",
                table: "MediaAssets",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_SourceUrl",
                table: "MediaAssets",
                column: "SourceUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_NickKey",
                table: "Players",
                column: "NickKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_OpenDotaAccountId",
                table: "Players",
                column: "OpenDotaAccountId",
                unique: true,
                filter: "\"OpenDotaAccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_PlayerId",
                table: "RosterEntries",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_TeamId_PlayerId",
                table: "RosterEntries",
                columns: new[] { "TeamId", "PlayerId" },
                unique: true,
                filter: "\"ValidTo\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RosterEntries_TeamId_ValidTo",
                table: "RosterEntries",
                columns: new[] { "TeamId", "ValidTo" });

            migrationBuilder.CreateIndex(
                name: "IX_SeedStates_Key",
                table: "SeedStates",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamAliases_Alias_Source",
                table: "TeamAliases",
                columns: new[] { "Alias", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamAliases_TeamId",
                table: "TeamAliases",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Slug",
                table: "Teams",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamStatSnapshots_TeamId_CapturedOn_WindowDays",
                table: "TeamStatSnapshots",
                columns: new[] { "TeamId", "CapturedOn", "WindowDays" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TierEntries_HeroId",
                table: "TierEntries",
                column: "HeroId");

            migrationBuilder.CreateIndex(
                name: "IX_TierEntries_Patch_HeroId_Position",
                table: "TierEntries",
                columns: new[] { "Patch", "HeroId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IngestRuns");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "RosterEntries");

            migrationBuilder.DropTable(
                name: "SeedStates");

            migrationBuilder.DropTable(
                name: "TeamAliases");

            migrationBuilder.DropTable(
                name: "TeamStatSnapshots");

            migrationBuilder.DropTable(
                name: "TierEntries");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Heroes");
        }
    }
}
