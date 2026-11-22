using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartParking.Application.Common.Exceptions;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.DTOs.StaffAssignments;

namespace SmartParking.Infrastructure.Services;

public sealed class RecognitionServiceClient(HttpClient client) : IStaffDetectionService
{
    public const string KeyHeader = "X-Recognition-Service-Key";
    private const string CorrelationHeader = "X-Correlation-Id";
    private const string IdempotencyHeader = "Idempotency-Key";

    public async Task<StaffDetectionDto> DetectAsync(
        Stream image, string contentType, long contentLength, long staffUserId, int lotId, string purpose,
        string? captureId, string? imageId, string? eventId, string? idempotencyKey, string? correlationId,
        CancellationToken cancellationToken)
    {
        if (contentLength <= 0 || !MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            throw new ImageValidationException("A non-empty image and a valid content type are required.");

        using var form = new MultipartFormDataContent();
        var content = new StreamContent(image);
        content.Headers.ContentType = mediaType;
        form.Add(content, "file", "staff-upload");
        var query = $"internal/recognition/detect?staffUserId={staffUserId}&lotId={lotId}&purpose={Uri.EscapeDataString(purpose)}";
        if (!string.IsNullOrWhiteSpace(captureId)) query += $"&captureId={Uri.EscapeDataString(captureId)}";
        if (!string.IsNullOrWhiteSpace(imageId)) query += $"&imageId={Uri.EscapeDataString(imageId)}";
        if (!string.IsNullOrWhiteSpace(eventId)) query += $"&eventId={Uri.EscapeDataString(eventId)}";
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            query)
        {
            Content = form
        };
        AddContextHeaders(request, idempotencyKey, correlationId);
        return (await SendAsync<StaffDetectionDto>(request, allowNotFound: false, cancellationToken))!;
    }

    public Task<StaffDetectionDto?> ConfirmAsync(
        long recognitionId, string plateNumber, string? note, long staffUserId, int lotId,
        string? captureId, string? eventId, string? ticketId, string? idempotencyKey, string? correlationId,
        CancellationToken cancellationToken) =>
        SendAsync<StaffDetectionDto>(CreateRequest(HttpMethod.Post, $"internal/recognition/{recognitionId}/confirm",
            JsonContent.Create(new ConfirmRecognition(plateNumber, note, staffUserId, lotId, captureId, eventId, ticketId, idempotencyKey)),
            idempotencyKey, correlationId), allowNotFound: true, cancellationToken);

    public Task<StaffDetectionDto?> ConfirmExitAsync(
        long recognitionId, long sessionId, string plateNumber, string? note, long staffUserId, int lotId,
        string? captureId, string? ticketId, string? idempotencyKey, string? correlationId,
        CancellationToken cancellationToken) =>
        SendAsync<StaffDetectionDto>(
            CreateRequest(HttpMethod.Post, $"internal/recognition/{recognitionId}/confirm-exit",
                JsonContent.Create(new ConfirmExitRecognition(sessionId, plateNumber, note, staffUserId, lotId, captureId, ticketId, idempotencyKey)),
                idempotencyKey, correlationId),
            allowNotFound: true,
            cancellationToken);

    public Task<StaffDetectionDto?> GetByIdAsync(long recognitionId, CancellationToken cancellationToken) =>
        SendAsync<StaffDetectionDto>(
            new HttpRequestMessage(HttpMethod.Get, $"internal/recognition/{recognitionId}"),
            allowNotFound: true,
            cancellationToken);

    public Task<StaffDetectionDto?> GetByCaptureIdAsync(string captureId, CancellationToken cancellationToken) =>
        SendAsync<StaffDetectionDto>(
            new HttpRequestMessage(HttpMethod.Get, $"internal/recognition/by-capture/{Uri.EscapeDataString(captureId)}"),
            allowNotFound: true,
            cancellationToken);

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
                    var error = await response.Content.ReadFromJsonAsync<RecognitionError>(cancellationToken: ct);
                    var message = error?.Message ?? "Recognition service rejected the request.";
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        throw new ImageValidationException(message);
                    throw new AiServiceException(response.StatusCode, message);
                }

                return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw Unavailable();
            }
            catch (HttpRequestException) { throw Unavailable(); }
            catch (JsonException) { throw Unavailable(); }
            catch (NotSupportedException) { throw Unavailable(); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
        }
    }

    private static AiServiceException Unavailable() =>
        new(HttpStatusCode.ServiceUnavailable, "Recognition service is unavailable.");

    private static HttpRequestMessage CreateRequest(HttpMethod method, string uri, HttpContent content,
        string? idempotencyKey, string? correlationId)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        AddContextHeaders(request, idempotencyKey, correlationId);
        return request;
    }

    private static void AddContextHeaders(HttpRequestMessage request, string? idempotencyKey, string? correlationId)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey)) request.Headers.TryAddWithoutValidation(IdempotencyHeader, idempotencyKey);
        if (!string.IsNullOrWhiteSpace(correlationId)) request.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId);
    }

    public sealed record ConfirmRecognition(string PlateNumber, string? Note, long StaffUserId, int LotId,
        string? CaptureId = null, string? EventId = null, string? TicketId = null,
        string? IdempotencyKey = null);
    public sealed record ConfirmExitRecognition(
        long SessionId, string PlateNumber, string? Note, long StaffUserId, int LotId,
        string? CaptureId = null, string? TicketId = null, string? IdempotencyKey = null);
    private sealed record RecognitionError(string Message);
}
