# Thiết kế: Chuyển ti2026-analytics sang .NET + pipeline dữ liệu thật

**Ngày:** 2026-08-03
**Phạm vi:** Giai đoạn 1 + 2 (nền tảng & pipeline + phân tích form theo thời gian)
**Trạng thái:** Đã được chủ project thông qua từng phần, chờ review bản viết

---

## 1. Bối cảnh và quyết định cốt lõi

### Hiện trạng

`ti2026-analytics` là một trang tĩnh: `index.html` (558 dòng, HTML + CSS + JS thuần, không framework, không build step) đọc 6 file JSON tĩnh trong `data/` bằng `fetch`, cộng một lời gọi OpenDota API trực tiếp từ browser tại `index.html:492`. Dữ liệu được cập nhật bằng cách sửa file JSON và commit tay — 4 trong 5 commit gần nhất đúng là như vậy (`Update tiers.json`, `Update rosters.json`, ...).

### Quyết định: có, chuyển sang .NET

Với project **như nó đang là**, chuyển sang .NET là lỗ vốn — không có backend, không DB, không auth, và deploy tĩnh thì gần như miễn phí. Quyết định đổi chiều vì ba yêu cầu mới:

| Yêu cầu | Vì sao trang tĩnh không đủ |
|---|---|
| Cập nhật dữ liệu thật | Cần job định kỳ, secret/API key, retry — browser không làm được |
| Phân tích dữ liệu | Cần **lịch sử**, mà 6 file JSON thì bị ghi đè mỗi lần cập nhật |
| Deploy lên VPS | VPS đã là một máy .NET + Docker + Caddy đang chạy |

Điểm quyết định thực sự: hạ tầng cần cho ti2026 **giống hệt** hạ tầng chủ project đã dựng và đang vận hành cho `PhuongKhanhTravel`:

- ASP.NET Core `net10.0` MVC + EF Core/SQLite + Migrations
- Docker Compose: một container app + Caddy reverse proxy giữ 80/443, tự Let's Encrypt
- Volume `app_data` để SQLite sống bền qua các lần deploy

Chuyển sang .NET **không** phải học stack mới — mà là về đúng stack đã thuần thục.

### Điều được giữ lại

Toàn bộ UI trong `index.html` được giữ: bảng màu "Sapphire nightfall whisper" (quy tắc 60/30/10), dark mode qua `[data-t=dark]`, layout, logic render. Xem §8.

---

## 2. Phạm vi

### Trong phạm vi (Giai đoạn 1 + 2)

1. Project ASP.NET Core `net10.0`, EF Core + SQLite + migrations
2. Ingest pipeline: OpenDota (số liệu) + dltv.org (ảnh, logo, lịch giải)
3. `BackgroundService` chạy định kỳ, có cổng kiểm tra tính hợp lý
4. Bảng snapshot lịch sử — nền tảng cho mọi phân tích về sau
5. Minimal API giữ đúng shape JSON hiện tại + endpoint `api/trend` mới
6. `index.html` chuyển vào `wwwroot`, đổi tối thiểu
7. Tab/biểu đồ form theo thời gian (Giai đoạn 2)
8. Deploy container riêng trên VPS, Caddy route theo path
9. Cache ảnh về VPS, bỏ hotlink dltv

### Ngoài phạm vi (giai đoạn sau, mỗi giai đoạn một spec riêng)

- **Giai đoạn 3** — phân tích draft/hero + phân tích cá nhân player: cần nạp chi tiết từng match (pick/ban, GPM/XPM), nặng hơn nhiều về dung lượng và thời gian ingest
- **Giai đoạn 4** — dự đoán kết quả trận: ăn đầu ra của GĐ2 + GĐ3 làm feature, và cần lịch sử đủ dài để backtest

### Lý do thứ tự này quan trọng

Dữ liệu lịch sử **không lấy lại được**. Mỗi ngày pipeline chưa chạy là một ngày dữ liệu mất vĩnh viễn. Dựng GĐ1 trước rồi để nó chạy nền trong lúc làm phần phân tích, nghĩa là đến GĐ4 đã có sẵn nhiều tuần dữ liệu để backtest. Làm ngược lại thì phải ngồi chờ dữ liệu. TI2026 là giải có deadline.

### Các quyết định đã chốt

