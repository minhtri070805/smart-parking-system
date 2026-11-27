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
using SmartParking.Domain.Constants;
using SmartParking.Domain.Entities;
using SmartParking.Infrastructure.Services;
using Xunit;

namespace SmartParking.Tests;

/// <summary>Sprint 5 (Phụng): receiver clearance server-to-server (HMAC) + capacity theo khu không trừ quá tay booking.</summary>
public class S5PaymentClearanceAndCapacityTests
{
    private const string Secret = "unit-test-shared-secret-0123456789-abcdef";

    // ---------- HMAC ----------
    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Signature_OutOfRangeTimestamp_ReturnsFalse_NotThrow(long ts)
    {
        var sig = PaymentServiceSignature.Compute(Secret, ts, "x");
        Assert.False(PaymentServiceSignature.Verify(Secret, ts.ToString(), sig, "x", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Signature_ValidRoundTrip_Passes_AndTamperedBodyOrStaleOrWeakSecretFail()
    {
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var sig = PaymentServiceSignature.Compute(Secret, ts, "{\"a\":1}");
        Assert.True(PaymentServiceSignature.Verify(Secret, ts.ToString(), sig, "{\"a\":1}", now));
        Assert.False(PaymentServiceSignature.Verify(Secret, ts.ToString(), sig, "{\"a\":2}", now));
        Assert.False(PaymentServiceSignature.Verify(Secret, ts.ToString(), sig, "{\"a\":1}", now.AddMinutes(6)));
        Assert.False(PaymentServiceSignature.Verify("short", ts.ToString(), PaymentServiceSignature.Compute("short", ts, "x"), "x", now));
        Assert.False(PaymentServiceSignature.Verify(Secret, "abc", sig, "{\"a\":1}", now));
        Assert.False(PaymentServiceSignature.Verify(Secret, ts.ToString(), null, "{\"a\":1}", now));
    }

    // ---------- Controller ----------
    private static async Task<(InternalPaymentClearancesController Ctl, TestSmartParkingDbContext Ctx, long SessionId)> ArrangeAsync(string? secret = Secret, decimal fee = 35000m)
    {
        var ctx = await TestDbHelper.CreateInMemoryDbContextAsync();
        ctx.ParkingLots.Add(new ParkingLot { Id = 900, Name = "Clearance lot", Address = "x", TotalCapacity = 5, IsActive = true });
        var s = new ParkingSession { LotId = 900, PlateNumber = "51A99999", Status = SessionStatuses.Active, TotalFee = fee, CheckInTime = DateTime.UtcNow.AddHours(-1) };
        ctx.ParkingSessions.Add(s);
        await ctx.SaveChangesAsync();
        var svc = new ParkingSessionService(ctx, new ParkingAuthorizationService(ctx), NullLogger<ParkingSessionService>.Instance);
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PaymentService:SharedSecret"] = secret }).Build();
        return (new InternalPaymentClearancesController(svc, new SmartParking.Infrastructure.Services.BookingService(ctx, new ParkingAuthorizationService(ctx), NullLogger<SmartParking.Infrastructure.Services.BookingService>.Instance, TestFakes.Gateway, TestFakes.Email), cfg), ctx, s.Id);
    }

    private static async Task<IActionResult> PostAsync(InternalPaymentClearancesController ctl, object body, string? secret = Secret, long? ts = null, bool sign = true)
    {
        var raw = JsonSerializer.Serialize(body);
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var t = ts ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        http.Request.Headers[PaymentServiceSignature.TimestampHeader] = t.ToString();
        if (sign) http.Request.Headers[PaymentServiceSignature.SignatureHeader] = PaymentServiceSignature.Compute(secret ?? Secret, t, raw);
        ctl.ControllerContext = new ControllerContext { HttpContext = http };
        return await ctl.Receive(CancellationToken.None);
    }

    private static object Body(long id, decimal amount, string txRef) => new { sessionId = id, amount, transactionRef = txRef, paymentMethod = "VNPAY" };

    [Fact]
    public async Task Receiver_NotConfigured_FailsClosed_503_AndSessionNotPaid()
    {
        var (ctl, ctx, id) = await ArrangeAsync(secret: "");
        var res = await PostAsync(ctl, Body(id, 35000m, "TX-1"));
        Assert.Equal(503, Assert.IsType<ObjectResult>(res).StatusCode);
        Assert.Equal(SessionStatuses.Active, (await ctx.ParkingSessions.FindAsync(id))!.Status);
    }

    [Fact]
    public async Task Receiver_MissingOrWrongSignature_Is401_AndSessionNotPaid()
    {
        var (ctl, ctx, id) = await ArrangeAsync();
        Assert.IsType<UnauthorizedObjectResult>(await PostAsync(ctl, Body(id, 35000m, "TX-1"), sign: false));
        Assert.IsType<UnauthorizedObjectResult>(await PostAsync(ctl, Body(id, 35000m, "TX-1"), secret: "another-secret-another-secret-another-secret"));
        Assert.IsType<UnauthorizedObjectResult>(await PostAsync(ctl, Body(id, 35000m, "TX-1"), ts: DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()));
        Assert.Equal(SessionStatuses.Active, (await ctx.ParkingSessions.FindAsync(id))!.Status);
    }

    [Fact]
    public async Task Receiver_ValidSigned_MarksPaid_ReplaySameRefIdempotent_DifferentRefConflict()
    {
        var (ctl, ctx, id) = await ArrangeAsync();
        Assert.IsType<OkObjectResult>(await PostAsync(ctl, Body(id, 35000m, "TX-OK")));
        var s = await ctx.ParkingSessions.FindAsync(id);
        Assert.Equal(SessionStatuses.Paid, s!.Status);
        Assert.NotNull(s.PaidAt);

        Assert.IsType<OkObjectResult>(await PostAsync(ctl, Body(id, 35000m, "TX-OK")));
        Assert.Equal(1, await ctx.AuditLogs.CountAsync(a => a.Action == "PAYMENT_CLEARED"));
        Assert.IsType<ConflictObjectResult>(await PostAsync(ctl, Body(id, 35000m, "TX-OTHER")));
    }

    [Fact]
    public async Task Receiver_UnderpaidOrZeroAmount_Rejected_AndUnknownSession404()
    {
        var (ctl, ctx, id) = await ArrangeAsync();
        Assert.IsType<BadRequestObjectResult>(await PostAsync(ctl, Body(id, 1000m, "TX-LOW")));
        Assert.IsType<BadRequestObjectResult>(await PostAsync(ctl, Body(id, 0m, "TX-ZERO")));
        Assert.IsType<NotFoundObjectResult>(await PostAsync(ctl, Body(id + 999, 35000m, "TX-404")));
        Assert.Equal(SessionStatuses.Active, (await ctx.ParkingSessions.FindAsync(id))!.Status);
    }

    [Fact]
    public async Task JwtAdminFlag_StillFailsClosed_NoClientSelfDeclaredPaid()
    {
        var (_, ctx, id) = await ArrangeAsync();
        var svc = new ParkingSessionService(ctx, new ParkingAuthorizationService(ctx), NullLogger<ParkingSessionService>.Instance);
        var r = await svc.RecordPaymentClearanceAsync(id, 35000m, "TX-X", "CASH", 1, Roles.Admin, "127.0.0.1", isTrustedPaymentServerFlow: false);
        Assert.False(r.Success);
        Assert.Equal(SessionStatuses.Active, (await ctx.ParkingSessions.FindAsync(id))!.Status);
    }

    // ---------- Capacity theo khu ----------
    private static async Task<TestSmartParkingDbContext> SeedZonesAsync(int pendingBookings)
    {
        var ctx = await TestDbHelper.CreateInMemoryDbContextAsync();
        var lot = new ParkingLot { Id = 950, Name = "Zoned", Address = "x", TotalCapacity = 4, IsActive = true };
        var floor = new ParkingFloor { Id = 9501, LotId = 950, Lot = lot, FloorNumber = 1, Name = "T1", TotalSlots = 4, IsActive = true };
        var za = new ParkingZone { Id = 9511, LotId = 950, Lot = lot, FloorId = 9501, Floor = floor, Code = "A", Name = "A", Type = "TRANSIENT" };
        var zb = new ParkingZone { Id = 9512, LotId = 950, Lot = lot, FloorId = 9501, Floor = floor, Code = "B", Name = "B", Type = "TRANSIENT" };
        ctx.AddRange(lot, floor, za, zb);
        var n = 0;
        foreach (var z in new[] { za, zb })
            for (var i = 0; i < 2; i++)
                ctx.ParkingSlots.Add(new ParkingSlot { Id = 95000 + ++n, FloorId = 9501, Floor = floor, ZoneId = z.Id, Zone = z, SlotCode = $"{z.Code}{i}", PhysicalStatus = SlotPhysicalStatuses.Empty, IsActive = true });
        await ctx.SaveChangesAsync();
        ctx.Users.Add(new User { Id = 70, Username = "u70", Email = "u70@t.com", FullName = "u", PasswordHash = "x", RoleId = 4, IsActive = true, ApprovalStatus = "APPROVED" });
        ctx.Vehicles.Add(new Vehicle { Id = 70, UserId = 70, PlateNumber = "51A00070" });
        await ctx.SaveChangesAsync();
        for (var i = 0; i < pendingBookings; i++)
            ctx.Bookings.Add(new Booking { LotId = 950, UserId = 70, VehicleId = 70, PlateNumber = $"51A0007{i}", Status = BookingStatuses.Pending });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task ZoneCapacity_DoesNotSubtractWholeLotPendingBookingsFromEachZone()
    {
        var ctx = await SeedZonesAsync(pendingBookings: 1);
        var zone = await ParkingLotCapacityHelper.CalculateAsync(ctx, 950, zoneId: 9511);
        var lot = await ParkingLotCapacityHelper.CalculateAsync(ctx, 950);
        Assert.Equal(3, lot.AvailableCapacity);   // 4 - 1 booking
        Assert.Equal(2, zone.AvailableCapacity);  // trước đây = 1 (trừ booking của cả bãi vào khu)
    }

    [Fact]
    public async Task ZoneCapacity_NeverExceedsLotWideAvailability_WhenBookingsHoldMostOfLot()
    {
        var ctx = await SeedZonesAsync(pendingBookings: 3);
        Assert.Equal(1, (await ParkingLotCapacityHelper.CalculateAsync(ctx, 950, zoneId: 9511)).AvailableCapacity);
        Assert.Equal(1, (await ParkingLotCapacityHelper.CalculateAsync(ctx, 950, zoneId: 9512)).AvailableCapacity);
        var ctx2 = await SeedZonesAsync(pendingBookings: 4);
        Assert.Equal(0, (await ParkingLotCapacityHelper.CalculateAsync(ctx2, 950, zoneId: 9511)).AvailableCapacity);
    }
}
