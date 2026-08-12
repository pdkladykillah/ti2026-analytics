# ti2026-analytics

Trang phân tích Dota 2 tiếng Việt: 16 đội dự The International 2026, cộng một lớp hồ sơ
cá nhân cho vài tài khoản được theo dõi lâu dài, cộng một tab học lối chơi từ 12 tuyển thủ
chuyên nghiệp.

ASP.NET Core `net10.0` + EF Core/SQLite. Frontend là HTML/CSS/JS **thuần, không build step**
— không Tailwind, không npm, không bundler. Dockerfile chỉ cần .NET SDK.

Chạy thật: <https://ti-2026-pdk.duckdns.org>

---

## Tra cứu nhanh — dùng codegraph, đừng grep mò

Dự án có `.codegraph/` (225 tệp, ~3.560 nút). Hỏi cấu trúc thì dùng `codegraph_*`:
`codegraph_search` tìm ký hiệu, `codegraph_context` lấy bối cảnh một vùng,
`codegraph_callers` xem ai gọi, `codegraph_explore` đọc mã nhiều ký hiệu một lần.

**Lý do có mục này:** đã xảy ra thật — một phiên viết lại nguyên `RoleResolver` vì không
biết nó tồn tại, rồi hai bản cài cùng một luật suýt lệch nhau. Trước khi viết một hàm suy
luận vai trò / thống kê / chuẩn hoá nào, tra xem nó có sẵn chưa.

`graphify` chưa cài trên máy này (không có trong PATH). Nếu cài thì nhớ `.graphifyignore`
và thêm `graphify-out/` vào `.gitignore` — hiện chưa có cả hai.

---

## Bản đồ thư mục

| Đường dẫn | Việc |
|---|---|
| `src/Ti2026.Data/Entities/` | Thực thể EF. Đọc doc comment trước khi thêm cột — phần lớn cột đều có lý do đã đo. |
| `src/Ti2026.Data/Migrations/` | Migration. Vài cái **cố ý** kèm `UPDATE ... SET DetailFetchedAt = NULL` để bắt nạp lại. |
| `src/Ti2026.Ingest/OpenDota/` | Gọi API và ghi DB. |
| `src/Ti2026.Ingest/Analytics/` | **Toàn bộ phần thống kê.** Tra ở đây trước khi viết mới. |
| `src/Ti2026.Web/Endpoints/` | Minimal API, mỗi tab một tệp. |
| `src/Ti2026.Web/wwwroot/` | Đúng 3 tệp: `index.html`, `app.js`, `app.css`. Đọc mục "Bẫy trong app.js" bên dưới trước khi thêm hàm. |
| `tests/Ti2026.Tests/` | 542 bài. Nhiều bài khoá lại một cái bẫy đã mắc — đọc doc comment của bài trước khi sửa nó. |

### Những lớp Analytics dễ bị viết lại nhất

- **`RoleResolver`** — nguồn sự thật DUY NHẤT về vai trò một ván. Mọi nơi khác phải gọi lại
  nó, không tự cài. `IdolStyle.PositionOf` chỉ là lớp bọc mỏng.
- `MultipleTests` — Benjamini–Hochberg, nhị thức chính xác tính trong không gian log,
  Mann–Whitney có hiệu chỉnh đồng hạng.
- `SkillComponents`, `DeathEffect`, `DeathTrade`, `LanePhase`, `PlayHabits`,
  `NemesisHeroes`, `TeammateAnalysis`, `HeroMetaGap`, `IdolStyle`.

---

## Luật phân tích — vi phạm là ra kết luận sai, không phải xấu

### 1. `lane_role` là LANE, không phải VỊ TRÍ

Hard support đứng safelane nên mang nhãn `safe` y hệt carry; support cơ động đứng offlane
nên mang nhãn `off` y hệt offlaner. Đo được: trong 116 ván nhãn "safe" của tài khoản chính,
nhóm hạng net worth 1–2 đạt 648 GPM / 381 lính, nhóm hạng 4–5 chỉ 302 GPM / 56 lính.

Vị trí thật = `lane_role` × hạng net worth trong đội → `RoleResolver.Resolve`.

