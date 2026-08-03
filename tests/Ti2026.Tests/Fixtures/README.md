# Fixture cho test ingest

## ⚠️ Trạng thái: TỰ DỰNG, CHƯA XÁC MINH VỚI API THẬT

Các file `opendota-*.json` trong thư mục này được **viết tay** theo shape đã biết của OpenDota
API, **không** phải chụp từ response thật. Nguyên nhân: môi trường phát triển bị chặn truy cập
`api.opendota.com` và `docs.opendota.com` ở tầng mạng (`example.com` truy cập được, nên là chặn
theo host chứ không phải mất mạng).

Nghĩa là: test đọc fixture này chứng minh **parser của ta đúng với shape ta tưởng**, chứ chưa
chứng minh shape đó khớp thực tế. Đừng tin số liệu M2 trước khi thay bằng dữ liệu thật.

## Cách thay bằng dữ liệu thật

Chạy trên máy có mạng hoặc trên VPS, trong thư mục gốc repo:

```bash
mkdir -p tests/Ti2026.Tests/Fixtures

# 1. Danh sách đội pro (để phân giải OpenDotaTeamId qua tên/tag)
curl -s "https://api.opendota.com/api/teams" \
  | head -c 400000 > tests/Ti2026.Tests/Fixtures/opendota-teams.json

# 2. Trận của một đội. 8291895 là Team Spirit; đổi thành id thật lấy được ở bước 1.
curl -s "https://api.opendota.com/api/teams/8291895/matches" \
  | head -c 200000 > tests/Ti2026.Tests/Fixtures/opendota-team-matches.json

# 3. Danh sách hero (cho ảnh tier list)
curl -s "https://api.opendota.com/api/heroes" \
  > tests/Ti2026.Tests/Fixtures/opendota-heroes.json

dotnet test
```

Nếu `dotnet test` đỏ sau khi thay, đó chính là mục đích của bước này: shape thật khác shape
đã giả định, và chỗ đỏ chỉ ra đúng field bị lệch.

## Điều cần kiểm bằng mắt sau khi chụp

Mở `opendota-team-matches.json` và xác nhận **không** có các field sau — toàn bộ thiết kế
"chưa biết ≠ bằng không" dựa trên việc chúng vắng mặt:

- `assists`
- `first_blood_time` / bất cứ field nào về first blood
- `series_id` / `series_type`
- timeline hay log kill

Nếu chúng **có mặt**, thì tin tốt: 5 chỉ số đang phải để `null` có thể tính thật ngay ở M2,
và cần cập nhật `OpenDotaTeamMatch` + `OpenDotaIngester` để đọc chúng.
