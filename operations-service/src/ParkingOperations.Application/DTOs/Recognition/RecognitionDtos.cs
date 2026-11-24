using System.Text.Json.Serialization;

namespace ParkingOperations.Application.DTOs.Recognition;

public sealed record RecognitionResultDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("staffUserId")] long StaffUserId,
    [property: JsonPropertyName("plateNumber")] string? PlateNumber,
    [property: JsonPropertyName("rawPlateNumber")] string? RawPlateNumber,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("isLegit")] bool IsLegit,
    [property: JsonPropertyName("vehicleConfidence")] double? VehicleConfidence,
    [property: JsonPropertyName("vehicleBbox")] IReadOnlyList<int>? VehicleBbox,
    [property: JsonPropertyName("plateBbox")] IReadOnlyList<int>? PlateBbox,
    [property: JsonPropertyName("inferenceTimeMs")] double InferenceTimeMs,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("confirmedPlateNumber")] string? ConfirmedPlateNumber = null,
    [property: JsonPropertyName("confirmedByStaffUserId")] long? ConfirmedByStaffUserId = null,
    [property: JsonPropertyName("confirmedAt")] DateTime? ConfirmedAt = null,
    [property: JsonPropertyName("confirmationNote")] string? ConfirmationNote = null,
    [property: JsonPropertyName("parkingLotId")] int? ParkingLotId = null,
    [property: JsonPropertyName("purpose")] string? Purpose = null,
    [property: JsonPropertyName("matchedParkingSessionId")] long? MatchedParkingSessionId = null,
    [property: JsonPropertyName("captureId")] string? CaptureId = null,
    [property: JsonPropertyName("imageId")] string? ImageId = null,
    [property: JsonPropertyName("eventId")] string? EventId = null,
    [property: JsonPropertyName("ticketId")] string? TicketId = null,
    [property: JsonPropertyName("sessionId")] long? SessionId = null,
    [property: JsonPropertyName("correlationId")] string? CorrelationId = null,
    [property: JsonPropertyName("idempotencyKey")] string? IdempotencyKey = null,
    [property: JsonPropertyName("modelVersion")] string? ModelVersion = null
);

public sealed record ConfirmRecognitionRequestDto(
    [property: JsonPropertyName("plateNumber")] string PlateNumber,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("lotId")] int LotId = 0,
    [property: JsonPropertyName("captureId")] string? CaptureId = null,
    [property: JsonPropertyName("eventId")] string? EventId = null,
    [property: JsonPropertyName("ticketId")] string? TicketId = null,
    [property: JsonPropertyName("idempotencyKey")] string? IdempotencyKey = null
);

public sealed record ConfirmExitRecognitionRequestDto(
    [property: JsonPropertyName("plateNumber")] string PlateNumber,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("lotId")] int LotId,
    [property: JsonPropertyName("sessionId")] long SessionId,
    [property: JsonPropertyName("captureId")] string? CaptureId = null,
    [property: JsonPropertyName("ticketId")] string? TicketId = null,
    [property: JsonPropertyName("idempotencyKey")] string? IdempotencyKey = null
);

public sealed record RecognitionErrorDto(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("code")] string? Code = null
);