### 2. Phân vị của giá trị 0 là rác

`raw = 0` mà `pct > 0.5` nghĩa là đa số người chơi cũng bằng 0, không phải giỏi. Đã đo ở
ván 8937662260: `hero_healing_per_min` raw 0, pct 0,93. Chặn ở chỗ đọc, xem
`TrackedMatchDetailIngester.Pct`.

Ngược lại `raw = 0` với pct THẤP (số chết) là thành tích thật.

### 3. Số chết đảo chiều

Phân vị cao của `deaths_per_min` = chết nhiều = tệ. Đảo trước khi gộp vào bất kỳ điểm nào.

### 4. Ván pub và ván chuyên nghiệp KHÔNG cùng thang

Đo trên 126 ván pro và 114 ván pub: pub nhiều hơn **46%** số mạng mỗi 10 phút và cao hơn
**28%** sát thương trên mỗi vàng, tính trên mọi người chơi. So số thô cho kết luận sai cả
**hướng**, không chỉ sai độ lớn.

Cách chữa đã cài: bảng `StyleAnchors` giữ tổng của cả 10 người mỗi ván, mỗi hồ ván có mốc
riêng (`pro`, `pub-idol`, `pub-{trackedPlayerId}`). Mọi chỉ số ở tab idol là **tỉ số** so
với mốc đó. Xem `IdolStyle`.

### 5. Chỉ so CÙNG vai trò

So chéo vai trò chỉ chứng minh được rằng carry chết ít hơn support. Người dùng đã bác đúng
một lần vì lỗi này.

### 6. Trung vị, không phải trung bình, cho phân vị

Phân vị là thang thứ hạng. Cộng rồi chia là phép tính không có nghĩa trên thang đó.

### 7. Kiểm nhiễu trước khi tin kết luận

Ba biến đã từng lật ngược dấu của kết quả trong dự án này: **kết quả ván**, **thời lượng
ván** (xem `DeathEffect.DurationBand`), **vai trò**. Và luôn chạy lại phép đo trên tài khoản
thứ hai (`nene`) làm nhóm đối chứng — cách này đã bắt được bốn kết luận sai.

### 8. Cẩn thận phép kiểm lặp vòng

Đã mắc hai lần. Ví dụ: đo "hạng net worth dự đoán core/support chính xác 98,5%" trong khi
"core/support" lại được định nghĩa bằng chính hạng đó. Xem `RoleResolver.CrossTab` — nó cố ý
**không** gắn con số phần trăm nào.

---

## Dữ liệu: cái gì có, cái gì không

| Trường | Phủ | Ghi chú |
|---|---|---|
| `benchmarks` (phân vị theo hero) | 100% | Có cả ở ván chưa parse. |
| Hạng net worth trong đội, GPM, lính | 100% | Từ `matches/{id}`, không cần parse. |
| `lane_role` | **~9%** | Chỉ ván đã parse. |
| `lane_efficiency_pct`, `gold_t`, `teamfights`, `purchase_log` | ~9% | Chỉ ván đã parse. |

**Replay của Valve hết hạn sau ~60 ngày** — đã đo: ván 61 ngày parse được, ván 70 ngày thì
không. Nên KHÔNG có cách nào lấy thêm nhãn cho ván cũ. Ván trong cửa sổ đã phủ 100%.

Ván chuyên nghiệp thì gần như luôn parse sẵn (300/300 với phần lớn tuyển thủ), nên tab idol
không phải xin parse.

**Đã thử và ĐÃ BÁC BỎ: bộ phân loại đoán lane từ chỉ số.** Kiểm chéo 68,2%/79,7% nhưng lớp
tệ nhất chỉ 16%, và khi lọc theo độ tin cậy ≥0,90 thì lệch có hệ thống (một người ra 0 ván
offlane trong khi thực tế có 31). Nó học "đây là ai", không học "ván này đi lane nào".

