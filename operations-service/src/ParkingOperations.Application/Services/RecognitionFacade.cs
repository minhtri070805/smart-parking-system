using Microsoft.Extensions.Logging;
using ParkingOperations.Application.Common.Exceptions;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.DTOs.Recognition;

namespace ParkingOperations.Application.Services;

public sealed class RecognitionFacade(
    IRecognitionClient recognitionClient,
    IFileValidationService fileValidationService,
    ILogger<RecognitionFacade> logger) : IRecognitionFacade
{
    public async Task<RecognitionResultDto> DetectAsync(
        Stream imageStream,
        string contentType,
        long contentLength,
        long staffUserId,
        int lotId,
        string purpose,
        string? captureId,
        string? imageId,
        string? eventId,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate business rules on input
        if (lotId <= 0)
        {
            throw new ImageValidationException("Invalid lotId. Must be greater than 0.");
        }

        if (staffUserId <= 0)
        {
            throw new ImageValidationException("Invalid staffUserId. Authentication required.");
        }

        var normalizedPurpose = purpose?.Trim().ToUpperInvariant();
        if (normalizedPurpose is not ("ENTRY" or "EXIT"))
        {
            throw new ImageValidationException("Purpose must be either 'ENTRY' or 'EXIT'.");
        }

        // 2. Strict file & magic byte validation
        var validation = fileValidationService.ValidateImageFile(imageStream, contentType, contentLength);
        if (!validation.IsValid)
        {
            throw new ImageValidationException(validation.ErrorMessage ?? "Invalid image file.");
        }

        logger.LogInformation(
            "Recognition detect requested. LotId: {LotId}, Purpose: {Purpose}, StaffUserId: {StaffUserId}, CorrelationId: {CorrelationId}, CaptureId: {CaptureId}, ImageId: {ImageId}, EventId: {EventId}",
            lotId, normalizedPurpose, staffUserId, correlationId, captureId, imageId, eventId);

        // 3. Delegate to typed downstream client
        return await recognitionClient.DetectAsync(
            imageStream,
            validation.DetectedContentType ?? contentType,
            contentLength,
            staffUserId,
            lotId,
            normalizedPurpose,
            captureId,
            imageId,
            eventId,
            idempotencyKey,
            correlationId,
            cancellationToken);
    }

    public Task<RecognitionResultDto?> ConfirmAsync(
        long recognitionId,
        ConfirmRecognitionRequestDto request,
        long staffUserId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (recognitionId <= 0)
        {
            throw new ImageValidationException("RecognitionId must be greater than 0.");
        }

        if (request.LotId <= 0)
        {
            throw new ImageValidationException("LotId must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(request.PlateNumber) || request.PlateNumber.Trim().Length > 32)
        {
            throw new ImageValidationException("PlateNumber is required and must not exceed 32 characters.");
        }

        if (request.Note?.Length > 256)
        {
            throw new ImageValidationException("Note must not exceed 256 characters.");
        }

        logger.LogInformation(
            "Recognition confirm ENTRY requested. RecognitionId: {RecognitionId}, Plate: {PlateNumber}, LotId: {LotId}, StaffUserId: {StaffUserId}, CorrelationId: {CorrelationId}, EventId: {EventId}, TicketId: {TicketId}",
            recognitionId, request.PlateNumber, request.LotId, staffUserId, correlationId, request.EventId, request.TicketId);

        return recognitionClient.ConfirmAsync(
            recognitionId,
            request.PlateNumber.Trim(),
            request.Note?.Trim(),
            staffUserId,
            request.LotId,
            request.CaptureId,
            request.EventId,
            request.TicketId,
            correlationId,
            cancellationToken);
    }

    public Task<RecognitionResultDto?> ConfirmExitAsync(
        long recognitionId,
        ConfirmExitRecognitionRequestDto request,
        long staffUserId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (recognitionId <= 0)
        {
            throw new ImageValidationException("RecognitionId must be greater than 0.");
        }

        if (request.LotId <= 0)
        {
            throw new ImageValidationException("LotId must be greater than 0.");
        }

        if (request.SessionId <= 0)
        {
            throw new ImageValidationException("SessionId must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(request.PlateNumber) || request.PlateNumber.Trim().Length > 32)
        {
            throw new ImageValidationException("PlateNumber is required and must not exceed 32 characters.");
        }

        if (request.Note?.Length > 256)
        {
            throw new ImageValidationException("Note must not exceed 256 characters.");
        }

        logger.LogInformation(
            "Recognition confirm EXIT requested. RecognitionId: {RecognitionId}, SessionId: {SessionId}, Plate: {PlateNumber}, LotId: {LotId}, StaffUserId: {StaffUserId}, CorrelationId: {CorrelationId}, TicketId: {TicketId}",
            recognitionId, request.SessionId, request.PlateNumber, request.LotId, staffUserId, correlationId, request.TicketId);

        return recognitionClient.ConfirmExitAsync(
            recognitionId,
            request.SessionId,
            request.PlateNumber.Trim(),
            request.Note?.Trim(),
            staffUserId,
            request.LotId,
            request.CaptureId,
            request.TicketId,
            correlationId,
            cancellationToken);
    }

    public Task<RecognitionResultDto?> GetByIdAsync(
        long recognitionId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (recognitionId <= 0)
        {
            throw new ImageValidationException("RecognitionId must be greater than 0.");
        }

        logger.LogInformation("Recognition query requested. RecognitionId: {RecognitionId}, CorrelationId: {CorrelationId}",
            recognitionId, correlationId);

        return recognitionClient.GetByIdAsync(recognitionId, correlationId, cancellationToken);
    }
}
