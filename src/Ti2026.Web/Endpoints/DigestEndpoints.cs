using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ti2026.Data;

namespace Ti2026.Web.Endpoints;

/// <summary>
/// api/digest — điểm nhấn từng ngày thi đấu, mới nhất trước.
///
/// Đọc thẳng từ bảng DailyDigest, không tính lại: bảng đó là kho lịch sử, và tính lại lúc đọc
/// sẽ cho ra con số của HÔM NAY gắn nhãn của hôm kia. Việc tính do bộ làm tươi 15 phút lo.
/// </summary>
public static class DigestEndpoints
{
    /// <summary>Số ngày trả về. Đủ cho một kỳ TI, không cần phân trang.</summary>
    public const int MaxDays = 30;

    public static void MapDigestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/digest", async (Ti2026DbContext db) =>
        {
            var rows = await db.DailyDigests
                .OrderByDescending(d => d.Day)
                .Take(MaxDays)
                .ToListAsync();

            if (rows.Count == 0)
                return Results.Ok(new
                {
                    ready = false,
                    note = "Chưa có ngày thi đấu nào để đúc kết. Mục này tự đầy lên khi giải bắt đầu.",
                    days = Array.Empty<object>(),
                });

            return Results.Ok(new
            {
                ready = true,
                days = rows.Select(d => new
                {
                    day = d.Day.ToString("yyyy-MM-dd"),
                    stage = d.StageName,

                    seriesTotal = d.SeriesTotal,
                    seriesCompleted = d.SeriesCompleted,

                    // Hai con số phủ, trả về CẢ HAI. Bảng đấu làm tươi mỗi 15 phút còn chi tiết
                    // ván đi theo vòng ingest 6 giờ, nên chúng gần như luôn lệch — và đó là
                    // thông tin người đọc cần, không phải lỗi cần giấu.
                    matchesRead = d.MatchesCounted,
                    matchesExpected = d.MatchesExpected,

                    medianMinutes = d.MedianDurationSeconds is int s ? s / 60 : (int?)null,

                    closed = d.ClosedAt != null,
                    closedAt = d.ClosedAt,
                    computedAt = d.ComputedAt,

                    // Payload đã là JSON — parse rồi gắn vào để nó thành đối tượng thật trong
                    // phản hồi, không phải một chuỗi mà giao diện phải tự parse lần nữa.
                    highlights = Parse(d.Payload),
                }).ToList(),
            });
        });
    }

    private static JsonElement Parse(string payload)
    {
        try
        {
            return JsonDocument.Parse(payload).RootElement.Clone();
        }
        catch
        {
            // Payload hỏng thì trả mảng rỗng chứ không để cả endpoint chết: phần còn lại của
            // dòng — số loạt, độ phủ, thời lượng — vẫn dùng được.
            return JsonDocument.Parse("[]").RootElement.Clone();
        }
    }
}
