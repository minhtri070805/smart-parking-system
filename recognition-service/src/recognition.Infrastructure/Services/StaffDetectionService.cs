using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using recognition.Application.Common.Exceptions;
using recognition.Application.Common.Interfaces;
using recognition.Application.DTOs;
using recognition.Domain.Entities;
using recognition.Domain.Enums;
using recognition.Persistence.Data;

namespace recognition.Infrastructure.Services;

public sealed class StaffDetectionService(
    AiRecognitionDbContext db,
    IAiServiceClient aiClient,
    ICoreContextClient coreClient,
    IConfiguration configuration) : IStaffDetectionService
{
    private static readonly HashSet<string> SupportedTypes = ["image/jpeg", "image/png", "image/webp"];
    private readonly long _maxBytes = configuration.GetValue<long?>("AiService:MaxUploadSizeBytes") ?? 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<StaffDetectionDto> DetectAsync(
        Stream image, string contentType, long contentLength, long staffUserId, int lotId, string purpose,
        string? captureId, string? imageId, string? eventId, string? idempotencyKey, string? correlationId,
        CancellationToken cancellationToken)
    {
        var normPurpose = purpose.Trim().ToUpperInvariant();
        if (!RecognitionPurpose.IsValid(normPurpose))
            throw new ImageValidationException("Purpose must be either 'ENTRY' or 'EXIT'.");

        if (!await coreClient.IsLotAvailableAsync(lotId, requireActive: normPurpose == RecognitionPurpose.Entry, cancellationToken))
            throw new ImageValidationException("Selected parking lot is unavailable or invalid.");

        if (string.IsNullOrWhiteSpace(captureId) && !string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ImageValidationException("CaptureId is required when Idempotency-Key is supplied.");

        byte[] bytes;
        await using (var buffer = new MemoryStream())
        {
            await image.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        if (bytes.Length == 0 || bytes.Length > _maxBytes || !HasImageSignature(bytes, contentType))
            throw new ImageValidationException("Image content is invalid, empty, or exceeds the configured size limit.");

        var contentHash = Convert.ToHexString(SHA256.HashData(bytes));

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingKey = await db.RecognitionRecords.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existingKey is not null)
            {
                if (existingKey.StaffUserId != staffUserId || existingKey.ParkingLotId != lotId ||
                    existingKey.Purpose != normPurpose || existingKey.CaptureId != captureId || existingKey.ImageId != imageId ||
                    existingKey.ContentSha256 != contentHash)
                    throw new ImageValidationException("Idempotency key was already used with different recognition context.");

                return Map(existingKey, ResolveStatus(existingKey));
            }
        }

        var aiResult = await aiClient.DetectAsync(bytes, contentType, cancellationToken);

        var recognized = aiResult.Success && aiResult.Data is not null
            && !string.IsNullOrWhiteSpace(aiResult.Data.PlateNumber)
            && !string.IsNullOrWhiteSpace(aiResult.Data.RawPlateNumber);

        var data = aiResult.Data;
        var record = new AiRecognitionRecord
        {
            StaffUserId = staffUserId,
            CaptureId = captureId,
            ImageId = imageId,
            EventId = eventId,
            IdempotencyKey = idempotencyKey,
            CorrelationId = correlationId,
            ParkingLotId = lotId,
            Purpose = normPurpose,
            ContentSha256 = contentHash,
            PlateNumber = recognized ? data!.PlateNumber : null,
            RawPlateNumber = recognized ? data!.RawPlateNumber : null,
            FailureReason = recognized ? null : aiResult.Message,
            Confidence = data?.Confidence ?? 0,
            IsLegit = data?.IsLegit ?? false,
            VehicleConfidence = data?.VehicleConfidence,
            VehicleBboxJson = data?.VehicleBbox is not null ? JsonSerializer.Serialize(data.VehicleBbox, JsonOpts) : null,
            PlateBboxJson = data?.PlateBbox is not null ? JsonSerializer.Serialize(data.PlateBbox, JsonOpts) : null,
            InferenceTimeMs = data?.InferenceTimeMs ?? 0
        };

        db.RecognitionRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        if (normPurpose == RecognitionPurpose.Exit && recognized)
        {
            var sessions = await coreClient.GetOpenSessionsAsync(lotId, cancellationToken);
            var matches = sessions.Where(s => IsMatch(data!.PlateNumber, s.PlateNumber)).ToList();
            if (matches.Count == 1)
            {
                record.MatchedParkingSessionId = matches[0].Id;
                record.ConfirmedPlateNumber = matches[0].PlateNumber;
                record.ConfirmationNote = "AI exit plate matched an open session; staff checkout confirmation is still required.";
                await db.SaveChangesAsync(cancellationToken);
            }
            else if (matches.Count > 1)
            {
                record.FailureReason = "AI plate matched more than one open session; manual review required.";
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        if (!recognized) return Map(record, RecognitionStatus.Unreadable);
        if (normPurpose == RecognitionPurpose.Entry) return Map(record, RecognitionStatus.PendingLink);
        if (record.MatchedParkingSessionId.HasValue) return Map(record, RecognitionStatus.PendingConfirmation);
        return Map(record, RecognitionStatus.PendingLink);
    }

    public async Task<StaffDetectionDto?> ConfirmAsync(
        long recognitionId, string plateNumber, string? note, long staffUserId, int lotId,
        string? captureId, string? eventId, string? ticketId, string? idempotencyKey, string? correlationId,
        CancellationToken cancellationToken)
    {
        var record = await db.RecognitionRecords.FirstOrDefaultAsync(x => x.Id == recognitionId, cancellationToken);
        if (record is null) return null;

        if (record.Purpose != RecognitionPurpose.Entry || record.ParkingLotId != lotId)
            throw new ImageValidationException("Recognition does not belong to an entry at the selected parking lot.");

        if (record.StaffUserId != staffUserId)
            throw new ImageValidationException("Only the staff member who captured this entry image may confirm it.");

        if (record.ConfirmedAt.HasValue)
        {
            if (!string.Equals(record.ConfirmedPlateNumber, plateNumber, StringComparison.OrdinalIgnoreCase))
                throw new ImageValidationException("This recognition has already been confirmed with another plate.");
            return Map(record, RecognitionStatus.Confirmed);
        }

        record.ConfirmedPlateNumber = plateNumber.Trim();
        record.ConfirmedByStaffUserId = staffUserId;
        record.ConfirmedAt = DateTime.UtcNow;
        record.ConfirmationNote = note?.Trim();
        record.ConfirmationIdempotencyKey = idempotencyKey;
        if (ticketId is not null) record.TicketId = ticketId;
        if (eventId is not null) record.EventId = eventId;
        if (correlationId is not null) record.CorrelationId = correlationId;

        await db.SaveChangesAsync(cancellationToken);
        return Map(record, RecognitionStatus.Confirmed);
    }

    public async Task<StaffDetectionDto?> ConfirmExitAsync(
        long recognitionId, long sessionId, string plateNumber, string? note,
        long staffUserId, int lotId, string? captureId, string? ticketId,
        string? idempotencyKey, string? correlationId, CancellationToken cancellationToken)
    {
        var record = await db.RecognitionRecords.FirstOrDefaultAsync(x => x.Id == recognitionId, cancellationToken);
        if (record is null) return null;

        if (record.Purpose != RecognitionPurpose.Exit || record.ParkingLotId != lotId)
            throw new ImageValidationException("Recognition does not belong to an exit at the selected parking lot.");

        if (record.StaffUserId != staffUserId)
            throw new ImageValidationException("Only the staff member who captured this exit image may confirm it.");

        if (record.ConfirmedAt.HasValue)
        {
            if (record.MatchedParkingSessionId != sessionId)
                throw new ImageValidationException("This recognition was already confirmed for another session.");
            return Map(record, RecognitionStatus.Confirmed);
        }

        record.MatchedParkingSessionId = sessionId;
        record.ConfirmedPlateNumber = plateNumber.Trim();
        record.ConfirmedByStaffUserId = staffUserId;
        record.ConfirmedAt = DateTime.UtcNow;
        record.ConfirmationNote = note?.Trim();
        record.ConfirmationIdempotencyKey = idempotencyKey;
        if (ticketId is not null) record.TicketId = ticketId;
        if (correlationId is not null) record.CorrelationId = correlationId;

        await db.SaveChangesAsync(cancellationToken);
        return Map(record, RecognitionStatus.Confirmed);
    }

    public async Task<StaffDetectionDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var record = await db.RecognitionRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return record is null ? null : Map(record, ResolveStatus(record));
    }

    public async Task<StaffDetectionDto?> GetByCaptureIdAsync(string captureId, CancellationToken cancellationToken)
    {
        var record = await db.RecognitionRecords.AsNoTracking().FirstOrDefaultAsync(x => x.CaptureId == captureId, cancellationToken);
        return record is null ? null : Map(record, ResolveStatus(record));
    }

    private static bool HasImageSignature(byte[] bytes, string contentType)
    {
        if (bytes.Length < 12) return false;
        if (contentType == "image/jpeg" && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
        if (contentType == "image/png" && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        if (contentType == "image/webp" && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P') return true;
        return false;
    }

    private static bool IsMatch(string? raw, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(candidate)) return false;
        var cleanRaw = Regex.Replace(raw, "[^A-Za-z0-9]", "").ToUpperInvariant();
        var cleanCandidate = Regex.Replace(candidate, "[^A-Za-z0-9]", "").ToUpperInvariant();
        return cleanRaw == cleanCandidate;
    }

    private static string ResolveStatus(AiRecognitionRecord r)
    {
        if (r.ConfirmedAt.HasValue) return RecognitionStatus.Confirmed;
        if (string.IsNullOrWhiteSpace(r.PlateNumber)) return RecognitionStatus.Unreadable;
        if (r.MatchedParkingSessionId.HasValue) return RecognitionStatus.PendingConfirmation;
        return RecognitionStatus.PendingLink;
    }

    private static StaffDetectionDto Map(AiRecognitionRecord r, string status)
    {
        IReadOnlyList<int>? vBox = !string.IsNullOrEmpty(r.VehicleBboxJson) ? JsonSerializer.Deserialize<List<int>>(r.VehicleBboxJson, JsonOpts) : null;
        IReadOnlyList<int>? pBox = !string.IsNullOrEmpty(r.PlateBboxJson) ? JsonSerializer.Deserialize<List<int>>(r.PlateBboxJson, JsonOpts) : null;

        return new StaffDetectionDto(
            r.Id, r.StaffUserId, r.PlateNumber, r.RawPlateNumber, status, r.FailureReason,
            r.Confidence, r.IsLegit, r.VehicleConfidence, vBox, pBox, r.InferenceTimeMs,
            r.CreatedAt, r.ConfirmedPlateNumber, r.ConfirmedByStaffUserId, r.ConfirmedAt,
            r.ConfirmationNote, r.ParkingLotId, r.Purpose, r.MatchedParkingSessionId,
            r.CaptureId, r.ImageId, r.EventId, r.TicketId, r.MatchedParkingSessionId,
            r.CorrelationId, r.IdempotencyKey, r.ModelVersion);
    }
}
