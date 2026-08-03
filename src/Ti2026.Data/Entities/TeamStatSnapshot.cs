namespace Ti2026.Data.Entities;

/// <summary>
/// Xương sống của phần phân tích form. Một dòng cho mỗi (đội, ngày, cửa sổ).
/// Snapshot của các ngày khác nhau KHÔNG bao giờ ghi đè nhau — đó là kho lịch sử,
/// và lịch sử là thứ không lấy lại được. Trong cùng một ngày thì upsert, nên chạy
/// ingest nhiều lần trong ngày cũng không nhân đôi dữ liệu.
///
/// 10 chỉ số dưới đây mirror đúng field "order" trong h2h.json hiện tại, nên UI H2H
/// chạy được không cần sửa logic.
/// </summary>
public class TeamStatSnapshot
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }
    public DateOnly CapturedOn { get; set; }

    /// <summary>30 | 90 | 180 ngày.</summary>
    public int WindowDays { get; set; }

    public int Maps { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }

    /// <summary>Phần trăm, 0..100.</summary>
    public double Winrate { get; set; }

    public double AvgKills { get; set; }
    public double AvgDeaths { get; set; }
    public double AvgAssists { get; set; }
    public double KillDiff { get; set; }
    public double TotalKills { get; set; }
    public double FirstBloodRate { get; set; }
    public double F10Rate { get; set; }
    public double WinWhenFbRate { get; set; }
    public double WinWhenF10Rate { get; set; }

    /// <summary>Phút — khớp đơn vị của field "duration" trong teams.json.</summary>
    public double AvgDurationMinutes { get; set; }
}
