using ParkingOperations.Application.DTOs.Recognition;

namespace ParkingOperations.Application.Common.Interfaces;

public interface IRecognitionClient
{
    Task<RecognitionResultDto> DetectAsync(
        Stream imageStream,
        string contentType,
        long contentLength,
        long staffUserId,
        int lotId,
        string purpose,
        string? captureId = null,
        string? imageId = null,
        string? eventId = null,
        string? idempotencyKey = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<RecognitionResultDto?> ConfirmAsync(
        long recognitionId,
        string plateNumber,
        string? note,
        long staffUserId,
        int lotId,
        string? captureId = null,
        string? eventId = null,
        string? ticketId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<RecognitionResultDto?> ConfirmExitAsync(
        long recognitionId,
        long sessionId,
        string plateNumber,
        string? note,
        long staffUserId,
        int lotId,
        string? captureId = null,
        string? ticketId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
