using System.Security.Cryptography;
using System.Text;

namespace SmartParking.Application.Common.Security;

/// <summary>
/// Chữ ký HMAC-SHA256 dùng chung cho các lời gọi server-to-server giữa các service:
/// hex lowercase của HMAC(secret, "{unixTimestamp}.{rawBody}").
/// </summary>
public static class InternalRequestSignature
{
    public const int MinSecretBytes = 32;

    public static bool IsSecretUsable(string? secret) =>
        !string.IsNullOrWhiteSpace(secret) && Encoding.UTF8.GetByteCount(secret) >= MinSecretBytes;

    public static string Compute(string secret, long unixTimestamp, string rawBody)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes($"{unixTimestamp}.{rawBody}");
        return Convert.ToHexString(HMACSHA256.HashData(key, data)).ToLowerInvariant();
    }
}
