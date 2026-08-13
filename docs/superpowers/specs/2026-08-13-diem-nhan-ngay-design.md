# Điểm nhấn ngày thi đấu

Ngày 2026-08-13. Mục con thứ ba của tab **Lịch thi đấu**.

## Câu trả lời cần có

"Hôm nay có gì đáng nhớ." Và lưu lại, để đến ngày thứ tư câu "hero này ngày nào cũng bị cấm"
mới có sức nặng — hôm nay thì chưa.

## Ranh giới quyết định mục này dùng được hay không

Một ngày Swiss có **8 series**. Với cỡ mẫu đó, ranh giới giữa mô tả và suy luận chính là ranh
giới giữa dùng được và bịa:

| loại | ví dụ | với 8 series |
|---|---|---|
| mô tả | "LGD thắng Falcons 2–1" | đúng chắc chắn |
| đếm | "Muerta bị cấm 7/8 series" | đúng chắc chắn |
| so ngày | "hero này ngày nào cũng bị cấm" | mạnh dần từ ngày 3–4 |
| suy luận | "đội X đang lên phong độ" | **nhiễu** — cần vài chục ván |

Bản này chỉ ra ba nhóm đầu. Nhóm thứ tư không xuất hiện cho tới khi có đủ ngày tích luỹ, và
khi xuất hiện thì phải đi qua `MultipleTests` như mọi kết luận khác của dự án.

## Hai nguồn, hai nhịp — và đó là ràng buộc chính

| nguồn | nội dung | nhịp |
|---|---|---|
| `ScheduledSeries` (Valve) | series nào xong, tỷ số series | **15 phút** |
| `Matches` + `MatchPlayers` + `DraftEvents` (OpenDota) | thời lượng, pick/ban, chỉ số | **6 giờ** |

Đo lúc 04:5x ngày 13/08: bảng đấu đã ghi 2/8 series xong, nhưng mới **4 ván** của giải nằm trong
`Matches`. Nghĩa là điểm nhấn KHÔNG được đợi cả hai nguồn đủ — nó phải tính từ thứ đang có và
**nói rõ độ phủ**. Một mục trống trơn suốt bốn tiếng vì chờ dữ liệu thì tệ hơn một mục nói
"mới đọc được 4/17 ván".

## Chốt ngày

Tự chốt khi **mọi series được xếp trong ngày đó đều `IsCompleted`**. Bộ làm tươi 15 phút đã biết
trạng thái từng series nên không cần thêm lịch riêng.

Trong lúc ngày còn mở thì vẫn tính và vẫn upsert — người xem giữa ngày thấy số lớn dần. Chốt
xong thì ghi `ClosedAt` và không tính lại nữa: từ đó nó là lịch sử, và lịch sử không được đổi
theo lần ingest sau.

Ngày lấy theo **UTC**, không theo múi giờ người xem: một bản ghi lịch sử phải có đúng một mốc,
còn phần hiển thị thì đổi sang giờ máy như mọi chỗ khác trên trang.

## Bảng `DailyDigest`

Theo đúng khuôn `TeamStatSnapshot`: upsert trong cùng ngày, **không bao giờ đè ngày khác**.

```
Id, LeagueId, Day (DateOnly, UTC), StageName
SeriesTotal, SeriesCompleted, MatchesCounted, MatchesExpected
MedianDurationSeconds
ClosedAt (null = ngày còn mở)
ComputedAt
Payload (JSON)
```

`Payload` là JSON chứ không phải cột riêng cho từng loại điểm nhấn, và đây là ngoại lệ có chủ ý
trong một dự án vốn khai kiểu chặt: danh sách điểm nhấn sẽ còn thêm bớt, mà mỗi lần thêm một
loại là một migration cộng một lần quét lại toàn bộ. Bù lại, phần TÍNH thì vẫn typed hoàn toàn —
JSON chỉ là lớp lưu.

## Các điểm nhấn bản đầu

1. **Tổng quan** — số series xong/tổng, số ván đọc được/ước tính, trung vị thời lượng.
2. **Ngược kèo** — series mà bên thắng có winrate 180 ngày (từ `TeamStatSnapshot`) THẤP hơn bên
   thua. Xếp theo độ chênh. Không dùng chữ "bất ngờ" khi chênh dưới 5 điểm.
3. **Bị cấm nhiều nhất** — từ `DraftEvents` (`IsPick = 0`) của các ván trong ngày.
4. **Được chọn nhiều nhất** — cùng nguồn, `IsPick = 1`, kèm số ván thắng.
5. **Ván dài nhất và ngắn nhất** — kèm tên hai đội.
6. **So với ngày trước** — chỉ hiện khi đã có digest ngày trước: hero mới vào nhóm cấm, hero
   rời khỏi nhóm cấm.

Mỗi điểm nhấn mang theo con số sinh ra nó. Không có câu nào không truy ngược được về ván cụ thể.

## Chỗ sẽ vỡ, và cách chặn

- **Chưa có ván nào được nạp.** Vẫn ra được mục 1, 2, 5 từ `ScheduledSeries`; mục 3, 4 nói rõ
  "chưa đọc được ván nào".
- **Ngày không có trận** (giữa hai vòng). Không tạo digest, không hiện mục rỗng.
- **Series bị dời sang ngày khác.** Ngày lấy theo `ActualAt` khi có, chỉ dùng `ScheduledAt` khi
  chưa đánh — vì hôm nay các trận đã trượt một tiếng so với giờ công bố.
- **Ngày đã chốt bị tính lại.** `ClosedAt` khác null thì bỏ qua hẳn.

## Bài kiểm

1. Ngày còn mở thì upsert, ngày đã chốt thì không đụng tới.
2. Digest của hai ngày khác nhau không đè nhau.
3. Chốt đúng lúc series cuối xong, không sớm hơn.
4. Không có ván nào nạp được thì vẫn ra digest, và cờ độ phủ nói đúng.
5. Series dời ngày được tính vào ngày ĐÁ THẬT, không phải ngày xếp lịch.
6. "Ngược kèo" không gắn nhãn khi chênh winrate dưới ngưỡng.
