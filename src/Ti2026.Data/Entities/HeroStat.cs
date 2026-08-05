namespace Ti2026.Data.Entities;

/// <summary>
/// Số liệu tổng hợp cho một hero, lấy từ heroStats của OpenDota.
///
/// Đây là nguồn PUB duy nhất của cả dự án. Tier list biên tập cũ lấy winrate pub từ Dotabuff
/// nhập tay, nên nó đóng băng ở thời điểm nhập; bảng này tự cập nhật mỗi vòng ingest.
///
/// GIỚI HẠN VỀ PATCH, phải nói rõ: heroStats là cửa sổ trượt gần đây của OpenDota, KHÔNG gắn
/// với một bản game cụ thể. Phần pro do ta tự đo thì gắn chặt với patch (lọc theo PatchVersion),
/// còn phần pub ở đây chỉ xấp xỉ "gần đây". Không được trình bày nó như "đúng 7.41e".
/// </summary>
public class HeroStat
{
    public int HeroId { get; set; }

    // ---- Chuyên nghiệp, theo cách đo của OpenDota trên TOÀN BỘ giải đấu ----
    // Khác với DraftEvents của ta: bảng kia chỉ có 16 đội TI, mẫu hẹp nhưng đúng đối tượng.
    public int ProPick { get; set; }
    public int ProWin { get; set; }
    public int ProBan { get; set; }

    // ---- Pub, toàn bộ bậc rank ----
    public long PubPick { get; set; }
    public long PubWin { get; set; }

    /// <summary>
    /// Bậc 7 + 8 (Divine + Immortal). Tách riêng vì pub toàn bậc trộn cả người mới chơi, mà
    /// hero mạnh ở bậc thấp thường là hero tự chơi được một mình — không nói lên gì về meta.
    /// </summary>
    public long HighPick { get; set; }
    public long HighWin { get; set; }

    /// <summary>UTC.</summary>
    public DateTime FetchedAt { get; set; }

    public double? PubWinrate => PubPick > 0 ? PubWin * 100.0 / PubPick : null;
    public double? HighWinrate => HighPick > 0 ? HighWin * 100.0 / HighPick : null;
    public double? ProWinrate => ProPick > 0 ? ProWin * 100.0 / ProPick : null;
}
