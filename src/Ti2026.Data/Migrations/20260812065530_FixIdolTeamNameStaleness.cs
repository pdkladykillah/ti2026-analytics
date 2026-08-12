using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ti2026.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixIdolTeamNameStaleness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TeamNameAt",
                table: "IdolPlayers",
                type: "TEXT",
                nullable: true);

            // Tên đang lưu KHÔNG đáng tin, phải bỏ đi chứ không giữ lại.
            //
            // Chúng được sinh ra bằng luật cũ "mới nhất trong lô", nên có cái đúng có cái sai mà
            // không phân biệt được cái nào là cái nào. Đã đối chiếu với nguồn: ván giải mới nhất
            // của Satanic (8904419709) ghi radiant_name = "PVISION" và anh ở phe Radiant, nhưng
            // bảng đang lưu "Team Falcons"; Whitemon mới nhất (8930664368) ghi dire_name = "1w"
            // còn bảng lưu "Tundra Esports" — đội cũ của anh.
            migrationBuilder.Sql("UPDATE IdolPlayers SET TeamName = NULL, TeamNameAt = NULL");

            // BẮT ĐỌC LẠI ĐÚNG MỘT VÁN MỖI NGƯỜI — ván giải mới nhất của chính họ.
            //
            // Vì sao không "UPDATE IdolMatches SET DetailFetchedAt = NULL" như các migration
            // trước: ở đây không có cột nào của IdolMatches đổi cả, chỉ cần một chuỗi tên đội.
            // Quét lại toàn bộ tốn ~1.200 lời gọi (60% hạn mức miễn phí một ngày) để lấy về
            // đúng 12 chuỗi. Lọc xuống ván mới nhất mỗi người thì còn 12 lời gọi.
            //
            // leagueid khác 0 là bắt buộc: phòng chờ pub cũng đặt được tên đội.
            migrationBuilder.Sql("""
                UPDATE IdolMatches SET DetailFetchedAt = NULL
                WHERE Id IN (
                    SELECT m.Id FROM IdolMatches m
                    WHERE m.LeagueId IS NOT NULL AND m.LeagueId != 0
                      AND m.StartTime = (
                          SELECT MAX(x.StartTime) FROM IdolMatches x
                          WHERE x.IdolPlayerId = m.IdolPlayerId
                            AND x.LeagueId IS NOT NULL AND x.LeagueId != 0))
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeamNameAt",
                table: "IdolPlayers");
        }
    }
}
