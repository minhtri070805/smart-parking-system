using ParkingOperations.Application.DTOs.Recognition;
using ParkingOperations.Domain.Enums;

namespace ParkingOperations.Application.Common.Interfaces;

public interface IRecognitionFacade
{
    Task<RecognitionResultDto> DetectAsync(
        Stream imageStream,
        string contentType,
        long contentLength,
        long staffUserId,
        int lotId,
        RecognitionPurpose purpose,
        string? captureId,
        string? imageId,
        string? eventId,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<RecognitionResultDto?> ConfirmAsync(
        long recognitionId,
        ConfirmRecognitionRequestDto request,
        long staffUserId,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<RecognitionResultDto?> ConfirmExitAsync(
        long recognitionId,
        ConfirmExitRecognitionRequestDto request,
        long staffUserId,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<RecognitionResultDto?> GetByIdAsync(
        long recognitionId,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task<RecognitionResultDto?> GetByCaptureIdAsync(
        string captureId,
        string? correlationId,
        CancellationToken cancellationToken = default);
}
