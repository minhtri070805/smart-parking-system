using Microsoft.EntityFrameworkCore;
using Moq;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.UseCases.CashHandovers;
using ParkingOperations.Domain.Entities;
using ParkingOperations.Infrastructure;
using ParkingOperations.Persistence.Data;
using Xunit;

namespace ParkingOperations.Tests.Unit;

public class CashHandoverServiceTests
{
    private static OperationsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OperationsDbContext(options);
    }

    [Fact]
    public async Task CreateAsync_ShiftsNotFound_Returns404()
    {
        using var db = CreateDbContext();
        var coreAccessMock = new Mock<ICoreStaffAccessClient>();

        var service = new CashHandoverService(db, coreAccessMock.Object);
        var req = new CreateCashHandoverRequest
        {
            OutgoingShiftId = Guid.NewGuid(),
            IncomingShiftId = Guid.NewGuid(),
            OutgoingStaffId = 101L,
            IncomingStaffId = 102L
        };

        var result = await service.CreateAsync(req, managerId: 1, "token", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ConfirmAsync_NonExistentHandover_Returns404()
    {
        using var db = CreateDbContext();
        var coreAccessMock = new Mock<ICoreStaffAccessClient>();

        var service = new CashHandoverService(db, coreAccessMock.Object);
        var result = await service.ConfirmAsync(Guid.NewGuid(), 500000m, actorId: 101L, role: "Staff", "token", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ConfirmAsync_CallerNotAssignedStaff_Returns403()
    {
        using var db = CreateDbContext();
        var handover = new CashHandover
        {
            Id = Guid.NewGuid(),
            LotId = 1,
            BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
            FromShiftCode = "MORNING",
            ToShiftCode = "AFTERNOON",
            OutgoingStaffId = 101L,
            IncomingStaffId = 102L,
            CreatedByManagerId = 1L,
            CreatedAtUtc = DateTime.UtcNow,
            Status = CashHandoverStatuses.AwaitingConfirmations,
            Version = 1
        };
        db.CashHandovers.Add(handover);
        await db.SaveChangesAsync();

        var coreAccessMock = new Mock<ICoreStaffAccessClient>();
        var service = new CashHandoverService(db, coreAccessMock.Object);

        // Caller is 999L (neither outgoing 101 nor incoming 102)
        var result = await service.ConfirmAsync(handover.Id, 500000m, actorId: 999L, role: "Staff", "token", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }
}
