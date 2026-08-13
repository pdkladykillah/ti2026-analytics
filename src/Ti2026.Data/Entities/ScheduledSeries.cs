namespace Ti2026.Data.Entities;

/// <summary>
/// Một NÚT trong bảng đấu của giải, lấy từ API chính chủ của Valve
/// (www.dota2.com/webapi/IDOTA2League/GetLeagueData).
///
/// Vì sao lưu vào DB chứ không gọi thẳng Valve mỗi lần mở trang: trang phải chịu được lúc
/// Valve chậm hoặc lỗi, và đây cũng là chỗ ánh xạ team_id của Valve về Team của ta — làm một
/// lần lúc nạp thay vì làm lại ở mỗi request.
///
/// Vì sao là "nút" chứ không phải "trận": bảng đấu loại trực tiếp có những nút chưa biết đội
/// nào vào (team_id = 0) nhưng ĐÃ có giờ và đã biết nó nhận đội thắng từ nút nào. Đó là thông
/// tin thật và phải giữ được, mà một bảng "trận đấu" phẳng thì không biểu diễn nổi.
/// </summary>
public class ScheduledSeries
{
    public int Id { get; set; }

    public long LeagueId { get; set; }

    /// <summary>node_id của Valve. Duy nhất trong phạm vi một giải.</summary>
    public int NodeId { get; set; }

    /// <summary>Swiss | Elimination Round | Playoff — tên nhóm chứa nút này.</summary>
    public string? GroupName { get; set; }

    /// <summary>Tên nút nếu Valve đặt, ví dụ "Grand Final".</summary>
    public string? Name { get; set; }

    /// <summary>Giờ dự kiến, UTC. null = Valve chưa xếp lịch cho nút này.</summary>
    public DateTime? ScheduledAt { get; set; }

    /// <summary>Giờ bắt đầu THẬT, UTC. null = chưa đánh.</summary>
    public DateTime? ActualAt { get; set; }

    // ---- team_id của Valve, giữ nguyên kể cả khi chưa khớp được về đội của ta ----
    //
    // 0 nghĩa là CHƯA BIẾT ĐỘI NÀO VÀO, không phải "đội có id 0". Đây là trạng thái bình
    // thường của mọi nút loại trực tiếp trước khi vòng trước kết thúc.
    public int? ValveTeamId1 { get; set; }
    public int? ValveTeamId2 { get; set; }

    public int? TeamId1 { get; set; }
    public Team? Team1 { get; set; }
    public int? TeamId2 { get; set; }
    public Team? Team2 { get; set; }

    public int Wins1 { get; set; }
    public int Wins2 { get; set; }

    public bool HasStarted { get; set; }
    public bool IsCompleted { get; set; }

    /// <summary>series_id của Valve, để đối chiếu với ván thật khi cần.</summary>
    public long? SeriesId { get; set; }

    /// <summary>
    /// Nút mà đội THẮNG/THUA đi tiếp tới. Đây là thứ dựng lại được hình dạng nhánh thắng và
    /// nhánh thua, và cũng là thứ cho phép nói "thắng trận này thì gặp ai".
    /// </summary>
    public int? WinningNodeId { get; set; }
    public int? LosingNodeId { get; set; }

    /// <summary>Hai nút cấp dưới đổ đội vào nút này.</summary>
    public int? IncomingNodeId1 { get; set; }
    public int? IncomingNodeId2 { get; set; }

    /// <summary>UTC, lần cuối đồng bộ từ Valve.</summary>
    public DateTime SyncedAt { get; set; }
}
