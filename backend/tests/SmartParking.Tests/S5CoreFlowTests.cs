using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SmartParking.Api.Controllers;
using SmartParking.Application.Common.Helpers;
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
/// Sprint 5 (Phụng) – Core: Entry -> Verify(Parked) -> signed Payment clearance -> Checkout -> slot free.
/// Dùng InMemory nhưng nhiều DbContext trên CÙNG một store để tái hiện request song song thật
/// (khóa bãi/clearance là SemaphoreSlim trong process; Postgres dùng thêm pg_advisory_xact_lock — xem ParkingOperationsPostgresTests).
/// </summary>
public class S5CoreFlowTests
{
    private const string Secret = "unit-test-shared-secret-0123456789-abcdef";
    private const long AdminId = 1, GateStaffId = 2, VerifierId = 3, OtherLotStaffId = 4;

    private sealed class Fx
    {
        public DbContextOptions<SmartParkingDbContext> Options = null!;
        public int LotId = 1, OtherLotId = 2, ZoneId = 11;
        public long SlotA = 101, SlotB = 102;
        public TestSmartParkingDbContext Ctx() => new(Options);

        public static async Task<Fx> CreateAsync(bool seedSlots = true)
        {
            var f = new Fx
            {
                Options = new DbContextOptionsBuilder<SmartParkingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options
            };
            await using var c = f.Ctx();
            c.Roles.AddRange(new Role { Id = 1, Code = Roles.Admin, Name = "a" }, new Role { Id = 2, Code = Roles.Manager, Name = "m" },
                new Role { Id = 3, Code = Roles.Staff, Name = "s" }, new Role { Id = 4, Code = Roles.User, Name = "u" });
            await c.SaveChangesAsync();
            foreach (var (id, role) in new[] { (AdminId, 1), (GateStaffId, 3), (VerifierId, 3), (OtherLotStaffId, 3) })
                c.Users.Add(new User { Id = id, Username = $"u{id}", Email = $"u{id}@t.com", FullName = "n", PasswordHash = "x", RoleId = role, IsActive = true, ApprovalStatus = "APPROVED" });
            var lot = new ParkingLot { Id = 1, Name = "Lot1", Address = "x", TotalCapacity = 10, IsActive = true };
            var lot2 = new ParkingLot { Id = 2, Name = "Lot2", Address = "x", TotalCapacity = 10, IsActive = true };
            var floor = new ParkingFloor { Id = 1, LotId = 1, Lot = lot, FloorNumber = 1, Name = "T1", TotalSlots = 2, IsActive = true };
            var zone = new ParkingZone { Id = 11, LotId = 1, Lot = lot, FloorId = 1, Floor = floor, Code = "A", Name = "A", Type = "TRANSIENT" };
            c.AddRange(lot, lot2, floor, zone);
            if (seedSlots)
                foreach (var (id, code) in new[] { (101L, "A1"), (102L, "A2") })
                    c.ParkingSlots.Add(new ParkingSlot { Id = id, FloorId = 1, Floor = floor, ZoneId = 11, Zone = zone, SlotCode = code, PhysicalStatus = SlotPhysicalStatuses.Empty, IsActive = true });
            c.ParkingPolicies.Add(new ParkingPolicy { LotId = 1, CreatedBy = AdminId, ApprovedBy = AdminId, ApprovedAt = DateTime.UtcNow, EffectiveFrom = DateTime.UtcNow.AddMinutes(-5), DecisionReference = "test" });
            c.StaffLotAssignments.AddRange(
                new StaffLotAssignment { UserId = GateStaffId, LotId = 1, Duty = StaffDuties.Gate, IsActive = true, AssignedBy = AdminId },
                new StaffLotAssignment { UserId = VerifierId, LotId = 1, Duty = StaffDuties.SlotVerifier, IsActive = true, AssignedBy = AdminId },
                new StaffLotAssignment { UserId = OtherLotStaffId, LotId = 2, Duty = StaffDuties.Gate, IsActive = true, AssignedBy = AdminId });
            await c.SaveChangesAsync();
            return f;
        }

        public Task<OperationSessionDto> EnterAsync(string ticket, Guid? eventId = null, long actor = GateStaffId, string role = Roles.Staff)
        {
            var c = Ctx();
            return new ParkingOperationsService(c, new ParkingAuthorizationService(c), new ParkingOperationsWriter(c))
                .Enter(LotId, new EntryRequest(eventId ?? Guid.NewGuid(), ZoneId, ticket, null, "manual test"), actor, role, default);
        }

