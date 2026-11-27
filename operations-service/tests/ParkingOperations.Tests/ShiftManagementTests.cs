using Microsoft.EntityFrameworkCore;
using Moq;
using ParkingOperations.Application.Common.Interfaces;
using ParkingOperations.Application.UseCases.ShiftManagement;
using ParkingOperations.Domain.Entities;
using ParkingOperations.Infrastructure;
using ParkingOperations.Persistence.Data;

namespace ParkingOperations.Tests;

public sealed class ShiftManagementTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static OperationsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new OperationsDbContext(options);
    }

    private static Mock<ICoreStaffAccessClient> CreateCoreAccess(bool managerAssigned = true, bool staffActive = true)
    {
        var mock = new Mock<ICoreStaffAccessClient>();
        mock.Setup(x => x.IsCallerAssignedToLotAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(managerAssigned);
        mock.Setup(x => x.IsActiveStaffAtLotAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staffActive);
        mock.Setup(x => x.GetActiveStaffAtLotAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StaffOptionDto(10, "Nguyen Van A"), new StaffOptionDto(11, "Tran Thi B")]);
        return mock;
    }

    [Fact]
    public async Task CreateAsync_RejectsPastShift()
    {
        await using var db = CreateDb();
        // 2026-11-27 10:00 VN = 03:00 UTC -> MORNING (05:00-13:00 VN) on 2026-11-26 has ended.
        var now = new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero);
        var service = new ParkingShiftService(db, CreateCoreAccess().Object, new FixedTimeProvider(now));

        var result = await service.CreateAsync(
            new CreateShiftRequest { LotId = 1, BusinessDate = new DateOnly(2026, 11, 26), ShiftCode = ParkingShiftCodes.Morning },
            managerId: 100, token: "jwt", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task AssignAsync_RejectsPastShift()
    {
        await using var db = CreateDb();
        var shift = new ParkingShift
        {
            Id = Guid.NewGuid(),
            LotId = 1,
            BusinessDate = new DateOnly(2026, 11, 26),
            Code = ParkingShiftCodes.Morning,
            StartsAtUtc = new DateTime(2026, 11, 25, 22, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 11, 26, 6, 0, 0, DateTimeKind.Utc),
            Status = ParkingShiftStatuses.Planned,
            CreatedByManagerId = 100,
            CreatedAtUtc = new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc),
            Version = 1
        };
        db.ParkingShifts.Add(shift);
        await db.SaveChangesAsync();

        var now = new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero);
        var service = new ParkingShiftService(db, CreateCoreAccess().Object, new FixedTimeProvider(now));

        var result = await service.AssignAsync(
            shift.Id,
            new AssignStaffToShiftRequest { StaffId = 10, DutyCode = ParkingShiftDuties.Gate },
            managerId: 100, token: "jwt", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task AssignAsync_RejectsCompletedShift()
    {
        await using var db = CreateDb();
        var shift = new ParkingShift
        {
            Id = Guid.NewGuid(),
            LotId = 1,
            BusinessDate = new DateOnly(2026, 11, 27),
            Code = ParkingShiftCodes.Morning,
            StartsAtUtc = new DateTime(2026, 11, 26, 22, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 11, 27, 6, 0, 0, DateTimeKind.Utc),
            Status = ParkingShiftStatuses.Completed,
            CreatedByManagerId = 100,
            CreatedAtUtc = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc),
            Version = 2
        };
        db.ParkingShifts.Add(shift);
        await db.SaveChangesAsync();

        var now = new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero);
        var service = new ParkingShiftService(db, CreateCoreAccess().Object, new FixedTimeProvider(now));

        var result = await service.AssignAsync(
            shift.Id,
            new AssignStaffToShiftRequest { StaffId = 10, DutyCode = ParkingShiftDuties.Gate },
            managerId: 100, token: "jwt", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task StartAsync_RejectsBeforeStartTime()
    {
        await using var db = CreateDb();
        var shift = new ParkingShift
        {
            Id = Guid.NewGuid(),
            LotId = 1,
            BusinessDate = new DateOnly(2026, 11, 27),
            Code = ParkingShiftCodes.Afternoon,
            StartsAtUtc = new DateTime(2026, 11, 27, 6, 0, 0, DateTimeKind.Utc), // 13:00 VN
            EndsAtUtc = new DateTime(2026, 11, 27, 14, 0, 0, DateTimeKind.Utc),  // 21:00 VN
            Status = ParkingShiftStatuses.Planned,
            CreatedByManagerId = 100,
            CreatedAtUtc = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc),
            Version = 1,
            Assignments =
            [
                new ShiftAssignment { Id = Guid.NewGuid(), StaffId = 10, DutyCode = ParkingShiftDuties.Both, AssignedByManagerId = 100, AssignedAtUtc = DateTime.UtcNow }
            ]
        };
        db.ParkingShifts.Add(shift);
        await db.SaveChangesAsync();

        var now = new DateTimeOffset(2026, 11, 27, 5, 0, 0, TimeSpan.Zero); // 12:00 VN (before 13:00)
        var service = new ParkingShiftService(db, CreateCoreAccess().Object, new FixedTimeProvider(now));

        var result = await service.StartAsync(shift.Id, "jwt", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task StartAsync_RejectsAfterEndTime()
    {
        await using var db = CreateDb();
        var shift = new ParkingShift
        {
            Id = Guid.NewGuid(),
            LotId = 1,
            BusinessDate = new DateOnly(2026, 11, 27),
            Code = ParkingShiftCodes.Morning,
            StartsAtUtc = new DateTime(2026, 11, 26, 22, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 11, 27, 6, 0, 0, DateTimeKind.Utc), // 13:00 VN
            Status = ParkingShiftStatuses.Planned,
            CreatedByManagerId = 100,
            CreatedAtUtc = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc),
            Version = 1,
            Assignments =
            [
                new ShiftAssignment { Id = Guid.NewGuid(), StaffId = 10, DutyCode = ParkingShiftDuties.Gate, AssignedByManagerId = 100, AssignedAtUtc = DateTime.UtcNow }
            ]
        };
        db.ParkingShifts.Add(shift);
        await db.SaveChangesAsync();

        var now = new DateTimeOffset(2026, 11, 27, 6, 0, 0, TimeSpan.Zero); // exact end time
        var service = new ParkingShiftService(db, CreateCoreAccess().Object, new FixedTimeProvider(now));

        var result = await service.StartAsync(shift.Id, "jwt", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task StartAsync_RejectsWithoutAssignments()
    {
        await using var db = CreateDb();
        var shift = new ParkingShift
        {
            Id = Guid.NewGuid(),
            LotId = 1,
            BusinessDate = new DateOnly(2026, 11, 27),
            Code = ParkingShiftCodes.Morning,
            StartsAtUtc = new DateTime(2026, 11, 26, 22, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 11, 27, 6, 0, 0, DateTimeKind.Utc),
            Status = ParkingShiftStatuses.Planned,
            CreatedByManagerId = 100,
            CreatedAtUtc = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc),
            Version = 1
        };
        db.ParkingShifts.Add(shift);
        await db.SaveChangesAsync();

        var now = new DateTimeOffset(2026, 11, 27, 1, 0, 0, TimeSpan.Zero);
        var service = new ParkingShiftService(db, CreateCoreAccess().Object, new FixedTimeProvider(now));

        var result = await service.StartAsync(shift.Id, "jwt", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task AssignAsync_RejectsDuplicateStaffInSameShift()
    {
        await using var db = CreateDb();
        var shift = new ParkingShift
        {
            Id = Guid.NewGuid(),
            LotId = 1,
            BusinessDate = new DateOnly(2026, 11, 28),
            Code = ParkingShiftCodes.Morning,
            StartsAtUtc = new DateTime(2026, 11, 27, 22, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 11, 28, 6, 0, 0, DateTimeKind.Utc),
            Status = ParkingShiftStatuses.Planned,
            CreatedByManagerId = 100,
            CreatedAtUtc = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc),
            Version = 1,
            Assignments =
            [
                new ShiftAssignment { Id = Guid.NewGuid(), StaffId = 10, DutyCode = ParkingShiftDuties.Gate, AssignedByManagerId = 100, AssignedAtUtc = DateTime.UtcNow }
            ]
        };
        db.ParkingShifts.Add(shift);
        await db.SaveChangesAsync();

        var now = new DateTimeOffset(2026, 11, 27, 11, 0, 0, TimeSpan.Zero);
        var service = new ParkingShiftService(db, CreateCoreAccess().Object, new FixedTimeProvider(now));

        var result = await service.AssignAsync(
            shift.Id,
            new AssignStaffToShiftRequest { StaffId = 10, DutyCode = ParkingShiftDuties.SlotVerifier },
            managerId: 100, token: "jwt", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task ManagerUnassignedToLot_CannotOperate()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero);
        var service = new ParkingShiftService(db, CreateCoreAccess(managerAssigned: false).Object, new FixedTimeProvider(now));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoadManagerWorkspaceAsync(99, new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 29), "jwt", CancellationToken.None));

        var createResult = await service.CreateAsync(
            new CreateShiftRequest { LotId = 99, BusinessDate = new DateOnly(2026, 11, 28), ShiftCode = ParkingShiftCodes.Morning },
            managerId: 100, token: "jwt", CancellationToken.None);

        Assert.False(createResult.Success);
        Assert.Equal(403, createResult.StatusCode);
    }
}
