namespace Ti2026.Ingest.Analytics;

/// <summary>
/// Chỉ số bản game của OpenDota.
///
/// OpenDota đánh số bản game bằng một SỐ NGUYÊN tăng dần (constants/patch), không phải chuỗi
/// "7.41". Match.PatchVersion lưu đúng con số đó dưới dạng chuỗi.
///
/// GIỚI HẠN QUAN TRỌNG: danh sách này chỉ có bản CHÍNH. 7.41a, 7.41b … 7.41e đều mang cùng
/// một chỉ số 60. Nghĩa là mô hình phân biệt được 7.40 với 7.41, nhưng KHÔNG phân biệt được
/// các bản vá chữ cái bên trong 7.41. Với mục đích ở đây thì chấp nhận được — bản chữ cái là
/// chỉnh số liệu, bản chính mới là thay đổi meta — nhưng đừng đọc số liệu như thể nó tách
/// được 7.41e khỏi 7.41a.
/// </summary>
public static class PatchIndex
{
    /// <summary>Chỉ số 45 ứng với 7.26, và từ đó trở đi mỗi bản chính là một bậc.</summary>
    private const int AnchorId = 45;
    private const int AnchorMinor = 26;

    public static int? Parse(string? raw) =>
        int.TryParse(raw, out var v) ? v : null;

    /// <summary>
    /// Nhãn hiển thị, ví dụ 60 → "7.41". CHỈ để hiển thị — mọi tính toán dùng chỉ số thô, nên
    /// nếu Valve đổi cách đánh số (7.50 → 8.00) thì nhãn sai chứ mô hình không sai.
    /// </summary>
    public static string Name(int id) =>
        id >= AnchorId ? $"7.{AnchorMinor + (id - AnchorId)}" : $"bản #{id}";

    public static string? Name(string? raw) =>
        Parse(raw) is int id ? Name(id) : null;
}
