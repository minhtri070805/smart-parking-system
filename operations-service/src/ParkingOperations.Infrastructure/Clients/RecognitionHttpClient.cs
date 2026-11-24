using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ParkingOperations.Application.Common.Exceptions;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.DTOs.Recognition;

namespace ParkingOperations.Infrastructure.Clients;

public sealed class RecognitionHttpClient(HttpClient client) : IRecognitionClient
{
    public const string KeyHeader = "X-Recognition-Service-Key";
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string IdempotencyHeader = "Idempotency-Key";

    public async Task<RecognitionResultDto> DetectAsync(
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
        CancellationToken cancellationToken = default)
    {
        if (contentLength <= 0 || !MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            throw new ImageValidationException("A non-empty image and a valid content type are required.");

        using var form = new MultipartFormDataContent();
        var content = new StreamContent(imageStream);
        content.Headers.ContentType = mediaType;
        form.Add(content, "file", "staff-upload");

        var query = $"internal/recognition/detect?staffUserId={staffUserId}&lotId={lotId}&purpose={Uri.EscapeDataString(purpose)}";
        if (!string.IsNullOrWhiteSpace(captureId))
        {
            query += $"&captureId={Uri.EscapeDataString(captureId)}";
        }
        if (!string.IsNullOrWhiteSpace(imageId))
        {
            query += $"&imageId={Uri.EscapeDataString(imageId)}";
        }
        if (!string.IsNullOrWhiteSpace(eventId))
        {
            query += $"&eventId={Uri.EscapeDataString(eventId)}";
        }

        var request = new HttpRequestMessage(HttpMethod.Post, query)
        {
            Content = form
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId);
        }
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation(IdempotencyHeader, idempotencyKey);
        }

        var rawResult = (await SendAsync<RecognitionResultDto>(request, allowNotFound: false, cancellationToken))!;
        
        // Enrich context if downstream does not reflect it back
        return rawResult with
        {
            CaptureId = rawResult.CaptureId ?? captureId,
            ImageId = rawResult.ImageId ?? imageId,
            EventId = rawResult.EventId ?? eventId,
            IdempotencyKey = rawResult.IdempotencyKey ?? idempotencyKey,
            CorrelationId = rawResult.CorrelationId ?? correlationId
        };
    }

    public async Task<RecognitionResultDto?> ConfirmAsync(
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
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"internal/recognition/{recognitionId}/confirm")
        {
            Content = JsonContent.Create(new InternalConfirmRecognition(plateNumber, note, staffUserId, lotId, captureId, eventId, ticketId))
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId);
        }
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation(IdempotencyHeader, idempotencyKey);
        }

        var rawResult = await SendAsync<RecognitionResultDto>(request, allowNotFound: true, cancellationToken);
        if (rawResult is null) return null;

        return rawResult with
        {
            CaptureId = rawResult.CaptureId ?? captureId,
            EventId = rawResult.EventId ?? eventId,
            TicketId = rawResult.TicketId ?? ticketId,
            CorrelationId = rawResult.CorrelationId ?? correlationId
        };
    }

    public async Task<RecognitionResultDto?> ConfirmExitAsync(
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
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"internal/recognition/{recognitionId}/confirm-exit")
        {
            Content = JsonContent.Create(new InternalConfirmExitRecognition(sessionId, plateNumber, note, staffUserId, lotId, captureId, ticketId))
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId);
        }
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation(IdempotencyHeader, idempotencyKey);
        }

        var rawResult = await SendAsync<RecognitionResultDto>(request, allowNotFound: true, cancellationToken);
        if (rawResult is null) return null;

        return rawResult with
        {
            CaptureId = rawResult.CaptureId ?? captureId,
            TicketId = rawResult.TicketId ?? ticketId,
            SessionId = sessionId,
            CorrelationId = rawResult.CorrelationId ?? correlationId
        };
    }

    public Task<RecognitionResultDto?> GetByIdAsync(
        long recognitionId,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"internal/recognition/{recognitionId}");
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId);
        }

        return SendAsync<RecognitionResultDto>(request, allowNotFound: true, cancellationToken);
    }

    public Task<RecognitionResultDto?> GetByCaptureIdAsync(
        string captureId,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"internal/recognition/by-capture/{Uri.EscapeDataString(captureId)}");
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId);
        }
        return SendAsync<RecognitionResultDto>(request, allowNotFound: true, cancellationToken);
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, bool allowNotFound, CancellationToken ct) where T : class
    {
        using (request)
        {
            if (client.BaseAddress is null)
                throw Unavailable();

            try
            {
                using var response = await client.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
                    return null;

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        throw Unavailable();

                    var error = await response.Content.ReadFromJsonAsync<RecognitionErrorDto>(cancellationToken: ct);
                    var message = error?.Message ?? "Recognition service rejected the request.";

                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        throw new ImageValidationException(message);

                    throw new RecognitionServiceException(response.StatusCode, message);
                }

                return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw Unavailable();
            }
            catch (HttpRequestException) { throw Unavailable(); }
            catch (JsonException) { throw Unavailable(); }
            catch (NotSupportedException) { throw Unavailable(); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
        }
    }

    private static RecognitionServiceException Unavailable() =>
        new(HttpStatusCode.ServiceUnavailable, "Recognition service is unavailable.");

    private sealed record InternalConfirmRecognition(
        string PlateNumber, string? Note, long StaffUserId, int LotId, string? CaptureId = null, string? EventId = null, string? TicketId = null);

    private sealed record InternalConfirmExitRecognition(
        long SessionId, string PlateNumber, string? Note, long StaffUserId, int LotId, string? CaptureId = null, string? TicketId = null);
}
