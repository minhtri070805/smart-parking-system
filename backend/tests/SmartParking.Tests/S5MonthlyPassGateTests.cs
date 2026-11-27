using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartParking.Application.Common.Security;
using SmartParking.Application.DTOs.Operations;
using SmartParking.Application.DTOs.Sessions;
using SmartParking.Application.UseCases.ParkingOperations;
using SmartParking.Domain.Constants;
using SmartParking.Domain.Entities;
using SmartParking.Infrastructure.Services;
using SmartParking.Persistence.Data;
using SmartParking.Persistence.Repositories;
using Xunit;

namespace SmartParking.Tests;

/// <summary>
/// Sprint 5 (Phụng) – xe vé tháng ở cổng: tra vé theo biển số, vào khu cố định không cần vé giấy,
/// ra không cần Payment clearance. Các lượt vãng lai vẫn bị chặn nếu chưa PAID.
/// </summary>
public class S5MonthlyPassGateTests
{
    private const long AdminId = 1, GateStaffId = 2, OtherLotStaffId = 3, VerifierOnlyId = 4, OwnerId = 10;
    private const int LotId = 1, OtherLotId = 2, FixedZone = 11, TransientZone = 12;
    private const string Plate = "51F-123.45";

    private sealed class Fx
    {
        public DbContextOptions<SmartParkingDbContext> Options = null!;
        public TestSmartParkingDbContext Ctx() => new(Options);

        public static async Task<Fx> CreateAsync()
        {
            var f = new Fx { Options = new DbContextOptionsBuilder<SmartParkingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options };
            await using var c = f.Ctx();
            c.Roles.AddRange(new Role { Id = 1, Code = Roles.Admin, Name = "a" }, new Role { Id = 3, Code = Roles.Staff, Name = "s" }, new Role { Id = 4, Code = Roles.User, Name = "u" });
            await c.SaveChangesAsync();
            foreach (var (id, role) in new[] { (AdminId, 1), (GateStaffId, 3), (OtherLotStaffId, 3), (VerifierOnlyId, 3), (OwnerId, 4) })
                c.Users.Add(new User { Id = id, Username = $"u{id}", Email = $"u{id}@t.com", FullName = "n", PasswordHash = "x", RoleId = role, IsActive = true, ApprovalStatus = "APPROVED" });
            var lot = new ParkingLot { Id = LotId, Name = "Lot1", Address = "x", TotalCapacity = 10, IsActive = true };
            var other = new ParkingLot { Id = OtherLotId, Name = "Lot2", Address = "x", TotalCapacity = 10, IsActive = true };
            var ground = new ParkingFloor { Id = 1, LotId = LotId, Lot = lot, FloorNumber = 0, Name = "Tầng G", IsActive = true };
            var upper = new ParkingFloor { Id = 2, LotId = LotId, Lot = lot, FloorNumber = 1, Name = "Tầng 1", IsActive = true };
            c.AddRange(lot, other, ground, upper,
                new ParkingZone { Id = FixedZone, LotId = LotId, Lot = lot, FloorId = 1, Floor = ground, Code = "G", Name = "Vé tháng", Type = "FIXED" },
                new ParkingZone { Id = TransientZone, LotId = LotId, Lot = lot, FloorId = 2, Floor = upper, Code = "A", Name = "Vãng lai", Type = "TRANSIENT" });
            c.Vehicles.Add(new Vehicle { Id = 5, UserId = OwnerId, PlateNumber = Plate });
            c.SubscriptionPlans.Add(new SubscriptionPlan { Id = 10, Code = "M1", Name = "Gói 1 tháng", LotId = LotId, TermMonths = 1, PriceAmount = 800000, Status = "APPROVED" });
            c.StaffLotAssignments.AddRange(
                new StaffLotAssignment { UserId = GateStaffId, LotId = LotId, Duty = StaffDuties.Gate, IsActive = true, AssignedBy = AdminId },
                new StaffLotAssignment { UserId = VerifierOnlyId, LotId = LotId, Duty = StaffDuties.SlotVerifier, IsActive = true, AssignedBy = AdminId },
                new StaffLotAssignment { UserId = OtherLotStaffId, LotId = OtherLotId, Duty = StaffDuties.Gate, IsActive = true, AssignedBy = AdminId });
            await c.SaveChangesAsync();
            return f;
        }

