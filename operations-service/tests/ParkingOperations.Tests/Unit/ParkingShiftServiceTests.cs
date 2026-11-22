using Microsoft.EntityFrameworkCore;
using Moq;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.UseCases.ShiftManagement;
using ParkingOperations.Domain.Entities;
using ParkingOperations.Infrastructure;
using ParkingOperations.Persistence.Data;
using Xunit;

namespace ParkingOperations.Tests.Unit;

public class ParkingShiftServiceTests
{
    private static OperationsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OperationsDbContext(options);
    }

    [Fact]
    public async Task CreateAsync_StaffNotAssignedToLot_Returns403()
    {
        using var db = CreateDbContext();
        var coreAccessMock = new Mock<ICoreStaffAccessClient>();
        coreAccessMock
            .Setup(c => c.IsCallerAssignedToLotAsync("token", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = new ParkingShiftService(db, coreAccessMock.Object);
        var req = new CreateShiftRequest
        {
            LotId = 10,
            BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ShiftCode = ParkingShiftCodes.Morning
        };

        var result = await service.CreateAsync(req, managerId: 1, "token", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesShiftSuccessfully()
    {
        using var db = CreateDbContext();
        var coreAccessMock = new Mock<ICoreStaffAccessClient>();
        coreAccessMock
            .Setup(c => c.IsCallerAssignedToLotAsync("token", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new ParkingShiftService(db, coreAccessMock.Object);
        var req = new CreateShiftRequest
        {
            LotId = 10,
            BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ShiftCode = ParkingShiftCodes.Morning
        };

        var result = await service.CreateAsync(req, managerId: 1, "token", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Equal(10, result.Data.LotId);
        Assert.Equal(ParkingShiftCodes.Morning, result.Data.Code);

        var inDb = await db.ParkingShifts.FirstOrDefaultAsync(s => s.LotId == 10);
        Assert.NotNull(inDb);
        Assert.Equal(ParkingShiftStatuses.Planned, inDb.Status);
    }

    [Fact]
    public async Task StartAsync_NonExistentShift_Returns404()
    {
        using var db = CreateDbContext();
        var coreAccessMock = new Mock<ICoreStaffAccessClient>();

        var service = new ParkingShiftService(db, coreAccessMock.Object);
        var result = await service.StartAsync(Guid.NewGuid(), "token", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task StartAsync_ShiftNotInPlannedStatus_Returns409()
    {
        using var db = CreateDbContext();
        var shift = new ParkingShift
        {
            Id = Guid.NewGuid(),
            LotId = 10,
            BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Code = ParkingShiftCodes.Morning,
            StartsAtUtc = DateTime.UtcNow,
            EndsAtUtc = DateTime.UtcNow.AddHours(8),
            Status = ParkingShiftStatuses.InProgress,
            CreatedByManagerId = 1,
            CreatedAtUtc = DateTime.UtcNow,
            Version = 1
        };
        db.ParkingShifts.Add(shift);
        await db.SaveChangesAsync();

        var coreAccessMock = new Mock<ICoreStaffAccessClient>();
        coreAccessMock
            .Setup(c => c.IsCallerAssignedToLotAsync("token", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new ParkingShiftService(db, coreAccessMock.Object);
        var result = await service.StartAsync(shift.Id, "token", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
    }
}
