using ParkingOperations.Infrastructure.Services;
using Xunit;

namespace ParkingOperations.Tests.Unit;

public class FileValidationServiceTests
{
    private readonly FileValidationService _service = new();

    [Fact]
    public void ValidateImageFile_EmptyStream_ReturnsInvalid()
    {
        using var stream = new MemoryStream();
        var result = _service.ValidateImageFile(stream, "image/jpeg", 0);

        Assert.False(result.IsValid);
        Assert.Contains("empty", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateImageFile_OversizedStream_ReturnsInvalid()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var result = _service.ValidateImageFile(stream, "image/jpeg", 11 * 1024 * 1024);

        Assert.False(result.IsValid);
        Assert.Contains("exceeds limit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateImageFile_InvalidMimeType_ReturnsInvalid()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var result = _service.ValidateImageFile(stream, "application/pdf", 3);

        Assert.False(result.IsValid);
        Assert.Contains("Only JPEG, PNG, and WebP", result.ErrorMessage);
    }

    [Fact]
    public void ValidateImageFile_ValidJpegSignature_ReturnsValid()
    {
        // JPEG magic bytes: FF D8 FF
        byte[] jpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
        using var stream = new MemoryStream(jpegHeader);

        var result = _service.ValidateImageFile(stream, "image/jpeg", jpegHeader.Length);

        Assert.True(result.IsValid);
        Assert.Equal("image/jpeg", result.DetectedContentType);
    }

    [Fact]
    public void ValidateImageFile_ValidPngSignature_ReturnsValid()
    {
        // PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
        byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        using var stream = new MemoryStream(pngHeader);

        var result = _service.ValidateImageFile(stream, "image/png", pngHeader.Length);

        Assert.True(result.IsValid);
        Assert.Equal("image/png", result.DetectedContentType);
    }

    [Fact]
    public void ValidateImageFile_SpoofedMimeType_ReturnsInvalid()
    {
        // Gửi content-type là image/png nhưng header là file text "HELLO"
        byte[] fakeHeader = "HELLO WORLD"u8.ToArray();
        using var stream = new MemoryStream(fakeHeader);

        var result = _service.ValidateImageFile(stream, "image/png", fakeHeader.Length);

        Assert.False(result.IsValid);
        Assert.Contains("signature does not match", result.ErrorMessage);
    }
}
