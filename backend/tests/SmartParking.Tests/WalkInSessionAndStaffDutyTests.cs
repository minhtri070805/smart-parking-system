using SmartParking.Application.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartParking.Application.DTOs.Sessions;
using SmartParking.Domain.Constants;
using SmartParking.Domain.Entities;
using SmartParking.Infrastructure.Services;
using Xunit;

namespace SmartParking.Tests;

public class WalkInSessionAndStaffDutyTests
{
    private async Task<(ParkingSessionService SessionService, StaffLotAssignmentService AssignmentService, TestSmartParkingDbContext Context)> CreateServicesAsync()
    {
        var context = await TestDbHelper.CreateInMemoryDbContextAsync();
        var adminUser = new User
        {
            Id = 1,
            Username = "admin_duty_test",
            Email = "admin@dutytest.com",
            FullName = "Admin Duty Test",
            PasswordHash = "dummy",
            RoleId = 1,
            IsActive = true,
            ApprovalStatus = "APPROVED"
        };
        context.Users.Add(adminUser);
        await context.SaveChangesAsync();

        var authService = new ParkingAuthorizationService(context);
        var sessionService = new ParkingSessionService(context, authService, NullLogger<ParkingSessionService>.Instance);
        var assignmentService = new StaffLotAssignmentService(context, NullLogger<StaffLotAssignmentService>.Instance);
        return (sessionService, assignmentService, context);
    }

