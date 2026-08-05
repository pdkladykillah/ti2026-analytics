using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ti2026.Data;
using Ti2026.Data.Entities;

namespace Ti2026.Ingest.Media;

/// <summary>
/// Tải ảnh về máy mình một lần rồi phục vụ từ đó, thay vì để trình duyệt hotlink.
///
/// VÌ SAO CẦN, và vì sao chỉ dùng cho avatar: ảnh hero và logo đội nằm trên
/// steamcdn-a.akamaihd.net, host đã chứng minh hiện được ở phía người dùng. Nhưng avatar Steam
/// CHỈ có trên avatars.steamstatic.com — đường akamai chỉ 301 trả về đúng host đó — mà cả họ
/// *.steamstatic.com lại không tới được từ mạng người dùng. Không có host thay thế nào, nên
/// cách duy nhất còn lại là tự phục vụ.
///
/// Tự phục vụ cũng dứt điểm luôn cả lớp vấn đề này: trang tải được từ máy mình thì ảnh trên máy
/// mình cũng tải được, không phụ thuộc vào việc mạng của ai tới được CDN nào.
///
/// Bảng MediaAsset có trong schema từ InitialCreate nhưng chưa bao giờ có ai cài phần tải —
/// ba cột LogoMediaAssetId / PhotoMediaAssetId / ImageMediaAssetId nằm không suốt từ đó.
/// </summary>
public class MediaCache(Ti2026DbContext db, HttpClient http, ILogger<MediaCache> logger)
{
    /// <summary>Ảnh nặng hơn mức này là dấu hiệu tải sai thứ — avatar Steam chỉ vài chục KB.</summary>
    public const int MaxBytes = 2 * 1024 * 1024;


    /// <summary>
    /// Trả về MediaAsset cho <paramref name="sourceUrl"/>, tải về nếu chưa có.
    /// Trả null khi không tải được — người gọi phải xử lý, không được coi là đã có ảnh.
    /// </summary>
    public async Task<MediaAsset?> EnsureAsync(string sourceUrl, string mediaDirectory, CancellationToken ct)
    {
        var existing = await db.MediaAssets.FirstOrDefaultAsync(m => m.SourceUrl == sourceUrl, ct);
        if (existing is not null && File.Exists(Path.Combine(mediaDirectory, existing.LocalPath)))
            return existing;

        try
        {
            using var res = await http.GetAsync(sourceUrl, ct);
            res.EnsureSuccessStatusCode();

            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0 || bytes.Length > MaxBytes)
            {
                logger.LogWarning("Bỏ qua {Url}: kích thước {Size} byte không hợp lý",
                    sourceUrl, bytes.Length);
                return null;
            }

            // Tên tệp theo hash NỘI DUNG, không theo URL: hai người dùng cùng một avatar thì chỉ
            // lưu một tệp, và URL phục vụ tự đổi khi ảnh đổi nên không cần lo cache của trình duyệt.
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes))[..32];
            var contentType = res.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var ext = contentType.Contains("png") ? ".png" : ".jpg";
            var localPath = hash + ext;

            Directory.CreateDirectory(mediaDirectory);
            await File.WriteAllBytesAsync(Path.Combine(mediaDirectory, localPath), bytes, ct);

            if (existing is null)
            {
                // Hash nội dung có unique index: ảnh giống hệt từ URL khác thì dùng lại hàng cũ
                // thay vì làm SaveChanges vỡ vì trùng khoá.
                var sameContent = await db.MediaAssets
                    .FirstOrDefaultAsync(m => m.ContentHash == hash, ct);

                if (sameContent is not null) return sameContent;

                existing = new MediaAsset
                {
                    SourceUrl = sourceUrl,
                    LocalPath = localPath,
                    ContentHash = hash,
                    ContentType = contentType,
                    ETag = res.Headers.ETag?.Tag,
                    FetchedAt = DateTime.UtcNow,
                };
                db.MediaAssets.Add(existing);
            }
            else
            {
                existing.LocalPath = localPath;
                existing.ContentHash = hash;
                existing.ContentType = contentType;
                existing.ETag = res.Headers.ETag?.Tag;
                existing.FetchedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            return existing;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                        or TimeoutException or IOException)
        {
            logger.LogInformation(ex, "Chưa tải được ảnh {Url}, sẽ thử lại vòng sau", sourceUrl);
            return null;
        }
    }
}
