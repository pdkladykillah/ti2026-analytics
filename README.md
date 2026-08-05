# TI2026 Analytics

Trang phân tích 16 đội tham dự **The International 2026** (Dota 2). Dữ liệu tự cập nhật mỗi 6
giờ từ OpenDota, tính chỉ số, dự đoán cặp đấu — và **tự đo xem dự đoán của mình có đáng tin
không**.

ASP.NET Core `net10.0` · EF Core + SQLite · không có bước build cho frontend · chạy trong một
container sau Caddy. Xem [DEPLOY.md](DEPLOY.md) để triển khai.

---

## 1. Dữ liệu đến từ đâu

### OpenDota API — nguồn chính, tự động

Toàn bộ số liệu **đo được** đến từ đây. Tự giới hạn **0.8 request/giây** (48/phút) vì hạn mức
miễn phí là 60/phút — ngồi đúng trên vạch thì sớm muộn cũng ăn 429.

| Endpoint | Dùng để |
|---|---|
| `/teams` | Khớp 16 đội của ta với `team_id` của OpenDota, lấy logo |
| `/teams/{id}/matches` | Danh sách ván của từng đội |
| `/matches/{id}` | **Nguồn giàu nhất**: bàn draft, mốc mua đồ, chỉ số lane, timeline hạ gục |
| `/heroes`, `/constants/items` | Bảng tra cứu tên và giá |
| `/leagues` | Hạng giải, để lọc trận nào được tính vào Elo |
| `/proPlayers` | Khớp tuyển thủ khi khớp theo tên đội thất bại |
| `/players/{id}`, `/players/{id}/heroes` | Chỉ khi **người dùng tự nhập** ID của mình |

### File JSON biên tập — trong git, không tự đổi

Sáu file trong [`data/`](data/) chứa phần OpenDota **không** có: danh sách 16 đội và vòng loại,
đội hình kèm vai trò và quốc tịch, tier list hero, và 5 chỉ số cho những đội chưa đo được.
Nguồn ban đầu là dltv.org, nhập tay, có ghi ngày.

Chúng được seed vào DB lúc khởi động, gác bằng **hash từng file** nên chạy lại bao nhiêu lần
cũng không nhân đôi. Cột `TeamStatSnapshot.Source` đánh dấu `opendota` / `mixed` để **không bao
giờ nhầm số biên tập với số đo**.

### Ảnh — Steam CDN

Logo đội, chân dung hero, ảnh item đều trỏ trực tiếp Steam CDN.

### dltv.org — đã TẮT

`robots.txt` của họ có `Disallow: /uploads/`, mà toàn bộ logo và ảnh tuyển thủ nằm trong đó, nên
**không được phép cache về**. Logo lấy từ OpenDota thay thế được (16/16 đội). Ảnh tuyển thủ thì
không có nguồn thay thế hợp lệ, nên vẫn là avatar chữ cái.

### Quy mô hiện tại

Tính tới 05/08/2026:

```
16 đội · 96 tuyển thủ · 1790 ván (11/07/2014 → 04/08/2026) · 17.900 bản ghi người-trong-ván
35 bản game, hiện tại 7.41 · 10.036 giải · 127 hero · 501 item
38.373 lượt cấm/chọn · 822.345 mốc mua đồ · 132 snapshot lịch sử
```

Bàn draft và mốc mua đồ mới trích từ tháng 8/2026 nên còn đang nạp bù dần cho các ván cũ;
`api/health` có trường `pendingDetails` để theo dõi. Bản 7.41 — thứ mọi bảng "học từ pro" dùng —
đã phủ đủ.

---

## 2. Trang này so sánh những gì

### Elo thay cho winrate thuần

**Winrate không biết đối thủ là ai.** Một đội 67% có thể chỉ toàn gặp đội yếu, còn đội 56% có
thể toàn gặp top. Elo thưởng khi thắng đội mạnh, phạt nhẹ khi thua đội mạnh — nhờ đó hai con số
mới so được với nhau.