        public async Task AddPassAsync(string status, double startDays, double endDays, int lotId = LotId, long id = 1, long vehicleId = 5)
        {
            await using var c = Ctx();
            c.ParkingPasses.Add(new ParkingPass
            {
                Id = id, OwnerUserId = OwnerId, VehicleId = vehicleId, PlanId = 10, PlanVersion = 1, LotId = lotId, Status = status,
                StartsAtUtc = DateTime.UtcNow.AddDays(startDays), EndsAtUtc = DateTime.UtcNow.AddDays(endDays), PriceAmount = 800000
            });
            await c.SaveChangesAsync();
        }

        public async Task SetPassAsync(string status, double endDays)
        {
            await using var c = Ctx();
            var pass = await c.ParkingPasses.SingleAsync();
            pass.Status = status;
            pass.EndsAtUtc = DateTime.UtcNow.AddDays(endDays);
            await c.SaveChangesAsync();
        }

        private ParkingOperationsService Ops(TestSmartParkingDbContext c) =>
            new(c, new ParkingAuthorizationService(c), new ParkingOperationsWriter(c));

        public async Task<PassLookupDto> LookupAsync(string? plate, long actor = GateStaffId, string role = Roles.Staff, int lotId = LotId)
        {
            await using var c = Ctx();
            return await Ops(c).LookupPass(lotId, plate, actor, role, default);
        }

        public async Task<PassEntryResult> EnterAsync(string plate = Plate, Guid? eventId = null, int zoneId = FixedZone, long actor = GateStaffId, string role = Roles.Staff)
        {
            await using var c = Ctx();
            return await Ops(c).EnterWithPass(LotId, new PassEntryRequest(eventId ?? Guid.NewGuid(), zoneId, plate, null, "test"), actor, role, default);
        }

        public async Task<(bool Success, string Message, ParkingSessionDto? Data)> CheckOutAsync(long sessionId, string? plate, long actor = GateStaffId, string role = Roles.Staff)
        {
            await using var c = Ctx();
            return await new ParkingSessionService(c, new ParkingAuthorizationService(c), NullLogger<ParkingSessionService>.Instance)
                .CheckOutAsync(sessionId, new CheckOutRequestDto { ConfirmedPlateNumber = plate }, actor, role, "127.0.0.1");
        }

        public async Task<ParkingSession> SessionAsync(long id) { await using var c = Ctx(); return await c.ParkingSessions.AsNoTracking().SingleAsync(s => s.Id == id); }
        public async Task<int> CountAsync<T>(Func<DbContext, IQueryable<T>> query) where T : class { await using var c = Ctx(); return await query(c).CountAsync(); }
    }

    private static async Task<ParkingOperationException> Rejected(Func<Task> action) => await Assert.ThrowsAsync<ParkingOperationException>(action);

    // ------------------------------------------------------------------ tra vé theo biển số
    [Fact]
    public async Task Lookup_ValidPass_MatchesAnyPlateSpelling_AndReturnsPlanAndExpiry()
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync("ACTIVE", -5, 25);

