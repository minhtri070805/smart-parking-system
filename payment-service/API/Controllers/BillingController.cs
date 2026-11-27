using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Domain.Enums;
using Infrastructure.Persistence;
using API.Security;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace API.Controllers
{
    [ApiController]
    [Route("api/billing")]
    public class BillingController : ControllerBase
    {
        private readonly IBillingEngine _billingEngine;
        private readonly IVnPayService _vnPayService;

        private readonly PaymentDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<BillingController> _logger;

        public BillingController(IBillingEngine billingEngine, IVnPayService vnPayService, PaymentDbContext context,
            IHttpClientFactory httpClientFactory, ILogger<BillingController> logger)
        {
            _billingEngine = billingEngine;
            _vnPayService = vnPayService;
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        [HttpPost("internal/booking-payment-url")]
        public async Task<IActionResult> CreateBookingPaymentUrl(CancellationToken ct)
        {
            using var reader = new StreamReader(Request.Body);
            var raw = await reader.ReadToEndAsync(ct);
            var secret = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["PaymentService:SharedSecret"];
            if (!PaymentRequestSignature.Verify(secret, Request.Headers["X-Payment-Timestamp"],
                    Request.Headers["X-Payment-Signature"], raw))
                return Unauthorized(new { message = "Chữ ký Core không hợp lệ." });

            BookingPaymentRequest? request;
            try { request = JsonSerializer.Deserialize<BookingPaymentRequest>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (JsonException) { return BadRequest(new { message = "Body không hợp lệ." }); }
            if (request == null || request.BookingId <= 0 || request.Amount <= 0 || request.Amount > 100_000_000 ||
                string.IsNullOrWhiteSpace(request.PlateNumber))
                return BadRequest(new { message = "Thông tin thanh toán đặt chỗ không hợp lệ." });

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(), BookingId = request.BookingId, Purpose = "BOOKING_DEPOSIT",
                TotalAmount = request.Amount, GrossAmount = request.Amount, Status = "Pending", CreatedAt = DateTime.UtcNow,
                SessionId = null, Session = null, Transactions = new List<PaymentTransaction>()
            };
            string url;
            try
            {
                url = _vnPayService.CreatePaymentUrl(HttpContext, invoice.Id.ToString(), request.BookingId.ToString(),
                    request.Amount, $"Dat coc booking #{request.BookingId} {request.PlateNumber}");
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
            }
            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync(ct);
            return Ok(new { paymentUrl = url, invoiceId = invoice.Id });
        }

        [HttpPost("internal/counter-payment")]
        public async Task<IActionResult> RecordCounterPayment(CancellationToken ct)
        {
            using var reader = new StreamReader(Request.Body);
            var raw = await reader.ReadToEndAsync(ct);
            var config = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var secret = config["PaymentService:SharedSecret"];
            if (!PaymentRequestSignature.Verify(secret, Request.Headers["X-Payment-Timestamp"],
                    Request.Headers["X-Payment-Signature"], raw))
                return Unauthorized(new { message = "Chữ ký Core không hợp lệ." });

            CounterPaymentRequest? request;
            try { request = JsonSerializer.Deserialize<CounterPaymentRequest>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (JsonException) { return BadRequest(new { message = "Body không hợp lệ." }); }
            if (request == null || request.SessionId <= 0 || string.IsNullOrWhiteSpace(request.PlateNumber) ||
                request.PrepaidAmount < 0 || request.PrepaidAmount > 100_000_000 || request.CheckInTime >= DateTime.UtcNow)
                return BadRequest(new { message = "Thông tin thanh toán tại quầy không hợp lệ." });

            var invoice = await _context.Invoices.Include(i => i.Transactions)
                .FirstOrDefaultAsync(i => i.CoreSessionId == request.SessionId && i.Purpose == "COUNTER_CHECKOUT");
            var now = DateTime.UtcNow;
            decimal totalFee;
            decimal amountDue;
            if (invoice == null)
            {
                // Core gửi giờ UTC; bảng giá theo giờ Việt Nam nên phải đổi múi giờ trước khi tính.
                totalFee = CounterFeeCalculator.Calculate(_billingEngine,
                    request.PlateNumber.Trim().ToUpperInvariant(), request.CheckInTime, now);
                amountDue = Math.Max(0m, totalFee - request.PrepaidAmount);
                invoice = new Invoice
                {
                    Id = Guid.NewGuid(), CoreSessionId = request.SessionId, BookingId = request.BookingId,
                    Purpose = "COUNTER_CHECKOUT", TotalAmount = amountDue, Status = "Paid", CreatedAt = now, PaidAt = now,
                    GrossAmount = totalFee,
                    SessionId = null, Session = null, Transactions = new List<PaymentTransaction>()
                };
                var transactionRef = $"CASH-{invoice.Id:N}";
                invoice.Transactions.Add(new PaymentTransaction
                {
                    Id = Guid.NewGuid(), InvoiceId = invoice.Id, Invoice = invoice, Amount = amountDue,
                    PaymentMethod = "CASH", TransactionNo = transactionRef, ResponseCode = "00",
                    Status = "Success", CreatedAt = now
                });
                _context.Invoices.Add(invoice);
                await _context.SaveChangesAsync(ct);
            }
            else
            {
                totalFee = invoice.GrossAmount ?? invoice.TotalAmount + request.PrepaidAmount;
                amountDue = invoice.TotalAmount;
            }

            var transaction = invoice.Transactions.FirstOrDefault(t => t.Status == "Success");
            var refCode = transaction?.TransactionNo ?? $"CASH-{invoice.Id:N}";
            var paidAt = invoice.PaidAt ?? now;
            var clearance = JsonSerializer.Serialize(new
            {
                sessionId = request.SessionId, amount = amountDue, totalFee,
                transactionRef = refCode, paymentMethod = "CASH", paidAt
            });
            var coreBaseUrl = config["Services:Core:BaseUrl"] ?? "http://localhost:5295/";
            if (string.IsNullOrWhiteSpace(secret) || System.Text.Encoding.UTF8.GetByteCount(secret) < 32)
                return StatusCode(503, new { message = "Chưa cấu hình khóa Payment Service–Core." });
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using var coreRequest = new HttpRequestMessage(HttpMethod.Post,
                $"{coreBaseUrl.TrimEnd('/')}/api/internal/payment-clearances")
            {
                Content = new StringContent(clearance, System.Text.Encoding.UTF8, "application/json")
            };
            coreRequest.Headers.Add("X-Payment-Timestamp", timestamp.ToString());
            coreRequest.Headers.Add("X-Payment-Signature", PaymentRequestSignature.Compute(secret, timestamp, clearance));
            using var coreResponse = await _httpClientFactory.CreateClient().SendAsync(coreRequest, ct);
            if (!coreResponse.IsSuccessStatusCode)
                return StatusCode(503, new { message = "Billing đã lưu biên nhận, Core chưa xác nhận được phiên. Hãy thử kiểm tra lại trạng thái." });

            return Ok(new { invoiceId = invoice.Id, transactionRef = refCode, totalFee, amountPaid = amountDue });
        }

        [HttpPost("calculate-turn-fee")]
        public IActionResult CalculateTurnFee([FromBody] ParkingSessionBilling request)
        {
            try
            {
                decimal fee = _billingEngine.CalculateFee(request);
                return Ok(new { SessionId = request.Id, FeeAmount = fee, Currency = "VND" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpGet("monthly-fee-quote")]
        public IActionResult GetMonthlyFeeQuote([FromQuery] VehicleType vehicleType, [FromQuery] ParkingZone zone)
        {
            try
            {
                decimal fee = _billingEngine.CalculateMonthlyFee(vehicleType, zone);
                return Ok(new { VehicleType = vehicleType.ToString(), Zone = zone.ToString(), FeeAmount = fee, Currency = "VND/tháng" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpPost("create-payment-url")]
        public async Task<IActionResult> CreatePaymentUrl([FromQuery] Guid sessionId, [FromQuery] decimal amount)
        {
            try
            {
                var invoice = new Invoice
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    TotalAmount = amount,
                    GrossAmount = amount,
                    Status = "Pending",
                    CreatedAt = DateTime.UtcNow,
                    Session = null!,
                    Transactions = new List<PaymentTransaction>()
                };

                var url = _vnPayService.CreatePaymentUrl(HttpContext, invoice.Id.ToString(), sessionId.ToString(), amount);
                _context.Add(invoice);
                await _context.SaveChangesAsync();
                return Ok(new { paymentUrl = url, invoiceId = invoice.Id });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpGet("vnpay-ipn")]
        public async Task<IActionResult> PaymentIpn(CancellationToken ct)
        {
            try
            {
                if (!_vnPayService.ValidateCallback(Request.Query))
                    return Ok(new { RspCode = "97", Message = "Invalid signature" });

                var transactionReference = Request.Query["vnp_TxnRef"].ToString();
                var responseCode = Request.Query["vnp_ResponseCode"].ToString();
                var transactionNumber = Request.Query["vnp_TransactionNo"].ToString();
                if (!decimal.TryParse(Request.Query["vnp_Amount"].ToString(), System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out var amountInMinorUnits))
                    return Ok(new { RspCode = "04", Message = "Invalid amount" });

                if (!Guid.TryParse(transactionReference, out var invoiceId))
                    return Ok(new { RspCode = "01", Message = "Order not found" });

                var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
                if (invoice == null)
                    return Ok(new { RspCode = "01", Message = "Order not found" });

                var amount = amountInMinorUnits / 100m;
                if (amount != invoice.TotalAmount)
                    return Ok(new { RspCode = "04", Message = "Amount mismatch" });

                if (invoice.Status == "Paid")
                    return Ok(new { RspCode = "02", Message = "Order already confirmed" });

                var transactionRef = string.IsNullOrWhiteSpace(transactionNumber) ? transactionReference : transactionNumber;
                if (responseCode != "00")
                {
                    invoice.Status = responseCode == "24" ? "Cancelled" : "Failed";
                    await AddVnPayTransactionAsync(invoice, invoice.TotalAmount, transactionRef, responseCode, "Failed", ct);
                    await _context.SaveChangesAsync(ct);
                    return Ok(new { RspCode = "00", Message = "Payment failure recorded" });
                }

                var paidAt = DateTime.UtcNow;
                if (invoice.BookingId.HasValue)
                {
                    var config = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                    var coreSecret = config["PaymentService:SharedSecret"];
                    if (string.IsNullOrWhiteSpace(coreSecret) || System.Text.Encoding.UTF8.GetByteCount(coreSecret) < 32)
                        return Ok(new { RspCode = "99", Message = "Payment clearance integration is not configured" });

                    var payload = JsonSerializer.Serialize(new
                    {
                        bookingId = invoice.BookingId.Value,
                        amount = invoice.TotalAmount,
                        transactionRef,
                        paymentMethod = "VNPAY",
                        paidAt
                    });
                    var coreBaseUrl = config["Services:Core:BaseUrl"] ?? "http://localhost:5295/";
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    using var clearance = new HttpRequestMessage(HttpMethod.Post,
                        $"{coreBaseUrl.TrimEnd('/')}/api/internal/payment-clearances/booking")
                    {
                        Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
                    };
                    clearance.Headers.Add("X-Payment-Timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    clearance.Headers.Add("X-Payment-Signature", PaymentRequestSignature.Compute(coreSecret, timestamp, payload));
                    using var coreResponse = await _httpClientFactory.CreateClient().SendAsync(clearance, ct);
                    if (!coreResponse.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Core did not confirm VNPay payment for booking {BookingId}; VNPay may retry the IPN.", invoice.BookingId);
                        return Ok(new { RspCode = "99", Message = "Core booking payment clearance failed; retry required" });
                    }
                }
                else if (invoice.SessionId.HasValue)
                {
                    var session = await _context.ParkingSessions.FirstOrDefaultAsync(s => s.Id == invoice.SessionId, ct);
                    if (session != null)
                        session.CheckOutTime = paidAt;
                }

                invoice.Status = "Paid";
                invoice.PaidAt = paidAt;
                await AddVnPayTransactionAsync(invoice, amount, transactionRef, responseCode, "Success", ct);
                await _context.SaveChangesAsync();
                return Ok(new { RspCode = "00", Message = "Payment confirmed" });
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "VNPay IPN processing failed for transaction {TransactionRef}.", Request.Query["vnp_TxnRef"].ToString());
                return Ok(new { RspCode = "99", Message = "Unknown error" });
            }
        }

        private async Task AddVnPayTransactionAsync(Invoice invoice, decimal amount, string transactionRef,
            string responseCode, string status, CancellationToken ct)
        {
            if (await _context.PaymentTransactions.AnyAsync(t => t.TransactionNo == transactionRef, ct))
                return;
            _context.PaymentTransactions.Add(new PaymentTransaction
            {
                Id = Guid.NewGuid(), InvoiceId = invoice.Id, Invoice = invoice, Amount = amount,
                PaymentMethod = "VNPAY", TransactionNo = transactionRef, ResponseCode = responseCode,
                Status = status, CreatedAt = DateTime.UtcNow
            });
        }

        private sealed class BookingPaymentRequest
        {
            public long BookingId { get; set; }
            public decimal Amount { get; set; }
            public string PlateNumber { get; set; } = string.Empty;
            public DateTime StartTime { get; set; }
        }

        private sealed class CounterPaymentRequest
        {
            public long SessionId { get; set; }
            public long? BookingId { get; set; }
            public string PlateNumber { get; set; } = string.Empty;
            public DateTime CheckInTime { get; set; }
            public decimal PrepaidAmount { get; set; }
        }

        [HttpGet("vnpay-return")]
        public async Task<IActionResult> PaymentCallback(CancellationToken ct)
        {
            try
            {
                var isValidSignature = _vnPayService.ValidateCallback(Request.Query);

                if (!isValidSignature)
                {
                    return BadRequest(new { message = "Lỗi xác thực chữ ký (Invalid Signature)." });
                }

                var vnp_ResponseCode = Request.Query["vnp_ResponseCode"].ToString();
                if (Guid.TryParse(Request.Query["vnp_TxnRef"].ToString(), out var invoiceId))
                {
                    var invoice = await _context.Invoices.Include(i => i.Transactions)
                        .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
                    if (invoice?.BookingId != null)
                    {
                        var paymentState = invoice.Status == "Paid" || vnp_ResponseCode == "00" ? "success" : "failed";
                        if (vnp_ResponseCode != "00" && invoice.Status != "Paid")
                        {
                            invoice.Status = vnp_ResponseCode == "24" ? "Cancelled" : "Failed";
                            var failedReference = Request.Query["vnp_TransactionNo"].ToString();
                            if (string.IsNullOrWhiteSpace(failedReference))
                                failedReference = Request.Query["vnp_TxnRef"].ToString();
                            await AddVnPayTransactionAsync(invoice, invoice.TotalAmount, failedReference,
                                vnp_ResponseCode, "Failed", ct);
                            await _context.SaveChangesAsync(ct);
                        }
                        if (vnp_ResponseCode == "00" && invoice.Status != "Paid")
                        {
                            if (!decimal.TryParse(Request.Query["vnp_Amount"], System.Globalization.NumberStyles.Number,
                                System.Globalization.CultureInfo.InvariantCulture, out var paidAmount) ||
                                paidAmount / 100m != invoice.TotalAmount)
                                return BadRequest(new { message = "Số tiền VNPay không khớp hóa đơn." });

                            var transactionRef = Request.Query["vnp_TransactionNo"].ToString();
                            if (string.IsNullOrWhiteSpace(transactionRef)) transactionRef = Request.Query["vnp_TxnRef"].ToString();
                            var paidAt = DateTime.UtcNow;
                            var payload = JsonSerializer.Serialize(new
                            {
                                bookingId = invoice.BookingId.Value,
                                amount = invoice.TotalAmount,
                                transactionRef,
                                paymentMethod = "VNPAY",
                                paidAt
                            });
                            var config = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                            var secret = config["PaymentService:SharedSecret"];
                            if (string.IsNullOrWhiteSpace(secret) || System.Text.Encoding.UTF8.GetByteCount(secret) < 32)
                            {
                                paymentState = "processing";
                            }
                            else
                            {
                                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                                var coreBaseUrl = config["Services:Core:BaseUrl"] ?? "http://localhost:5295/";
                                using var clearance = new HttpRequestMessage(HttpMethod.Post,
                                    $"{coreBaseUrl.TrimEnd('/')}/api/internal/payment-clearances/booking")
                                {
                                    Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
                                };
                                clearance.Headers.Add("X-Payment-Timestamp", timestamp.ToString());
                                clearance.Headers.Add("X-Payment-Signature", PaymentRequestSignature.Compute(secret, timestamp, payload));
                                using var response = await _httpClientFactory.CreateClient().SendAsync(clearance, ct);
                                if (response.IsSuccessStatusCode)
                                {
                                    invoice.Status = "Paid";
                                    invoice.PaidAt = paidAt;
                                    await AddVnPayTransactionAsync(invoice, invoice.TotalAmount, transactionRef,
                                        vnp_ResponseCode, "Success", ct);
                                    await _context.SaveChangesAsync(ct);
                                }
                                else
                                {
                                    paymentState = "processing";
                                }
                            }
                        }
                        var frontendUrl = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()
                            ["Client:BookingPaymentReturnUrl"] ?? "https://localhost:7189/app/customer";
                        var separator = frontendUrl.Contains('?') ? "&" : "?";
                        return Redirect($"{frontendUrl}{separator}bookingPayment={paymentState}&bookingId={invoice.BookingId}");
                    }
                }
                var vnp_OrderInfo = Request.Query["vnp_OrderInfo"].ToString();
                var vnp_TransactionNo = Request.Query["vnp_TransactionNo"].ToString();

                if (vnp_ResponseCode == "00")
                {
                    return Ok(new
                    {
                        message = "Thanh toán thành công!",
                        orderInfo = vnp_OrderInfo,
                        transactionNo = vnp_TransactionNo
                    });
                }

                return BadRequest(new { message = $"Thanh toán thất bại. Mã lỗi VNPAY: {vnp_ResponseCode}" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }
    }
}
