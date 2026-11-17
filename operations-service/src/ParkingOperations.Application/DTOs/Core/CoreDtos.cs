using System.Text.Json.Serialization;

namespace ParkingOperations.Application.DTOs.Core;

public sealed record AssignedLotSummaryDto(
    [property: JsonPropertyName("lotId")] int LotId,
    [property: JsonPropertyName("lotName")] string LotName,
    [property: JsonPropertyName("lotAddress")] string? LotAddress,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("assignedAt")] DateTime AssignedAt,
    [property: JsonPropertyName("duty")] string Duty,
    [property: JsonPropertyName("floorId")] int? FloorId,
    [property: JsonPropertyName("zoneId")] int? ZoneId
);

public sealed record CoreApiResponse<T>(
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("data")] T? Data
);

public sealed record ValidationContextResult(
    bool IsValid,
    string? ErrorMessage = null,
    string? Duty = null
);
