# S5 – Báo cáo phần Phụng (Core: Entry → Verify/Parked → Paid → Checkout)
Branch `phung/s5-rush`, chưa commit. 18/11/2026. Đối chiếu từ SOURCE, không từ kế hoạch.

## 1. Checklist một lượt vãng lai (theo code)
| # | Transition | API thật | Guard / chống lặp | Trạng thái |
|---|---|---|---|---|
| 1 | ENTRY evidence / AI-confirm | `POST /api/v1/parking-lots/{lotId}/operations/entries` (`ParkingOperationsController.cs:42`) | `EntryRequest` chỉ có `ImageId` (phải thuộc actor) + `ManualReason`; **không có `RecognitionId`/CaptureId** (`ParkingOperationsService.cs:187` hash cố định `RecognitionId=null`) | ⚠ Liên kết AI↔Entry chưa có (Đức/Song) |
| 2 | Ticket + session pending | cùng API | Duty GATE_IN + zone (`:198`), zone TRANSIENT active (`:199`), `TICKET_IN_USE`/`TICKET_USED` (`:200-202`), capacity theo khu (`:203`), policy duyệt (`:206`), replay `EventId` ⇒ cùng session / `EVENT_MISMATCH` (`:190-195`), unique `EntryEventId` + partial unique vé | ✅ test |
| 3 | Chưa chiếm ô khi mới entry | – | Enter không đụng slot | ✅ test |
| 4 | Verify “đã đỗ” | `POST …/operations/sessions/{id}/verify` (`ParkingOperationsController.cs:50`) | Duty SLOT_VERIFIER + tầng/khu (`ParkingOperationsService.cs:305`), `ExpectedRevision` (`:341`), chỉ ACTIVE+WAITING_VERIFICATION (`:297`), `PLATE_CONFLICT` (`:303`), slot cùng khu/trống/fuel/`SLOT_CONFLICT` (`:348-357`), lock bãi trong `ParkingOperationsWriter` (semaphore + `pg_advisory_xact_lock`), unique `CurrentSlotId` | ✅ test (song song) |
| 5 | Exit plate/ticket/photo | `POST /api/v1/parking-sessions/{id}/check-out` (`ParkingSessionsController.cs:133`) | GATE_OUT + khu (`ParkingSessionService.cs:341-347`); vé điện tử phải VERIFIED + biển khớp + vé ISSUED khớp (`:422-428`); `ExitRecognitionId` nếu gửi phải EXIT/đúng bãi/đúng staff/đúng session/đã confirm (`:348-361`). Ảnh exit **tùy chọn** | ✅ (Q3) |
| 6 | Signed clearance | `POST /api/internal/payment-clearances` (HMAC, không JWT) | Xem §2 | ✅ harden |
| 7 | Check-out | như #5 | Chỉ khi `Status==PAID`, kiểm lại dưới lock sau reload (`:414`); đóng session, thu hồi vé, hủy dispatch mở, release slot một lần (`:420-468`); lặp ⇒ từ chối (`:405`) | ✅ |
| 8 | Slot free | – | Slot về EMPTY chỉ trong checkout hợp lệ; thu tiền không giải phóng ô | ✅ test |
| 9 | Booking path | check-in / `ConfirmBookingParkedAsync` (không `EntryEventId`) | Không qua nhánh vé; vẫn cần PAID trước checkout | ✅ test seed trực tiếp; luồng `ConfirmBookingParkedAsync` chưa test được vì test cũ hỏng fixture |

