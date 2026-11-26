using System.Net;
using System.Text;
using System.Text.Json;
using Application.Exceptions.PayOS;
using Application.Interfaces.PayOS;
using Application.Models.PayOS;
using Infrastructure.Features.PayOS.Security;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Features.PayOS.Clients;

public sealed class CoreSubscriptionClient(HttpClient http, IConfiguration configuration) : ICoreSubscriptionClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<CorePrepareResponse> PrepareAsync(long orderCode, long userId, long planId, long vehicleId, CancellationToken ct) =>
        SendAsync<CorePrepareResponse>("prepare", new { orderCode, userId, planId, vehicleId }, ct);

    public Task<CoreConfirmResponse> ConfirmAsync(long orderCode, decimal amount, string currency, string providerReference, DateTime paidAtUtc, CancellationToken ct) =>
        SendAsync<CoreConfirmResponse>("confirm", new { orderCode, amount, currency, providerReference, paidAtUtc }, ct);

    public Task CancelAsync(long orderCode, CancellationToken ct) =>
        SendAsync<JsonElement>("cancel", new { orderCode }, ct);

    private async Task<T> SendAsync<T>(string action, object body, CancellationToken ct)
    {
        var baseUrl = configuration["Services:Core:BaseUrl"] ?? "http://localhost:5295/";
        var secret = configuration["PaymentService:SharedSecret"];
        if (!PaymentRequestSignature.IsSecretUsable(secret))
            throw new CoreSubscriptionException("CORE_PAYMENT_AUTH_NOT_CONFIGURED", "PaymentService:SharedSecret must contain at least 32 bytes.");
        var json = JsonSerializer.Serialize(body, Json);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), $"api/internal/subscription-payments/{action}"));
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        request.Headers.Add("X-Payment-Timestamp", timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("X-Payment-Signature", PaymentRequestSignature.Compute(secret!, timestamp, json));
        using var response = await http.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var code = response.StatusCode == HttpStatusCode.Conflict ? "CORE_CONFLICT" : $"CORE_HTTP_{(int)response.StatusCode}";
            throw new CoreSubscriptionException(code, responseText);
        }
        return JsonSerializer.Deserialize<T>(responseText, Json)
            ?? throw new CoreSubscriptionException("CORE_INVALID_RESPONSE", "Core returned an empty response.");
    }
}

