using System.Security.Cryptography;
using System.Text;

namespace API.Security;

internal static class PaymentRequestSignature
{
    public static string Compute(string secret, long timestamp, string body)
    {
        var bytes = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bytes)).ToLowerInvariant();
    }

    public static bool Verify(string? secret, string? timestamp, string? signature, string body)
    {
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32 ||
            !long.TryParse(timestamp, out var seconds) || string.IsNullOrWhiteSpace(signature)) return false;
        if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) > 300) return false;
        var expected = Compute(secret, seconds, body);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }
}