## 2. Hardening `RecordPaymentClearanceAsync` (`ParkingSessionService.cs` ~1490-1650)
- **P0 race**: trước không transaction/lock ⇒ hai ref/hai request đồng thời cùng thắng. Nay: `ClearanceGate` toàn cục (ref unique xuyên bãi; DB chưa có unique constraint, chưa duyệt migration) + khóa bãi (chung với checkout/verify) + transaction + `ReloadAsync` dưới khóa; Postgres thêm 2 `pg_advisory_xact_lock` theo thứ tự cố định (không deadlock với checkout).
- **P0 partial-ref**: `NewValues.Contains(ref)` khiến `TX-1` khớp `TX-10`. Nay prefilter + parse JSON, so khớp `TransactionRef` ordinal; ref giới hạn `[A-Za-z0-9._:-]{1,100}`.
- **Replay / cross-session**: ref dùng cho session khác ⇒ 409; replay cùng ref khác amount ⇒ từ chối; replay đúng ⇒ 200 idempotent, 1 audit.
- **P1 ghi đè phí**: không còn `TotalFee = amountPaid` khi Core đã có phí; amount phải **bằng** phí (thiếu và thừa đều từ chối). `TotalFee==0` (Core chưa có bảng giá – không bịa giá) mới ghi amount để doanh thu không mất. Audit lưu `AmountPaid`, `FeeDue`.
- **P1 kẹt phiên**: clearance cho vé điện tử chưa VERIFIED bị từ chối (Verify chỉ nhận ACTIVE ⇒ PAID sớm làm session không verify được nữa).
- Giữ nguyên: endpoint Admin cũ fail-closed; amount ≤ 0 từ chối (chưa duyệt ngoại lệ phí 0).
- Giới hạn: ref unique hiện bảo vệ bằng khóa ứng dụng + audit log, chưa phải unique index (cần migration được duyệt, nhiều instance Core cần advisory lock Postgres – đã có).

## 3. Capacity S4
- Zone-scoped không trừ toàn bộ PENDING booking của bãi; `min(zone, lot-wide)` chống oversell (`ParkingLotCapacityHelper.cs:100-135`).
- Test: zero slots ⇒ 0 (không fallback `TotalCapacity`); MAINTENANCE bị trừ; sensor OCCUPIED chưa khớp session ⇒ giữ chỗ; FIXED zone không bán vãng lai; PENDING→CHECKED_IN không đếm kép; Entry hết chỗ ⇒ `CAPACITY_EXHAUSTED`.
- **Còn mở, không tự sửa**: `Booking` không có `ZoneId`, không dùng `StartTime/EndTime` để giữ chỗ ⇒ hold tính mức bãi, mọi PENDING đều giữ chỗ. Cần quyết định/migration (Dương/Trí). Race booking hold vs Enter dùng cùng khóa bãi (`BookingService.cs:159,264,408`); test song song thật chỉ có thể ở Postgres.

## 3b. Mâu thuẫn / lưu ý tài liệu
- `operations-service/README.md` cũ kế hoạch chuyển gate/dispatch; Sprint 5 (§3.1, §6.3) giữ Core là owner Session/Slot/Gate, Operations chỉ Shift/Handover ⇒ theo Sprint 5.
- Sprint 5 §6.2 gán Phụng luồng UI Staff Entry → Dispatch → Parked → Checkout; Song sở hữu giao diện chung/mobile. Nhận định cũ “không làm UI” là SAI và đã được sửa: không thêm màn hình mới, nhưng đã audit và sửa phần UI của slice Phụng trên UI Staff hiện có (xem §8).
- Endpoint clearance `AllowAnonymous` + HMAC, YARP route `/api/**` về Core nên endpoint lộ qua gateway ⇒ cân nhắc chặn/giới hạn ở gateway (Trí).

