# S5 – Hợp đồng giữa Core và các service khác (Phụng)

Nguyên tắc: mỗi service chỉ ghi dữ liệu của mình. Core không đọc hay ghi DB của service khác; mọi trao đổi đi qua API nội bộ có xác thực. Khi gọi sang service khác bị lỗi, hết thời gian chờ hoặc nhận phản hồi sai, Core **từ chối** thao tác (fail-closed).

| Hợp đồng | Chiều gọi | Xác thực | Trạng thái |
|---|---|---|---|
| Ca trực (quyền STAFF) | Core → Operations | HMAC `X-Ops-Timestamp` / `X-Ops-Signature` | Core xong, có công tắc. **Chờ Dương** làm endpoint |
| Tra cứu nhận diện lúc xe ra | Core → Recognition | `X-Recognition-Service-Key` | Core xong; endpoint `GET` đã thêm vào Recognition API, **cần Đức xác nhận** |
| Clearance thanh toán | Payment → Core | HMAC `X-Payment-*` | Core xong (xem `S5_PAYMENT_CLEARANCE_CONTRACT.md`) |
| Dữ kiện tính phí | Payment → Core | HMAC `X-Payment-*` | Core xong (xem `S5_PAYMENT_CLEARANCE_CONTRACT.md`) |

## 1. Core → Operations: kiểm tra ca trực

Từ quyết định của Dương: quyền thao tác của STAFF lấy theo **ca trực** của Operations, không theo bảng phân công cũ của Core.

**Công tắc ở Core** (`appsettings`):

```json
"StaffDuty": { "Source": "CoreAssignments" },
"OperationsApi": { "BaseUrl": "http://localhost:5174/", "SharedSecret": "<>= 32 byte, cấu hình qua env>" }
```

- `CoreAssignments` (mặc định): dùng bảng `staff_lot_assignments` như cũ. Demo không đổi.
- `OperationsShifts`: với tài khoản STAFF, khi vào cổng (`GATE_IN`), ra cổng (`GATE_OUT`) hoặc xác minh ô (`SLOT_VERIFIER`), Core hỏi Operations. Admin không đổi. Manager vẫn tính theo phân công cũ.
- Bật `OperationsShifts` mà thiếu secret (< 32 byte) hoặc đặt giá trị lạ thì Core **không khởi động**.

**Endpoint Operations cần có** (việc của Dương):

`POST /api/internal/shift-duty/check`

Headers:
- `X-Ops-Timestamp`: unix seconds.
- `X-Ops-Signature`: hex lowercase `HMAC-SHA256(secret, "{timestamp}.{rawBody}")`, cùng secret với `OperationsApi:SharedSecret` của Core. Nên từ chối nếu timestamp lệch quá 5 phút.

Body:
```json
{ "staffId": 12, "lotId": 4, "duty": "GATE_IN", "at": "2026-11-20T12:00:00Z" }
```
`duty` ∈ `GATE_IN`, `GATE_OUT`, `GATE` (vào hoặc ra), `SLOT_VERIFIER`.

Response 200:
```json
{ "allowed": true, "shiftId": "…", "reason": null }
```
`reason` gợi ý khi `allowed=false`: `NO_SHIFT`, `WRONG_DUTY`, `SHIFT_NOT_STARTED`.

Core coi là **không có quyền** khi: HTTP khác 2xx, sai chữ ký, timeout 3 giây, JSON sai, hoặc `allowed` khác `true`.

**Cần Dương chốt:**
1. Chỉ ca `IN_PROGRESS` mới tính, hay chỉ cần `at` nằm trong khung giờ của ca?
2. `DutyCode` hiện có `GATE` / `SLOT_VERIFIER` / `BOTH`: `GATE` cho cả vào lẫn ra, hay tách `GATE_IN` / `GATE_OUT`?
3. Ca chưa có tầng/khu, nên khi dùng ca, Core tính quyền trên **toàn bãi** (bỏ kiểm tầng/khu). Có cần thêm tầng/khu vào ca không?
4. Danh sách bãi của staff (`GET /api/v1/staff-assignments/my-lots`) vẫn đọc phân công cũ. Khi bỏ hẳn phân công, cần một nguồn khác cho "bãi của tôi".

## 2. Core → Recognition: tra cứu kết quả nhận diện lúc xe ra

Trước đây `CheckOutAsync` đọc thẳng DB nhận diện (`AiRecognitionDbContext`), vi phạm nguyên tắc mỗi service một DB. Nay Core gọi:

`GET /internal/recognition/{id}` (Recognition API, header `X-Recognition-Service-Key`)

Response 200:
```json
{ "id": 9, "purpose": "EXIT", "parkingLotId": 4, "staffUserId": 12,
  "matchedParkingSessionId": 500, "confirmedPlateNumber": "51A12345", "confirmedAt": "…" }
```
404 nghĩa là không có bản ghi.

Core kiểm: `purpose = EXIT`, đúng bãi, đúng staff, đúng phiên, biển số đã xác nhận khớp và có `confirmedAt`. Recognition API không trả lời được thì Core **từ chối** cho ra kèm kết quả AI. Staff vẫn có thể cho xe ra không kèm kết quả AI.

**Cấu hình Core:**
```json
"RecognitionService": { "BaseUrl": "http://localhost:5002/", "InternalKey": "<cùng Recognition:InternalKey, >= 32 byte>" }
```
- Có `BaseUrl`: dùng HTTP. Thiếu khóa thì Core không khởi động.
- `BaseUrl` rỗng: **tạm thời** đọc DB nhận diện như cũ (`SharedDatabaseExitRecognitionLookup`) để demo vẫn chạy khi chưa triển khai Recognition API. Khi đã triển khai thì đặt `BaseUrl` để bỏ hẳn việc đọc chéo DB.

**Còn lại cho Đức:** `StaffDetectionService` trong Core vẫn đọc/ghi DB nhận diện, và Core vẫn bắt buộc `ConnectionStrings:AiRecognitionConnection` khi khởi động. Muốn Core độc lập hoàn toàn thì các API `staff/detect`, `confirm`, `confirm-exit` của Core nên chuyển sang gọi Recognition API (`RecognitionServiceClient` đã có sẵn).

## 3. Gateway (Trí)

Các endpoint `/api/internal/**` của Core là server-to-server. YARP đang chuyển tiếp `/api/**` nên chúng lộ ra ngoài (vẫn có chữ ký bảo vệ). Đề nghị chặn `/api/internal/**` ở gateway.
