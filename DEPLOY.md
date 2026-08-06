# Triển khai

> Thông tin VPS thật (IP, đường dẫn, tên key) nằm trong `VPS-DEPLOY-GUIDE.md` của repo
> **phuongkhanh-travel** (private). Repo này public nên cố ý không nhắc tới chúng.

App chạy chung một VPS với project `phuongkhanh-travel`, sau Caddy reverse proxy, route
theo **tên miền riêng** `ti-2026-pdk.duckdns.org`, tách khỏi trang du lịch.

## Chạy ở máy

```bash
dotnet run --project src/Ti2026.Web     # http://localhost:5000
dotnet test                              # toàn bộ test, không gọi mạng
```

## Kiến trúc trên VPS

```
Internet :80
    │
    ▼
phuongkhanh-caddy-1                 (giữ 80/443, KHÔNG đụng vào)
    ├── ti-2026-pdk.duckdns.org  ──►  ti2026-app-1:8080   ← project này (HTTPS tự động)
    └── :80 catch-all            ──►  web:8080            ← phuongkhanh-travel
```

- Compose project tên `ti2026` (bắt buộc khác `phuongkhanh` để không trộn volume/container)
- Container **không publish port** — chỉ `expose`, Caddy gọi qua network dùng chung `web`
- Volume `ti2026_app_data` giữ SQLite; xoá volume là mất toàn bộ lịch sử snapshot

### Định tuyến theo hostname, không theo đường dẫn

App chạy ở **gốc** của tên miền nên KHÔNG cần `UsePathBase` — biến `Ti2026__PathBase` đã bỏ
khỏi compose. Caddy tự xin chứng chỉ Let's Encrypt và tự chuyển HTTP sang HTTPS.

Bản trước định tuyến theo `/ti2026/*` bằng `handle` (không cắt prefix) và app tự bóc bằng
`UsePathBase`. Cách đó vẫn đúng, chỉ là không còn cần khi đã có tên miền riêng.

Trang du lịch giữ nguyên khối `:80` bắt tất cả phần còn lại, nên nó không bị ảnh hưởng —
nhưng **vẫn phải kiểm lại sau mỗi lần sửa Caddyfile**, không được tin là đương nhiên.

### Hai file untracked trên VPS, cố ý không commit

| File | Vì sao |
|---|---|
| `/opt/phuongkhanh/docker-compose.override.yml` | Giữ caddy trong network `web`. Untracked nên `git pull --ff-only` của CI bên kia không bị ảnh hưởng. Chỉ dùng `docker network connect` thì lần `compose up -d` sau sẽ tạo lại caddy và mất kết nối |
| `/opt/ti2026/.env` | Chứa `TI2026_INGEST_TOKEN` và `TI2026_OPENDOTA_API_KEY`. `chmod 600`, đã có trong `.gitignore` — **không bao giờ commit** |

### API key của OpenDota

Không bắt buộc. Thiếu key thì app vẫn chạy ở bậc miễn phí; có key thì đổi hẳn về quy mô:

| | Không key | Có key |
|---|---|---|
| Trần ngày | 3000 request | không có |
| Nhịp cho phép | 60/phút | 3000/phút |
| Nhịp ta dùng | 0,8 req/s | 8 req/s (1/6 mức cho phép) |
| Trần mỗi vòng | 300 ván | 2000 ván |
| Nạp bù 1798 ván | hơn hai ngày | một vòng, vài phút |

Giá: **$0,0001 mỗi request** — toàn bộ nạp bù 1798 ván tốn khoảng **$0,18**. Phản hồi 404, 429
và 500 không bị tính tiền, nên luật dừng-sau-ba-lỗi của ta không tốn gì.

Đặt key:

```bash
echo "TI2026_OPENDOTA_API_KEY=<key>" >> /opt/ti2026/.env
chmod 600 /opt/ti2026/.env
docker compose up -d --build
```

Kiểm đã ăn chưa — `api/ingest/status` báo `apiKey: "đang dùng"` và `requestsPerSecond: 8`.
Endpoint đó **không bao giờ** trả về giá trị key: nó không cần xác thực.