## 4. Test
- Mới: `S5CoreFlowTests.cs` (16) + `S5PaymentClearanceAndCapacityTests.cs` (10) — **26/26 pass**. Mutation check: bỏ guard PAID và exact-ref ⇒ 5 test đỏ.
- Phủ: Entry→Verify→PAID (qua controller HMAC)→Checkout→slot free + tái dùng ô; unpaid (kể cả `AllowExitWithoutPayment` + Admin); sai biển/vé/thiếu vé; staff bãi khác; chỉ SLOT_VERIFIER; checkout lặp & song song (1 audit, release một lần); hai xe một ô song song; verify trùng; vé/event replay; clearance replay/mismatch/partial-ref/cross-session/song song; booking path.
- Song song dùng nhiều `DbContext` trên cùng store InMemory (khóa semaphore trong process). `pg_advisory_xact_lock` và unique index chỉ kiểm bằng `ParkingOperationsPostgresTests` (cần `PARKING_TEST_POSTGRES`, **chưa có ⇒ không chạy**).
- Full suite: 131 fail / 360 pass / 3 skip (trước đó 132 fail). Lỗi còn lại do fixture cũ (`ParkingZone.FloorId unknown`, NullReference, legacy `CheckInAsync`), không do thay đổi này; không sa đà sửa.

## 5. Cách chạy
```
cd backend
dotnet build tests/SmartParking.Tests
dotnet test tests/SmartParking.Tests --filter "FullyQualifiedName~S5CoreFlow|FullyQualifiedName~S5PaymentClearance"
# Core là process riêng; secret qua env/User Secrets, KHÔNG commit:
$env:PaymentService__SharedSecret = "<>=32 byte>"   # thiếu ⇒ endpoint clearance trả 503 (fail-closed)
dotnet run --project src/SmartParking.Api
```
Không chạy migration, không ghi Neon dùng chung. Không tách DB, không chuyển Gate/Dispatch/Exit sang Operations, không sửa payment-service/operations-service.

## 6. Blocker ngoài scope
- **Nam Anh**: Payment chưa gọi Core; `SessionId` Guid vs Core long (lưu `CoreSessionId`); `vnpay-ipn` tự set `CheckOutTime` (trái “Payment không đóng session”); `amount` lấy từ client query; cần ký HMAC + retry tới 200/409; quote (`AmountDue`, `BillingStartAt/EndAt`) để Core đối chiếu (Core chưa tính phí, `TotalFee`=0 ở luồng vé điện tử).
- **Trí**: `PaymentService:SharedSecret` ở môi trường chạy; DB Postgres test riêng; cấu hình gateway cho endpoint nội bộ.
- **Dương**: Shift/Handover (Operations); `Booking.ZoneId`/khung giờ nếu quyết định làm.
- **Đức/Song**: `RecognitionId/CaptureId` trong `EntryRequest` (TicketEntry vẫn không gắn AI lúc vào); giao diện chung/mobile.

## 7. Câu hỏi nghiệp vụ (giữ fail-closed tới khi có trả lời)
1. Phí 0 / `NO_PAYMENT_REQUIRED` (ân hạn, miễn phí): có cho Core nhận clearance amount=0 không, tiêu chí?
2. Ai tính phí: Core (`TotalFee`) hay Payment quote? Nếu Payment, Core đối chiếu `AmountDue` theo contract nào?
3. Ảnh exit có bắt buộc để checkout không? Hiện tùy chọn.
4. PAID hết hạn (ra trễ sau khi thanh toán) xử lý thế nào (re-fee)?
5. Cho Admin/Manager ghi thu tiền mặt thay Payment không? Hiện không.

## 8. UI Staff (slice Phụng) – audit & sửa
Không viết lại UI của Song, không thêm màn hình. Chỉ sửa component Staff hiện có.