    private static (ParkingLot Lot, ParkingFloor Floor, List<ParkingSlot> Slots) SeedLot(TestSmartParkingDbContext context, int lotId, string lotName, int slotCount = 5)
    {
        var lot = new ParkingLot
        {
            Id = lotId,
            Name = lotName,
            Address = "123 Test St",
            TotalCapacity = slotCount,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.ParkingLots.Add(lot);

        var floor = new ParkingFloor
        {
            Id = lotId * 10 + 1,
            LotId = lotId,
            Lot = lot,
            FloorNumber = 1,
            Name = "Tầng 1",
            TotalSlots = slotCount,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.ParkingFloors.Add(floor);

        // Schema hiện tại: mọi ô bắt buộc thuộc một khu; khu TRANSIENT mới bán chỗ cho xe vãng lai.
        var zone = new ParkingZone
        {
            Id = lotId,
            LotId = lotId,
            Lot = lot,
            FloorId = floor.Id,
            Floor = floor,
            Code = $"Z{lotId}",
            Name = $"Khu {lotId}",
            Type = "TRANSIENT",
            IsActive = true
        };
        context.ParkingZones.Add(zone);

        var slots = new List<ParkingSlot>();
        for (int i = 1; i <= slotCount; i++)
        {
            var slot = new ParkingSlot
            {
                Id = lotId * 100 + i,
                FloorId = floor.Id,
                Floor = floor,
                ZoneId = zone.Id,
                Zone = zone,
                SlotCode = $"P{lotId}-{i:D2}",
                PhysicalStatus = SlotPhysicalStatuses.Empty,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            slots.Add(slot);
            context.ParkingSlots.Add(slot);
        }

        context.SaveChanges();
        return (lot, floor, slots);
    }

    private static void AddStaffWithDuty(TestSmartParkingDbContext context, long userId, int lotId, string duty)
    {
        var user = new User
        {
            Id = userId,
            Username = $"staff_duty_{userId}",
            Email = $"staff_{userId}@test.com",
            FullName = $"Staff {userId}",
            PasswordHash = "dummy",
            RoleId = 3, // Staff
            IsActive = true,
            ApprovalStatus = "APPROVED"
        };
        context.Users.Add(user);

        var assignment = new StaffLotAssignment
        {
            UserId = userId,
            LotId = lotId,
            Duty = duty,
            AssignedBy = 1,
            IsActive = true,
            AssignedAt = DateTime.UtcNow
        };
        context.StaffLotAssignments.Add(assignment);
    }

    // Lượt xe đã được tiếp nhận (không vé) – dựng trực tiếp vì check-in theo biển số kiểu cũ đã ngừng.
    private static async Task<long> AdmitAsync(TestSmartParkingDbContext context, int lotId, string plate, long? slotId = null)
    {
        var session = new ParkingSession
        {
            LotId = lotId,
            ZoneId = lotId,
            PlateNumber = PlateNumberHelper.Normalize(plate),
            Status = SessionStatuses.Active,
            CurrentSlotId = slotId,
            CheckInTime = DateTime.UtcNow.AddMinutes(-30),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.ParkingSessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id;
    }

    // Mô phỏng clearance đến từ Payment Service (trusted server flow); không dùng client flag.
    private static async Task ClearPaymentAsync(ParkingSessionService svc, long sessionId, decimal amount, string txRef)
    {
        var r = await svc.RecordPaymentClearanceAsync(sessionId, amount, txRef, "CASH", 1, Roles.Admin, "127.0.0.1", isTrustedPaymentServerFlow: true);
        Assert.True(r.Success, r.Message);
    }

    private async Task<(ParkingSessionService Svc, TestSmartParkingDbContext Ctx, long SessionId, string Plate)> SeedActiveSessionAsync(int lotId, string plate)
    {
        var (svc, _, ctx) = await CreateServicesAsync();
        SeedLot(ctx, lotId, $"Clearance lot {lotId}");
        AddStaffWithDuty(ctx, lotId * 10 + 1, lotId, StaffDuties.Gate);
        await ctx.SaveChangesAsync();
        // Legacy CheckInAsync is retired, so seed an admitted, parked session directly.
        var slot = await ctx.ParkingSlots.FirstAsync(x => x.Id == lotId * 100 + 1L);
        slot.PhysicalStatus = SlotPhysicalStatuses.Occupied;
        var session = new ParkingSession
        {
            LotId = lotId,
            ZoneId = lotId,
            PlateNumber = plate,
            Status = SessionStatuses.Active,
            CurrentSlotId = slot.Id,
            CheckInTime = DateTime.UtcNow.AddHours(-1),
            VerificationStatus = "VERIFIED"
        };
        ctx.ParkingSessions.Add(session);
        await ctx.SaveChangesAsync();
        session.TotalFee = 35000m;
        await ctx.SaveChangesAsync();
        return (svc, ctx, session.Id, plate);
    }

    [Fact]
    public async Task StaffDuty_GateOnly_CanCheckOut_CannotVerifySlot()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 201, "Bãi Gate Staff");
        AddStaffWithDuty(context, 2011, 201, StaffDuties.Gate);
        await context.SaveChangesAsync();
        var sessionId = await AdmitAsync(context, 201, "51A-GATE-01");

        // GATE staff attempts VerifySessionSlot -> rejected with duty error
        var verifyRes = await sessionService.VerifySessionSlotAsync(
            sessionId,
            new VerifySessionSlotRequestDto { SlotId = 20101 },
            actorId: 2011,
            actorRole: Roles.Staff,
            ipAddress: "127.0.0.1");

        Assert.False(verifyRes.Success);
        Assert.Contains("SLOT_VERIFIER", verifyRes.Message);

        // GATE staff check-out requires financial clearance (PAID) and a confirmed exit plate
        await ClearPaymentAsync(sessionService, sessionId, 35000m, "TX-201-1");

        var checkOutRes = await sessionService.CheckOutAsync(
            sessionId,
            new CheckOutRequestDto { ConfirmedPlateNumber = "51A-GATE-01" },
            actorId: 2011,
            actorRole: Roles.Staff,
            ipAddress: "127.0.0.1");

        Assert.True(checkOutRes.Success, checkOutRes.Message);
        Assert.Equal(SessionStatuses.Completed, checkOutRes.Data!.Status);
    }

    [Fact]
    public async Task StaffDuty_SlotVerifierOnly_CanVerifySlot_CannotCheckInOrCheckOut()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 202, "Bãi Verifier Staff");
        AddStaffWithDuty(context, 2021, 202, StaffDuties.SlotVerifier);
        await context.SaveChangesAsync();

        // 1. SLOT_VERIFIER attempts check-in -> rejected for missing gate duty (before the retirement notice)
        var checkInRes = await sessionService.CheckInAsync(
            new CheckInRequestDto { LotId = 202, PlateNumber = "51A-VERIFIER-01" },
            actorId: 2021,
            actorRole: Roles.Staff,
            ipAddress: "127.0.0.1");

        Assert.False(checkInRes.Success);
        Assert.Contains("GATE", checkInRes.Message);

        var sessionId = await AdmitAsync(context, 202, "51A-VERIFIER-01");

        // 2. SLOT_VERIFIER verifies slot -> succeeds
        var verifyRes = await sessionService.VerifySessionSlotAsync(
            sessionId,
            new VerifySessionSlotRequestDto { SlotId = 20201 },
            actorId: 2021,
            actorRole: Roles.Staff,
            ipAddress: "127.0.0.1");

        Assert.True(verifyRes.Success, verifyRes.Message);
        Assert.Equal(20201, verifyRes.Data!.CurrentSlotId);

        // A staff verification confirms that the vehicle is physically present in the selected slot.
        var slot = await context.ParkingSlots.FindAsync(20201L);
        Assert.Equal(SlotPhysicalStatuses.Occupied, slot!.PhysicalStatus);
        var verifyAudit = context.AuditLogs.FirstOrDefault(a => a.Action == "VERIFY_SESSION_SLOT");
        Assert.NotNull(verifyAudit);
        Assert.Contains("\"SensorStatusBeforeVerification\":\"EMPTY\"", verifyAudit.NewValues);
        Assert.Contains("\"PhysicalStatusAfterVerification\":\"OCCUPIED\"", verifyAudit.NewValues);

        // 3. SLOT_VERIFIER attempts check-out -> rejected
        var checkOutRes = await sessionService.CheckOutAsync(
            sessionId,
            new CheckOutRequestDto { ConfirmedPlateNumber = "51A-VERIFIER-01" },
            actorId: 2021,
            actorRole: Roles.Staff,
            ipAddress: "127.0.0.1");

        Assert.False(checkOutRes.Success);
        Assert.Contains("GATE", checkOutRes.Message);
    }

