# S5 Rush – Checkout payment guard (Core / Phụng)

## Đã đổi
- `ParkingSessionService.CheckOutAsync`: dưới lock lot + transaction, sau reload session, chỉ cho checkout khi `Status == PAID`.
  ACTIVE → từ chối, không đóng session, không hủy dispatch, không giải phóng slot, không ghi audit `CHECK_OUT_SESSION`.
- `AllowExitWithoutPayment` / `ExemptionReason` từ client **không** là bypass (chỉ còn ghi audit). Cần xóa field hoặc gắn contract duyệt sau.
- PAID chỉ được đặt bởi `RecordPaymentClearanceAsync(isTrustedPaymentServerFlow: true)`; endpoint hiện tại luôn truyền `false` ⇒ trả 503 `PAYMENT_INTEGRATION_BLOCKED`.

## BLOCKER demo (Nam Anh)
Chưa có đường server-to-server thật đặt PAID ⇒ Core hiện **không thể checkout ở môi trường chạy thật**. Cần chốt:
1. Cách Payment Service gọi Core (service credential/HMAC, không dùng JWT staff/admin; thay `[Authorize(Roles=Admin)]` + `isTrustedPaymentServerFlow:false`).
2. Payload: sessionId, amount, transactionRef (idempotent, unique), method, paidAt, signature. Core nên đối chiếu amount với phí server-side, không tin số client.
3. Ngoại lệ phí 0 (bãi miễn phí/ân hạn): chưa mở bypass. Đề xuất Payment Service phát clearance amount=0 với `transactionRef` riêng; cần nới rule `amountPaid <= 0` trong Core SAU khi contract được duyệt.
4. Trạng thái PAID hết hạn/ra trễ (re-fee) chưa định nghĩa.

## Test
- Mới (WalkInSessionAndStaffDutyTests): unpaid bị từ chối (kể cả flag exemption, Admin), paid thành công, checkout lặp không giải phóng slot lần 2 + chỉ 1 audit, sai bãi/sai duty bị từ chối và session vẫn PAID.
- Test race song song thật chưa có: fixture InMemory dùng chung 1 DbContext (không thread-safe); khóa dưới lock + reload giữ nguyên cơ chế cũ. Nên có test Postgres (ParkingOperationsPostgresTests) cho Trí/Phụng.
- Nền tảng test đã hỏng sẵn: 133 test fail trước thay đổi (fixture cũ; legacy `CheckInAsync` bị chặn ở bãi có zone: "Bãi đã có khu…", slot fixture thiếu `ZoneId`/`ParkingZone.FloorId`). Sau thay đổi: 132 fail, 0 test mới fail.
  Các test checkout Booking/walk-in cũ (BookingAndCapacity, PerLotAuthorization…) nằm trong nhóm fail sẵn ⇒ chưa xác minh được Booking checkout qua test.

## Sprint 4 – lỗ hổng capacity (rà nhanh)
- `Booking` không có `ZoneId`; `pendingBookingsCount` trong `ParkingLotCapacityHelper` đếm toàn bãi dù tính capacity theo zone ⇒ trừ quá tay ở mỗi zone.
- Booking hold không xét `StartTime/EndTime` (chưa có "khung giờ"): mọi PENDING đều giữ chỗ, kể cả booking tương lai/quá hạn nếu chưa EXPIRED.
- Walk-in `CheckInAsync` bị đóng ở bãi có zone ⇒ walk-in phải đi qua operations/entries; fixture/test cũ chưa cập nhật.

## reCAPTCHA
`Register.jsx` gửi `recaptchaToken` (v3, action `register`) khi có `VITE_RECAPTCHA_SITE_KEY`. Chưa cấu hình site key mà backend `Recaptcha.Enabled=true` ⇒ đăng ký bị 400 (đúng thiết kế). Dương/Trí cần set site key (public) ở frontend và SecretKey ở backend; CSP nếu có cần cho phép google.com/recaptcha.
