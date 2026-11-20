using System.Security.Cryptography;
using System.Text;

namespace SmartParking.Application.Common.Security;

/// <summary>
/// Xác thực server-to-server giữa Payment Service và Core bằng HMAC-SHA256.
/// Chữ ký = hex(HMAC(secret, "{unixTimestamp}.{rawBody}")). Không dùng JWT của user/staff/admin.
/// Fail-closed: thiếu/yếu secret ⇒ luôn từ chối.
/// </summary>
public static class PaymentServiceSignature
{
    public const string SectionName = "PaymentService";
    public const string TimestampHeader = "X-Payment-Timestamp";
    public const string SignatureHeader = "X-Payment-Signature";
    public const int MinSecretBytes = 32;
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);
    public const string ServiceActorRole = "PAYMENT_SERVICE";

    private static readonly long MinUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    public static bool IsSecretUsable(string? secret) => InternalRequestSignature.IsSecretUsable(secret);

    public static string Compute(string secret, long unixTimestamp, string rawBody) =>
        InternalRequestSignature.Compute(secret, unixTimestamp, rawBody);

    public static bool Verify(string? secret, string? timestampHeader, string? signatureHeader, string rawBody, DateTimeOffset now)
    {
        if (!IsSecretUsable(secret) || string.IsNullOrWhiteSpace(signatureHeader)) return false;
        if (!long.TryParse(timestampHeader, out var ts)) return false;
        // Timestamp không tin cậy: ngoài phạm vi DateTimeOffset sẽ throw ⇒ từ chối (fail-closed).
        if (ts < MinUnixSeconds || ts > MaxUnixSeconds) return false;
        var sent = DateTimeOffset.FromUnixTimeSeconds(ts);
        if ((now - sent).Duration() > MaxClockSkew) return false;
        var expected = Compute(secret!, ts, rawBody);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signatureHeader.Trim().ToLowerInvariant()));
    }
}