        public Task<OperationSessionDto> VerifyAsync(OperationSessionDto s, long slot, string plate, long actor = VerifierId, string role = Roles.Staff)
        {
            var c = Ctx();
            return new ParkingOperationsService(c, new ParkingAuthorizationService(c), new ParkingOperationsWriter(c))
                .Verify(LotId, s.Id, new VerifyVehicleRequest(slot, plate, "PETROL", s.Revision, true, "observed"), actor, role, default);
        }

        public async Task<(bool Success, string Message, ParkingSessionDto? Data)> CheckOutAsync(long sessionId, string? plate, string? ticket, long actor = GateStaffId, string role = Roles.Staff)
        {
            await using var c = Ctx();
            return await new ParkingSessionService(c, new ParkingAuthorizationService(c), NullLogger<ParkingSessionService>.Instance)
                .CheckOutAsync(sessionId, new CheckOutRequestDto { ConfirmedPlateNumber = plate, TicketCode = ticket }, actor, role, "127.0.0.1");
        }

        public async Task<(bool Success, string Message, ParkingSessionDto? Data)> ClearAsync(long sessionId, decimal amount, string txRef)
        {
            await using var c = Ctx();
            return await new ParkingSessionService(c, new ParkingAuthorizationService(c), NullLogger<ParkingSessionService>.Instance)
                .RecordPaymentClearanceAsync(sessionId, amount, txRef, "VNPAY", null, PaymentServiceSignature.ServiceActorRole, "127.0.0.1", isTrustedPaymentServerFlow: true);
        }

        public async Task<IActionResult> SignedPostAsync(object body)
        {
            await using var c = Ctx();
            var svc = new ParkingSessionService(c, new ParkingAuthorizationService(c), NullLogger<ParkingSessionService>.Instance);
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PaymentService:SharedSecret"] = Secret }).Build();
            var ctl = new InternalPaymentClearancesController(svc, cfg);
            var raw = JsonSerializer.Serialize(body);
            var http = new DefaultHttpContext();
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(raw));
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            http.Request.Headers[PaymentServiceSignature.TimestampHeader] = ts.ToString();
            http.Request.Headers[PaymentServiceSignature.SignatureHeader] = PaymentServiceSignature.Compute(Secret, ts, raw);
            ctl.ControllerContext = new ControllerContext { HttpContext = http };
            return await ctl.Receive(default);
        }

