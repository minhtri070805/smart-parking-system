using ParkingOperations.Application.DTOs.Recognition;
using ParkingOperations.Domain.Enums;

namespace ParkingOperations.Application.Common.Interfaces;

public interface IRecognitionClient
{
    Task<RecognitionResultDto> DetectAsync(
        Stream imageStream,
        string contentType,
        long contentLength,
        long staffUserId,
        int lotId,
        RecognitionPurpose purpose,
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
        string? idempotencyKey = null,
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
        string? idempotencyKey = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<RecognitionResultDto?> GetByIdAsync(
        long recognitionId,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<RecognitionResultDto?> GetByCaptureIdAsync(
        string captureId,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