| Câu hỏi | Chốt |
|---|---|
| Nguồn dữ liệu | **Kết hợp**: OpenDota làm số liệu chính, dltv.org bù phần OpenDota không có |
| Phạm vi phân tích | Cả 4 hướng, nhưng **chia giai đoạn**; spec này chỉ GĐ1 + GĐ2 |
| Chỗ đứng trên VPS | App riêng, container riêng, Caddy **route theo path** (chạy được ngay với IP, chưa cần domain) |
| Dữ liệu biên tập | **Giữ file JSON trong git**, app seed vào DB lúc startup |
| Kiến trúc | **Hướng A** — một app, ingest chạy trong process bằng `BackgroundService` |

---

## 3. Kiến trúc

```
┌─ container ti2026-web ─────────────────────────────┐
│                                                    │
│  IngestBackgroundService   ──ghi──┐                │
│   (PeriodicTimer, 6h/lần)         │                │
│                                   ▼                │
│                            SQLite (WAL)            │
│                            /app/App_Data/ti2026.db │
│                                   │                │
│  Minimal API  ──────────────đọc───┘                │
│   api/teams, api/h2h, api/trend…                   │
│                                                    │
│  wwwroot/index.html  ← UI hiện tại, gần như y nguyên│
└────────────────────────────────────────────────────┘
```

Một image, một volume `App_Data`, migrations tự chạy lúc startup — giống `PhuongKhanhTravel` về mọi mặt vận hành.

### Các hướng đã cân nhắc và loại

- **Hướng B — web + ingester tách thành 2 container** dùng chung volume SQLite. Được: scraper chết không ảnh hưởng web, chạy lại một nguồn cụ thể tiện khi debug. Loại vì: hai process cùng mở một file SQLite (chạy được nhờ WAL nhưng thêm chỗ để sai), và ở quy mô 16 đội thì đây là độ phức tạp chưa cần trả tiền. **Đây là hướng tiến lên nếu ingest sau này nặng thật.**
- **Hướng C — console app + cron sinh ra `data/*.json` tĩnh, Caddy serve tĩnh.** Được: đơn giản nhất, frontend không đổi một dòng. Loại vì: phần form cần **truy vấn theo khoảng thời gian** (30 vs 90 ngày, lọc theo patch), GĐ3–4 cần hơn nữa. Sinh tĩnh nghĩa là phải đoán trước mọi câu hỏi và render sẵn hết — sớm muộn cũng phải bỏ.

---

## 4. Cấu trúc project

Chia nhỏ để mỗi phần có một trách nhiệm rõ và test được độc lập:

```
Ti2026.slnx
src/Ti2026.Web/          Minimal API + wwwroot + Program.cs + Dockerfile
src/Ti2026.Data/         DbContext, entities, migrations, seeder
src/Ti2026.Ingest/       OpenDota client, dltv scraper, normalizer,
                         snapshot writer, sanity gate
tests/Ti2026.Tests/      xUnit — chạy offline hoàn toàn
data/                    6 file JSON biên tập (giữ nguyên, nguồn seed)
docs/superpowers/specs/  spec này
docker-compose.yml
```

Phụ thuộc một chiều: `Web → Ingest → Data`. `Ingest` không biết gì về HTTP request của web; `Data` không biết gì về OpenDota hay dltv.

---

## 5. Mô hình dữ liệu

Bám sát dữ liệu đang có, không phát minh thêm. `h2h.json` có field `order` liệt kê đúng 10 chỉ số đang dùng — `winrate, kills, deaths, killDiff, totalKills, firstBlood, f10, winWhenFb, winWhenF10, duration` — nên bảng snapshot mirror đúng 10 cột đó, và UI H2H hiện tại chạy được không cần sửa logic.

### Các bảng

