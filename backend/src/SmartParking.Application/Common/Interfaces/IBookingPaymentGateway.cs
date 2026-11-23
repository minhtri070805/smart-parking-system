namespace SmartParking.Application.Common.Interfaces;

public sealed record BookingPaymentLink(string PaymentUrl, string InvoiceId);
public sealed record CounterPaymentReceipt(string InvoiceId, string TransactionRef, decimal TotalFee, decimal AmountPaid);

public interface IBookingPaymentGateway
{
    Task<BookingPaymentLink> CreateBookingPaymentLinkAsync(
        long bookingId, decimal amount, string plateNumber, DateTime startTime, CancellationToken ct = default);

    Task<CounterPaymentReceipt> RecordCounterPaymentAsync(
        long sessionId, long? bookingId, string plateNumber, DateTime checkInTime,
        decimal prepaidAmount, CancellationToken ct = default);
}