`K = 24`, chỉ tính giải hạng `premium`/`professional`. Đây là hệ **khép kín** trong 16 đội: tổng
điểm luôn không đổi, không so được với đội ngoài giải.

### Dự đoán, và mức đáng tin của chính nó

Mỗi cặp đấu ra một xác suất. Nhưng con số quan trọng hơn là **hiệu chuẩn hồi tố ngoài mẫu**:
chọn tham số trên 70% trận cũ nhất, rồi chấm điểm trên 30% trận mới nhất mà phần đó không tham
gia việc chọn.

```
Tập huấn luyện   1192 dự đoán · Brier 0.2445 · đúng 56.7%
Tập kiểm định     515 dự đoán · Brier 0.2358 · đúng 60.0%    ← con số thật
Tung đồng xu                    Brier 0.25
```

Việc tách tập không phải hình thức: lưới quét **chọn thang 700** trên tập huấn luyện, nhưng trên
tập kiểm định thang 700 lại gần như tệ nhất. Chọn và báo cáo trên cùng một tập thì đã đổi sai.

### Phân phối cho kèo trên/dưới

Trung vị, P25–P75 và xác suất vượt mốc cho tổng kills và thời lượng ván. Dưới 10 mẫu thì trả
`null` chứ không trả một con số trông có vẻ đáng tin.

### Phát hiện biến động

So snapshot hôm nay với mốc trước đó. Mỗi chỉ số có **ngưỡng đáng chú ý riêng** (winrate 5 điểm,
chênh lệch kill 1.5, Elo 40…) và chỉ nêu khi vượt ngưỡng. **Im lặng nghĩa là không có gì bất
thường** — một trang báo động liên tục thì chẳng khác gì không có cảnh báo.

### Yếu tố bản game — đã đo, và kết luận là không cần

Cơ chế `PatchRegression` kéo rating về mốc trung bình ở mỗi ranh giới bản chính. Nhưng đo ngoài
mẫu thì **không mức nào cải thiện được gì** (ghép cặp `t = −0.51`), và vứt hẳn dữ liệu trước
7.41 còn **hơi tệ hơn**. Lý do: Elo vốn đã tự quên — một trận từ hai năm trước đã bị hàng trăm
trận sau ghi đè. Cơ chế ở lại, tắt, và lộ ra ở `api/calibration` để bật khi dữ liệu nói khác.

### Cho người chơi: pro coi trọng hero nào

**Bị cấm cũng là được coi trọng**, và với hero mạnh nhất thì đó là hình thức chủ yếu:

```
Lone Druid   cấm 244/368 ván · lượt cấm TB 3.4 · chỉ được chơi 46 lần
Treant       winrate 66.7% — cao nhất bảng
```

Chỉ nhìn winrate thì Treant đứng trên. Nhưng Lone Druid là hero **không ai được phép chơi**.

### Cho người chơi: mốc lên đồ

Trung vị và khoảng P25–P75 cho từng món, **tách ván thắng khỏi ván thua**. Cùng một món mà ván
thắng lên sớm hơn rõ rệt nghĩa là mốc đó thật sự quyết định — ví dụ Eul's Scepter của Hoodwink
sớm hơn 5 phút trong ván thắng.

### Cho người chơi: ai thắng lane

Hiệu suất lane của OpenDota, so với **trung vị của chính vị trí đó**. So hero đi mid với hero hỗ
trợ bằng số tuyệt đối là so hai thứ khác nhau.

### Cho người chơi: hero pool của bạn so với pro

Nhập Dota account ID hoặc Steam ID64 → đối chiếu hero bạn hay chơi với mức pro coi trọng ở bản
hiện tại. Chỉ đọc hồ sơ **công khai**, **không lưu** xuống DB, đệm 15 phút, trần 20 lần tra mỗi
5 phút.

### Đối đầu và series