| Bảng | Vai trò | Ghi chú thiết kế |
|---|---|---|
| `Team` | 16 đội | `Slug` unique, dùng lại slug sẵn có (`team-falcons`) |
| `TeamAlias` | Ánh xạ tên giữa các nguồn | Giải quyết đúng cái `_note` trong `teams.json`: OpenDota gọi `PARIVISION`, dltv gọi `TEAM VISION`; `HULIGANI` = `L1GA TEAM`. Không có bảng này thì ingest tạo đội trùng |
| `Player` | Danh tính player | `OpenDotaAccountId` unique (nullable); `players.json` đã có sẵn `id` |
| `RosterEntry` | Player thuộc đội nào, từ ngày nào đến ngày nào | `ValidFrom` / `ValidTo` (nullable = đang hiệu lực) |
| `Hero` | Hero + ảnh | `Id` theo OpenDota hero id. Nạp từ endpoint heroes của OpenDota; cần cho GĐ1 vì tier list hiển thị ảnh hero |
| `Match` | Từng game | Có `SeriesId` để gom thành series Bo3/Bo5; FK đội **nullable** vì OpenDota đôi khi thiếu ánh xạ đội |
| `TeamStatSnapshot` | Xương sống của Giai đoạn 2 | Một dòng / đội / ngày / cửa sổ |
| `TierEntry` | Tier list biên tập | Seed từ `tiers.json`, khóa theo `Patch` |
| `MediaAsset` | Ảnh đã tải về VPS | `SourceUrl`, `LocalPath`, `ETag` → lần sau chỉ tải nếu ETag đổi. File nằm trong volume tại `/app/App_Data/media/`, phục vụ qua route `media/{hash}` (đường dẫn tương đối, tôn trọng path base) |
| `IngestRun` | Lịch sử mỗi vòng chạy | Source, thời gian, số bản ghi, nguyên văn lỗi |
| `SeedState` | Hash file JSON biên tập | Chỉ seed lại khi file thật sự đổi |

### `TeamStatSnapshot` — chi tiết

Cột: `Id`, `TeamId`, `CapturedOn` (date), `WindowDays` (30/90/180), `Matches`, `Wins`, `Losses`, `Winrate`, `AvgKills`, `AvgDeaths`, `KillDiff`, `TotalKills`, `FirstBloodRate`, `F10Rate`, `WinWhenFbRate`, `WinWhenF10Rate`, `AvgDurationSeconds`.

Unique index `(TeamId, CapturedOn, WindowDays)` với ghi theo kiểu upsert → **chạy ingest nhiều lần trong ngày không nhân đôi dữ liệu**. Đây là thứ làm cho pipeline an toàn khi chạy lại.

### Hai quyết định thiết kế đáng ghi lại

**`RosterEntry` có lịch sử vào/ra đội.** Roster đổi giữa giải. Nếu chỉ lưu "player X thuộc đội Y" ở dạng phẳng, thì hôm một player chuyển đội, toàn bộ lịch sử bị tính lại như thể anh ta luôn ở đội mới — dữ liệu form quá khứ lặng lẽ sai đi mà **không có lỗi nào báo**. Một bảng có `ValidFrom`/`ValidTo` là cái giá rẻ để tránh chuyện đó, và GĐ4 cần chính xác thông tin "lúc trận đó ai đang trong đội".

**H2H không có bảng riêng.** Tính trực tiếp từ `Match` + `SeriesId` lúc query. Lý do: bảng dẫn xuất là thứ sẽ lệch khỏi nguồn khi ingest chạy lại, và ở quy mô 16 đội thì query trực tiếp nhanh hơn ngưỡng cảm nhận rất nhiều.

### Dung lượng

16 đội × 3 cửa sổ × 365 ngày ≈ **17.500 dòng snapshot/năm**. SQLite xử lý mức này không cần nghĩ. **Không thiết kế cơ chế xóa dữ liệu cũ** — dữ liệu lịch sử là thứ có giá trị nhất ở đây và nó không bao giờ lớn đến mức thành vấn đề.

---

## 6. Ingest pipeline

### Một vòng chạy

1. `PeriodicTimer` đánh thức → tạo `IngestRun` trạng thái `Running`
2. **OpenDotaIngester** — lấy match/player theo `account_id` có sẵn trong `players.json` → ghi `Match`, `Player`
3. **DltvIngester** — lấy phần OpenDota không có (ảnh, logo, lịch giải) → **tải ảnh về đĩa**, không hotlink
4. **SnapshotWriter** — tính lại chỉ số theo cửa sổ 30/90/180 ngày → upsert `TeamStatSnapshot` cho mỗi đội
5. `IngestRun` chuyển `Succeeded` kèm số bản ghi, hoặc `Failed` kèm nguyên văn lỗi

Bước 4 là điểm mấu chốt của GĐ2: snapshot là bản ghi **không bao giờ bị ghi đè giữa các ngày**, nên biểu đồ form theo tuần chỉ là một câu `GROUP BY`.