| Gap | Bằng chứng source | Sửa |
|---|---|---|
| P0 Sau `confirm-parked`, `StaffWorkspace` `open("exit")`. SLOT_VERIFIER không có view `exit` (`staffDuty.js` `canExit`) ⇒ bị đẩy về tab đầu, mất phản hồi; GATE/BOTH thấy như xe sắp ra | `StaffWorkspace.jsx` onConfirmed | Ở lại “Xe đặt qua web”, báo trạng thái thật từ server (ACTIVE “Đang gửi”), nói rõ chưa PAID, nút “Xem trong Xe đang gửi” (cùng duty area) |
| P1 Checkout vé điện tử tắt AI EXIT, luôn gửi `exitRecognitionId=null` dù backend có contract (`/staff/detect?purpose=EXIT` → `confirm-exit` gắn đúng session → `CheckOutAsync` kiểm `ConfirmedAt`, biển, staff, session) | `StaffExit.jsx` | Bật AI cho vé điện tử; chỉ gửi `exitRecognitionId` khi staff đã chạy detect và confirm-exit thành công; nhập tay ⇒ không gửi id. Vẫn bắt buộc `ticketCode` + biển khớp + PAID |
| P1 Bug: `confirm-exit` trả `ConfirmedPlateNumber = session.PlateNumber` (`StaffDetectionService.cs:~203`) nhưng UI so chuỗi thô với biển staff nhập ⇒ `51F12345` vs `51F-123.45` báo “Chưa xác nhận được biển số nhận diện” | `PlateCapture.jsx prepareCapture` | So khớp bằng `platesMatch` (bỏ dấu cách/chấm/gạch) giống `PlateNumberHelper.IsMatch` |
| P1 ACTIVE chỉ báo chung “chức năng thu phí chưa sẵn sàng”, không phân biệt chưa verify/chưa PAID, không có cách đọc lại | `StaffExit.jsx` | Lý do riêng: vé điện tử chưa VERIFIED ⇒ “Chưa xác minh xe tại ô”; còn lại ⇒ “Chưa xác nhận thanh toán” (ACTIVE, Core chưa nhận clearance, UI không thu/không tự đặt PAID). Nút “Kiểm tra lại trạng thái” gọi lại session từ Core, không đổi dữ liệu |
| P1 Entry bị khóa sau lỗi 4xx rõ ràng (TICKET_IN_USE…), chỉ thoát bằng reset mất ảnh/lý do | `TicketOperations.jsx` | 4xx (trừ 408/429) ⇒ mở khóa, giữ dữ liệu; lỗi mạng/5xx giữ nguyên `eventId` để gửi lại cùng lượt |
| P1 `confirm-parked` lỗi/mất phản hồi ⇒ danh sách ô/booking cũ | `StaffBookings.jsx` | Lỗi ⇒ tải lại ô, booking, capacity; lỗi hiện cả khi booking đã biến mất |

Đã đúng sẵn (có test, không đổi): đổi bãi xóa nháp/ảnh (view key theo lot), gửi lặp bị chặn (`useStaffAction` + `eventId`), upload ảnh lỗi không tạo lượt, đổi biển số nhập tay không gửi AI.
Test UI mới/sửa: `StaffWorkspace.ui.test.jsx` (+7 test, sửa 3 test cũ; 2 test cũ vốn hỏng vì tìm role `tab` trong khi nav là link ở Header).

### Blocker còn lại phía UI
- Checkout trên UI chỉ chạy được khi Core có PAID; PAID chỉ đến từ Payment clearance (Nam Anh chưa gọi Core) ⇒ demo đầy đủ tới “xe ra” cần clearance HMAC thật; không có bypass.
- TicketEntry chưa gắn AI lúc vào (`EntryRequest` thiếu RecognitionId).
- `StaffExit` mở từ danh sách không có `ticketCode` (DTO phiên không trả) ⇒ staff nhập mã vé theo vé thực tế.
- Chưa kiểm thử trên trình duyệt/ mobile thật và với backend thật (chỉ vitest mock fetch).

