# Demo thanh toán VNPay Sandbox

Payment Service hiện dùng VNPay Sandbox. Không dùng thông tin cấu hình Sandbox cho môi trường thật.

## Chạy demo local

1. Chạy Core API tại `http://localhost:5295` và Payment Service tại `http://localhost:5183`.
2. Cấu hình cùng một `PaymentService:SharedSecret` cho Core API và Payment Service. Payment Service gọi Core tại `Core:BaseUrl` (mặc định `http://localhost:5295`).
3. Chạy tunnel công khai tới Payment Service, ví dụ:

   ```powershell
   cloudflared tunnel --url http://localhost:5183
   ```

4. Trong cấu hình terminal VNPay Sandbox, đặt **IPN URL** thành URL tunnel cộng đường dẫn `/api/billing/vnpay-ipn`. URL IPN cần truy cập được từ máy chủ VNPay. Giữ `VnPay:ReturnUrl` là URL browser quay về Payment Service; khi demo trên cùng máy có thể dùng `http://localhost:5183/api/billing/vnpay-return`.
5. Khách đặt chỗ với thanh toán trước. Core tạo booking rồi yêu cầu Payment Service tạo hóa đơn `Pending`, sau đó chuyển browser sang VNPay.
6. Hoàn tất giao dịch trên trang VNPay. Dùng thẻ test trong [trang demo VNPay](https://sandbox.vnpayment.vn/apis/vnpay-demo/). Thẻ NCB thành công dùng tên `NGUYEN VAN A`, ngày phát hành `07/15`, OTP `123456`.

## Kết quả mong đợi

- Thanh toán thành công: `Invoices.Status = Paid`, có một `PaymentTransactions` trạng thái `Success`, booking trong Core thành `PAID`, và trang khách quay về `bookingPayment=success`.
- Thanh toán thất bại hoặc khách hủy: hóa đơn được đánh dấu `Failed` hoặc `Cancelled`, giao dịch lỗi được ghi lại, booking chưa được xác nhận đã thanh toán.
- IPN và browser return đều kiểm chữ ký và số tiền. IPN thành công có thể gửi lại an toàn; Core và Payment Service kiểm tra giao dịch đã xử lý để tránh ghi nhận lặp.

## Cấu hình môi trường

- `VnPay:Environment` phải là `Sandbox`.
- `VnPay:BaseUrl` phải trỏ tới `https://sandbox.vnpayment.vn/paymentv2/vpcpay.html`.
- Giữ `TmnCode`, `HashSecret` và khóa Core–Payment trong User Secrets hoặc biến môi trường; không đưa khóa thật vào Git.
- `ReturnUrl` là nơi trình duyệt khách quay về; **IPN URL** là endpoint server-to-server cấu hình trong terminal VNPay. Hai URL phục vụ hai việc khác nhau.