    [Fact]
    public async Task StaffDuty_Both_CanVerifyAndCheckOut()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 203, "Bãi Both Duty Staff");
        AddStaffWithDuty(context, 2031, 203, StaffDuties.Both);
        await context.SaveChangesAsync();
        var sessionId = await AdmitAsync(context, 203, "51A-BOTH-01");

        var verifyRes = await sessionService.VerifySessionSlotAsync(
            sessionId,
            new VerifySessionSlotRequestDto { SlotId = 20301 },
            actorId: 2031,
            actorRole: Roles.Staff,
            ipAddress: "127.0.0.1");
        Assert.True(verifyRes.Success, verifyRes.Message);

        await ClearPaymentAsync(sessionService, sessionId, 35000m, "TX-203-1");

        var checkOutRes = await sessionService.CheckOutAsync(
            sessionId,
            new CheckOutRequestDto { ConfirmedPlateNumber = "51A-BOTH-01" },
            actorId: 2031,
            actorRole: Roles.Staff,
            ipAddress: "127.0.0.1");
        Assert.True(checkOutRes.Success, checkOutRes.Message);
        context.ChangeTracker.Clear();
        Assert.Equal(SlotPhysicalStatuses.Empty, (await context.ParkingSlots.FindAsync(20301L))!.PhysicalStatus);
    }

    [Fact]
    public async Task UnmatchedSessions_AdmittedWithoutSlot_AppearInQueue()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 204, "Bãi Unmatched Queue");
        await context.SaveChangesAsync();
        var sessionId = await AdmitAsync(context, 204, "51A-WALKIN-QUEUE");

        var unmatched = await sessionService.GetUnmatchedSessionsAsync(204, actorId: 1, actorRole: Roles.Admin);
        Assert.Contains(unmatched, s => s.PlateNumber == "51A-WALKIN-QUEUE" && s.SessionId == sessionId);
    }

    [Fact]
    public async Task UnmatchedOccupiedSlots_IdentifiesSensorOccupancyWithoutSession()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        var (_, _, slots) = SeedLot(context, 205, "Bãi Sensor Unmatched");

        // Simulate IoT sensor reporting slot OCCUPIED
        slots[2].PhysicalStatus = SlotPhysicalStatuses.Occupied;
        await context.SaveChangesAsync();

        var unmatchedSlots = await sessionService.GetUnmatchedOccupiedSlotsAsync(205, actorId: 1, actorRole: Roles.Admin);
        Assert.Single(unmatchedSlots);
        Assert.Equal(slots[2].Id, unmatchedSlots[0].SlotId);
        Assert.Equal(slots[2].SlotCode, unmatchedSlots[0].SlotCode);
    }

    [Fact]
    public async Task VerifySessionSlot_BindsSessionAndSlot_PreventsDuplicateAssignment()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 206, "Bãi Anti Duplicate");
        await context.SaveChangesAsync();
        var session1 = await AdmitAsync(context, 206, "51A-FIRST-CAR");
        var session2 = await AdmitAsync(context, 206, "51A-SECOND-CAR");

        var verify1 = await sessionService.VerifySessionSlotAsync(
            session1,
            new VerifySessionSlotRequestDto { SlotId = 20601 },
            1, Roles.Admin, "127.0.0.1");
        Assert.True(verify1.Success, verify1.Message);

        // Second vehicle cannot be bound to the same slot
        var verify2 = await sessionService.VerifySessionSlotAsync(
            session2,
            new VerifySessionSlotRequestDto { SlotId = 20601 },
            1, Roles.Admin, "127.0.0.1");

        Assert.False(verify2.Success);
    }

    [Fact]
    public async Task ReportSessionWrongSlot_MovesToActualSlot_AndMarksActualSlotOccupied()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 207, "Bãi Wrong Slot Correction");
        await context.SaveChangesAsync();
        var sessionId = await AdmitAsync(context, 207, "51A-LOST-CAR");

        // Initially verified at slot 20701
        var verify = await sessionService.VerifySessionSlotAsync(
            sessionId,
            new VerifySessionSlotRequestDto { SlotId = 20701 },
            1, Roles.Admin, "127.0.0.1");
        Assert.True(verify.Success, verify.Message);

        // Staff detects car actually parked in slot 20702
        var reportRes = await sessionService.ReportSessionWrongSlotAsync(
            sessionId,
            new ReportWrongSlotSessionRequestDto { ActualSlotId = 20702, Reason = "Tài xế nhầm ô bên cạnh" },
            1, Roles.Admin, "127.0.0.1");

        Assert.True(reportRes.Success, reportRes.Message);
        Assert.Equal(20702, reportRes.Data!.CurrentSlotId);

        // Staff confirms the vehicle is physically present in the actual slot.
        var slot2 = await context.ParkingSlots.FindAsync(20702L);
        Assert.Equal(SlotPhysicalStatuses.Occupied, slot2!.PhysicalStatus);
    }

    [Fact]
    public async Task CheckOut_UnpaidActiveSession_IsRejected_AndSlotNotReleased()
    {
        var (svc, ctx, id, plate) = await SeedActiveSessionAsync(208, "51A-DEMO-01");
        var slotId = (await ctx.ParkingSessions.FindAsync(id))!.CurrentSlotId;
        var slotBefore = slotId.HasValue ? (await ctx.ParkingSlots.FindAsync(slotId.Value))!.PhysicalStatus : null;

        var res = await svc.CheckOutAsync(id, new CheckOutRequestDto { ConfirmedPlateNumber = plate }, 2081, Roles.Staff, "127.0.0.1");
        Assert.False(res.Success);
        Assert.Contains("thanh toán", res.Message);

        // Client-supplied exemption flag must never be a bypass, even for Admin.
        var res2 = await svc.CheckOutAsync(id, new CheckOutRequestDto { ConfirmedPlateNumber = plate, AllowExitWithoutPayment = true, ExemptionReason = "x" }, 1, Roles.Admin, "127.0.0.1");
        Assert.False(res2.Success);

        ctx.ChangeTracker.Clear();
        var session = await ctx.ParkingSessions.FindAsync(id);
        Assert.Equal(SessionStatuses.Active, session!.Status);
        Assert.Null(session.CheckOutTime);
        if (slotId.HasValue)
        {
            var slot = await ctx.ParkingSlots.FindAsync(slotId.Value);
            Assert.Equal(slotBefore, slot!.PhysicalStatus);
        }
        Assert.Equal(0, await ctx.AuditLogs.CountAsync(a => a.Action == "CHECK_OUT_SESSION"));
    }

    [Fact]
    public async Task CheckOut_PaidSession_Succeeds_AndDuplicateCheckoutDoesNotReleaseSlotTwice()
    {
        var (svc, ctx, id, plate) = await SeedActiveSessionAsync(214, "51A-PAID-01");
        await ClearPaymentAsync(svc, id, 35000m, "TX-214-1");

        var first = await svc.CheckOutAsync(id, new CheckOutRequestDto { ConfirmedPlateNumber = plate }, 2141, Roles.Staff, "127.0.0.1");
        Assert.True(first.Success, first.Message);
        Assert.Equal(SessionStatuses.Completed, first.Data!.Status);

        // Slot is later taken by another vehicle; a replayed checkout must not free it.
        var slotId = (await ctx.ParkingSessions.FindAsync(id))!.CurrentSlotId;
        if (slotId.HasValue)
        {
            var slot = await ctx.ParkingSlots.FindAsync(slotId.Value);
            slot!.PhysicalStatus = SlotPhysicalStatuses.Occupied;
            await ctx.SaveChangesAsync();
        }

        var second = await svc.CheckOutAsync(id, new CheckOutRequestDto { ConfirmedPlateNumber = plate }, 2141, Roles.Staff, "127.0.0.1");
        Assert.False(second.Success);
        if (slotId.HasValue)
        {
            ctx.ChangeTracker.Clear();
            Assert.Equal(SlotPhysicalStatuses.Occupied, (await ctx.ParkingSlots.FindAsync(slotId.Value))!.PhysicalStatus);
        }
        Assert.Equal(1, await ctx.AuditLogs.CountAsync(a => a.Action == "CHECK_OUT_SESSION"));
    }

    [Fact]
    public async Task CheckOut_PaidSession_WrongLotOrDuty_IsRejected_AndSessionStaysPaid()
    {
        var (svc, ctx, id, plate) = await SeedActiveSessionAsync(215, "51A-DUTY-01");
        await ClearPaymentAsync(svc, id, 35000m, "TX-215-1");
        SeedLot(ctx, 216, "Other lot");
        AddStaffWithDuty(ctx, 2161, 216, StaffDuties.Gate);          // wrong lot
        AddStaffWithDuty(ctx, 2152, 215, StaffDuties.SlotVerifier);  // wrong duty
        await ctx.SaveChangesAsync();

        foreach (var staffId in new long[] { 2161, 2152 })
        {
            var res = await svc.CheckOutAsync(id, new CheckOutRequestDto { ConfirmedPlateNumber = plate }, staffId, Roles.Staff, "127.0.0.1");
            Assert.False(res.Success);
        }
        ctx.ChangeTracker.Clear();
        Assert.Equal(SessionStatuses.Paid, (await ctx.ParkingSessions.FindAsync(id))!.Status);
    }

    [Fact]
    public async Task LegacyCheckIn_IsRetired_AndCreatesNoSession()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 209, "Bãi Retired CheckIn");
        context.ParkingLots.Add(new ParkingLot { Id = 2099, Name = "Bãi chưa có khu", Address = "x", TotalCapacity = 10, IsActive = true });
        await context.SaveChangesAsync();

        foreach (var lotId in new[] { 209, 2099 })
        {
            var res = await sessionService.CheckInAsync(
                new CheckInRequestDto { LotId = lotId, PlateNumber = "51A-RETIRED", SlotId = lotId == 209 ? 20901 : null },
                1, Roles.Admin, "127.0.0.1");
            Assert.False(res.Success);
            Assert.Equal(ParkingSessionService.LegacyCheckInRetiredMessage, res.Message);
        }

        Assert.Equal(0, await context.ParkingSessions.CountAsync());
        Assert.Equal(SlotPhysicalStatuses.Empty, (await context.ParkingSlots.FindAsync(20901L))!.PhysicalStatus);
    }

    [Fact]
    public async Task VerifySessionSlot_ConcurrentAttempts_OnlyOneSucceeds()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 210, "Bãi Concurrency Verify");
        await context.SaveChangesAsync();
        var session1 = await AdmitAsync(context, 210, "51A-RACE-01");
        var session2 = await AdmitAsync(context, 210, "51A-RACE-02");

        var task1 = sessionService.VerifySessionSlotAsync(
            session1,
            new VerifySessionSlotRequestDto { SlotId = 21001 },
            1, Roles.Admin, "127.0.0.1");

        var task2 = sessionService.VerifySessionSlotAsync(
            session2,
            new VerifySessionSlotRequestDto { SlotId = 21001 },
            1, Roles.Admin, "127.0.0.1");

        var results = await Task.WhenAll(task1, task2);

        Assert.Single(results, r => r.Success);
        Assert.Single(results, r => !r.Success);
    }

    [Fact]
    public async Task StaffDuty_CreateAssignment_DefaultDutyIsGate_AndInvalidDutyRejected()
    {
        var (_, assignmentService, context) = await CreateServicesAsync();
        SeedLot(context, 211, "Bãi Duty Validation");

        var staff1 = new User
        {
            Id = 2111,
            Username = "staff_val_2111",
            Email = "staff_val@test.com",
            FullName = "Staff Val",
            PasswordHash = "dummy",
            RoleId = 3,
            IsActive = true,
            ApprovalStatus = "APPROVED"
        };

        var staff2 = new User
        {
            Id = 2112,
            Username = "staff_val_2112",
            Email = "staff_val2@test.com",
            FullName = "Staff Val 2",
            PasswordHash = "dummy",
            RoleId = 3,
            IsActive = true,
            ApprovalStatus = "APPROVED"
        };
        context.Users.AddRange(staff1, staff2);
        await context.SaveChangesAsync();

        (await context.ParkingFloors.FindAsync(2111))!.ZoneId = 211; // khu 211 do SeedLot tạo
        await context.SaveChangesAsync();

        // 1. Unspecified duty -> defaults to GATE_IN (least privilege, NOT BOTH)
        var defaultDutyRes = await assignmentService.CreateAssignmentAsync(
            new SmartParking.Application.DTOs.StaffAssignments.CreateStaffAssignmentRequestDto
            {
                UserId = 2111,
                LotId = 211, FloorId = 2111, ZoneId = null,
                Duty = ""
            },
            actorId: 1,
            ipAddress: "127.0.0.1");

        Assert.True(defaultDutyRes.Success);
        Assert.Equal(StaffDuties.GateIn, defaultDutyRes.Data!.Duty);

        // 2. Invalid duty -> rejected
        var invalidDutyRes = await assignmentService.CreateAssignmentAsync(
            new SmartParking.Application.DTOs.StaffAssignments.CreateStaffAssignmentRequestDto
            {
                UserId = 2112,
                LotId = 211, FloorId = 2111, ZoneId = 211,
                Duty = "SUPER_STAFF"
            },
            actorId: 1,
            ipAddress: "127.0.0.1");

        Assert.False(invalidDutyRes.Success);
        Assert.Contains("không hợp lệ", invalidDutyRes.Message);
    }

    [Fact]
    public async Task PaymentClearance_RecordsPaidStatusWithoutCheckoutDependency()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 212, "Bãi Payment Clearance Contract");
        AddStaffWithDuty(context, 2121, 212, StaffDuties.Gate);
        await context.SaveChangesAsync();

        // 1. Vehicle checks in at gate
        var sessionId = await AdmitAsync(context, 212, "51A-CLEARANCE-01");

        // Payment clearance remains available, but checkout no longer depends on it for the demo.
        var clearanceRes = await sessionService.RecordPaymentClearanceAsync(
            sessionId,
            amountPaid: 35000m,
            transactionRef: "VNPAY-20261029-TX999",
            paymentMethod: "VNPAY_QR",
            actorId: 1,
            actorRole: Roles.Admin,
            ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: true);

        Assert.True(clearanceRes.Success);
        Assert.Equal(SessionStatuses.Paid, clearanceRes.Data!.Status);
        Assert.Equal(35000m, clearanceRes.Data.TotalFee);
        Assert.NotNull(clearanceRes.Data.PaidAt);

        // Verify audit trail for PAYMENT_CLEARED
        var audit = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            context.AuditLogs, a => a.Action == "PAYMENT_CLEARED" && a.TargetId == sessionId.ToString());
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task PaymentClearance_UntrustedCaller_FailsClosedWithPaymentIntegrationBlocked()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 212, "Bãi Untrusted Clearance");
        var sessionId = await AdmitAsync(context, 212, "51A-UNTRUSTED-01");

        // Even with Admin role, untrusted caller without server-side verification must fail-closed
        var res = await sessionService.RecordPaymentClearanceAsync(
            sessionId, 20000m, "TX-SELF-DECLARED", "MOCK",
            actorId: 1, actorRole: Roles.Admin, ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: false);

        Assert.False(res.Success);
        Assert.Contains("PAYMENT INTEGRATION BLOCKED", res.Message);
    }

    [Fact]
    public async Task PaymentClearance_UnauthorizedRole_StaffOrUser_IsRejected()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 213, "Bãi Auth Clearance");
        AddStaffWithDuty(context, 2131, 213, StaffDuties.Gate);
        await context.SaveChangesAsync();

        var sessionId = await AdmitAsync(context, 213, "51A-UNAUTH-01");

        // Staff attempts payment clearance -> 403 Forbidden
        var staffClearance = await sessionService.RecordPaymentClearanceAsync(
            sessionId, 20000m, "TX-STAFF-ATTEMPT", "CASH",
            actorId: 2131, actorRole: Roles.Staff, ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: true);
        Assert.False(staffClearance.Success);
        Assert.Contains("Dịch vụ Thanh toán", staffClearance.Message);

        // User attempts payment clearance -> 403 Forbidden
        var userClearance = await sessionService.RecordPaymentClearanceAsync(
            sessionId, 20000m, "TX-USER-ATTEMPT", "MOCK",
            actorId: 999, actorRole: Roles.User, ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: true);
        Assert.False(userClearance.Success);
        Assert.Contains("Dịch vụ Thanh toán", userClearance.Message);
    }

    [Fact]
    public async Task PaymentClearance_AmountZeroOrNegative_IsRejected()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 214, "Bãi Zero Amount");
        var sessionId = await AdmitAsync(context, 214, "51A-ZERO-01");

        // Amount <= 0 rejected
        var zeroClearance = await sessionService.RecordPaymentClearanceAsync(
            sessionId, 0m, "TX-ZERO", "MOCK",
            actorId: 1, actorRole: Roles.Admin, ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: true);
        Assert.False(zeroClearance.Success);
        Assert.Contains("phải lớn hơn 0", zeroClearance.Message);

        var negClearance = await sessionService.RecordPaymentClearanceAsync(
            sessionId, -5000m, "TX-NEG", "MOCK",
            actorId: 1, actorRole: Roles.Admin, ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: true);
        Assert.False(negClearance.Success);
        Assert.Contains("phải lớn hơn 0", negClearance.Message);
    }

    [Fact]
    public async Task PaymentClearance_UnderpaymentOfTotalFee_IsRejected()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 215, "Bãi Underpayment");
        var sessionId = await AdmitAsync(context, 215, "51A-UNDER-01");

        // Simulate authoritative pricing calculation by Nam Anh set TotalFee = 50,000 VNĐ
        var session = await context.ParkingSessions.FindAsync(sessionId);
        session!.TotalFee = 50000m;
        await context.SaveChangesAsync();

        // Caller attempts to clear payment with only 30,000 VNĐ -> rejected
        var underpaid = await sessionService.RecordPaymentClearanceAsync(
            sessionId, 30000m, "TX-UNDERPAID-01", "VNPAY",
            actorId: 1, actorRole: Roles.Admin, ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: true);
        Assert.False(underpaid.Success);
        Assert.Contains("không khớp phí gửi xe cần thanh toán", underpaid.Message);
    }

    [Fact]
    public async Task PaymentClearance_DuplicateTransactionRef_AcrossDifferentSessions_IsRejected()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 216, "Bãi Duplicate Ref");
        var s1ResId = await AdmitAsync(context, 216, "51A-REF-01");
        var s2ResId = await AdmitAsync(context, 216, "51A-REF-02");

        // Clear session 1 with ref TX-UNIQUE-100
        var c1 = await sessionService.RecordPaymentClearanceAsync(
            s1ResId, 25000m, "TX-UNIQUE-100", "MOMOPAY",
            actorId: 1, actorRole: Roles.Admin, ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: true);
        Assert.True(c1.Success);

        // Attempt to clear session 2 with SAME ref TX-UNIQUE-100 -> rejected
        var c2 = await sessionService.RecordPaymentClearanceAsync(
            s2ResId, 25000m, "TX-UNIQUE-100", "MOMOPAY",
            actorId: 1, actorRole: Roles.Admin, ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: true);
        Assert.False(c2.Success);
        Assert.Contains("đã được sử dụng cho một phiên gửi xe khác", c2.Message);
    }

    [Fact]
    public async Task PaymentClearance_IdempotencyAndMismatchedRetry_HandledCorrectly()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        SeedLot(context, 217, "Bãi Idempotency");
        var sResId = await AdmitAsync(context, 217, "51A-IDEMP-01");
        var sessionId = sResId;

        // First clearance
        var c1 = await sessionService.RecordPaymentClearanceAsync(
            sessionId, 40000m, "TX-IDEMPOTENT-888", "ZALOPAY",
            actorId: 1, actorRole: Roles.Admin, ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: true);
        Assert.True(c1.Success);

        // Idempotent retry with identical ref -> returns true
        var retry = await sessionService.RecordPaymentClearanceAsync(
            sessionId, 40000m, "TX-IDEMPOTENT-888", "ZALOPAY",
            actorId: 1, actorRole: Roles.Admin, ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: true);
        Assert.True(retry.Success);
        Assert.Contains("đã được ghi nhận thanh toán hợp lệ trước đó", retry.Message);

        // Retry on already PAID session with DIFFERENT ref -> rejected
        var diffRef = await sessionService.RecordPaymentClearanceAsync(
            sessionId, 40000m, "TX-ANOTHER-999", "ZALOPAY",
            actorId: 1, actorRole: Roles.Admin, ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: true);
        Assert.False(diffRef.Success);
        Assert.Contains("đã hoàn tất thanh toán trước đó với một giao dịch khác", diffRef.Message);
    }

    [Fact]
    public async Task VerifySessionSlot_MaintenanceSlot_IsRejected_AndSensorPreserved()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        var (_, _, slots) = SeedLot(context, 220, "Bãi Verify Maint", slotCount: 3);
        var maintSlot = slots[1];
        maintSlot.PhysicalStatus = SlotPhysicalStatuses.Maintenance;
        await context.SaveChangesAsync();

        var sessionId = await AdmitAsync(context, 220, "51A-VERIFY-MAINT");

        // Attempt to verify to maintenance slot -> rejected
        var verifyMaint = await sessionService.VerifySessionSlotAsync(
            sessionId,
            new VerifySessionSlotRequestDto { SlotId = maintSlot.Id },
            actorId: 1, actorRole: Roles.Admin, ipAddress: "127.0.0.1");
        Assert.False(verifyMaint.Success);
        Assert.Contains("bảo trì hoặc không khả dụng", verifyMaint.Message);

        // Verify to normal slot -> succeeds and marks the occupied position.
        var normalSlot = slots[0];
        var verifyNormal = await sessionService.VerifySessionSlotAsync(
            sessionId,
            new VerifySessionSlotRequestDto { SlotId = normalSlot.Id },
            actorId: 1, actorRole: Roles.Admin, ipAddress: "127.0.0.1");
        Assert.True(verifyNormal.Success, verifyNormal.Message);
        var reloadedSlot = await context.ParkingSlots.FindAsync(normalSlot.Id);
        Assert.Equal(SlotPhysicalStatuses.Occupied, reloadedSlot!.PhysicalStatus);
    }

    [Fact]
    public async Task CheckOut_ConfirmedPlateMismatch_IsRejected_AndNormalizedMatch_Succeeds()
    {
        // Admitted, parked session seeded directly (legacy CheckInAsync is closed for zoned lots); staff 2211 has Gate duty.
        var (sessionService, _, sessionId, _) = await SeedActiveSessionAsync(221, "51A-123.45");

        // Clear payment so checkout financial precondition passes
        var clearance = await sessionService.RecordPaymentClearanceAsync(
            sessionId, 35000m, "TX-PLATE-01", "CASH",
            actorId: 1, actorRole: Roles.Admin, ipAddress: "127.0.0.1",
            isTrustedPaymentServerFlow: true);
        Assert.True(clearance.Success, clearance.Message);

        // Attempt checkout with wrong plate
        var wrongPlateRes = await sessionService.CheckOutAsync(
            sessionId,
            new CheckOutRequestDto { ConfirmedPlateNumber = "29B-999.99" },
            actorId: 2211, actorRole: Roles.Staff, ipAddress: "127.0.0.1");
        Assert.False(wrongPlateRes.Success);
        Assert.Contains("không khớp", wrongPlateRes.Message);

        // Checkout with matching plate normalized (e.g. 51A12345 without punctuation) -> succeeds
        var matchPlateRes = await sessionService.CheckOutAsync(
            sessionId,
            new CheckOutRequestDto { ConfirmedPlateNumber = "51a12345" },
            actorId: 2211, actorRole: Roles.Staff, ipAddress: "127.0.0.1");
        Assert.True(matchPlateRes.Success);
        Assert.Equal(SessionStatuses.Completed, matchPlateRes.Data!.Status);
    }

    [Fact]
    public async Task Capacity_DisjointAndOverlappingOccupancy_CalculatesCorrectConservativeCapacity()
    {
        var (sessionService, _, context) = await CreateServicesAsync();
        var (lot, _, slots) = SeedLot(context, 222, "Bãi Capacity Calc", slotCount: 5);

        // Slot 0: Confirmed pair (car checks in to slot 0 when EMPTY, then IoT sensor detects OCCUPIED)
        await AdmitAsync(context, 222, "51A-PAIR-01", slots[0].Id);
        slots[0].PhysicalStatus = SlotPhysicalStatuses.Occupied;

        // Slot 1: Unmatched physical occupancy (Occupied sensor without any backend session)
        slots[1].PhysicalStatus = SlotPhysicalStatuses.Occupied;

        // Session 2: Unassigned roaming walk-in session (Active, CurrentSlotId = null)
        await AdmitAsync(context, 222, "51A-ROAMING-02");

        // Slot 4: Maintenance slot (must be excluded from usable capacity)
        slots[4].PhysicalStatus = SlotPhysicalStatuses.Maintenance;
        await context.SaveChangesAsync();

        var calc = await ParkingLotCapacityHelper.CalculateAsync(context, 222, lot);

        // Usable capacity: 4 (5 slots total - 1 maintenance = 4)
        Assert.Equal(4, calc.TotalCapacity);
        // Active sessions: 2
        Assert.Equal(2, calc.ActiveSessionsCount);
        // Physical occupied slots: 2 (slots[0] and slots[1])
        Assert.Equal(2, calc.PhysicalOccupiedSlotsCount);
        // Effective occupied: Matched (1) + Unassigned sessions (1) + Unmatched physical (1) = 3 (not undercounted as 2)
        Assert.Equal(3, calc.EffectiveOccupied);
        // Available capacity: 4 - 3 = 1
        Assert.Equal(1, calc.AvailableCapacity);
        // Unmatched physical occupied count: 1 (slots[1])
        Assert.Equal(1, calc.UnmatchedPhysicalOccupiedCount);
        // Unassigned sessions count: 1 (s2)
        Assert.Equal(1, calc.UnassignedSessionsCount);
    }

    [Fact]
    public async Task Capacity_ConservativeUndercountGuard_CalculatesExactlySeven()
    {
        // Test bắt buộc:
        // - 2 Session đã ghép IoT Slot
        // - 2 Session chưa ghép Slot
        // - 3 Slot OCCUPIED chưa ghép Session
        // - Expected conservative occupied count = 7, không phải 5.
        var (sessionService, _, context) = await CreateServicesAsync();
        var (lot, _, slots) = SeedLot(context, 223, "Bãi Capacity Test 7", slotCount: 10);

        // 2 matched pairs (Session assigned to Slot and Slot is Occupied)
        await AdmitAsync(context, 223, "51A-MATCH-01", slots[0].Id);
        slots[0].PhysicalStatus = SlotPhysicalStatuses.Occupied;

        await AdmitAsync(context, 223, "51A-MATCH-02", slots[1].Id);
        slots[1].PhysicalStatus = SlotPhysicalStatuses.Occupied;

        // 2 unassigned sessions (Active sessions without slot assigned)
        await AdmitAsync(context, 223, "51A-UNASSIGNED-03");
        await AdmitAsync(context, 223, "51A-UNASSIGNED-04");

        // 3 slots OCCUPIED unmatched (no session linked to them)
        slots[2].PhysicalStatus = SlotPhysicalStatuses.Occupied;
        slots[3].PhysicalStatus = SlotPhysicalStatuses.Occupied;
        slots[4].PhysicalStatus = SlotPhysicalStatuses.Occupied;

        await context.SaveChangesAsync();

        var calc = await ParkingLotCapacityHelper.CalculateAsync(context, 223, lot);

        Assert.Equal(10, calc.TotalCapacity);
        Assert.Equal(4, calc.ActiveSessionsCount);
        Assert.Equal(5, calc.PhysicalOccupiedSlotsCount); // slots 0, 1, 2, 3, 4
        Assert.Equal(2, calc.MatchedPairsCount);
        Assert.Equal(2, calc.UnassignedSessionsCount);
        Assert.Equal(3, calc.UnmatchedPhysicalOccupiedCount);
        // Conservative occupied count = 2 matched + 2 unassigned + 3 unmatched = 7!
        Assert.Equal(7, calc.EffectiveOccupied);
        Assert.Equal(3, calc.AvailableCapacity); // 10 - 7 = 3
    }
}
