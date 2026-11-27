using SmartParking.Application.Common.Interfaces;

namespace SmartParking.Tests;

/// <summary>Bản giả cho các dịch vụ ngoài mà BookingService cần; test không gọi Billing hay gửi email thật.</summary>
internal static class TestFakes
{
    public static readonly IBookingPaymentGateway Gateway = new FakeBookingPaymentGateway();
    public static readonly IEmailService Email = new FakeEmailService();

    private sealed class FakeBookingPaymentGateway : IBookingPaymentGateway
    {
        public Task<BookingPaymentLink> CreateBookingPaymentLinkAsync(
            long bookingId, decimal amount, string plateNumber, DateTime startTime, CancellationToken ct = default) =>
            Task.FromResult(new BookingPaymentLink($"https://pay.test/booking/{bookingId}", $"INV-{bookingId}"));

        public Task<CounterPaymentReceipt> RecordCounterPaymentAsync(
            long sessionId, long? bookingId, string plateNumber, DateTime checkInTime,
            decimal prepaidAmount, CancellationToken ct = default) =>
            Task.FromResult(new CounterPaymentReceipt($"INV-{sessionId}", $"TX-{sessionId}", 0m, 0m));
    }

    private sealed class FakeEmailService : IEmailService
    {
        public Task<bool> SendVerificationOtpAsync(string toEmail, string otpCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> SendBookingPaymentReceiptAsync(string toEmail, long bookingId, string plateNumber, string lotName,
            decimal amount, string transactionRef, DateTime paidAtUtc, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