**Nguồn nhãn khác đều bị chặn:** STRATZ `robots.txt` có `User-agent: ClaudeBot → Disallow: /`;
Dotabuff tương tự; `api.steampowered.com` cấm toàn bộ; Liquipedia cấm `/dota2/api.php`;
`dota2protracker.com` cấm ClaudeBot. **Không được truy cập.**

---

## Bẫy API OpenDota đã trả giá

- **Ván thi đấu chuyên nghiệp mang `lobby_type = 1`**, không phải 2. Lọc theo 2 được đúng
  con số không.
- **`leagueid` không có trong `players/{id}/matches`** — luôn trả 0. Chỉ `matches/{id}` mới có.
- **`radiant_name`/`dire_name` KHÔNG chỉ có ở ván giải.** Phòng chờ pub cũng đặt được tên;
  một nhóm pub tên "Sniper monkeys" đã từng hiện lên cạnh Team Falcons. Lọc theo `leagueid != 0`.
- **`radiant_gold_adv` luôn là Radiant trừ Dire** — phải đảo dấu khi ở phe Dire. Quên thì gần
  nửa số ván đọc ngược và kết quả trung hoà về 0 mà vẫn trông hợp lý.
- **`hero_id` là SỐ**, không phải chuỗi. Khai chuỗi thì endpoint trả 500.
- **`damage_taken` là từ điển**, phải tự cộng. Từ điển rỗng phải ra `null` chứ không phải 0.
- **Tra tuyển thủ theo TÊN là nhặt nhầm người.** Có 4 tài khoản mang persona "TOPSON", 3 mang
  "AMMAR_THE_F", 6 mang biến thể "Yatoro". Tra theo đội hình cũng nhầm: tài khoản "gpk~" trong
  roster Team Spirit chỉ có 40 ván và 0 ván thi đấu. **Xác minh bằng số liệu** — người thật có
  hàng trăm ván `lobby_type` 1.
- Không có endpoint nào cho biết đã tiêu bao nhiêu tiền. Chỉ có
  `x-rate-limit-remaining-minute`. Số thật xem ở <https://www.opendota.com/api-keys>.

---

## Hệ giao diện "Aegis"

Nền giấy da ấm `#f6f1e4` / than ấm `#17130d`. Điểm nhấn **vàng Aegis** `--accent #e8a317`,
xanh Radiant `--pos`, đỏ Dire `--neg`. Font **Nunito** + **JetBrains Mono** cho mọi con số.

### Ba luật cứng

1. **Vàng chỉ làm NỀN, không làm chữ và không làm viền mảnh.** Trên nền sáng nó chỉ đạt
   2,2:1. Cần chữ vàng thì dùng `--accent-ink` (đảo chiều sẵn theo chủ đề).
2. **Hai chủ đề, hai chiến lược.** Nền thẻ so với nền trang chênh 1,109 ở chủ đề sáng nhưng
   chỉ 1,073 ở tối, và bóng đổ trên nền tối gần như không tồn tại. Nên: **sáng thì độ sâu
   (`--card-edge: transparent`), tối thì nét.**
3. **Xanh thắng phải ngả một chút sang lam.** Xanh lá thuần với đỏ thuần chỉ cách nhau ΔE
   17,7 (sáng) và 10,7 (tối) sau mô phỏng deuteranopia — gần như một màu. Ngả lam đưa lên
   80,7 / 73,5. **Màu không bao giờ được là kênh duy nhất** — luôn kèm dấu, chữ hoặc thứ tự.

`ThemeContrastTests` khoá ~45 cặp màu ở mức WCAG AA trên **cả hai** chủ đề, gồm cả nền
`--surface-2` (nền này từng bị bỏ sót và có 4 chỗ chữ chỉ đạt 3,84:1).

### Font phải có tiếng Việt

Poppins **không có** subset `vietnamese` — dùng nó cho một trang tiếng Việt thì mọi chữ có
dấu rơi về font hệ thống. Cũng thiếu: Fredoka, Rubik, Figtree. `ThemeContrastTests` khoá lại
danh sách này.

### Gấp chữ

`foldExplanations()` trong `app.js` gấp mọi đoạn dài hơn 90 ký tự. Chạy bằng
`MutationObserver` nên phủ cả chữ tĩnh lẫn chữ do JS sinh — **đừng gọi tay ở từng chỗ
render**, chỗ thứ mười một sẽ quên.

