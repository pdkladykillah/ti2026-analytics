namespace Ti2026.Ingest.Http;

/// <summary>
/// Đếm số lời gọi thực sự bay ra OpenDota, theo từng ngày UTC.
///
/// VÌ SAO CẦN. OpenDota tính tiền theo lời gọi và KHÔNG có endpoint nào cho biết đã tiêu bao
/// nhiêu — đã kiểm cả 55 endpoint, chỉ có header `x-rate-limit-remaining-minute`. Nên nếu tự
/// mình không đếm thì mọi câu hỏi về chi phí đều chỉ trả lời được bằng phỏng đoán, và phỏng
/// đoán đã sai một lần rồi: báo 0,59 đô trong khi số thật là 2,97 đô, vì mỗi lần đổi lược đồ
/// lại quét lại gần mười nghìn ván mà không ai cộng vào.
///
/// GIỮ TRONG BỘ NHỚ, KHÔNG GHI DB. Ghi một dòng mỗi lời gọi là biến một chuyện đếm thành một
/// chuyện ghi đĩa, và SQLite ở đây chỉ có một người ghi — nó sẽ tranh chấp với chính vòng nạp
/// đang chạy. Đổi lại, số đếm mất khi container khởi động lại, và điều đó phải nói ra ở chỗ
/// hiển thị chứ không giấu đi.
///
/// Cửa sổ 30 ngày là đủ: câu hỏi thực tế là "một tháng tốn bao nhiêu", và giữ vô hạn thì
/// từ điển này lớn dần mãi trong một tiến trình chạy nhiều tháng.
/// </summary>
public sealed class ApiCallMeter
{
    public const int KeepDays = 30;

    private readonly Lock _lock = new();
    private readonly Dictionary<DateOnly, int> _byDay = [];

    /// <summary>UTC, lúc bộ đếm bắt đầu — để biết con số đang phủ bao nhiêu thời gian.</summary>
    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public void Record()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        lock (_lock)
        {
            _byDay[today] = _byDay.GetValueOrDefault(today) + 1;

            if (_byDay.Count > KeepDays)
                foreach (var old in _byDay.Keys.Where(d => d < today.AddDays(-KeepDays)).ToList())
                    _byDay.Remove(old);
        }
    }

    public IReadOnlyDictionary<DateOnly, int> Snapshot()
    {
        lock (_lock) return new Dictionary<DateOnly, int>(_byDay);
    }

    public int Total()
    {
        lock (_lock) return _byDay.Values.Sum();
    }

    /// <summary>Xoá sạch — dùng khi muốn đo đúng một vòng ingest.</summary>
    public void Reset()
    {
        lock (_lock) _byDay.Clear();
    }
}
