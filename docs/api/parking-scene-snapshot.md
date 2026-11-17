# Parking scene snapshot contract

**Trạng thái:** Hiện hành từ Sprint 5.
**Owner dữ liệu:** Core API. Frontend 3D chỉ đọc projection này và không truy cập PostgreSQL/Neon trực tiếp.

## Endpoint

`GET /api/v1/parking-lots/{lotId}/scene`

Endpoint cho phép anonymous, nhưng projection phụ thuộc account và quyền bãi hiện hành:

- Guest chỉ thấy trạng thái/capacity công khai, không thấy biển số hoặc `sessionId`.
- Customer chỉ thấy biển số và session của chính mình.
- Admin/Manager được phân công thấy dữ liệu nội bộ trong phạm vi bãi.
- Staff chỉ thấy biển số/session trong zone được phép xác minh.

Response không được cache (`Cache-Control: no-store`).

## Snapshot envelope

| Trường | Ý nghĩa |
|---|---|
| `id` | `lotId` authoritative; client phải khớp với bãi đang chọn |
| `snapshotVersion` | Số tăng đơn điệu theo thứ tự Core bắt đầu tạo snapshot |
| `generatedAtUtc` | Thời điểm UTC Core bắt đầu tạo snapshot |
| `capacity` | Sức chứa authoritative do Core tính |
| `canManage`, `canEnter`, `canExit`, `canVerify` | Capability server-side; UI không tự suy từ role |
| `floors[]` | Danh sách tầng và capability theo tầng |
| `floors[].zones[]` | Một tầng có thể có nhiều khu |
| `floors[].slots[]` | Slot thuộc đúng `zoneId`; `zoneName` chỉ là nhãn hiển thị |

Slot projection giữ ID ổn định (`id`, `floorId`, `zoneId`) và có `status`,
`physicalStatus`, `isAvailable`, `isProtected`, tọa độ `positionX/Y/Z`. `plate` và
`sessionId` có thể là `null` theo quyền.

## Quy tắc chống dữ liệu stale

1. Mỗi request phía client có `requestId` tăng dần và source key gồm identity + lot.
2. Chỉ response của request đang active và đúng `lotId` mới được xét nhận.
3. Response có `snapshotVersion` nhỏ hơn snapshot đang hiển thị bị bỏ qua.
4. Khi refresh lỗi, giữ snapshot tốt cuối cùng, đánh dấu stale và khóa thao tác ghi.
5. Khi đổi bãi hoặc identity, xóa snapshot cũ; response của source trước bị bỏ qua.
6. Sau mutation thành công, client tải lại full snapshot authoritative từ Core.

`generatedAtUtc` phục vụ hiển thị/quan sát; client không dùng đồng hồ máy người dùng
làm căn cứ duy nhất để sắp thứ tự snapshot.

`snapshotVersion` hiện được tạo trong một Core process. Trước khi chạy nhiều Core
replica active-active, nhóm phải chuyển version sang nguồn dùng chung (database
revision/event sequence) hoặc bảo đảm sticky/single-writer cho endpoint này.