Đoạn nào nằm trong `<header>` của thẻ thì được `hoistToHeading()` biến thành **dấu "i" dính
vào đuôi tiêu đề** — chiếm đúng không chiều dọc. Đoạn không có tiêu đề để bám (khối `.note`
cấp trang) thì giữ dạng gấp.

Đã thử và loại bốn dạng trước: viên thuốc vàng, viên thuốc nhạt, chữ gạch chân chấm, dấu `?`
tròn. Tất cả đều sai cùng một kiểu — **chiếm một dòng riêng**, nhân 28 khối thành 28 dòng
trống, và chỗ nào có hai khối thì hai dấu xếp chồng trông như lỗi.

Hover chỉ là lối tắt cho chuột: nút phải bấm được và nhận được tiêu điểm, nếu không thì với
màn hình cảm ứng và người dùng bàn phím nội dung coi như biến mất.

Chú thích KHÔNG đi sau một tiêu đề thì `hoistToHeading()` không với tới — viết thẳng bằng
`infoDot(text)`. Việc mở/đóng do **một** bộ nghe uỷ quyền ở `document` (`setupInfoDots()`) lo;
đừng gán `onclick` cho từng nút, gán cả hai chỗ thì mỗi cú bấm đảo trạng thái hai lần và dấu
"i" thành ra bấm không ăn.

### Khoảng hở dưới tiêu đề là của TIÊU ĐỀ, không phải của đoạn chữ dưới nó

`.card h3, .card h4 { margin-bottom }` tồn tại vì một sự cố: khoảng hở đó vốn do đoạn `.desc`
đứng ngay dưới tạo ra, mà `hoistToHeading()` lại gấp chính đoạn đó thành dấu "i" rồi đặt
`hidden` lên nó — `[hidden]` là `display:none` nên margin biến mất theo. Mọi mục có dấu "i"
đều dính, tức gần như mọi mục. Tiêu đề trong `<header>` thì được đặt lại về 0: ở đó nó là một
ô của hàng flex, margin vừa thừa vừa làm lệch căn hàng.

### Chữ sáng trên nền tối MỎNG hơn cùng tỉ số đó ở nền sáng

Nhãn tab dùng `--ink` ở chủ đề tối chứ không phải `--ink-2`. Đây không phải lỗi tương phản —
đo được `--ink-2` trên nền thẻ đạt **9,29:1** (tối lam) và **9,52:1** (tối vàng), vượt xa AA.
Nhưng tỉ số đo mảng màu đặc, còn thứ mắt đọc là nét chữ 15px: khử răng cưa ăn mòn hai bên nét
sáng nên chữ trông nhạt hơn hẳn cùng cặp màu ở chủ đề sáng.

---

## Bẫy trong app.js

### HAI HÀM TRÙNG TÊN = một hàm chết lặng

Đã xảy ra và sống sót nhiều phiên: có hai `async function loadSchedule()` — một vẽ bảng đấu
vào `#schedule-body`, một vẽ trạng thái vòng nạp vào `#sched-body`. JavaScript **không** coi
đó là lỗi, khai báo sau lặng lẽ đè lên khai báo trước.

Hậu quả là kiểu hỏng khó truy nhất: bấm tab "Lịch thi đấu" gọi trúng hàm còn sống, nó ghi vào
một thẻ ở **tab khác**, còn `#schedule-body` không ai đụng tới nên trắng trơn. Không ném lỗi,
không cảnh báo, không dấu vết trong console. Mọi dấu hiệu bên ngoài đều chỉ sai chỗ —
`api/schedule` vẫn trả đủ 27 nút, tệp triển khai vẫn khớp bản local, và bài kiểm "mọi id đều
tồn tại" vẫn xanh vì cả hai id đều có thật.

`StaticAssetTests.Khong_hai_ham_nao_trong_app_js_trung_ten` khoá lại.

### Đừng lồng một chuỗi HTML có dấu nháy vào một thuộc tính HTML

