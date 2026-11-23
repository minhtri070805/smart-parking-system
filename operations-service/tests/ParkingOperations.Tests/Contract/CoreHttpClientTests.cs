using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using ParkingOperations.Application.DTOs.Core;
using ParkingOperations.Infrastructure.Clients;
using Xunit;

namespace ParkingOperations.Tests.Contract;

public class CoreHttpClientTests
{
    private readonly Mock<ILogger<CoreHttpClient>> _loggerMock = new();

    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handlerFunc(request));
    }

    [Fact]
    public async Task ValidateStaffAssignmentAsync_ActiveDutyGate_ReturnsValid()
    {
        var assignedLots = new List<AssignedLotSummaryDto>
        {
            new(LotId: 1, LotName: "Lot A", LotAddress: "123 St", IsActive: true, AssignedAt: DateTime.UtcNow, Duty: "GATE", FloorId: null, ZoneId: null)
        };

        var responseJson = JsonSerializer.Serialize(new CoreApiResponse<List<AssignedLotSummaryDto>>("Success", assignedLots));
        var handler = new DelegatingHandlerStub(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        var coreClient = new CoreHttpClient(client, _loggerMock.Object);

        var result = await coreClient.ValidateStaffAssignmentAsync("valid-token", lotId: 1, requiredDuty: "GATE");

        Assert.True(result.IsValid);
        Assert.Equal("GATE", result.Duty);
    }

    [Fact]
    public async Task ValidateStaffAssignmentAsync_WrongLotId_ReturnsInvalid()
    {
        var assignedLots = new List<AssignedLotSummaryDto>
        {
            new(LotId: 1, LotName: "Lot A", LotAddress: "123 St", IsActive: true, AssignedAt: DateTime.UtcNow, Duty: "GATE", FloorId: null, ZoneId: null)
        };

        var responseJson = JsonSerializer.Serialize(new CoreApiResponse<List<AssignedLotSummaryDto>>("Success", assignedLots));
        var handler = new DelegatingHandlerStub(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        var coreClient = new CoreHttpClient(client, _loggerMock.Object);

        var result = await coreClient.ValidateStaffAssignmentAsync("valid-token", lotId: 99, requiredDuty: "GATE");

        Assert.False(result.IsValid);
        Assert.Contains("not actively assigned", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateStaffAssignmentAsync_MismatchedDuty_ReturnsInvalid()
    {
        var assignedLots = new List<AssignedLotSummaryDto>
        {
            new(LotId: 1, LotName: "Lot A", LotAddress: "123 St", IsActive: true, AssignedAt: DateTime.UtcNow, Duty: "SLOT_VERIFIER", FloorId: null, ZoneId: null)
        };

        var responseJson = JsonSerializer.Serialize(new CoreApiResponse<List<AssignedLotSummaryDto>>("Success", assignedLots));
        var handler = new DelegatingHandlerStub(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
        var coreClient = new CoreHttpClient(client, _loggerMock.Object);

        // Yêu cầu GATE nhưng duty hiện tại là SLOT_VERIFIER
        var result = await coreClient.ValidateStaffAssignmentAsync("valid-token", lotId: 1, requiredDuty: "GATE");

        Assert.False(result.IsValid);
        Assert.Contains("does not have required 'GATE' duty", result.ErrorMessage);
    }
}