Dữ liệu biên tập (`tiers.json` và các ghi chú) được seed từ file vào DB lúc startup, chỉ khi hash file đổi (`SeedState`).

### Nhịp chạy

Mặc định **6 giờ/lần**, đổi qua env var. Vòng đầu chạy sau khi app ready ~30 giây, để restart liên tục không thành spam OpenDota.

### Về việc scrape dltv.org

Thiết kế theo hướng scrape lịch sự, và điều này là **bắt buộc trong kế hoạch triển khai, không phải tuỳ chọn**:

- **Kiểm tra `robots.txt` của dltv.org trước khi viết scraper.** Nếu nội dung cần lấy bị cấm, rút phần scrape về mức tối thiểu hoặc bỏ, và ghi lại quyết định đó
- Tối đa ~1 request/giây
- Cache theo `ETag` / `Last-Modified`, không tải lại thứ chưa đổi
- Tải ảnh về VPS thay vì hotlink (vừa bớt tốn bandwidth của họ, vừa hết cảnh ảnh chết)
- User-Agent tường minh, có thể liên hệ được

---

## 7. Hợp đồng API

```
GET  api/meta            shape cũ (updatedAt, window, source, teamsWithData)
GET  api/teams           shape cũ
GET  api/rosters         shape cũ
GET  api/players         shape cũ
GET  api/h2h             shape cũ (pairs + order)
GET  api/tiers           shape cũ
GET  api/trend?team=<slug>&window=30&from=<date>&to=<date>    MỚI — GĐ2
GET  api/health          vòng ingest gần nhất: trạng thái, thời điểm, số bản ghi
POST api/ingest/run      chạy ingest tay
```

`api/trend` trả **mảng phẳng** `[{teamSlug, date, winrate, kills, deaths, killDiff, ...}]` để nhét thẳng vào chart, không cần biến đổi ở client.

Ngữ nghĩa tham số: `team` **tuỳ chọn** — không truyền thì trả cả 16 đội (dùng cho biểu đồ so sánh nhiều đường). `window` mặc định `30`, chỉ nhận `30|90|180`, giá trị khác trả `400`. `from`/`to` tuỳ chọn, mặc định 90 ngày gần nhất. Khoảng ngày không có snapshot thì **khuyết dòng** chứ không trả `0` — số 0 giả sẽ vẽ thành cú sụt phong độ không có thật.

`POST api/ingest/run` bảo vệ bằng header token đọc từ env var — không dựng login đầy đủ cho một endpoint. Nhưng **phải được bảo vệ**: để hở thì bất kỳ ai cũng ép VPS spam OpenDota tới mức bị chặn IP.

---

## 8. Thay đổi ở frontend

Các endpoint trả **đúng cấu trúc** mà `index.html:254-261` đang parse. Toàn bộ phần đổi là:

```js
fetch("data/teams.json")   →   fetch("api/teams")
```

Không viết lại logic render, không sờ vào CSS. Nếu API lỗi, vẫn có thể trỏ ngược về file JSON cũ để so sánh — một đường lùi rất rẻ.

**Đường dẫn phải là tương đối** (`api/teams`, không phải `/api/teams`) để không phụ thuộc vào path prefix — xem §9.

Phần mới của GĐ2 là một tab "Form" với biểu đồ theo thời gian, đọc từ `api/trend`. Tab này viết theo đúng phong cách hiện tại: dùng biến CSS sẵn có (`--bl`, `--aq`, `--rd`, `--mu`), tôn trọng dark mode qua `[data-t=dark]`, và giữ quy tắc 60/30/10 đã ghi trong comment đầu file.

---

## 9. Deploy trên VPS

Caddy nằm trong compose của `PhuongKhanhTravel`, còn ti2026 là repo riêng. Để Caddy gọi được sang container ở repo khác, cần **một docker network dùng chung**, tạo một lần:

```
docker network create edge
```

### `ti2026-analytics/docker-compose.yml`

```yaml
services:
  ti2026:
    build: ./src/Ti2026.Web
    container_name: ti2026-web
    restart: unless-stopped
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Ti2026__PathBase=/ti2026
      - Ti2026__IngestIntervalHours=6
      - Ti2026__IngestToken=${TI2026_INGEST_TOKEN}
    volumes:
      - ti2026_data:/app/App_Data
    expose:
      - "8080"
    networks:
      - edge

volumes:
  ti2026_data:

networks:
  edge:
    external: true
```

