using ParkingOperations.Application.Common.Interfaces;

namespace ParkingOperations.Infrastructure.Services;

public sealed class FileValidationService : IFileValidationService
{
    private const long MaxSizeBytes = 10 * 1024 * 1024; // 10MB
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    public FileValidationResult ValidateImageFile(Stream stream, string contentType, long contentLength)
    {
        if (contentLength <= 0)
        {
            return new FileValidationResult(false, "File cannot be empty.");
        }

        if (contentLength > MaxSizeBytes)
        {
            return new FileValidationResult(false, $"File size exceeds limit of {MaxSizeBytes / (1024 * 1024)}MB.");
        }

        if (string.IsNullOrWhiteSpace(contentType) || !AllowedMimeTypes.Contains(contentType))
        {
            return new FileValidationResult(false, "Only JPEG, PNG, and WebP images are allowed.");
        }

        if (!stream.CanRead)
        {
            return new FileValidationResult(false, "Image stream cannot be read.");
        }

        // Validate Magic Bytes (file signature)
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            Span<byte> header = stackalloc byte[12];
            var bytesRead = stream.Read(header);
            if (bytesRead < 4)
            {
                return new FileValidationResult(false, "Corrupt or invalid image file.");
            }

            var detectedMime = DetectMimeType(header[..bytesRead]);
            if (detectedMime == null || !AllowedMimeTypes.Contains(detectedMime))
            {
                return new FileValidationResult(false, "File content signature does not match allowed image types (JPEG, PNG, WebP).");
            }

            return new FileValidationResult(true, DetectedContentType: detectedMime);
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }
        }
    }

    private static string? DetectMimeType(ReadOnlySpan<byte> header)
    {
        // JPEG: FF D8 FF
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return "image/png";
        }

        // WebP: RIFF .... WEBP
        if (header.Length >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return "image/webp";
        }

        return null;
    }
}
