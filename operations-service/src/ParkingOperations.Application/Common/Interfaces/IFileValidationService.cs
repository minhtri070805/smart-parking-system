namespace ParkingOperations.Application.Common.Interfaces;

public interface IFileValidationService
{
    FileValidationResult ValidateImageFile(Stream stream, string contentType, long contentLength);
}

public sealed record FileValidationResult(
    bool IsValid,
    string? ErrorMessage = null,
    string? DetectedContentType = null
);