Service đặt tên `ti2026` với `container_name` tường minh, **không** đặt tên `web`: cả hai compose đều có service `web`, để chung một network là xung đột alias.

`TI2026_INGEST_TOKEN` đọc từ file `.env` cạnh `docker-compose.yml` trên VPS. File này **phải nằm trong `.gitignore`** — token lọt vào git là mất tác dụng bảo vệ. Repo chỉ commit `.env.example` với giá trị rỗng.

### Sửa bên `PhuongKhanhTravel` (tối thiểu)

`Caddyfile`:

```caddyfile
:80 {
    handle /ti2026/* {
        reverse_proxy ti2026-web:8080
    }
    reverse_proxy web:8080          # PhuongKhanh giữ nguyên phần còn lại
}
```

`docker-compose.yml` — chỉ thêm network cho service `caddy`:

```yaml
  caddy:
    networks:
      - default          # để vẫn gọi được web:8080 của PhuongKhanh
      - edge             # để gọi được ti2026-web:8080
```

### Cái bẫy path prefix

Dùng `handle` **chứ không phải** `handle_path`, tức prefix `/ti2026` **không bị cắt** — rồi app khai `app.UsePathBase("/ti2026")`. Nếu để Caddy cắt prefix, mọi URL app tự sinh sẽ trỏ về gốc `/` và đâm vào web du lịch.

### Ảnh hưởng đến web đang chạy

Sửa `Caddyfile` đòi **restart Caddy** → web du lịch gián đoạn vài giây. Không đụng gì tới app du lịch ngoài hai file trên. Chuyển sang subdomain sau này chỉ là sửa vài dòng Caddyfile.

---

## 10. Xử lý lỗi

### Failure mode nguy hiểm nhất

Không phải scraper chết — scraper chết thì biết ngay. Cái đáng sợ là **scraper "thành công" nhưng trả về rỗng**: dltv đổi layout, selector không match, parser trả `0 đội`, ingest ghi con số rỗng đó đè lên dữ liệu tốt. Mở trang thấy trắng trơn, không có lỗi nào trong log.

**Cổng kiểm tra tính hợp lý (sanity gate):** một vòng ingest chỉ được commit nếu đạt ngưỡng tối thiểu — `>= 16 đội`, `>= 60 player`. Không đạt thì đánh `Failed`, ghi nguyên văn nguyên nhân, **giữ nguyên dữ liệu cũ**. Dữ liệu hơi lỗi thời tốt hơn dữ liệu rỗng, luôn luôn.

Gate áp **cho từng ingester riêng, trên đúng loại dữ liệu ingester đó ghi** — không áp một ngưỡng chung cho cả vòng. Cụ thể: ingester nào ghi đội thì bị kiểm `MinTeams`, ingester nào ghi player thì bị kiểm `MinPlayers`. Một ingester trượt gate thì transaction của **riêng nó** bị rollback, các ingester khác trong cùng vòng vẫn commit bình thường.

### Các lớp bảo vệ khác

| Rủi ro | Cách xử lý |
|---|---|
| Exception thoát khỏi `BackgroundService` | `try/catch` ở vòng ngoài cùng. Trong .NET, exception không bắt ở đây **hạ cả host** — sập luôn web, không chỉ ingest |
| Một nguồn chết làm mất cả vòng | OpenDota và dltv độc lập, mỗi nguồn một transaction riêng; một bên fail, bên kia vẫn commit |
| Bị OpenDota chặn IP | Rate limiter cứng phía client (~1 req/s). Chủ động giới hạn, **không** đợi bị 429 rồi mới xử lý |
| Lỗi mạng tạm thời | `IHttpClientFactory` + retry backoff, tôn trọng `Retry-After` |
| Deploy lần đầu, DB trống | Seed từ đúng 6 file JSON hiện có → trang có dữ liệu **ngay giây đầu**, không phải chờ vòng ingest đầu tiên |
| Migration lỗi | App không lên, log rõ ràng — thà không lên còn hơn lên với schema sai |

### Quan sát hệ thống

Log structured ra stdout (`docker logs`, như PhuongKhanh) + `api/health` trả vòng ingest gần nhất: thành công hay không, lúc nào, ghi bao nhiêu bản ghi.

---

## 11. Testing

**Nguyên tắc: không test nào gọi mạng thật.** Bộ test phụ thuộc dltv.org còn sống sẽ đỏ vì lý do chẳng liên quan gì đến code.

