using System.Text.Json;
using Microsoft.Extensions.Logging;
using ParkingOperations.Application.Common.Exceptions;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.DTOs.Recognition;

namespace ParkingOperations.Application.Services;

/// <summary>
/// Recognition facade with idempotency & atomic in-flight concurrency guard.
/// </summary>
public sealed class RecognitionFacade(
    IRecognitionClient recognitionClient,
    IFileValidationService fileValidationService,
    IIdempotencyCache idempotencyCache,
    ILogger<RecognitionFacade> logger) : IRecognitionFacade
{
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan InFlightLockTtl = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ───────────────────────────────────────────────── Detect ──────────────────
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
        if (lotId <= 0)
            throw new ImageValidationException("Invalid lotId. Must be greater than 0.");

        if (staffUserId <= 0)
            throw new ImageValidationException("Invalid staffUserId. Authentication required.");

        var normalizedPurpose = purpose?.Trim().ToUpperInvariant();
        if (normalizedPurpose is not ("ENTRY" or "EXIT"))
            throw new ImageValidationException("Purpose must be either 'ENTRY' or 'EXIT'.");

        var validation = fileValidationService.ValidateImageFile(imageStream, contentType, contentLength);
        if (!validation.IsValid)
            throw new ImageValidationException(validation.ErrorMessage ?? "Invalid image file.");

        var iKey = NormaliseKey(idempotencyKey);
        string? cacheKey = iKey is not null ? $"detect:{iKey}" : null;

        if (cacheKey is not null)
        {
            var (status, cached) = await idempotencyCache.AcquireOrGetAsync(cacheKey, InFlightLockTtl, cancellationToken);
            if (status == IdempotencyStatus.Cached && cached is not null)
            {
                logger.LogInformation("Detect idempotency hit. Key: {IKey}, CorrelationId: {CorrelationId}", iKey, correlationId);
                return JsonSerializer.Deserialize<RecognitionResultDto>(cached, JsonOpts)!;
            }

            if (status == IdempotencyStatus.InFlight)
            {
                logger.LogWarning("Concurrent duplicate detect request in-flight. Key: {IKey}, CorrelationId: {CorrelationId}", iKey, correlationId);
                throw new OperationConflictException($"Another detect request with idempotency key '{iKey}' is currently in progress.");
            }
        }

        try
        {
            logger.LogInformation(
                "Recognition detect requested. LotId: {LotId}, Purpose: {Purpose}, StaffUserId: {StaffUserId}, " +
                "CorrelationId: {CorrelationId}, CaptureId: {CaptureId}, ImageId: {ImageId}, EventId: {EventId}",
                lotId, normalizedPurpose, staffUserId, correlationId, captureId, imageId, eventId);

            var result = await recognitionClient.DetectAsync(
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

            if (cacheKey is not null)
            {
                await idempotencyCache.SetAsync(
                    cacheKey,
                    JsonSerializer.Serialize(result, JsonOpts),
                    IdempotencyTtl,
                    cancellationToken);
            }

            return result;
        }
        catch
        {
            if (cacheKey is not null)
            {
                await idempotencyCache.ReleaseInFlightAsync(cacheKey, cancellationToken);
            }
            throw;
        }
    }

    // ───────────────────────────────────────────────── Confirm ENTRY ───────────
    public async Task<RecognitionResultDto?> ConfirmAsync(
        long recognitionId,
        ConfirmRecognitionRequestDto request,
        long staffUserId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (recognitionId <= 0)
            throw new ImageValidationException("RecognitionId must be greater than 0.");

        if (request.LotId <= 0)
            throw new ImageValidationException("LotId must be greater than 0.");

        if (string.IsNullOrWhiteSpace(request.PlateNumber) || request.PlateNumber.Trim().Length > 32)
            throw new ImageValidationException("PlateNumber is required and must not exceed 32 characters.");

        if (request.Note?.Length > 256)
            throw new ImageValidationException("Note must not exceed 256 characters.");

        var iKey = NormaliseKey(request.IdempotencyKey);
        string? cacheKey = iKey is not null ? $"confirm:{recognitionId}:{iKey}" : null;

        if (cacheKey is not null)
        {
            var (status, cached) = await idempotencyCache.AcquireOrGetAsync(cacheKey, InFlightLockTtl, cancellationToken);
            if (status == IdempotencyStatus.Cached && cached is not null)
            {
                logger.LogInformation("Confirm ENTRY idempotency hit. RecognitionId: {RecognitionId}, Key: {IKey}, CorrelationId: {CorrelationId}",
                    recognitionId, iKey, correlationId);
                return JsonSerializer.Deserialize<RecognitionResultDto>(cached, JsonOpts);
            }

            if (status == IdempotencyStatus.InFlight)
            {
                logger.LogWarning("Concurrent duplicate confirm ENTRY request in-flight. RecognitionId: {RecognitionId}, Key: {IKey}", recognitionId, iKey);
                throw new OperationConflictException($"Another confirm request for recognition '{recognitionId}' is currently in progress.");
            }
        }

        try
        {
            logger.LogInformation(
                "Recognition confirm ENTRY requested. RecognitionId: {RecognitionId}, Plate: {PlateNumber}, " +
                "LotId: {LotId}, StaffUserId: {StaffUserId}, CorrelationId: {CorrelationId}, EventId: {EventId}, TicketId: {TicketId}",
                recognitionId, request.PlateNumber, request.LotId, staffUserId, correlationId,
                request.EventId, request.TicketId);

            var result = await recognitionClient.ConfirmAsync(
                recognitionId,
                request.PlateNumber.Trim(),
                request.Note?.Trim(),
                staffUserId,
                request.LotId,
                request.CaptureId,
                request.EventId,
                request.TicketId,
                request.IdempotencyKey,
                correlationId,
                cancellationToken);

            if (result is not null && cacheKey is not null)
            {
                await idempotencyCache.SetAsync(
                    cacheKey,
                    JsonSerializer.Serialize(result, JsonOpts),
                    IdempotencyTtl,
                    cancellationToken);
            }

            return result;
        }
        catch
        {
            if (cacheKey is not null)
            {
                await idempotencyCache.ReleaseInFlightAsync(cacheKey, cancellationToken);
            }
            throw;
        }
    }

    // ───────────────────────────────────────────────── Confirm EXIT ────────────
    public async Task<RecognitionResultDto?> ConfirmExitAsync(
        long recognitionId,
        ConfirmExitRecognitionRequestDto request,
        long staffUserId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (recognitionId <= 0)
            throw new ImageValidationException("RecognitionId must be greater than 0.");

        if (request.LotId <= 0)
            throw new ImageValidationException("LotId must be greater than 0.");

        if (request.SessionId <= 0)
            throw new ImageValidationException("SessionId must be greater than 0.");

        if (string.IsNullOrWhiteSpace(request.PlateNumber) || request.PlateNumber.Trim().Length > 32)
            throw new ImageValidationException("PlateNumber is required and must not exceed 32 characters.");

        if (request.Note?.Length > 256)
            throw new ImageValidationException("Note must not exceed 256 characters.");

        var iKey = NormaliseKey(request.IdempotencyKey);
        string? cacheKey = iKey is not null ? $"confirm-exit:{recognitionId}:{iKey}" : null;

        if (cacheKey is not null)
        {
            var (status, cached) = await idempotencyCache.AcquireOrGetAsync(cacheKey, InFlightLockTtl, cancellationToken);
            if (status == IdempotencyStatus.Cached && cached is not null)
            {
                logger.LogInformation("Confirm EXIT idempotency hit. RecognitionId: {RecognitionId}, Key: {IKey}, CorrelationId: {CorrelationId}",
                    recognitionId, iKey, correlationId);
                return JsonSerializer.Deserialize<RecognitionResultDto>(cached, JsonOpts);
            }

            if (status == IdempotencyStatus.InFlight)
            {
                logger.LogWarning("Concurrent duplicate confirm EXIT request in-flight. RecognitionId: {RecognitionId}, Key: {IKey}", recognitionId, iKey);
                throw new OperationConflictException($"Another confirm exit request for recognition '{recognitionId}' is currently in progress.");
            }
        }

        try
        {
            logger.LogInformation(
                "Recognition confirm EXIT requested. RecognitionId: {RecognitionId}, SessionId: {SessionId}, " +
                "Plate: {PlateNumber}, LotId: {LotId}, StaffUserId: {StaffUserId}, CorrelationId: {CorrelationId}, TicketId: {TicketId}",
                recognitionId, request.SessionId, request.PlateNumber, request.LotId, staffUserId,
                correlationId, request.TicketId);

            var result = await recognitionClient.ConfirmExitAsync(
                recognitionId,
                request.SessionId,
                request.PlateNumber.Trim(),
                request.Note?.Trim(),
                staffUserId,
                request.LotId,
                request.CaptureId,
                request.TicketId,
                request.IdempotencyKey,
                correlationId,
                cancellationToken);

            if (result is not null && cacheKey is not null)
            {
                await idempotencyCache.SetAsync(
                    cacheKey,
                    JsonSerializer.Serialize(result, JsonOpts),
                    IdempotencyTtl,
                    cancellationToken);
            }

            return result;
        }
        catch
        {
            if (cacheKey is not null)
            {
                await idempotencyCache.ReleaseInFlightAsync(cacheKey, cancellationToken);
            }
            throw;
        }
    }

    // ───────────────────────────────────────────────── Lookups ─────────────────
    public Task<RecognitionResultDto?> GetByIdAsync(
        long recognitionId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (recognitionId <= 0)
            throw new ImageValidationException("RecognitionId must be greater than 0.");

        logger.LogInformation("Recognition query requested. RecognitionId: {RecognitionId}, CorrelationId: {CorrelationId}",
            recognitionId, correlationId);

        return recognitionClient.GetByIdAsync(recognitionId, correlationId, cancellationToken);
    }

    public Task<RecognitionResultDto?> GetByCaptureIdAsync(
        string captureId,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(captureId) || captureId.Length > 128)
            throw new ImageValidationException("CaptureId is required and must not exceed 128 characters.");

        logger.LogInformation("Recognition lookup by CaptureId. CaptureId: {CaptureId}, CorrelationId: {CorrelationId}",
            captureId, correlationId);

        return recognitionClient.GetByCaptureIdAsync(captureId, correlationId, cancellationToken);
    }

    private static string? NormaliseKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        return trimmed.Length is > 0 and <= 256 ? trimmed : null;
    }
}
