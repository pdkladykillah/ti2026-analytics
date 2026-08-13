# So sánh hai đội: từng vị trí, chỉ số, và hero

Ngày 2026-08-13. Mở rộng tab **Đối đầu** đã có.

## Câu hỏi tính năng trả lời

"Hai đội này khác nhau ở đâu, người nào đối đầu người nào, và bên nào có hero mà bên kia
không đụng tới." Đây là màn **để hiểu**, không phải màn dự đoán — tab Dự đoán đã làm việc đó,
và trộn hai phép đo khác nhau vào một màn là cách chắc chắn để cả hai cùng mất nghĩa.

## Vì sao mở rộng tab cũ chứ không thêm tab

`#view-h2h` đã mang đúng tiêu đề "So sánh hai đội" và đã có hai ô chọn đội. Thêm tab thứ 11
là tạo ra hai chỗ làm cùng một việc, rồi người dùng phải đoán xem chỗ nào mới nhất.

Chia thành ba mục con qua `data-sec` (dùng `setupSubTabs()` sẵn có):

| mục con | nội dung |
|---|---|
| Chỉ số đội | bảng 10 chỉ số hiện tại, giữ nguyên |
| Từng vị trí | mới |
| Hero | mới |

## Dữ liệu — đã có đủ, không tốn thêm lời gọi API nào

Đo trên DB sản xuất ngày 2026-08-12:

| thứ cần | con số |
|---|---|
| `MatchPlayers` tổng | 27.470 dòng |
| thuộc 16 đội (`PlayerId` khác null) | 18.914 |
| trong đó có `LaneRole` | **18.909 — 99,97%** |
| ván có đủ 10 dòng người chơi | **2.747/2.747** |
| hồ neo `pro` trong `StyleAnchors` | 1.583 ván (ngưỡng cần: 40) |
| ảnh + tên thật tuyển thủ | 80/80, đã cache cục bộ |

Phủ nhãn lane ở đây gần như tuyệt đối vì ván chuyên nghiệp luôn được parse sẵn — ngược hẳn
với ~9% ở tài khoản pub. Nghĩa là vị trí thật tính được cho gần như mọi ván.

## Endpoint

`GET /api/versus?a={slug}&b={slug}`

1. Giải hai đội theo slug. Đội hình lấy từ `RosterEntries` đang hiệu lực (`ValidTo` null).
2. Cửa sổ 730 ngày, cùng cửa sổ với tab idol.
3. Nạp `MatchPlayers` nối `Matches` cho **toàn bộ 10 người mỗi ván**, không chỉ người của ta —
   cần đủ 10 để tính hạng net worth và tổng net worth của phe.
4. **Vị trí = `RoleResolver.Resolve(LaneRole, teamFarmRank)`**, không phải `lane_role`.
   `teamFarmRank` = thứ hạng `NetWorth` trong cùng `(MatchId, phe)`.
5. Mỗi người lấy vị trí **hay gặp nhất** trong các ván có kết luận chính xác, kèm tỉ lệ.
6. Chữ ký: `IdolStyle.Signature(games, IdolStyle.Normalizer(anchors["pro"]))` trên các ván
   **đúng vị trí đó**.
7. Hero theo vị trí: tập ≥3 ván, giao và hiệu; cộng danh sách lệch tần suất nhiều nhất.
8. Đối đầu trực tiếp: đếm ván có đúng cặp `(RadiantTeamId, DireTeamId)`.

## Ba luật của dự án phải giữ

1. **`lane_role` là LANE, không phải vị trí.** pos1 và pos5 dùng chung nhãn `safe`. Đo được
   hậu quả: đếm theo lane cho PARIVISION ra 68 hero "safelane", con số đó trộn carry với hard
   support. Mọi phép nhóm phải đi qua `RoleResolver`.
2. **Số thô không cùng thang.** Hai đội đánh khác giải, khác bản game, khác đối thủ. Dùng lại
   `IdolStyle.Axes` + hồ neo `pro` để mọi con số là **tỉ số** so với người bình thường.
3. **Chỉ so cùng vai trò.** pos1 với pos1, pos5 với pos5.

## Giao diện

### Mục "Từng vị trí"