1. **Tính toán chỉ số** — unit test thuần, không I/O. Phần đáng test nhất: tính sai winrate thì không có gì báo lỗi, chỉ có số sai
2. **Ingest client** — chụp response thật của OpenDota/dltv **một lần**, lưu thành fixture trong repo, chạy qua `HttpMessageHandler` giả
3. **Sanity gate** — test quan trọng nhất cả bộ: đưa fixture rỗng vào, assert ingest bị `Failed` **và dữ liệu cũ còn nguyên vẹn**
4. **Hợp đồng API** — dùng chính 6 file JSON hiện tại làm golden fixture, assert `api/teams` trả đúng shape đó. Bảo vệ trực tiếp lời hứa ở §8
5. **Snapshot idempotency** — chạy `SnapshotWriter` hai lần cùng ngày, assert không nhân đôi dòng

Integration test dùng SQLite file tạm.

---

## 12. Cấu hình

| Env var | Mặc định | Ý nghĩa |
|---|---|---|
| `Ti2026__PathBase` | `` (rỗng) | Path prefix khi chạy sau reverse proxy |
| `Ti2026__IngestIntervalHours` | `6` | Nhịp chạy pipeline |
| `Ti2026__IngestToken` | (bắt buộc ở Production) | Token bảo vệ `POST api/ingest/run` |
| `Ti2026__OpenDota__RequestsPerSecond` | `1` | Rate limit phía client |
| `Ti2026__Dltv__Enabled` | `true` | Tắt nhanh phần scrape nếu cần |
| `Ti2026__SanityGate__MinTeams` | `16` | Ngưỡng tối thiểu để commit một vòng ingest |
| `Ti2026__SanityGate__MinPlayers` | `60` | Ngưỡng tối thiểu để commit một vòng ingest |

---

## 13. Không làm ở giai đoạn này (YAGNI)

Login/phân quyền đầy đủ · admin UI (dữ liệu biên tập vẫn sửa qua file JSON + git) · Hangfire/Quartz (`PeriodicTimer` là đủ) · Postgres (SQLite đủ cho quy mô này) · Redis hay cache layer · CI/CD mới (deploy bằng `docker compose up -d --build` bằng tay, như đang làm với PhuongKhanh) · cơ chế xóa dữ liệu cũ.

Schema được thiết kế sao cho thêm admin UI ở giai đoạn sau không phải đập đi làm lại: dữ liệu biên tập đã nằm trong bảng DB (`TierEntry`), chỉ là hiện tại được nạp từ file thay vì từ form.

---

## 14. Xong Giai đoạn 1+2 nghĩa là

- Trang chạy tại `IP-VPS/ti2026`, **trông y như bây giờ**
- Dữ liệu tự cập nhật 6h/lần, không cần sửa JSON tay nữa
- Có tab/biểu đồ **form theo thời gian** — thứ trang tĩnh không thể làm
- Ảnh phục vụ từ VPS, không hotlink dltv
- dltv gãy hay OpenDota chặn thì trang vẫn hiển thị dữ liệu gần nhất, `api/health` nói rõ đang lỗi gì
- Bộ test chạy offline được
- Web du lịch vẫn chạy bình thường

---

## 15. Giả định cần xác minh khi lập kế hoạch

Những điểm dưới đây chưa được kiểm chứng và **phải xác minh trước khi viết code phần liên quan**:

1. **`robots.txt` và ToS của dltv.org** — quyết định phần scrape được làm tới đâu (§6)
2. **Giới hạn rate thật của OpenDota API** ở tier không API key — con số ~60 req/phút là theo hiểu biết chung, cần đọc lại docs hiện hành
3. **OpenDota có đủ dữ liệu team-level cho 16 đội TI2026 hay không** — nếu thiếu ánh xạ đội cho một số trận, `TeamAlias` phải làm nhiều việc hơn dự kiến
4. **Phiên bản Caddy 2 trên VPS hỗ trợ cú pháp `handle` như viết ở §9** — cần kiểm tra trên máy thật, không đoán
5. **Network `edge` chưa tồn tại trên VPS** — cần tạo trước khi `docker compose up`
6. `net10.0` + EF Core 10.0.9 là phiên bản đang dùng ở PhuongKhanh; giữ đồng bộ để không phải cài thêm SDK trên VPS