Bo3/Bo5 gom theo `series_id`. Thắng ván 1 nói được bao nhiêu về cả series.

---

## 3. Trang này KHÔNG cho biết điều gì

Phần này quan trọng ngang phần trên.

**Ưu thế dự đoán rất mỏng.** Brier `0.2358` so với `0.25` của tung đồng xu — khoảng **3%**. Mô
hình giờ *trung thực* (nói 65% thì thật sự khoảng 65%), nhưng nó **không phải máy in tiền**.
Trang nói thẳng điều này ngay tại tab Dự đoán.

**Trên nửa dữ liệu mới, mô hình nghiêng về dè dặt** — nói 63.5% thì thực tế 72.2%. Giữ nguyên
thang 600 chứ không siết lại, vì sai theo hướng dè dặt an toàn hơn hẳn sai theo hướng tự tin.

**Hệ rating khép kín.** Chỉ 16 đội. Không so được với đội ngoài giải.

**Dữ liệu chuyên nghiệp không phải dữ liệu pub.** Nó trả lời tốt *"bản này cái gì mạnh"* và
*"mốc thời gian chuẩn là bao nhiêu"*, và trả lời tệ mọi thứ cần 5 người phối hợp. Mốc của pro
giả định có người hỗ trợ nhường lính; ở pub chậm hơn 2–4 phút là bình thường.

**Không phân biệt được bản vá chữ cái.** Chỉ số bản game của OpenDota chỉ có bản chính — 7.41a
đến 7.41e đều là `7.41`.

**Không có dữ liệu facet.** Trường `hero_variant` bằng `0` ở mọi trận đã kiểm, nghĩa là không xác
định.

**Không có bảng cặp hero khắc chế nhau** — cố tình. Chia số ván hiện có cho 3 lane rồi cho hơn
120 hero thì gần như mọi cặp có mẫu dưới 5; bảng đó sẽ trông rất thuyết phục và hoàn toàn là
nhiễu.

**Năm chỉ số của một số đội là số biên tập, không phải số đo** — assists, first blood, mốc 10
mạng và hai tỷ lệ đi kèm. Cột `Source` đánh dấu rõ, và giá trị chưa biết là `null` chứ không
phải `0`.

---

## 4. Nguyên tắc xuyên suốt

- **Chưa biết ≠ bằng không.** Mọi chỉ số chưa đo được là `null` end-to-end, và UI hiện `—`.
- **Mẫu nhỏ thì nói ra.** Ngưỡng tối thiểu ở mọi bảng, và endpoint trả kèm số mẫu.
- **Đã lọc thì khai.** Bảng nào cắt bớt đều báo cắt bao nhiêu và vì sao.
- **Lưu thô, lọc lúc đọc.** Lọc lúc nạp thì mất dữ liệu vĩnh viễn và không hỏi lại được.
- **Tham số phải chọn từ dữ liệu**, ngoài mẫu, và ghi lại phép đo ngay cạnh hằng số.

---

## 5. Kiến trúc

```
src/Ti2026.Data     17 entity + 5 migration tăng dần
src/Ti2026.Ingest   OpenDota client, ingester, phân tích, seeder
src/Ti2026.Web      21 endpoint (20 GET + 1 POST) + frontend không cần build
tests/Ti2026.Tests  158 test
```

Ingest chạy in-process bằng `BackgroundService`, mỗi 6 giờ, một vòng gồm ba bước
(`opendota` → `match-detail` → `snapshot`). `IngestGate` đảm bảo không bao giờ có hai vòng chồng
nhau — SQLite chỉ có một người ghi.

`Match.DetailSchemaVersion` cho phép **nạp bù mà không cần sửa SQL tay** trên production: khi
trích thêm trường mới từ cùng một payload, chỉ cần tăng hằng số và scheduler tự nạp lại.

Thiết kế và kế hoạch đầy đủ: [`docs/superpowers/`](docs/superpowers/).