`teamLogo()` dùng được `onerror="this.outerHTML='<span class=&quot;…&quot;>'"` vì chuỗi thay
thế của nó không chứa dấu nháy nào. Thêm `style="width:…"` vào đó thì nó tự cắt ngang chính
thuộc tính `onerror`, mảnh HTML vỡ đôi và một khúc thuộc tính rơi ra thành **chữ hiện trên
trang** — đã thấy thật: thẻ Topson hiện ra `T'"> Topson`. `idolFace()` làm cách khác: chữ cái
dự phòng nằm dưới, ảnh phủ lên, ảnh hỏng thì chữ lộ ra. Không cần JS, không phải tự thoát.

### Kiểm giao diện bằng jsdom trước khi triển khai

Không có bộ kiểm DOM trong repo, nhưng `npm i jsdom` rồi nạp `index.html` + `app.js` thật với
`fetch` giả là đủ bắt cả hai lỗi trên trong một lần chạy. Cách này tìm ra lỗi trùng tên hàm sau
khi đọc mã ba lượt mà không thấy.

Chốt chống gấp lồng nhau phải hỏi "có CON NÀO là phần đã gấp không", không hỏi "con ĐẦU TIÊN
có phải không" — khối cảnh báo giữ icon ở đầu, và bản đầu đã tự gói 65 lớp lồng nhau.

---

## Vận hành

- VPS `103.90.227.207`, thư mục `/opt/ti2026`, Docker Compose. Service tên là **`app`**,
  không phải `web`.
- **Compose dùng `expose`, KHÔNG `ports`** — app không tới được từ `127.0.0.1:8080` trên host.
  Mọi lời gọi phải qua `https://ti-2026-pdk.duckdns.org`.
- Nạp dữ liệu: `POST /api/ingest/run` kèm header `X-Ingest-Token`. Trả **409** nếu đang có
  vòng chạy — chờ chứ đừng gọi dồn.
- Lệnh SSH dài hay bị ngắt. Dùng script `setsid nohup ... &` rồi đọc log.
- Đọc DB sản xuất: `/var/lib/docker/volumes/ti2026_app_data/_data/ti2026.db`. Mở read-only
  qua URI. **Sao chép DB phải kèm cả `-wal`**, thiếu thì đọc ra trạng thái cũ mà không báo lỗi.
- Điều kiện dừng của mọi vòng nạp phải đòi hỏi ĐÃ CÓ dữ liệu, không chỉ "còn lại 0" — bảng
  trống cũng cho 0.

### Chi phí API — ĐANG CHẠY BẬC MIỄN PHÍ

**Khoá OpenDota đang TẮT** — `Ti2026__OpenDota__UseApiKey=false` trong `docker-compose.yml`.
Khoá vẫn nằm trong `.env` để bật lại khi cần.

Đo được bằng bộ đếm ở `api/ingest/status` (đếm ngay tại `RateLimitedHandler`, nên một lời
gọi bị 429 rồi thử lại được tính là hai — đúng như OpenDota tính tiền):

| khoản | phép tính | mỗi ngày |
|---|---|---|
| vòng ingest thường | 36–37 × 4 vòng | ~148 |
| `pro-pub` (80 tuyển thủ, cửa 24h) | 80 × 1 | 80 |
| `idol` (12 người, cửa 12h) | 24 × 2 | 48 |
| chi tiết ván idol mới | — | ~3 |
| **tổng** | | **~279** |

Bậc miễn phí là **2.000 lời gọi/ngày** → đang dùng **14%**. Hoá đơn: **0 đồng**.

Dấu hiệu xác nhận đang ở bậc miễn phí: một vòng ingest mất ~57 giây thay vì ~28, vì bộ giới
hạn 0,8 req/s trở thành thứ ràng buộc thay vì lượng việc.

**BẬT KHOÁ KHI NẠP BÙ LƯỢNG LỚN.** Không khoá thì trần mỗi vòng tụt từ 2.000 xuống 300 ván
và nhịp chậm gấp mười — đợt 1.200 ván idol sẽ mất hơn 25 phút và chiếm nửa hạn mức ngày thay
vì 4 phút. Sửa `UseApiKey=true` trong `docker-compose.yml`, chạy `docker compose up -d app`,
nạp xong thì trả về `false`.

