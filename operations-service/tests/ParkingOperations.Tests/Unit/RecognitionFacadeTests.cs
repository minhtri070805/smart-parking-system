using Microsoft.Extensions.Logging;
using Moq;
using ParkingOperations.Application.Common.Exceptions;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.DTOs.Recognition;
using ParkingOperations.Application.Services;
using Xunit;

namespace ParkingOperations.Tests.Unit;

public class RecognitionFacadeTests
{
    private readonly Mock<IRecognitionClient> _clientMock = new();
    private readonly Mock<IFileValidationService> _validationMock = new();
    private readonly Mock<IIdempotencyCache> _idempotencyCacheMock = new();
    private readonly Mock<ILogger<RecognitionFacade>> _loggerMock = new();
    private readonly RecognitionFacade _facade;

    public RecognitionFacadeTests()
    {
        _facade = new RecognitionFacade(_clientMock.Object, _validationMock.Object, _idempotencyCacheMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task DetectAsync_InvalidLotId_ThrowsImageValidationException()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<ImageValidationException>(() =>
            _facade.DetectAsync(stream, "image/jpeg", 100, staffUserId: 1, lotId: 0, purpose: "ENTRY", null, null, null, null, null));
    }

    [Fact]
    public async Task DetectAsync_InvalidPurpose_ThrowsImageValidationException()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<ImageValidationException>(() =>
            _facade.DetectAsync(stream, "image/jpeg", 100, staffUserId: 1, lotId: 1, purpose: "UNKNOWN", null, null, null, null, null));
    }

    [Fact]
    public async Task DetectAsync_FileValidationFails_ThrowsImageValidationException()
    {
        using var stream = new MemoryStream();
        _validationMock
            .Setup(v => v.ValidateImageFile(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>()))
            .Returns(new FileValidationResult(false, "Corrupt file."));

        await Assert.ThrowsAsync<ImageValidationException>(() =>
            _facade.DetectAsync(stream, "image/jpeg", 100, staffUserId: 1, lotId: 1, purpose: "ENTRY", null, null, null, null, null));
    }

    [Fact]
    public async Task ConfirmAsync_InvalidPlateNumber_ThrowsImageValidationException()
    {
        var request = new ConfirmRecognitionRequestDto(string.Empty, "Note", LotId: 1);
        await Assert.ThrowsAsync<ImageValidationException>(() =>
            _facade.ConfirmAsync(1, request, staffUserId: 1, correlationId: null));
    }

    [Fact]
    public async Task ConfirmExitAsync_MissingSessionId_ThrowsImageValidationException()
    {
        var request = new ConfirmExitRecognitionRequestDto("51F-12345", "Note", LotId: 1, SessionId: 0);
        await Assert.ThrowsAsync<ImageValidationException>(() =>
            _facade.ConfirmExitAsync(1, request, staffUserId: 1, correlationId: null));
    }
}
