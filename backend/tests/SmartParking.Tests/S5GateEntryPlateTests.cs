using Microsoft.EntityFrameworkCore;
using SmartParking.Application.DTOs.Operations;
using SmartParking.Application.UseCases.ParkingOperations;
using SmartParking.Domain.Constants;
using SmartParking.Domain.Entities;
using SmartParking.Infrastructure.Services;
using SmartParking.Persistence.Data;
using SmartParking.Persistence.Repositories;
using Xunit;

namespace SmartParking.Tests;

/// <summary>
/// Sprint 5 – xe vãng lai vào tự động: AI đọc biển số lúc vào được lưu làm biển số TẠM (không phải chính thức),
/// để Staff khu vực đối chiếu ở bước xác minh ô. Lượt cũ không gửi biển số phải chạy y như trước.
/// </summary>
public class S5GateEntryPlateTests
{
    private const long AdminId = 1, GateStaffId = 2;
    private const int LotId = 1, ZoneId = 11;

    private static async Task<DbContextOptions<SmartParkingDbContext>> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<SmartParkingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var c = new TestSmartParkingDbContext(options);
        c.Roles.AddRange(new Role { Id = 1, Code = Roles.Admin, Name = "a" }, new Role { Id = 3, Code = Roles.Staff, Name = "s" });
        c.Users.AddRange(
            new User { Id = AdminId, Username = "admin", Email = "a@t.com", FullName = "a", PasswordHash = "x", RoleId = 1, IsActive = true, ApprovalStatus = "APPROVED" },
            new User { Id = GateStaffId, Username = "gate", Email = "g@t.com", FullName = "g", PasswordHash = "x", RoleId = 3, IsActive = true, ApprovalStatus = "APPROVED" });
        var lot = new ParkingLot { Id = LotId, Name = "Lot", Address = "x", TotalCapacity = 5, IsActive = true };
        var floor = new ParkingFloor { Id = 1, LotId = LotId, Lot = lot, FloorNumber = 1, Name = "T1", IsActive = true };
        var zone = new ParkingZone { Id = ZoneId, LotId = LotId, Lot = lot, FloorId = 1, Floor = floor, Code = "A", Name = "A", Type = "TRANSIENT" };
        c.AddRange(lot, floor, zone,
            new ParkingSlot { Id = 101, FloorId = 1, Floor = floor, ZoneId = ZoneId, Zone = zone, SlotCode = "A1", PhysicalStatus = SlotPhysicalStatuses.Empty, IsActive = true },
            new ParkingSlot { Id = 102, FloorId = 1, Floor = floor, ZoneId = ZoneId, Zone = zone, SlotCode = "A2", PhysicalStatus = SlotPhysicalStatuses.Empty, IsActive = true },
            new ParkingSlot { Id = 103, FloorId = 1, Floor = floor, ZoneId = ZoneId, Zone = zone, SlotCode = "A3", PhysicalStatus = SlotPhysicalStatuses.Empty, IsActive = true });
        c.StaffLotAssignments.Add(new StaffLotAssignment { UserId = GateStaffId, LotId = LotId, Duty = StaffDuties.Gate, IsActive = true, AssignedBy = AdminId });
        await c.SaveChangesAsync();
        return options;
    }

    private static ParkingOperationsService Ops(TestSmartParkingDbContext c) =>
        new(c, new ParkingAuthorizationService(c), new ParkingOperationsWriter(c));

    private static async Task<OperationSessionDto> EnterAsync(DbContextOptions<SmartParkingDbContext> o, string ticket, Guid? eventId = null, string? plate = null)
    {
        await using var c = new TestSmartParkingDbContext(o);
        return await Ops(c).Enter(LotId, new EntryRequest(eventId ?? Guid.NewGuid(), ZoneId, ticket, null, "auto", plate), GateStaffId, Roles.Staff, default);
    }

    [Fact]
    public async Task Entry_WithAiPlate_StoresItAsTemporaryOnly_AndLeavesTheOfficialPlateAndSlotUntouched()
    {
        var o = await SeedAsync();
        var s = await EnterAsync(o, "VL-0001", plate: " 51f-123.45 ");

        Assert.Equal("51F-123.45", s.TemporaryPlateNumber);
        Assert.Equal("", s.PlateNumber);
        Assert.Equal("WAITING_VERIFICATION", s.VerificationStatus);
        await using var c = new TestSmartParkingDbContext(o);
        Assert.All(await c.ParkingSlots.ToListAsync(), slot => Assert.Equal(SlotPhysicalStatuses.Empty, slot.PhysicalStatus));
        var audit = Assert.Single(await c.AuditLogs.Where(a => a.Action == "ISSUE_ENTRY_TICKET").ToListAsync());
        Assert.Contains("51F-123.45", audit.NewValues);
    }

    [Fact]
    public async Task Entry_WithoutPlate_BehavesAsBefore()
    {
        var o = await SeedAsync();
        var eventId = Guid.NewGuid();
        var first = await EnterAsync(o, "VL-0002", eventId);
        Assert.Null(first.TemporaryPlateNumber);

        // Gửi lại đúng yêu cầu cũ (không biển số) vẫn là cùng một lượt: dấu vân tay không đổi.
        var again = await EnterAsync(o, "VL-0002", eventId);
        Assert.Equal(first.Id, again.Id);
        await using var c = new TestSmartParkingDbContext(o);
        Assert.Equal(1, await c.ParkingSessions.CountAsync());
    }

    [Fact]
    public async Task Entry_Replay_WithSamePlateIsIdempotent_AndAnotherPlateIsAMismatch()
    {
        var o = await SeedAsync();
        var eventId = Guid.NewGuid();
        var first = await EnterAsync(o, "VL-0003", eventId, "51F-123.45");
        var again = await EnterAsync(o, "VL-0003", eventId, "51F12345");
        Assert.Equal(first.Id, again.Id);

        var error = await Assert.ThrowsAsync<ParkingOperationException>(() => EnterAsync(o, "VL-0003", eventId, "30H-999.99"));
        Assert.Equal("EVENT_MISMATCH", error.Code);
        // Cùng EventId mà lần đầu không có biển số, lần sau thêm biển số: nội dung đã khác nên không được dùng lại.
        var noPlate = Guid.NewGuid();
        await EnterAsync(o, "VL-0004", noPlate);
        Assert.Equal("EVENT_MISMATCH", (await Assert.ThrowsAsync<ParkingOperationException>(() => EnterAsync(o, "VL-0004", noPlate, "51F-123.45"))).Code);
    }

    [Fact]
    public async Task Entry_RejectsAnInvalidPlate_AndCreatesNothing()
    {
        var o = await SeedAsync();
        var error = await Assert.ThrowsAsync<ParkingOperationException>(() => EnterAsync(o, "VL-0005", plate: "ABC"));
        Assert.Equal("INVALID_PLATE", error.Code);
        Assert.Equal(400, error.Status);
        await using var c = new TestSmartParkingDbContext(o);
        Assert.Equal(0, await c.ParkingSessions.CountAsync());
        Assert.Equal(0, await c.ParkingTickets.CountAsync());
    }

    [Fact]
    public async Task Verify_WithTheSameAiPlate_NeedsNoCorrectionReason_ButADifferentPlateDoes()
    {
        var o = await SeedAsync();
        var s = await EnterAsync(o, "VL-0006", plate: "51F-123.45");

        await using (var c = new TestSmartParkingDbContext(o))
        {
            var mismatch = await Assert.ThrowsAsync<ParkingOperationException>(() => Ops(c).Verify(LotId, s.Id,
                new VerifyVehicleRequest(101, "30H-999.99", "PETROL", s.Revision, true, null), AdminId, Roles.Admin, default));
            Assert.Equal(400, mismatch.Status);
        }
        await using (var c = new TestSmartParkingDbContext(o))
        {
            var ok = await Ops(c).Verify(LotId, s.Id,
                new VerifyVehicleRequest(101, "51F12345", "PETROL", s.Revision, true, null), AdminId, Roles.Admin, default);
            Assert.Equal("VERIFIED", ok.VerificationStatus);
            Assert.Equal("51F12345", ok.PlateNumber); // biển số chính thức được lưu ở dạng chuẩn hóa, không dấu
        }
    }
}
