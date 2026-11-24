# S5 – Core ⇄ Payment: contract clearance (draft, Phụng → Nam Anh)

## Core đã có (chạy được, có test)
`POST /api/internal/payment-clearances` (Core, `InternalPaymentClearancesController`). Không dùng JWT.
Code nằm trên nhánh `phung/s5-rush` (PR vào `s5/demo`).

Headers:
- `X-Payment-Timestamp`: unix seconds (lệch tối đa ±5 phút).
- `X-Payment-Signature`: hex `HMAC-SHA256(secret, "{timestamp}.{rawBody}")` (Core so sánh không phân biệt hoa/thường).
- Ký trên **đúng chuỗi byte UTF-8 của body được gửi đi**. Không serialize lại JSON sau khi ký, nếu không Core trả 401.
- Secret: cấu hình Core `PaymentService:SharedSecret` (≥ 32 byte, qua env/User Secrets, KHÔNG commit). Chưa cấu hình ⇒ 503 `PAYMENT_INTEGRATION_NOT_CONFIGURED` (fail-closed).

Request body (JSON):
```json
{ "sessionId": 123, "amount": 35000, "transactionRef": "VNP-...", "paymentMethod": "VNPAY", "paidAt": "2026-11-20T03:00:00Z" }
```
| Trường | Bắt buộc | Ghi chú |
|---|---|---|
| `sessionId` | có | `long` ≥ 1 (mã phiên của Core) |
| `amount` | có | 0.01 – 100.000.000; ≤ 0 bị từ chối ngay ở bước kiểm body (`INVALID_BODY`) |
| `transactionRef` | có | `[A-Za-z0-9._:-]{1,100}` sau khi trim; duy nhất xuyên mọi session |
| `paymentMethod` | không | Chuỗi tự do, tối đa 50 ký tự; Core chỉ lưu vào audit, **không** kiểm danh sách. Khuyến nghị dùng `VNPAY` / `MOMO` / `CASH`. |
| `paidAt` | không | **Hiện Core bỏ qua**; `PaidAt` của session = giờ server Core lúc nhận clearance. |

Response/lỗi:
| HTTP | code | Ý nghĩa | Payment nên làm |
|---|---|---|---|
| 200 | – | Session → PAID (hoặc replay cùng `transactionRef` + cùng amount – idempotent, không audit thêm) | Dừng, thành công |
| 400 | INVALID_BODY | JSON sai, thiếu trường, amount ≤ 0 hoặc vượt giới hạn | Dừng, không retry; sửa dữ liệu |
| 400 | CLEARANCE_REJECTED | `transactionRef` sai định dạng; amount ≠ `TotalFee` Core đã tính (thiếu hoặc thừa); replay cùng ref nhưng khác amount; vé điện tử chưa được staff verify đã đỗ | Dừng, không retry; chuyển xử lý ngoại lệ |
| 401 | INVALID_SIGNATURE | thiếu/sai chữ ký hoặc timestamp lệch quá 5 phút | Retry có backoff, **ký lại với timestamp mới**; nếu vẫn 401 thì kiểm secret/đồng hồ |
| 404 | SESSION_NOT_FOUND | | Dừng, không retry |
| 409 | CLEARANCE_CONFLICT | session đã đóng/hủy, đã PAID bằng ref khác, hoặc ref đã dùng cho session khác | Dừng, không retry; đối soát |
| 5xx / lỗi mạng / timeout | – | gồm 503 `PAYMENT_INTEGRATION_NOT_CONFIGURED` | Retry có backoff với **cùng** `transactionRef`, ký lại mỗi lần |

Chống lặp/race: khóa clearance toàn cục + khóa bãi, transaction, đọc lại session dưới khóa; `transactionRef` so khớp tuyệt đối (không khớp tiền tố). Core KHÔNG ghi đè `TotalFee` đã tính bằng số tiền trả (audit `PAYMENT_CLEARED` lưu `AmountPaid`, `FeeDue`, `PaymentMethod`); chỉ khi `TotalFee`=0 (Core chưa có bảng giá) mới ghi amount vào `TotalFee`.

