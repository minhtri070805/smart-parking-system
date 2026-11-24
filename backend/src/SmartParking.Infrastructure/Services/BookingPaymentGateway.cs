using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.Common.Security;

namespace SmartParking.Infrastructure.Services;

public sealed class BookingPaymentGateway : IBookingPaymentGateway
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public BookingPaymentGateway(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<BookingPaymentLink> CreateBookingPaymentLinkAsync(
        long bookingId, decimal amount, string plateNumber, DateTime startTime, CancellationToken ct = default)
    {
        var secret = _configuration[$"{PaymentServiceSignature.SectionName}:SharedSecret"];
        if (!PaymentServiceSignature.IsSecretUsable(secret))
            throw new InvalidOperationException("Chưa cấu hình khóa tích hợp Payment Service trên Core.");

        var baseUrl = _configuration[$"{PaymentServiceSignature.SectionName}:BaseUrl"] ?? "http://localhost:5183";
        var payload = JsonSerializer.Serialize(new { bookingId, amount, plateNumber, startTime });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{baseUrl.TrimEnd('/')}/api/billing/internal/booking-payment-url")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add(PaymentServiceSignature.TimestampHeader, timestamp.ToString());
        request.Headers.Add(PaymentServiceSignature.SignatureHeader, PaymentServiceSignature.Compute(secret!, timestamp, payload));

        using var response = await _httpClientFactory.CreateClient(nameof(BookingPaymentGateway))
            .SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Payment Service từ chối tạo link thanh toán (HTTP {(int)response.StatusCode}).");

        var result = await response.Content.ReadFromJsonAsync<PaymentLinkResponse>(cancellationToken: ct);
        if (result == null || string.IsNullOrWhiteSpace(result.PaymentUrl) || string.IsNullOrWhiteSpace(result.InvoiceId))
            throw new InvalidOperationException("Payment Service trả về link thanh toán không hợp lệ.");
        return new BookingPaymentLink(result.PaymentUrl, result.InvoiceId);
    }

    public async Task<CounterPaymentReceipt> RecordCounterPaymentAsync(
        long sessionId, long? bookingId, string plateNumber, DateTime checkInTime,
        decimal prepaidAmount, CancellationToken ct = default)
    {
        var secret = _configuration[$"{PaymentServiceSignature.SectionName}:SharedSecret"];
        if (!PaymentServiceSignature.IsSecretUsable(secret))
            throw new InvalidOperationException("Chưa cấu hình khóa tích hợp Payment Service trên Core.");
        var baseUrl = _configuration[$"{PaymentServiceSignature.SectionName}:BaseUrl"] ?? "http://localhost:5183";
        var payload = JsonSerializer.Serialize(new { sessionId, bookingId, plateNumber, checkInTime, prepaidAmount });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{baseUrl.TrimEnd('/')}/api/billing/internal/counter-payment")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add(PaymentServiceSignature.TimestampHeader, timestamp.ToString());
        request.Headers.Add(PaymentServiceSignature.SignatureHeader,
            PaymentServiceSignature.Compute(secret!, timestamp, payload));
        using var response = await _httpClientFactory.CreateClient(nameof(BookingPaymentGateway)).SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Payment Service từ chối ghi nhận thanh toán tại quầy (HTTP {(int)response.StatusCode}).");
        var result = await response.Content.ReadFromJsonAsync<CounterPaymentResponse>(cancellationToken: ct);
        if (result == null || string.IsNullOrWhiteSpace(result.InvoiceId) ||
            string.IsNullOrWhiteSpace(result.TransactionRef) || result.AmountPaid < 0 || result.TotalFee < 0)
            throw new InvalidOperationException("Payment Service trả về biên nhận quầy không hợp lệ.");
        return new CounterPaymentReceipt(result.InvoiceId, result.TransactionRef, result.TotalFee, result.AmountPaid);
    }

    private sealed record PaymentLinkResponse(string PaymentUrl, string InvoiceId);
    private sealed record CounterPaymentResponse(string InvoiceId, string TransactionRef, decimal TotalFee, decimal AmountPaid);
}