        foreach (var spelling in new[] { "51F-123.45", "51f12345", " 51F 123.45 ", "51F-12345" })
        {
            var r = await f.LookupAsync(spelling);
            Assert.True(r.CanEnter, spelling);
            Assert.Equal("OK", r.Reason);
            Assert.Equal("Gói 1 tháng", r.PlanName);
            Assert.Equal(1L, r.PassId);
            Assert.Equal(5L, r.VehicleId);
            Assert.True(r.EndsAtUtc > DateTime.UtcNow.AddDays(24));
        }
    }

    [Theory]
    [InlineData("ACTIVE", -40, -10, "PASS_EXPIRED")]     // đã quá hạn nhưng worker chưa kịp đánh dấu
    [InlineData("EXPIRED", -40, -10, "PASS_EXPIRED")]
    [InlineData("SUSPENDED", -5, 25, "PASS_SUSPENDED")]
    [InlineData("SCHEDULED", 5, 35, "PASS_NOT_STARTED")]
    [InlineData("ACTIVE", 5, 35, "PASS_NOT_STARTED")]
    [InlineData("PENDING", -1, 29, "PASS_UNPAID")]
    public async Task Lookup_PassThatCannotBeUsedNow_ExplainsWhy(string status, double start, double end, string reason)
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync(status, start, end);
        var r = await f.LookupAsync(Plate);
        Assert.False(r.CanEnter);
        Assert.Equal(reason, r.Reason);
        Assert.NotEmpty(r.Message);
    }

    [Fact]
    public async Task Lookup_NoPass_OtherLotPass_CancelledPass_AndBadPlate_AreNotAllowed()
    {
        var f = await Fx.CreateAsync();
        Assert.Equal("PASS_NOT_FOUND", (await f.LookupAsync(Plate)).Reason);
        Assert.Equal("INVALID_PLATE", (await f.LookupAsync("ABC")).Reason);
        Assert.Equal("INVALID_PLATE", (await f.LookupAsync(null)).Reason);

        await f.AddPassAsync("ACTIVE", -5, 25, lotId: OtherLotId, id: 1);   // vé của bãi khác
        Assert.Equal("PASS_NOT_FOUND", (await f.LookupAsync(Plate)).Reason);
        await f.AddPassAsync("CANCELLED", -5, 25, id: 2);                   // vé đã hủy
        Assert.Equal("PASS_NOT_FOUND", (await f.LookupAsync(Plate)).Reason);
    }

    [Fact]
    public async Task Lookup_RequiresGateDutyAtTheLot()
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync("ACTIVE", -5, 25);
        Assert.Equal(403, (await Rejected(() => f.LookupAsync(Plate, OtherLotStaffId))).Status);
        Assert.Equal(403, (await Rejected(() => f.LookupAsync(Plate, 9999))).Status);
        Assert.True((await f.LookupAsync(Plate, AdminId, Roles.Admin)).CanEnter);
    }

    // ------------------------------------------------------------------ vào bằng vé tháng
    [Fact]
    public async Task Entry_ValidPass_CreatesPassSession_WithoutTicketOrSlot_AndAuditsThePass()
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync("ACTIVE", -5, 25);

        var result = await f.EnterAsync(" 51f-123.45 ");
        var session = result.Session;
        Assert.False(result.Replay);
        Assert.Equal(1L, result.Pass!.PassId);
        Assert.Equal("51F-123.45", session.PlateNumber);
        Assert.Equal(FixedZone, session.ZoneId);
        Assert.Equal(VerificationStatuses.MonthlyPass, session.VerificationStatus);
        Assert.Equal("ACTIVE", session.Status);
        Assert.Null(session.CurrentSlotId);
        Assert.Null(session.TicketCode);

        var stored = await f.SessionAsync(session.Id);
        Assert.Equal(5L, stored.VehicleId);
        Assert.Equal(GateStaffId, stored.VerifiedBy);
        Assert.Equal(0m, stored.TotalFee);
        Assert.Equal(0, await f.CountAsync(c => c.Set<ParkingTicket>()));
        await using var ctx = f.Ctx();
        var audit = Assert.Single(await ctx.AuditLogs.Where(a => a.Action == "MONTHLY_PASS_ENTRY").ToListAsync());
        Assert.Contains("\"PassId\":1", audit.NewValues);
    }

    [Fact]
    public async Task Entry_SameEventReplay_ReturnsTheSameSession_AndADifferentBodyIsRejected()
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync("ACTIVE", -5, 25);
        var eventId = Guid.NewGuid();

        var first = await f.EnterAsync(Plate, eventId);
        var again = await f.EnterAsync("51F12345", eventId);
        Assert.True(again.Replay);
        Assert.Equal(first.Session.Id, again.Session.Id);
        Assert.Equal(1, await f.CountAsync(c => c.Set<ParkingSession>()));

        // Cùng EventId nhưng khác khu/nội dung không được dùng lại.
        var mismatch = await Rejected(() => f.EnterAsync("51F-123.46", eventId));
        Assert.Equal("EVENT_MISMATCH", mismatch.Code);
    }

    [Fact]
    public async Task Entry_SecondEntryWhileInside_IsRejected()
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync("ACTIVE", -5, 25);
        await f.EnterAsync();

        var error = await Rejected(() => f.EnterAsync());
        Assert.Equal("ALREADY_INSIDE", error.Code);
        Assert.Equal(1, await f.CountAsync(c => c.Set<ParkingSession>()));
    }

    [Theory]
    [InlineData("ACTIVE", -40, -10, "PASS_EXPIRED")]
    [InlineData("SUSPENDED", -5, 25, "PASS_SUSPENDED")]
    [InlineData("SCHEDULED", 5, 35, "PASS_NOT_STARTED")]
    public async Task Entry_PassNotUsableNow_IsRejected_AndNoSessionIsCreated(string status, double start, double end, string reason)
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync(status, start, end);
        var error = await Rejected(() => f.EnterAsync());
        Assert.Equal(reason, error.Code);
        Assert.Equal(0, await f.CountAsync(c => c.Set<ParkingSession>()));
    }

    [Fact]
    public async Task Entry_RejectsTransientZone_UnknownZone_AndStaffWithoutGateInDuty()
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync("ACTIVE", -5, 25);

        Assert.Equal("NOT_A_MONTHLY_ZONE", (await Rejected(() => f.EnterAsync(zoneId: TransientZone))).Code);
        Assert.Equal("NOT_A_MONTHLY_ZONE", (await Rejected(() => f.EnterAsync(zoneId: 999))).Code);
        Assert.Equal(403, (await Rejected(() => f.EnterAsync(actor: OtherLotStaffId))).Status);
        Assert.Equal(403, (await Rejected(() => f.EnterAsync(actor: VerifierOnlyId))).Status);
        Assert.Equal("INVALID_PLATE", (await Rejected(() => f.EnterAsync("ABC"))).Code);
        Assert.Equal(0, await f.CountAsync(c => c.Set<ParkingSession>()));
    }

    // ------------------------------------------------------------------ ra không cần thanh toán
    [Fact]
    public async Task Exit_PassSession_CompletesWithoutPayment_AndIsAuditedAsPassCovered()
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync("ACTIVE", -5, 25);
        long id = (await f.EnterAsync()).Session.Id;

        var exit = await f.CheckOutAsync(id, "51f12345");
        Assert.True(exit.Success, exit.Message);
        Assert.Equal(SessionStatuses.Completed, exit.Data!.Status);
        var stored = await f.SessionAsync(id);
        Assert.NotNull(stored.CheckOutTime);
        Assert.Equal(0m, stored.TotalFee);
        Assert.Null(stored.PaidAt);

        await using var ctx = f.Ctx();
        var audit = Assert.Single(await ctx.AuditLogs.Where(a => a.Action == "CHECK_OUT_SESSION").ToListAsync());
        Assert.Contains("\"ConfirmationType\":\"MONTHLY_PASS\"", audit.NewValues);
        Assert.Contains("\"PassCovered\":true", audit.NewValues);

        // Xe đã ra; vào lại bằng vé vẫn được.
        Assert.NotNull((await f.EnterAsync()).Session);
    }

    [Fact]
    public async Task Exit_PassSession_StillNeedsPlateConfirmationAndGateOutDuty()
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync("ACTIVE", -5, 25);
        long id = (await f.EnterAsync()).Session.Id;

        Assert.False((await f.CheckOutAsync(id, null)).Success);
        Assert.False((await f.CheckOutAsync(id, "  ")).Success);
        Assert.False((await f.CheckOutAsync(id, "29B-999.99")).Success);
        Assert.False((await f.CheckOutAsync(id, Plate, VerifierOnlyId)).Success);
        Assert.False((await f.CheckOutAsync(id, Plate, OtherLotStaffId)).Success);
        Assert.Equal(SessionStatuses.Active, (await f.SessionAsync(id)).Status);
    }

    [Fact]
    public async Task Exit_PassSession_IsAllowedEvenIfThePassExpiredWhileParked_SoNoCarIsTrapped()
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync("ACTIVE", -5, 25);
        long id = (await f.EnterAsync()).Session.Id;
        await f.SetPassAsync("EXPIRED", -0.01);

        var exit = await f.CheckOutAsync(id, Plate);
        Assert.True(exit.Success, exit.Message);
    }

    [Fact]
    public async Task Exit_PassSession_CannotBeExitedTwice()
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync("ACTIVE", -5, 25);
        long id = (await f.EnterAsync()).Session.Id;
        Assert.True((await f.CheckOutAsync(id, Plate)).Success);
        Assert.False((await f.CheckOutAsync(id, Plate)).Success);
        await using var ctx = f.Ctx();
        Assert.Equal(1, await ctx.AuditLogs.CountAsync(a => a.Action == "CHECK_OUT_SESSION"));
    }

    [Fact]
    public async Task Exit_RegularUnpaidSession_IsStillBlocked_PassExceptionDoesNotLeak()
    {
        var f = await Fx.CreateAsync();
        long id;
        await using (var c = f.Ctx())
        {
            var s = new ParkingSession { LotId = LotId, ZoneId = TransientZone, PlateNumber = "30H99999", Status = SessionStatuses.Active, VerificationStatus = "VERIFIED", CheckInTime = DateTime.UtcNow.AddHours(-1) };
            c.ParkingSessions.Add(s);
            await c.SaveChangesAsync();
            id = s.Id;
        }
        var exit = await f.CheckOutAsync(id, "30H-999.99");
        Assert.False(exit.Success);
        Assert.Contains("thanh toán", exit.Message);
        Assert.Equal(SessionStatuses.Active, (await f.SessionAsync(id)).Status);
    }

    // ------------------------------------------------------------------ không có thanh toán cho xe vé tháng
    [Fact]
    public async Task PaymentClearance_ForPassSession_IsRejected_AndSessionNeverBecomesPaid()
    {
        var f = await Fx.CreateAsync();
        await f.AddPassAsync("ACTIVE", -5, 25);
        long id = (await f.EnterAsync()).Session.Id;

        await using var c = f.Ctx();
        var svc = new ParkingSessionService(c, new ParkingAuthorizationService(c), NullLogger<ParkingSessionService>.Instance);
        var clearance = await svc.RecordPaymentClearanceAsync(id, 35000m, "TX-PASS-1", "VNPAY", null,
            PaymentServiceSignature.ServiceActorRole, "127.0.0.1", isTrustedPaymentServerFlow: true);
        Assert.False(clearance.Success);
        Assert.Contains("vé tháng", clearance.Message);

        var counter = await svc.RecordCounterPaymentAsync(id, GateStaffId, Roles.Staff);
        Assert.False(counter.Success);
        Assert.Contains("vé tháng", counter.Message);
        Assert.Equal(SessionStatuses.Active, (await f.SessionAsync(id)).Status);
    }
}
