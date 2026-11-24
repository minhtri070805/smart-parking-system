using SmartParking.Application.DTOs.StaffAssignments;

namespace SmartParking.Application.Common.Interfaces;

public interface IStaffDetectionService
{
    Task<StaffDetectionDto> DetectAsync(
        Stream image,
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
        CancellationToken cancellationToken);

    Task<StaffDetectionDto?> ConfirmAsync(
        long recognitionId,
        string plateNumber,
        string? note,
        long staffUserId,
        int lotId,
        string? captureId,
        string? eventId,
        string? ticketId,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken);

    Task<StaffDetectionDto?> ConfirmExitAsync(
        long recognitionId,
        long sessionId,
        string plateNumber,
        string? note,
        long staffUserId,
        int lotId,
        string? captureId,
        string? ticketId,
        string? idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken);

    Task<StaffDetectionDto?> GetByIdAsync(
        long recognitionId,
        CancellationToken cancellationToken);
}
