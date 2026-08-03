namespace Ti2026.Data.Entities;

/// <summary>
/// Một dự đoán đã đưa ra, và kết quả thật khi trận đấu xong.
///
/// VÌ SAO CẦN LƯU: không có bảng này thì mô hình dự đoán là thứ KHÔNG THỂ BÁC BỎ — nó phun ra
/// con số và không ai biết nó đúng bao nhiêu phần. Lưu lại mọi dự đoán rồi đối chiếu kết quả
/// cho phép vẽ đường hiệu chuẩn: trong những lần mô hình nói "70%", thực tế thắng bao nhiêu
/// phần trăm? Lệch nhiều nghĩa là mô hình đang nói dối, và ta biết ngay thay vì tin mãi.
/// </summary>
public class Prediction
{
    public int Id { get; set; }

    public int TeamAId { get; set; }
    public Team? TeamA { get; set; }
    public int TeamBId { get; set; }
    public Team? TeamB { get; set; }

    /// <summary>Xác suất đội A thắng, 0..100, tại thời điểm dự đoán.</summary>
    public double ProbabilityA { get; set; }

    /// <summary>Elo của hai bên lúc dự đoán — để truy lại vì sao mô hình nói vậy.</summary>
    public double EloA { get; set; }
    public double EloB { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Trận thật đã khớp với dự đoán này; null khi chưa có kết quả.</summary>
    public long? ResolvedMatchId { get; set; }

    /// <summary>true = đội A thắng. null = chưa biết kết quả.</summary>
    public bool? TeamAWon { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