Key gửi qua header `Authorization: Bearer`, không phải `?api_key=`. Query param sẽ nằm lại
trong log truy cập của nguồn, trong thông báo lỗi, và trong mọi chuỗi URL bị in ra khi gỡ lỗi.

⚠️ `/opt/phuongkhanh/Caddyfile` **đã bị sửa trực tiếp** để thêm khối tên miền, nên cây git
ở đó đang bẩn. Bản gốc lưu tại `Caddyfile.bak-<ngày>`. Muốn sạch hẳn thì commit thay đổi này
vào repo phuongkhanh-travel.

## Deploy bản mới

```bash
ssh -i <key> root@<vps> "cd /opt/ti2026 && git pull --ff-only && docker compose up -d --build"
```

Không cần đụng Caddy. Nếu có sửa Caddyfile:

```bash
docker exec phuongkhanh-caddy-1 caddy validate --config /etc/caddy/Caddyfile
docker exec phuongkhanh-caddy-1 caddy reload   --config /etc/caddy/Caddyfile   # không rớt kết nối
```

## Kiểm tra sức khoẻ

```bash
curl -s https://ti-2026-pdk.duckdns.org/api/health
```

`recentRuns` cho biết vòng ingest gần nhất thành công hay lỗi, kèm nguyên văn lỗi.

Chạy ingest tay:

```bash
TOKEN=$(grep TI2026_INGEST_TOKEN /opt/ti2026/.env | cut -d= -f2)
curl -X POST -H "X-Ingest-Token: $TOKEN" https://ti-2026-pdk.duckdns.org/api/ingest/run
```

## Ràng buộc của VPS cần nhớ

| | |
|---|---|
| RAM 1.9 GB, swap 2 GB | Swap được tạo vì build .NET trên máy không swap có thể OOM-kill website du lịch đang chạy |
| `mem_limit: 512m` | Trần cho container này, để nó không kéo sập project kia |
| Build cache | `docker builder prune -f` khi đĩa đầy. **Không** dùng `docker system prune -a` — xoá cả image đang chạy |

## ⚠️ Migration: KHÔNG sinh lại `InitialCreate` khi DB đã deploy

Đã mắc lỗi này một lần và làm app sập trên VPS. Ở máy dev, cách nhanh nhất khi đổi schema là
xoá thư mục `Migrations/` rồi `dotnet ef migrations add InitialCreate` lại. **Trên DB đã chạy
thì cách đó phá app**: migration mới mang ID (timestamp) khác, `__EFMigrationsHistory` không
có ID đó nên EF coi là chưa chạy và phát `CREATE TABLE` trên bảng đã tồn tại → `Migrate()` ném
lỗi ngay lúc startup và host không lên nổi.

Đổi schema sau khi đã deploy thì **thêm migration mới**:

```bash
dotnet ef migrations add TenMoTaThayDoi --project src/Ti2026.Data --startup-project src/Ti2026.Web
```

Nếu lỡ sinh lại `InitialCreate`: dữ liệu của project này **tái tạo được hoàn toàn** từ
`data/*.json` + OpenDota, nên cách khôi phục nhanh nhất là xoá volume rồi nạp lại:

```bash
cd /opt/ti2026 && docker compose down && docker volume rm ti2026_app_data && docker compose up -d
```

Cái mất là snapshot lịch sử đã tích luỹ — càng về sau càng đắt, nên đừng lặp lại.

## Bẫy khi viết vòng chờ app khởi động

`curl -s -o /dev/null <url>` **thành công kể cả khi HTTP 502**. Vòng chờ dùng lệnh đó sẽ thoát
sớm trong lúc container còn đang khởi động, và bước tiếp theo nhận 502 một cách khó hiểu. Phải
kiểm mã trạng thái:

```bash
until [ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost/api/health)" = "200" ]; do
  sleep 2
done
```

## Rollback

```bash
cd /opt/ti2026
git log --oneline -10
git checkout <sha>
docker compose up -d --build
```

Dữ liệu nằm trong volume nên rollback code không mất snapshot đã tích luỹ.
