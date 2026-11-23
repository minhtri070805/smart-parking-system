using System.Text.Json.Serialization;

namespace ParkingOperations.Application.DTOs.Common;

public sealed record ErrorResponseDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("correlationId")] string? CorrelationId = null,
    [property: JsonPropertyName("timestamp")] DateTime? Timestamp = null
)
{
    public static ErrorResponseDto Create(string code, string message, string? correlationId = null) =>
        new(code, message, correlationId, DateTime.UtcNow);
}
