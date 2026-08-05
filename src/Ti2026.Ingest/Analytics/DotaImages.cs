namespace Ti2026.Ingest.Analytics;

/// <summary>
/// Dựng URL ảnh hero và item trên CDN của Steam.
///
/// TÍNH LÚC ĐỌC, KHÔNG LƯU. URL là hàm thuần của tên hero/khoá item, nên lưu nó xuống DB chỉ
/// tạo ra một bản sao có thể lỗi thời: đổi host phải nạp lại cả bảng mới có tác dụng. Bài học
/// thật — bản đầu lưu URL với host cdn.cloudflare.steamstatic.com, và khi phát hiện host đó
/// không tới được từ mạng người dùng thì không thể sửa mà không nạp lại.
///
/// VÌ SAO LÀ HOST NÀY: cả hai host đều trả 200 khi thử từ VPS, nhưng ảnh hero trên
/// cdn.cloudflare.steamstatic.com KHÔNG hiện trên trình duyệt người dùng, trong khi logo đội —
/// vốn nằm trên steamcdn-a.akamaihd.net vì OpenDota trả về thế — thì hiện bình thường. Nguyên
/// nhân chính xác chưa xác định được (có thể do ISP), nên chọn cách chắc chắn: dùng đúng host
/// đã CHỨNG MINH chạy được ở phía người dùng.
/// </summary>
public static class DotaImages
{
    public const string Host = "https://steamcdn-a.akamaihd.net/apps/dota2/images";

    private const string NpcPrefix = "npc_dota_hero_";

    /// <summary>
    /// "npc_dota_hero_lone_druid" -> .../dota_react/heroes/lone_druid.png
    /// Trả null khi không có tên, để tầng trên hiện chữ cái thay thế chứ không hiện ảnh vỡ.
    /// </summary>
    public static string? Hero(string? npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName)) return null;

        var slug = npcName.StartsWith(NpcPrefix, StringComparison.Ordinal)
            ? npcName[NpcPrefix.Length..]
            : npcName;

        return $"{Host}/dota_react/heroes/{slug}.png";
    }

    public static string? Item(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : $"{Host}/dota_react/items/{key}.png";
}