## 9. Rà soát lại check-in / check-out (20/11/2026)
- **Check-in biển số kiểu cũ** (`POST /api/v1/parking-sessions/check-in`, `CheckInAsync`): sau commit `0fff930` mọi ô bắt buộc thuộc một khu, nên endpoint này không còn đường hợp lệ (bãi có khu ⇒ bị chặn; bãi không khu ⇒ sức chứa 0 và báo nhầm "hết chỗ"). Nay trả **410 `LEGACY_CHECKIN_RETIRED`** với thông báo chỉ đường sang `operations/entries`; vẫn kiểm bãi tồn tại/đang mở và quyền GATE trước. UI Staff không gọi endpoint này.
- **Check-out**: bắt buộc `confirmedPlateNumber` cho MỌI lượt (trước đây lượt không vé có thể ra mà không đối chiếu biển số). UI Staff vốn đã gửi trường này.
- **Test**: `WalkInSessionAndStaffDutyTests` dựng bãi đúng schema (khu TRANSIENT) và tạo lượt trực tiếp thay vì qua check-in cũ ⇒ toàn bộ file pass. Bỏ 6 test chỉ kiểm hành vi của check-in cũ (gán ô lúc check-in, anti-passback lúc check-in, check-in lại sau phiên cũ); hành vi tương ứng đã có ở luồng vé (`EligibleSlot` SLOT_CONFLICT/SLOT_UNAVAILABLE, `PLATE_CONFLICT` khi verify) và được phủ trong `S5CoreFlowTests`.
- Full suite: 107 fail / 378 pass / 3 skip (trước: 131 fail). 0 test hỏng mới. Còn `CheckIn_WalkIn_RejectedWhenRemainingCapacityIsReserved`, `M07_3_Concurrency_CheckOutReleasesCapacity…` (BookingAndCapacityTests) đỏ từ trước vì dựa vào check-in cũ – cần viết lại theo `operations/entries`.

## 10. Ranh giới microservice của Core (20/11/2026)
Yêu cầu của mentor: chia hệ thống thành microservice. Core vẫn là một service lớn (kế hoạch S5 §15 để việc tách nhỏ Core sau 29/11), nhưng phần của Phụng giờ chỉ giao tiếp với service khác qua API. Chi tiết hợp đồng: `S5_CORE_SERVICE_CONTRACTS.md`.
- **Quyền STAFF theo ca trực (theo Dương):** `ParkingAuthorizationService` có nguồn quyền `IStaffShiftDutyClient`; bật bằng `StaffDuty:Source = OperationsShifts`, mặc định `CoreAssignments` để demo không đổi. Core gọi `POST /api/internal/shift-duty/check` của Operations (HMAC), lỗi thì từ chối. **Chờ Dương** làm endpoint và sửa vòng khóa ở `ParkingShiftService.cs:70`.
- **Bỏ đọc chéo DB nhận diện khi check-out:** `CheckOutAsync` dùng `IExitRecognitionLookup`. Có `RecognitionService:BaseUrl` thì gọi `GET /internal/recognition/{id}` (đã thêm vào Recognition API); không có thì tạm đọc DB như cũ. Recognition API không trả lời thì từ chối cho ra kèm kết quả AI.
- **Dữ kiện tính phí cho Payment:** `POST /api/internal/payment-billing-context` (HMAC giống clearance), chỉ đọc.
- **Test:** `S5ServiceBoundaryTests` (10 test): chữ ký HMAC, fail-closed với mọi lỗi HTTP/timeout/JSON, công tắc nguồn quyền, cấu hình sai thì dừng khởi động, check-out kèm kết quả AI, billing context. Viết lại 2 test sức chứa theo luồng vé; `SeedLotWithSlots` tạo khu TRANSIENT đúng schema. Cấp chuỗi kết nối giả cho DB nhận diện trong môi trường test.
- **Full suite sau khi merge `s5/demo` vào máy:** 96 fail / 400 pass / 3 skip. 5 test đỏ mới so với nhánh cũ đều do `s5/demo` gỡ API phân công (`/candidates`, `/locations`), thuộc phần Dương đang sửa.
- **Còn mở (không thuộc Phụng):** Đức – `StaffDetectionService` vẫn dùng DB nhận diện, và Core vẫn bắt buộc `AiRecognitionConnection`; Trí – chặn `/api/internal/**` ở gateway; Nam Anh – Payment gọi billing context và clearance, lưu `CoreSessionId` kiểu long; Phụng + Nam Anh – chốt mốc tính phí.
