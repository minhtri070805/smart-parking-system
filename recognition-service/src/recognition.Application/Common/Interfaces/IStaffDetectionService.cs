using recognition.Application.DTOs;

namespace recognition.Application.Common.Interfaces;

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

    Task<StaffDetectionDto?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<StaffDetectionDto?> GetByCaptureIdAsync(string captureId, CancellationToken cancellationToken);

    Task<StaffDetectionDto?> LinkToSessionAsync(long recognitionId, long sessionId, string? ticketId, CancellationToken cancellationToken);
}

public interface IAiServiceClient
{
    Task<AiResponseDto> DetectAsync(byte[] imageBytes, string contentType, CancellationToken cancellationToken);
}

public interface ICoreContextClient
{
    Task<bool> IsLotAvailableAsync(int lotId, bool requireActive, CancellationToken cancellationToken);
    Task<IReadOnlyList<OpenParkingSessionDto>> GetOpenSessionsAsync(int lotId, CancellationToken cancellationToken);
}
