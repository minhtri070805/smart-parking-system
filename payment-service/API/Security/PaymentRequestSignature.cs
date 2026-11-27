namespace API.Security;

// Compatibility facade for existing API controllers. New service-to-service
// signing is implemented in Infrastructure.Features.PayOS.Security.
internal static class PaymentRequestSignature
{
    public static bool IsSecretUsable(string? secret) =>
        Infrastructure.Features.PayOS.Security.PaymentRequestSignature.IsSecretUsable(secret);

    public static string Compute(string secret, long timestamp, string body) =>
        Infrastructure.Features.PayOS.Security.PaymentRequestSignature.Compute(secret, timestamp, body);

    public static bool Verify(string? secret, string? timestamp, string? signature, string body) =>
        Infrastructure.Features.PayOS.Security.PaymentRequestSignature.Verify(secret, timestamp, signature, body);
}
