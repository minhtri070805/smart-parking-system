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
    string PlateNumber,
    string RawPlateNumber,
    double Confidence,
    bool IsLegit,
    double? VehicleConfidence,
    IReadOnlyList<int>? VehicleBbox,
    IReadOnlyList<int>? PlateBbox,
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
