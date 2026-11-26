using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using recognition.Application.Common.Interfaces;
using recognition.Application.DTOs;

namespace recognition.Api.Endpoints;

public static class RecognitionEndpoints
{
    public static RouteGroupBuilder MapRecognitionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/detect", async (
            HttpRequest request, long staffUserId, int lotId, string purpose,
            string? captureId, string? imageId, string? eventId,
            IStaffDetectionService service, CancellationToken ct) =>
        {
            if (staffUserId <= 0 || lotId <= 0 || !request.HasFormContentType)
                return Results.BadRequest(new { message = "A valid staff ID, lotId, purpose and image upload are required." });

            var file = (await request.ReadFormAsync(ct)).Files.GetFile("file");
            if (file is null)
                return Results.BadRequest(new { message = "An image file is required." });

            await using var stream = file.OpenReadStream();
            var idempotencyKey = request.Headers.TryGetValue("Idempotency-Key", out var idempotency) ? idempotency.ToString() : null;
            var correlationId = request.Headers.TryGetValue("X-Correlation-Id", out var correlation) ? correlation.ToString() : null;

            var result = await service.DetectAsync(
                stream, file.ContentType, file.Length, staffUserId, lotId, purpose,
                captureId, imageId, eventId, idempotencyKey, correlationId, ct);

            return Results.Ok(result);
        });

        group.MapPost("/{id:long}/confirm", async (
            HttpRequest httpRequest, long id, ConfirmRecognitionRequest request,
            IStaffDetectionService service, CancellationToken ct) =>
        {
            if (request.StaffUserId <= 0 || request.LotId <= 0 || string.IsNullOrWhiteSpace(request.PlateNumber) ||
                request.PlateNumber.Length > 32 || request.Note?.Length > 256)
                return Results.BadRequest(new { message = "Invalid recognition confirmation." });

            var correlationId = httpRequest.Headers.TryGetValue("X-Correlation-Id", out var correlation) ? correlation.ToString() : null;
            var idempotencyKey = httpRequest.Headers.TryGetValue("Idempotency-Key", out var idempotency) ? idempotency.ToString() : request.IdempotencyKey;

            var result = await service.ConfirmAsync(
                id, request.PlateNumber, request.Note, request.StaffUserId, request.LotId,
                request.CaptureId, request.EventId, request.TicketId, idempotencyKey, correlationId, ct);

            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/{id:long}/confirm-exit", async (
            HttpRequest httpRequest, long id, ConfirmExitRecognitionRequest request,
            IStaffDetectionService service, CancellationToken ct) =>
        {
            if (request.StaffUserId <= 0 || request.LotId <= 0 || request.SessionId <= 0 || string.IsNullOrWhiteSpace(request.PlateNumber))
                return Results.BadRequest(new { message = "Invalid exit recognition confirmation." });

            var correlationId = httpRequest.Headers.TryGetValue("X-Correlation-Id", out var correlation) ? correlation.ToString() : null;
            var idempotencyKey = httpRequest.Headers.TryGetValue("Idempotency-Key", out var idempotency) ? idempotency.ToString() : request.IdempotencyKey;

            var result = await service.ConfirmExitAsync(
                id, request.SessionId, request.PlateNumber, request.Note, request.StaffUserId, request.LotId,
                request.CaptureId, request.TicketId, idempotencyKey, correlationId, ct);

            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/{id:long}", async (long id, IStaffDetectionService service, CancellationToken ct) =>
        {
            if (id <= 0) return Results.BadRequest(new { message = "Invalid recognition ID." });
            var result = await service.GetByIdAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/by-capture/{captureId}", async (string captureId, IStaffDetectionService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(captureId) || captureId.Length > 128)
                return Results.BadRequest(new { message = "Invalid CaptureId." });

            var result = await service.GetByCaptureIdAsync(captureId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/{id:long}/link", async (long id, long sessionId, string? ticketId, IStaffDetectionService service, CancellationToken ct) =>
        {
            if (id <= 0 || sessionId <= 0)
                return Results.BadRequest(new { message = "Valid recognition ID and session ID are required." });

            var result = await service.LinkToSessionAsync(id, sessionId, ticketId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return group;
    }
}