Năm hàng, mỗi hàng một cặp:

```
pos2   [ảnh] No[o]ne-              │              Larl [ảnh]
       PARIVISION · 88% ván mid    │    Team Spirit · 91% ván mid
       298 ván                     │                     289 ván
────────────────────────────────────────────────────────────────
Tốc độ farm            1,08× █████ │ ███████ 1,15×
Sát thương trên vàng   1,21× ██████│ ████    0,97×
...
▸ Bảy trục, số đầy đủ (GPM, XPM, KDA, lính, lane%)
```

Thanh đối xứng hai bên trục giữa. Trục *phong cách* vươn ra chỉ nghĩa là **khác**, không phải
giỏi hơn — giữ nguyên phân biệt `Kind` của `StyleAxis`.

### Mục "Hero"

Theo từng vị trí, ba ô cứng (chỉ A / cả hai / chỉ B, ngưỡng ≥3 ván) cộng một dải "lệch nhiều
nhất" xếp theo hiệu tần suất. Ngưỡng cứng một mình sẽ xếp hero đánh 20 lần và hero đánh 3 lần
vào cùng một ô; dải lệch bắt đúng phần ngưỡng bỏ sót.

**Không làm hero cấp đội.** Đo được: PARIVISION 120 hero, Team Spirit 118, trên tổng 127 hero
của game, chung 112. Biểu đồ Venn ở mức đó chỉ nói "cả hai đội chơi gần hết".

### Dải đối đầu trực tiếp

Chỉ hiện khi **≥5 ván**, và luôn ghi rõ số ván. Đo được trên 120 cặp có thể có: 20 cặp chưa
gặp nhau lần nào, trung vị chỉ 8 ván, 21 cặp có ≤2 ván. Dưới ngưỡng thì nói thẳng "hai đội mới
gặp nhau N lần, chưa đủ để đọc" thay vì hiện một con số dựng trên hai ván.

## Quyết định về cửa sổ thời gian

Tính **mọi ván trong 730 ngày, kể cả ván người đó đánh cho đội cũ**. Whitemon vừa rời Tundra
sang 1Win; lọc theo hợp đồng hiện tại sẽ cắt gần hết mẫu của anh. Nhãn ghi rõ số ván và khoảng
thời gian, và đánh dấu người đã đổi đội trong cửa sổ (suy từ `RosterEntries.ValidTo`).

## Chỗ sẽ vỡ, và cách chặn

- **Người ít ván ở một vị trí.** Dưới `IdolStyle.MinGames` (25) thì `Signature` tự bỏ trục đó.
  Giao diện phải hiện số ván và nói rõ thay vì vẽ một thanh trống.
- **Đội mẫu mỏng.** Team Resilience 79 ván so với Team Liquid 574. Số ván hiện cạnh mỗi người
  để hai bên không trông ngang hàng.
- **Một người đánh nhiều vị trí.** Nisha 30% số ván ở vị trí khác. Ghép theo vị trí hay gặp nhất
  và hiện tỉ lệ ngay đó.
- **Vị trí trống một bên.** Nếu đội B không có ai mang vị trí đó thì hiện một bên, không bịa.

## Bài kiểm

1. Ghép vị trí khi hai người cùng đội cùng mang nhãn lane `safe` — phải tách ra pos1 và pos5
   theo hạng net worth. Đây là bài khoá luật số 1; nó sẽ đỏ nếu ai đó rút gọn thành `lane_role`.
2. Phép tính tập hero ở ngưỡng biên (đúng 2 và đúng 3 ván).
3. Cặp đội chưa gặp nhau lần nào — không được ném, phải trả về dải rỗng có lời giải thích.
4. Dải đối đầu không hiện khi dưới 5 ván.
5. Người dưới 25 ván ở một vị trí — không có trục nào được vẽ.
6. Hợp đồng endpoint: slug sai trả 404, thiếu tham số trả 400.

## Việc tách riêng

Ảnh idol: nối `IdolPlayers.AccountId` sang `Players.PhotoUrl` để lấy ảnh thi đấu đã cache sẵn.
11/12 người có ngay; chỉ Topson thiếu vì không thuộc đội nào dự TI. Không liên quan tính năng
này, làm riêng.
