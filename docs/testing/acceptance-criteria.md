# Acceptance criteria

**Trạng thái:** Baseline QA cho các quyết định hiện hành.
**Chủ sở hữu:** QA cùng owner từng module.
**Rà soát:** 29/10/2026.

## Nhận xe vãng lai

- OCR đúng tạo một session `WAITING_VERIFICATION`; slot chưa `OCCUPIED`.
- OCR sai được Staff tại slot sửa trong cùng session; giữ cả giá trị gốc và actor/time.
- OCR không đọc được đi vào `NEEDS_REVIEW`, không lấy biển số phiên trước.
- Retry cùng EventId/vé không tạo session thứ hai.
- Xe đã vào nhưng chưa xác minh slot vẫn giảm khả năng nhận xe.

## Xác minh slot

- Cảm biến báo vật thể không tự gắn xe hoặc đổi physical status.
- Chỉ Staff có duty/bãi đúng được xác minh actual slot.
- Hai người cùng xác nhận một slot: chỉ một kết quả thành công, kết quả còn lại xung đột và có log.
- Hàng việc đã xử lý đổi trạng thái, không biến mất khỏi lịch sử.

## Cổng ra và tiền

- Quẹt đúng vé/thẻ tải một session mở và ảnh/thông tin lúc vào.
- Thẻ mất/hỏng/sai xe vào nhánh xác minh, không tạo session mới.
- Phí lấy từ server và có snapshot bảng giá; sửa amount client không thay kết quả.
- Retry payment không ghi hai khoản.
- Thu đủ chưa làm trống slot; chỉ confirm xe thực sự qua cổng mới `COMPLETED`.

## Khu cố định và quyền xem

- Cảnh 3D hỗ trợ zone cố định bên trong và bên ngoài bãi gốc với đúng parent.
- Bảng IoT đúng slot hiển thị full biển số đăng ký còn hiệu lực.
- Guest API/Web không trả biển số hoặc ảnh xe.
- Customer không xem xe/quyền của Customer khác.
- Staff sai bãi hoặc sai duty bị chặn tại API.
- Đổi xe chỉ cập nhật bảng IoT sau khi yêu cầu được xác minh và có hiệu lực.

## Gói gửi cố định (Core slice hiện có)

- Catalog chỉ trả plan APPROVED đang trong khoảng hiệu lực; DRAFT/RETIRED không xuất hiện.
- Admin tạo plan version mới cho zone FIXED; giá lấy từ request Admin và lưu snapshot trên pass.
- Customer chỉ yêu cầu pass cho xe active của chính mình; không nhận owner, giá, trạng thái hoặc scope từ browser.
- Purchase cần `Idempotency-Key`; retry cùng key trả lại cùng pass, và unique DB indexes chặn trùng xe-zone/slot đang PENDING/SCHEDULED/ACTIVE.
- Purchase trả PENDING và không cấp quyền truy cập; cấu hình gốc tắt, Development bật để thử, production phải giữ tắt cho tới khi payment/policy được cấu hình.
- Customer chỉ đọc pass của mình; Admin suspension có lý do và audit.
- Payment clearance, renewal/change request, cards/barrier, expiry job và UI chưa có tiêu chí chạy đạt; không mở bán hoặc coi pass là entitlement hoàn chỉnh.

## Phục hồi

- Restart không mất session chờ, queue, event result hoặc khoản thu.
- Event cũ tới sau không ghi đè trạng thái mới.
- Hai thiết bị nối lại tải cùng snapshot server.
- Kiểm thử concurrency dùng PostgreSQL với hai kết nối, không chỉ EF InMemory.
