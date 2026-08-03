# Triển khai

> Thông tin VPS thật (IP, đường dẫn, tên key) nằm trong `VPS-DEPLOY-GUIDE.md` của repo
> **phuongkhanh-travel** (private). Repo này public nên cố ý không nhắc tới chúng.

App chạy chung một VPS với project `phuongkhanh-travel`, sau Caddy reverse proxy, route
theo đường dẫn `/ti2026/*`.

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
    ├── handle /ti2026/*  ──►  ti2026-app-1:8080      ← project này
    └── handle            ──►  web:8080               ← phuongkhanh-travel
```

- Compose project tên `ti2026` (bắt buộc khác `phuongkhanh` để không trộn volume/container)
- Container **không publish port** — chỉ `expose`, Caddy gọi qua network dùng chung `web`
- Volume `ti2026_app_data` giữ SQLite; xoá volume là mất toàn bộ lịch sử snapshot

### Vì sao dùng `handle` chứ không `handle_path`

`handle` **không cắt** prefix `/ti2026`, và app tự bóc bằng `UsePathBase`. Nếu để Caddy cắt,
mọi URL app tự sinh sẽ trỏ về gốc `/` và đâm vào website du lịch.

### Hai file untracked trên VPS, cố ý không commit

| File | Vì sao |
|---|---|
| `/opt/phuongkhanh/docker-compose.override.yml` | Giữ caddy trong network `web`. Untracked nên `git pull --ff-only` của CI bên kia không bị ảnh hưởng. Chỉ dùng `docker network connect` thì lần `compose up -d` sau sẽ tạo lại caddy và mất kết nối |
| `/opt/ti2026/.env` | Chứa `TI2026_INGEST_TOKEN` |

⚠️ `/opt/phuongkhanh/Caddyfile` **đã bị sửa trực tiếp** để thêm route `/ti2026/*`, nên cây git
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
curl -s <host>/ti2026/api/health
```

`recentRuns` cho biết vòng ingest gần nhất thành công hay lỗi, kèm nguyên văn lỗi.

Chạy ingest tay:

```bash
TOKEN=$(grep TI2026_INGEST_TOKEN /opt/ti2026/.env | cut -d= -f2)
curl -X POST -H "X-Ingest-Token: $TOKEN" <host>/ti2026/api/ingest/run
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
until [ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost/ti2026/api/health)" = "200" ]; do
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