**CHỖ NGUY HIỂM:** header xác thực, nhịp gọi và trần ván mỗi vòng đều rẽ nhánh theo cùng một
thuộc tính `OpenDotaOptions.HasKey`. Đừng bao giờ để ba thứ đó tự kiểm `ApiKey` riêng — gửi
request không kèm khoá mà chạy 8 req/giây là gấp mười lần bậc miễn phí, và hậu quả không phải
một vòng ingest hỏng mà là **VPS bị chặn IP**, mất luôn nguồn dữ liệu. Ba bài kiểm trong
`Ti2026OptionsTests` khoá bất biến này.

Đã tiêu tổng cộng **~$3,6** tính tới 2026-08-12, phần lớn do đổi lược đồ rồi quét lại toàn bộ
ván — **thiết kế cột một lần cho đủ**.


---

## Ranh giới an toàn — không được vi phạm

- VPS còn chạy một site khác ở `/opt/phuongkhanh`. **Không** đụng vào nội dung đó, **không**
  chiếm cổng 80/443, **không** đặt tên project compose là `phuongkhanh`, **không**
  `docker system prune` không giới hạn, **không** `docker compose down -v` trong thư mục đó.
- `/opt/ti2026/.env` (chmod 600, đã gitignore) giữ `TI2026_INGEST_TOKEN` và
  `TI2026_OPENDOTA_API_KEY`. Khoá OpenDota gửi qua header `Authorization: Bearer`, **không bao
  giờ** đặt vào query string, và `api/ingest/status` **không được** trả về giá trị của nó.
- Còn treo: `TI2026_INGEST_TOKEN` từng lọt vào một bản ghi hội thoại; lời đề nghị xoay khoá
  chưa được trả lời.

---

## Hai chế độ xem

Công tắc phân đoạn ở đầu trang đổi **cả bảng màu lẫn nhóm tab**, lưu vào `localStorage`:

- **Giải TI 2026** — 10 tab, vàng Aegis, nền ấm.
- **Lối chơi của tôi** — 3 tab (Hồ sơ, Học lối chơi, Học từ pro), lam ngọc, nền lạnh.

Thành **bốn** bảng màu (2 chế độ × 2 chủ đề). `ThemeContrastTests` kiểm cả bốn — 89 bài.
Màu ngữ nghĩa `--pos`/`--neg` **giữ nguyên qua mọi chế độ**: thắng/thua phải trông giống
nhau ở mọi nơi, và chúng đã được cân cho người mù màu đỏ-lục.

Thứ tự khối trong `app.css` quan trọng: `:root` → `[data-mode="me"]` → `[data-theme="dark"]`
→ `[data-theme="dark"][data-mode="me"]`. Ba khối giữa cùng độ ưu tiên nên thứ tự trong tệp
quyết định.

## Đã thử và ĐÃ GỠ BỎ: suy vị trí bằng tiền nghiệm hero

Học lane hay gặp của từng hero từ 3.297 ván chuyên nghiệp rồi ghép với hạng net worth thật.
Chính xác **65,0%** so với mốc đoán bừa 46,4% — nghe như dùng được.

**Nhưng vô dụng.** Nhãn thật cho biên độ **25 điểm** thắng-thua giữa các vị trí (pos4 57,5%
xuống pos5 32,5%); nhãn ước lượng làm biên độ đó co còn **2,2 điểm**, cả năm ô đều bám sát
50,1% — đúng tỷ lệ thắng chung. Tức ô ước lượng gần như không mang thông tin về vị trí.

Đã loại trừ cách giải thích "mẫu nhãn thật là ván gần đây": 530 ván nhãn thật trải từ 2019-12
tới 2026-08, và giới hạn ô ước lượng vào cùng thời kỳ chỉ đổi khoảng cách từ 6,7 xuống 6,4.

**Đừng dựng lại.** Ván chưa parse chỉ nói core hay hỗ trợ.