Core vẫn tự quyết định checkout: `CheckOutAsync` chỉ chạy khi `Status == PAID`; Payment không đóng session. Endpoint Admin cũ `/api/v1/parking-sessions/{id}/payment-clearance` luôn trả 503 với code `PAYMENT_INTEGRATION_BLOCKED` (khác với `PAYMENT_INTEGRATION_NOT_CONFIGURED` của endpoint nội bộ) – không có đường tự khai báo đã trả tiền.

Lưu ý triển khai: endpoint là `AllowAnonymous` + HMAC và hiện đi qua gateway (`/api/**`); cần chặn/giới hạn ở gateway để chỉ Payment Service gọi được (Trí).

## Không khớp thực tế Payment Service (BLOCKER – Nam Anh quyết định)
1. Payment hiện **không gọi Core**; `vnpay-ipn` chỉ cập nhật `Invoice`, thậm chí tự set `ParkingSessionBilling.CheckOutTime` (trái nguyên tắc "Payment không đóng session").
2. `SessionId` ở Payment là `Guid`, Core là `long`. Cần chốt: Payment lưu `CoreSessionId (long)` hay Core cấp mã tham chiếu.
3. `create-payment-url` nhận `amount` từ query (client). Phải tính server-side từ quote (Core cần `BillingStartAt/EndAt`, `QuoteId`, `AmountDue`).
4. Cần ký request bằng cùng secret và retry theo bảng trên (chỉ retry 401/5xx/lỗi mạng; 200/400/404/409 là kết quả cuối).
5. Phí 0 / `NO_PAYMENT_REQUIRED`: Core hiện từ chối amount ≤ 0. Cần quyết định có mở không (và tiêu chí) trước khi nới.
6. Phí Core: `TotalFee` tại thời điểm clearance có thể 0 (chưa tính) ⇒ Core chưa đối chiếu được amount với phí; cần Quote contract để Core kiểm amount == AmountDue.
7. Nếu Payment cần `PaidAt` theo thời điểm provider báo thành công, Core phải được sửa để đọc `paidAt` (hiện bỏ qua).

## Dữ kiện tính phí cho Payment (Core đã có)
`POST /api/internal/payment-billing-context`, ký **giống hệt** endpoint clearance (`X-Payment-Timestamp`, `X-Payment-Signature`, cùng `PaymentService:SharedSecret`). Chỉ đọc, không ghi audit.

Body: `{ "sessionId": 123 }`

Response 200 (thời gian UTC):
```json
{ "sessionId": 123, "lotId": 4, "zoneId": 11, "policyId": 7, "bookingId": null,
  "plateNumber": "51A12345", "status": "ACTIVE", "verificationStatus": "VERIFIED", "ticketed": true,
  "enteredAt": "2026-11-20T01:00:00Z", "verifiedAt": "2026-11-20T01:05:00Z", "checkedOutAt": null,
  "totalFee": 0, "paidAt": null, "generatedAt": "2026-11-20T03:00:00Z" }
```
| HTTP | code | Ý nghĩa |
|---|---|---|
| 200 | – | Dữ kiện của phiên |
| 400 | INVALID_BODY | thiếu hoặc sai `sessionId` |
| 401 | INVALID_SIGNATURE | sai chữ ký hoặc hết hạn |
| 404 | SESSION_NOT_FOUND | |
| 503 | PAYMENT_INTEGRATION_NOT_CONFIGURED | Core thiếu secret |

Payment dùng dữ kiện này để tự tính `AmountDue` phía server (giải quyết blocker 3). `sessionId` là `long` của Core (blocker 2: Payment cần lưu `CoreSessionId`). Core **chưa** chốt mốc bắt đầu/kết thúc tính phí nên chỉ trả dữ kiện gốc (`enteredAt`, `verifiedAt`, `generatedAt`). Nam Anh và Phụng cần chốt: bắt đầu tính từ `enteredAt` hay `verifiedAt`, kết thúc tại thời điểm báo giá hay khi staff xác nhận ra.