        public async Task<ParkingSession> SessionAsync(long id) { await using var c = Ctx(); return await c.ParkingSessions.AsNoTracking().SingleAsync(s => s.Id == id); }
        public async Task<string> SlotStatusAsync(long id) { await using var c = Ctx(); return (await c.ParkingSlots.AsNoTracking().SingleAsync(s => s.Id == id)).PhysicalStatus; }
        public async Task<string?> TicketStatusAsync(long sessionId) { await using var c = Ctx(); return (await c.ParkingTickets.AsNoTracking().SingleAsync(t => t.SessionId == sessionId)).Status; }
        public async Task<int> AuditCountAsync(string action) { await using var c = Ctx(); return await c.AuditLogs.CountAsync(a => a.Action == action); }
    }

    /// <summary>Entry -> Verify tại ô (Parked). Trả về session đã VERIFIED.</summary>
    private static async Task<OperationSessionDto> ParkedAsync(Fx f, string ticket = "VE-0001", string plate = "51A12345", long slot = 101)
    {
        var s = await f.EnterAsync(ticket);
        return await f.VerifyAsync(s, slot, plate);
    }

    // ------------------------------------------------------------------ happy path
    [Fact]
    public async Task Walkin_Entry_Parked_Paid_Checkout_ReleasesSlotExactlyOnce()
    {
        var f = await Fx.CreateAsync();

        // ENTRY: có session + vé, chưa chiếm ô.
        var entered = await f.EnterAsync("VE-0001");
        Assert.Equal("ACTIVE", entered.Status);
        Assert.Equal("WAITING_VERIFICATION", entered.VerificationStatus);
        Assert.Equal("ISSUED", entered.TicketStatus);
        Assert.Equal(SlotPhysicalStatuses.Empty, await f.SlotStatusAsync(f.SlotA));

        // Chưa đỗ/verify: không thanh toán, không checkout được.
        Assert.False((await f.ClearAsync(entered.Id, 35000m, "TX-EARLY")).Success);
        Assert.False((await f.CheckOutAsync(entered.Id, "51A12345", "VE-0001")).Success);

        // PARKED: verify tại ô => OCCUPIED.
        var parked = await f.VerifyAsync(entered, f.SlotA, "51A12345");
        Assert.Equal("VERIFIED", parked.VerificationStatus);
        Assert.Equal(SlotPhysicalStatuses.Occupied, await f.SlotStatusAsync(f.SlotA));

        // UNPAID: checkout bị chặn; slot/vé/session giữ nguyên.
        var unpaid = await f.CheckOutAsync(entered.Id, "51A12345", "VE-0001");
        Assert.False(unpaid.Success);
        Assert.Equal(SlotPhysicalStatuses.Occupied, await f.SlotStatusAsync(f.SlotA));
        Assert.Equal("ISSUED", await f.TicketStatusAsync(entered.Id));
        Assert.Equal("ACTIVE", (await f.SessionAsync(entered.Id)).Status);
        Assert.Equal(0, await f.AuditCountAsync("CHECK_OUT_SESSION"));

        // PAID: qua endpoint HMAC (không JWT).
        var ok = await f.SignedPostAsync(new { sessionId = entered.Id, amount = 35000m, transactionRef = "VNP-100", paymentMethod = "VNPAY" });
        Assert.IsType<OkObjectResult>(ok);
        Assert.Equal("PAID", (await f.SessionAsync(entered.Id)).Status);
        Assert.Equal(SlotPhysicalStatuses.Occupied, await f.SlotStatusAsync(f.SlotA)); // thu tiền không giải phóng ô

        // CHECKOUT hợp lệ.
        var done = await f.CheckOutAsync(entered.Id, "51a-123.45", "ve-0001");
        Assert.True(done.Success, done.Message);
        Assert.Equal("COMPLETED", (await f.SessionAsync(entered.Id)).Status);
        Assert.Equal(SlotPhysicalStatuses.Empty, await f.SlotStatusAsync(f.SlotA));
        Assert.Equal("REVOKED", await f.TicketStatusAsync(entered.Id));

        // Checkout lặp: từ chối, không audit/giải phóng lần hai.
        Assert.False((await f.CheckOutAsync(entered.Id, "51A12345", "VE-0001")).Success);
        Assert.Equal(1, await f.AuditCountAsync("CHECK_OUT_SESSION"));

        // Ô đã trống: xe khác được verify vào lại (không kẹt ô).
        var next = await ParkedAsync(f, "VE-0002", "51B99999", 101);
        Assert.Equal(f.SlotA, next.CurrentSlotId);
    }

    // ------------------------------------------------------------------ guards ở checkout
    [Fact]
    public async Task Checkout_WrongPlate_WrongTicket_MissingTicket_Rejected_AndSlotNotReleased()
    {
        var f = await Fx.CreateAsync();
        var s = await ParkedAsync(f);
        Assert.True((await f.ClearAsync(s.Id, 35000m, "TX-G1")).Success);

        Assert.False((await f.CheckOutAsync(s.Id, "51A99999", "VE-0001")).Success);   // sai biển
        Assert.False((await f.CheckOutAsync(s.Id, "51A12345", "VE-9999")).Success);   // sai vé
        Assert.False((await f.CheckOutAsync(s.Id, "51A12345", null)).Success);        // thiếu vé
        Assert.False((await f.CheckOutAsync(s.Id, null, "VE-0001")).Success);         // thiếu biển
        Assert.Equal("PAID", (await f.SessionAsync(s.Id)).Status);
        Assert.Equal(SlotPhysicalStatuses.Occupied, await f.SlotStatusAsync(f.SlotA));
        Assert.Equal("ISSUED", await f.TicketStatusAsync(s.Id));
    }

    [Fact]
    public async Task Checkout_WrongLotStaff_VerifierOnlyDuty_AndAdminFlagBypass_AreRejected()
    {
        var f = await Fx.CreateAsync();
        var s = await ParkedAsync(f);
        Assert.True((await f.ClearAsync(s.Id, 35000m, "TX-G2")).Success);

        Assert.False((await f.CheckOutAsync(s.Id, "51A12345", "VE-0001", OtherLotStaffId)).Success); // staff bãi khác
        Assert.False((await f.CheckOutAsync(s.Id, "51A12345", "VE-0001", VerifierId)).Success);      // chỉ SLOT_VERIFIER, không GATE
        Assert.Equal(SlotPhysicalStatuses.Occupied, await f.SlotStatusAsync(f.SlotA));

        // Cờ AllowExitWithoutPayment trên session chưa PAID không phải bypass, kể cả Admin.
        var f2 = await Fx.CreateAsync();
        var s2 = await ParkedAsync(f2);
        await using var c = f2.Ctx();
        var r = await new ParkingSessionService(c, new ParkingAuthorizationService(c), NullLogger<ParkingSessionService>.Instance)
            .CheckOutAsync(s2.Id, new CheckOutRequestDto { ConfirmedPlateNumber = "51A12345", TicketCode = "VE-0001", AllowExitWithoutPayment = true, ExemptionReason = "demo" }, AdminId, Roles.Admin, null);
        Assert.False(r.Success);
        Assert.Equal(SlotPhysicalStatuses.Occupied, await f2.SlotStatusAsync(f2.SlotA));
    }

    // ------------------------------------------------------------------ Entry guards / idempotency
    [Fact]
    public async Task Entry_SameEventReplay_ReturnsSameSession_SameTicketNewEvent_Rejected_NoDuplicates()
    {
        var f = await Fx.CreateAsync();
        var ev = Guid.NewGuid();
        var a = await f.EnterAsync("VE-0001", ev);
        var again = await f.EnterAsync("VE-0001", ev);
        Assert.Equal(a.Id, again.Id);
        await Assert.ThrowsAsync<ParkingOperationException>(() => f.EnterAsync("VE-0001", Guid.NewGuid())); // vé đang dùng
        await Assert.ThrowsAsync<ParkingOperationException>(() => f.EnterAsync("VE-0002", ev));              // event cũ nội dung khác
        await using var c = f.Ctx();
        Assert.Equal(1, await c.ParkingSessions.CountAsync());
        Assert.Equal(1, await c.ParkingTickets.CountAsync());
    }

    [Fact]
    public async Task Entry_WrongLotStaffAndVerifierOnlyDuty_Forbidden_AndCapacityExhausted_Rejected()
    {
        var f = await Fx.CreateAsync();
        await Assert.ThrowsAsync<ParkingOperationException>(() => f.EnterAsync("VE-0001", null, OtherLotStaffId));
        await Assert.ThrowsAsync<ParkingOperationException>(() => f.EnterAsync("VE-0001", null, VerifierId));
        await f.EnterAsync("VE-0010"); await f.EnterAsync("VE-0011");          // 2 ô => 2 xe chờ chiếm hết khả năng nhận
        var ex = await Assert.ThrowsAsync<ParkingOperationException>(() => f.EnterAsync("VE-0012"));
        Assert.Equal("CAPACITY_EXHAUSTED", ex.Code);
    }

    // ------------------------------------------------------------------ Verify duplicate / concurrent
    [Fact]
    public async Task Verify_TwoVehiclesSameSlot_Concurrent_ExactlyOneWins_AndSlotConflictForTheOther()
    {
        var f = await Fx.CreateAsync();
        var s1 = await f.EnterAsync("VE-0001"); var s2 = await f.EnterAsync("VE-0002");
        var results = await Task.WhenAll(
            Try(() => f.VerifyAsync(s1, f.SlotA, "51A00001")),
            Try(() => f.VerifyAsync(s2, f.SlotA, "51A00002")));
        Assert.Equal(1, results.Count(r => r));
        await using var c = f.Ctx();
        Assert.Equal(1, await c.ParkingSessions.CountAsync(s => s.CurrentSlotId == f.SlotA));
        Assert.Equal(SlotPhysicalStatuses.Occupied, await f.SlotStatusAsync(f.SlotA));
    }

    [Fact]
    public async Task Verify_SameSessionTwice_Concurrent_OnlyOneApplies_AndSamePlateTwoSessionsRejected()
    {
        var f = await Fx.CreateAsync();
        var s = await f.EnterAsync("VE-0001");
        var r = await Task.WhenAll(Try(() => f.VerifyAsync(s, f.SlotA, "51A00001")), Try(() => f.VerifyAsync(s, f.SlotB, "51A00001")));
        Assert.Equal(1, r.Count(x => x));
        await using var c = f.Ctx();
        Assert.Equal(1, await c.ParkingSlots.CountAsync(x => x.PhysicalStatus == SlotPhysicalStatuses.Occupied));

        var s2 = await f.EnterAsync("VE-0002");
        await Assert.ThrowsAsync<ParkingOperationException>(() => f.VerifyAsync(s2, f.SlotB, "51A-000.01")); // cùng biển (chuẩn hóa) đang mở
    }

    [Fact]
    public async Task Checkout_TwiceConcurrent_OnlyOneSucceeds_SlotReleasedOnce()
    {
        var f = await Fx.CreateAsync();
        var s = await ParkedAsync(f);
        Assert.True((await f.ClearAsync(s.Id, 35000m, "TX-C1")).Success);
        var r = await Task.WhenAll(Try(async () => (await f.CheckOutAsync(s.Id, "51A12345", "VE-0001")).Success ? 1 : throw new InvalidOperationException()),
                                   Try(async () => (await f.CheckOutAsync(s.Id, "51A12345", "VE-0001")).Success ? 1 : throw new InvalidOperationException()));
        Assert.Equal(1, r.Count(x => x));
        Assert.Equal(1, await f.AuditCountAsync("CHECK_OUT_SESSION"));
        Assert.Equal(SlotPhysicalStatuses.Empty, await f.SlotStatusAsync(f.SlotA));
    }

    private static async Task<bool> Try<T>(Func<Task<T>> op)
    {
        try { await op(); return true; } catch (Exception e) when (e is ParkingOperationException or InvalidOperationException or DbUpdateException) { return false; }
    }

    // ------------------------------------------------------------------ Payment clearance hardening
    [Fact]
    public async Task Clearance_PartialRef_IsNotTreatedAsSameOrDuplicate()
    {
        var f = await Fx.CreateAsync();
        var a = await ParkedAsync(f, "VE-0001", "51A00001", 101);
        var b = await ParkedAsync(f, "VE-0002", "51A00002", 102);
        Assert.True((await f.ClearAsync(a.Id, 35000m, "TX-10")).Success);
        // "TX-1" là tiền tố của "TX-10": trước đây Contains() khiến sai; nay phải là ref khác và được nhận cho session khác.
        Assert.True((await f.ClearAsync(b.Id, 35000m, "TX-1")).Success);
        // Và ngược lại: dùng lại "TX-10" cho session b (đã PAID bằng TX-1) phải bị từ chối.
        Assert.False((await f.ClearAsync(b.Id, 35000m, "TX-10")).Success);
        Assert.Equal(2, await f.AuditCountAsync("PAYMENT_CLEARED"));
    }

    [Fact]
    public async Task Clearance_SameRefOnAnotherSession_AndDifferentRefOnPaidSession_Conflict_ReplayIdempotent()
    {
        var f = await Fx.CreateAsync();
        var a = await ParkedAsync(f, "VE-0001", "51A00001", 101);
        var b = await ParkedAsync(f, "VE-0002", "51A00002", 102);
        Assert.True((await f.ClearAsync(a.Id, 35000m, "TX-A")).Success);
        Assert.True((await f.ClearAsync(a.Id, 35000m, "TX-A")).Success);          // replay idempotent
        Assert.Equal(1, await f.AuditCountAsync("PAYMENT_CLEARED"));
        Assert.False((await f.ClearAsync(a.Id, 35000m, "TX-B")).Success);          // ref khác trên session đã PAID
        Assert.False((await f.ClearAsync(a.Id, 99000m, "TX-A")).Success);          // replay sai số tiền
        Assert.False((await f.ClearAsync(b.Id, 35000m, "TX-A")).Success);          // cross-session
        Assert.Equal("ACTIVE", (await f.SessionAsync(b.Id)).Status);
        var http = await f.SignedPostAsync(new { sessionId = b.Id, amount = 35000m, transactionRef = "TX-A", paymentMethod = "VNPAY" });
        Assert.IsType<ConflictObjectResult>(http);
    }

    [Fact]
    public async Task Clearance_ConcurrentSameRefTwoSessions_AndTwoRefsOneSession_ExactlyOneWins()
    {
        var f = await Fx.CreateAsync();
        var a = await ParkedAsync(f, "VE-0001", "51A00001", 101);
        var b = await ParkedAsync(f, "VE-0002", "51A00002", 102);
        var r = await Task.WhenAll(f.ClearAsync(a.Id, 35000m, "TX-RACE"), f.ClearAsync(b.Id, 35000m, "TX-RACE"));
        Assert.Equal(1, r.Count(x => x.Success));

        var f2 = await Fx.CreateAsync();
        var s = await ParkedAsync(f2);
        var r2 = await Task.WhenAll(f2.ClearAsync(s.Id, 35000m, "TX-X1"), f2.ClearAsync(s.Id, 35000m, "TX-X2"));
        Assert.Equal(1, r2.Count(x => x.Success));
        Assert.Equal(1, await f2.AuditCountAsync("PAYMENT_CLEARED"));

        var f3 = await Fx.CreateAsync();
        var s3 = await ParkedAsync(f3);
        var r3 = await Task.WhenAll(f3.ClearAsync(s3.Id, 35000m, "TX-SAME"), f3.ClearAsync(s3.Id, 35000m, "TX-SAME"));
        Assert.All(r3, x => Assert.True(x.Success));       // cả hai thành công (idempotent)…
        Assert.Equal(1, await f3.AuditCountAsync("PAYMENT_CLEARED")); // …nhưng chỉ ghi một lần
    }

    [Fact]
    public async Task Clearance_FeeDueKept_OverUnderpaymentRejected_BadRefRejected()
    {
        var f = await Fx.CreateAsync();
        var s = await ParkedAsync(f);
        await using (var c = f.Ctx()) { (await c.ParkingSessions.SingleAsync()).TotalFee = 30000m; await c.SaveChangesAsync(); }

        Assert.False((await f.ClearAsync(s.Id, 20000m, "TX-LOW")).Success);
        Assert.False((await f.ClearAsync(s.Id, 50000m, "TX-HIGH")).Success);   // thừa tiền cũng là mismatch (fail-closed)
        Assert.False((await f.ClearAsync(s.Id, 30000m, "TX bad;ref")).Success);
        Assert.False((await f.ClearAsync(s.Id, 30000m, new string('X', 101))).Success);
        Assert.False((await f.ClearAsync(s.Id, 0m, "TX-ZERO")).Success);       // phí 0 chưa được duyệt contract
        Assert.Equal("ACTIVE", (await f.SessionAsync(s.Id)).Status);

        Assert.True((await f.ClearAsync(s.Id, 30000m, "TX-OK")).Success);
        Assert.Equal(30000m, (await f.SessionAsync(s.Id)).TotalFee);            // phí phải trả không bị ghi đè
    }

    [Fact]
    public async Task Clearance_ForCompletedOrCancelledSession_Rejected_AndUnknownSession404Message()
    {
        var f = await Fx.CreateAsync();
        var s = await ParkedAsync(f);
        await using (var c = f.Ctx()) { (await c.ParkingSessions.SingleAsync()).Status = SessionStatuses.Cancelled; await c.SaveChangesAsync(); }
        Assert.False((await f.ClearAsync(s.Id, 35000m, "TX-CAN")).Success);
        Assert.False((await f.ClearAsync(9999, 35000m, "TX-NONE")).Success);
        Assert.Equal(0, await f.AuditCountAsync("PAYMENT_CLEARED"));
    }

    // ------------------------------------------------------------------ Booking path (không ticket/EntryEvent)
    [Fact]
    public async Task BookingSession_StillCheckoutOnlyAfterClearance_AndReleasesSlot()
    {
        var f = await Fx.CreateAsync();
        await using (var c = f.Ctx())
        {
            (await c.ParkingSlots.SingleAsync(x => x.Id == 101)).PhysicalStatus = SlotPhysicalStatuses.Occupied;
            c.ParkingSessions.Add(new ParkingSession { Id = 500, LotId = 1, ZoneId = 11, PlateNumber = "51A55555", Status = SessionStatuses.Active, CurrentSlotId = 101, VerificationStatus = "LEGACY" });
            await c.SaveChangesAsync();
        }
        Assert.False((await f.CheckOutAsync(500, "51A55555", null)).Success);
        Assert.Equal(SlotPhysicalStatuses.Occupied, await f.SlotStatusAsync(101));
        Assert.True((await f.ClearAsync(500, 35000m, "TX-BK")).Success);
        Assert.True((await f.CheckOutAsync(500, "51A55555", null)).Success);
        Assert.Equal(SlotPhysicalStatuses.Empty, await f.SlotStatusAsync(101));
    }

    // ------------------------------------------------------------------ Capacity: zero / maintenance / fixed zone / unmatched / pending
    [Fact]
    public async Task Capacity_ZeroSlots_MaintenanceSlot_FixedZone_Unmatched_PendingBooking()
    {
        var zero = await Fx.CreateAsync(seedSlots: false);
        await using (var c = zero.Ctx())
            Assert.Equal(0, (await ParkingLotCapacityHelper.CalculateAsync(c, 1, zoneId: 11)).AvailableCapacity); // không fallback TotalCapacity

        var f = await Fx.CreateAsync();
        await using var ctx = f.Ctx();
        Assert.Equal(2, (await ParkingLotCapacityHelper.CalculateAsync(ctx, 1, zoneId: 11)).AvailableCapacity);

        (await ctx.ParkingSlots.SingleAsync(x => x.Id == 102)).PhysicalStatus = SlotPhysicalStatuses.Maintenance;
        await ctx.SaveChangesAsync();
        Assert.Equal(1, (await ParkingLotCapacityHelper.CalculateAsync(ctx, 1, zoneId: 11)).AvailableCapacity);

        // Sensor báo OCCUPIED mà không có session: giữ chỗ (không đếm thiếu).
        (await ctx.ParkingSlots.SingleAsync(x => x.Id == 101)).PhysicalStatus = SlotPhysicalStatuses.Occupied;
        await ctx.SaveChangesAsync();
        var unmatched = await ParkingLotCapacityHelper.CalculateAsync(ctx, 1, zoneId: 11);
        Assert.Equal(1, unmatched.UnmatchedPhysicalOccupiedCount);
        Assert.Equal(0, unmatched.AvailableCapacity);

        // Khu cố định không bán vãng lai.
        var floor = await ctx.ParkingFloors.SingleAsync();
        var fixedZone = new ParkingZone { Id = 12, LotId = 1, FloorId = 1, Floor = floor, Code = "F", Name = "F", Type = "FIXED" };
        ctx.ParkingZones.Add(fixedZone);
        ctx.ParkingSlots.Add(new ParkingSlot { Id = 103, FloorId = 1, Floor = floor, ZoneId = 12, Zone = fixedZone, SlotCode = "F1", PhysicalStatus = SlotPhysicalStatuses.Empty, IsActive = true });
        await ctx.SaveChangesAsync();
        Assert.Equal(1, (await ParkingLotCapacityHelper.CalculateAsync(ctx, 1)).TotalCapacity); // ô FIXED không cộng vào vãng lai
        Assert.Equal(0, (await ParkingLotCapacityHelper.CalculateAsync(ctx, 1, zoneId: 12)).TotalCapacity);
    }

    [Fact]
    public async Task Capacity_PendingBookingHold_ThenCheckedIn_IsNotDoubleCounted_AndNeverOversells()
    {
        var f = await Fx.CreateAsync(); // 2 ô vãng lai
        await using var c = f.Ctx();
        c.Users.Add(new User { Id = 70, Username = "cust", Email = "c@t.com", FullName = "c", PasswordHash = "x", RoleId = 4, IsActive = true, ApprovalStatus = "APPROVED" });
        c.Vehicles.Add(new Vehicle { Id = 70, UserId = 70, PlateNumber = "51A00070" });
        await c.SaveChangesAsync();
        var booking = new Booking { LotId = 1, UserId = 70, VehicleId = 70, PlateNumber = "51A00070", Status = BookingStatuses.Pending };
        c.Bookings.Add(booking); await c.SaveChangesAsync();

        Assert.Equal(1, (await ParkingLotCapacityHelper.CalculateAsync(c, 1, zoneId: 11)).AvailableCapacity); // giữ 1 chỗ cho booking
        // Booking đã check-in: hold chuyển thành session, không bị trừ hai lần.
        booking.Status = BookingStatuses.CheckedIn;
        (await c.ParkingSlots.SingleAsync(x => x.Id == 101)).PhysicalStatus = SlotPhysicalStatuses.Occupied;
        c.ParkingSessions.Add(new ParkingSession { LotId = 1, ZoneId = 11, BookingId = booking.Id, PlateNumber = "51A00070", Status = SessionStatuses.Active, CurrentSlotId = 101 });
        await c.SaveChangesAsync();
        var after = await ParkingLotCapacityHelper.CalculateAsync(c, 1, zoneId: 11);
        Assert.Equal(1, after.AvailableCapacity);
        Assert.Equal(0, after.PendingBookingsCount);
    }
}
