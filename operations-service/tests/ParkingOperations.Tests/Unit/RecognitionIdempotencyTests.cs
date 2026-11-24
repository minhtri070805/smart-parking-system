using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using ParkingOperations.Application.Common.Exceptions;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.DTOs.Recognition;
using ParkingOperations.Application.Services;
using Xunit;

namespace ParkingOperations.Tests.Unit;

public class RecognitionIdempotencyTests
{
    private readonly Mock<IRecognitionClient> _clientMock = new();
    private readonly Mock<IFileValidationService> _validationMock = new();
    private readonly Mock<IIdempotencyCache> _cacheMock = new();
    private readonly Mock<ILogger<RecognitionFacade>> _loggerMock = new();
    private readonly RecognitionFacade _facade;

    public RecognitionIdempotencyTests()
    {
        _validationMock
            .Setup(v => v.ValidateImageFile(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>()))
            .Returns(new FileValidationResult(true, null, "image/jpeg"));

        _facade = new RecognitionFacade(_clientMock.Object, _validationMock.Object, _cacheMock.Object, _loggerMock.Object);
    }

    private static RecognitionResultDto CreateSampleDto(
        long id,
        string plate,
        string status,
        string purpose,
        string? captureId = null,
        string? imageId = null,
        string? eventId = null,
        string? ticketId = null,
        string? idempotencyKey = null,
        int? lotId = null) =>
        new(
            Id: id,
            StaffUserId: 1L,
            PlateNumber: plate,
            RawPlateNumber: plate,
            Status: status,
            FailureReason: null,
            Confidence: 0.98,
            IsLegit: true,
            VehicleConfidence: 0.95,
            VehicleBbox: null,
            PlateBbox: null,
            InferenceTimeMs: 15.0,
            CreatedAt: DateTime.UtcNow,
            ConfirmedPlateNumber: status == "CONFIRMED" ? plate : null,
            ConfirmedByStaffUserId: status == "CONFIRMED" ? 1L : null,
            ConfirmedAt: status == "CONFIRMED" ? DateTime.UtcNow : null,
            ConfirmationNote: "OK",
            ParkingLotId: lotId,
            Purpose: purpose,
            MatchedParkingSessionId: null,
            CaptureId: captureId,
            ImageId: imageId,
            EventId: eventId,
            TicketId: ticketId,
            SessionId: null,
            CorrelationId: "corr-test",
            IdempotencyKey: idempotencyKey,
            ModelVersion: "v1.0"
        );

    [Fact]
    public async Task DetectAsync_WithCachedKey_ReturnsCachedResultWithoutCallingClient()
    {
        var cachedDto = CreateSampleDto(123, "29A-12345", "SUCCESS", "ENTRY", "cap-1", "img-1", "evt-1", null, "idem-key-1", 10);
        var cachedJson = JsonSerializer.Serialize(cachedDto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        _cacheMock
            .Setup(c => c.AcquireOrGetAsync("detect:idem-key-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyStatus.Cached, cachedJson));

        using var stream = new MemoryStream([1, 2, 3]);
        var result = await _facade.DetectAsync(
            stream, "image/jpeg", 3, staffUserId: 1, lotId: 10, purpose: "ENTRY",
            captureId: "cap-1", imageId: "img-1", eventId: "evt-1",
            idempotencyKey: "idem-key-1", correlationId: "corr-1");

        Assert.NotNull(result);
        Assert.Equal(123, result.Id);
        Assert.Equal("29A-12345", result.PlateNumber);

        _clientMock.Verify(c => c.DetectAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(),
            It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DetectAsync_InFlightLockActive_ThrowsOperationConflictException()
    {
        _cacheMock
            .Setup(c => c.AcquireOrGetAsync("detect:concurrent-key", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyStatus.InFlight, (string?)null));

        using var stream = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<OperationConflictException>(() =>
            _facade.DetectAsync(
                stream, "image/jpeg", 3, staffUserId: 1, lotId: 10, purpose: "ENTRY",
                captureId: "cap-1", imageId: "img-1", eventId: "evt-1",
                idempotencyKey: "concurrent-key", correlationId: "corr-conflict"));
    }

    [Fact]
    public async Task DetectAsync_CacheMiss_CallsClientAndSavesCache()
    {
        _cacheMock
            .Setup(c => c.AcquireOrGetAsync("detect:idem-key-2", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyStatus.None, (string?)null));

        var downstreamDto = CreateSampleDto(456, "30B-99999", "SUCCESS", "ENTRY", "cap-2", "img-2", "evt-2", null, "idem-key-2", 10);

        _clientMock
            .Setup(c => c.DetectAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(),
                1L, 10, "ENTRY", "cap-2", "img-2", "evt-2", "idem-key-2", "corr-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(downstreamDto);

        using var stream = new MemoryStream([1, 2, 3]);
        var result = await _facade.DetectAsync(
            stream, "image/jpeg", 3, staffUserId: 1, lotId: 10, purpose: "ENTRY",
            captureId: "cap-2", imageId: "img-2", eventId: "evt-2",
            idempotencyKey: "idem-key-2", correlationId: "corr-2");

        Assert.NotNull(result);
        Assert.Equal(456, result.Id);
        _clientMock.Verify(c => c.DetectAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(),
            1L, 10, "ENTRY", "cap-2", "img-2", "evt-2", "idem-key-2", "corr-2", It.IsAny<CancellationToken>()), Times.Once);

        _cacheMock.Verify(c => c.SetAsync("detect:idem-key-2", It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmAsync_WithCachedKey_ReturnsCachedResultWithoutCallingClient()
    {
        var cachedDto = CreateSampleDto(555, "51A-11111", "CONFIRMED", "ENTRY", "cap-5", null, "evt-5", "101", "idem-confirm-1", 5);
        var cachedJson = JsonSerializer.Serialize(cachedDto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        _cacheMock
            .Setup(c => c.AcquireOrGetAsync("confirm:555:idem-confirm-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyStatus.Cached, cachedJson));

        var req = new ConfirmRecognitionRequestDto("51A-11111", "OK", LotId: 5, CaptureId: "cap-5", EventId: "evt-5", TicketId: "101", IdempotencyKey: "idem-confirm-1");
        var result = await _facade.ConfirmAsync(555, req, staffUserId: 2, correlationId: "corr-c1");

        Assert.NotNull(result);
        Assert.Equal(555, result.Id);
        Assert.Equal("CONFIRMED", result.Status);

        _clientMock.Verify(c => c.ConfirmAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmExitAsync_WithCachedKey_ReturnsCachedResultWithoutCallingClient()
    {
        var cachedDto = CreateSampleDto(777, "51A-11111", "CONFIRMED", "EXIT", "cap-7", null, null, "202", "idem-exit-1", 5);
        var cachedJson = JsonSerializer.Serialize(cachedDto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        _cacheMock
            .Setup(c => c.AcquireOrGetAsync("confirm-exit:777:idem-exit-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyStatus.Cached, cachedJson));

        var req = new ConfirmExitRecognitionRequestDto(
            PlateNumber: "51A-11111",
            Note: "OK",
            LotId: 5,
            SessionId: 999L,
            CaptureId: "cap-7",
            TicketId: "202",
            IdempotencyKey: "idem-exit-1");

        var result = await _facade.ConfirmExitAsync(777, req, staffUserId: 2, correlationId: "corr-e1");

        Assert.NotNull(result);
        Assert.Equal(777, result.Id);
        Assert.Equal("EXIT", result.Purpose);

        _clientMock.Verify(c => c.ConfirmExitAsync(
            It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
