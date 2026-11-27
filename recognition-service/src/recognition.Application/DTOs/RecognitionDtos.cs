using System.Text.Json.Serialization;

namespace recognition.Application.DTOs;

public sealed record StaffDetectionDto(
    long Id,
    long StaffUserId,
    string? PlateNumber,
    string? RawPlateNumber,
    string Status,
    string? FailureReason,
    double Confidence,
    bool IsLegit,
    double? VehicleConfidence,
    IReadOnlyList<int>? VehicleBbox,
    IReadOnlyList<int>? PlateBbox,
    double InferenceTimeMs,
    DateTime CreatedAt,
    string? ConfirmedPlateNumber = null,
    long? ConfirmedByStaffUserId = null,
    DateTime? ConfirmedAt = null,
    string? ConfirmationNote = null,
    int? ParkingLotId = null,
    string? Purpose = null,
    long? MatchedParkingSessionId = null,
    string? CaptureId = null,
    string? ImageId = null,
    string? EventId = null,
    string? TicketId = null,
    long? SessionId = null,
    string? CorrelationId = null,
    string? IdempotencyKey = null,
    string? ModelVersion = null
);

public sealed record ConfirmRecognitionRequest(
    string PlateNumber,
    string? Note,
    long StaffUserId,
    int LotId,
    string? CaptureId = null,
    string? EventId = null,
    string? TicketId = null,
    string? IdempotencyKey = null
);

public sealed record ConfirmExitRecognitionRequest(
    long SessionId,
    string PlateNumber,
    string? Note,
    long StaffUserId,
    int LotId,
    string? CaptureId = null,
    string? TicketId = null,
    string? IdempotencyKey = null
);

public sealed record AiPlateDetectionData(
    [property: JsonPropertyName("plate_number")]
    string PlateNumber,
    [property: JsonPropertyName("raw_plate_number")]
    string RawPlateNumber,
    double Confidence,
    [property: JsonPropertyName("is_legit")]
    bool IsLegit,
    [property: JsonPropertyName("vehicle_confidence")]
    double? VehicleConfidence,
    [property: JsonPropertyName("vehicle_bbox")]
    IReadOnlyList<int>? VehicleBbox,
    [property: JsonPropertyName("plate_bbox")]
    IReadOnlyList<int>? PlateBbox,
    [property: JsonPropertyName("inference_time_ms")]
    double InferenceTimeMs
);

public sealed record AiResponseDto(
    bool Success,
    string Message,
    AiPlateDetectionData? Data
);

public sealed record OpenParkingSessionDto(
    long Id,
    string PlateNumber,
    string Status
);
