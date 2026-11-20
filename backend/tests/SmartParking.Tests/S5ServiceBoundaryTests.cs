using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartParking.Api.Controllers;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.Common.Security;
using SmartParking.Application.DTOs.Sessions;
using SmartParking.Domain.Constants;
using SmartParking.Domain.Entities;
using SmartParking.Infrastructure;
using SmartParking.Infrastructure.Services;
using SmartParking.Persistence.Data;
using Xunit;

namespace SmartParking.Tests;

/// <summary>
/// Sprint 5 (Phụng) – ranh giới microservice của Core:
/// quyền STAFF lấy từ ca trực của Operations (có công tắc), kết quả nhận diện lúc ra lấy qua Recognition API,
/// và dữ kiện tính phí cung cấp cho Payment qua API nội bộ có ký. Mọi lỗi giao tiếp đều fail-closed.
/// </summary>
public class S5ServiceBoundaryTests
{
    private const string PaymentSecret = "unit-test-shared-secret-0123456789-abcdef";
    private const string OpsSecret = "operations-shared-secret-0123456789-abcdef";
    private const long AdminId = 1, StaffId = 2;
    private const int LotId = 1;

    // ------------------------------------------------------------------ fixtures
    private static async Task<DbContextOptions<SmartParkingDbContext>> SeedAsync(bool staffHasCoreAssignment = false)
    {
        var options = new DbContextOptionsBuilder<SmartParkingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var c = new TestSmartParkingDbContext(options);
        c.Roles.AddRange(new Role { Id = 1, Code = Roles.Admin, Name = "a" }, new Role { Id = 3, Code = Roles.Staff, Name = "s" });
        c.Users.AddRange(
            new User { Id = AdminId, Username = "admin", Email = "a@t.com", FullName = "a", PasswordHash = "x", RoleId = 1, IsActive = true, ApprovalStatus = "APPROVED" },
            new User { Id = StaffId, Username = "staff", Email = "s@t.com", FullName = "s", PasswordHash = "x", RoleId = 3, IsActive = true, ApprovalStatus = "APPROVED" });
        var lot = new ParkingLot { Id = LotId, Name = "Lot", Address = "x", TotalCapacity = 5, IsActive = true };
        var floor = new ParkingFloor { Id = 1, LotId = LotId, Lot = lot, FloorNumber = 1, Name = "T1", IsActive = true };
        var zone = new ParkingZone { Id = 11, LotId = LotId, Lot = lot, FloorId = 1, Floor = floor, Code = "A", Name = "A", Type = "TRANSIENT" };
        var slot = new ParkingSlot { Id = 101, FloorId = 1, Floor = floor, ZoneId = 11, Zone = zone, SlotCode = "A1", PhysicalStatus = SlotPhysicalStatuses.Occupied, IsActive = true };
        c.AddRange(lot, floor, zone, slot);
        if (staffHasCoreAssignment)
            c.StaffLotAssignments.Add(new StaffLotAssignment { UserId = StaffId, LotId = LotId, Duty = StaffDuties.Both, IsActive = true, AssignedBy = AdminId });
        c.ParkingSessions.Add(new ParkingSession
        {
            Id = 500, LotId = LotId, ZoneId = 11, PolicyId = 7, PlateNumber = "51A12345", Status = SessionStatuses.Paid,
            CurrentSlotId = 101, VerificationStatus = "VERIFIED", TotalFee = 35000m,
            CheckInTime = new DateTime(2026, 11, 20, 1, 0, 0, DateTimeKind.Utc),
            VerifiedAt = new DateTime(2026, 11, 20, 1, 5, 0, DateTimeKind.Utc),
            PaidAt = new DateTime(2026, 11, 20, 3, 0, 0, DateTimeKind.Utc)
        });
        await c.SaveChangesAsync();
        return options;
    }

    private sealed class FakeShiftDuty(Func<long, int, string, bool> decide) : IStaffShiftDutyClient
    {
        public List<(long Staff, int Lot, string Duty)> Calls { get; } = new();
        public Task<bool> IsOnDutyAsync(long staffId, int lotId, string duty, CancellationToken ct)
        {
            Calls.Add((staffId, lotId, duty));
            return Task.FromResult(decide(staffId, lotId, duty));
        }
    }

    private sealed class FakeRecognition(ExitRecognitionLookupResult result) : IExitRecognitionLookup
    {
        public Task<ExitRecognitionLookupResult> FindAsync(long recognitionId, CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return await respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, object body) =>
        new(code) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static IConfiguration Config(Dictionary<string, string?> extra)
    {
        var values = new Dictionary<string, string?>
        {
            ["AiService:BaseUrl"] = "http://127.0.0.1:8000/",
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=smartparking;Username=test",
            ["ConnectionStrings:AiRecognitionConnection"] = "Host=localhost;Database=recognition_test;Username=test"
        };
        foreach (var (k, v) in extra) values[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    // ------------------------------------------------------------------ 1. quyền theo ca (Operations)
    [Fact]
    public async Task ShiftSource_StaffWithoutCoreAssignment_IsAllowedOnlyForTheDutyOperationsConfirms()
    {
        var options = await SeedAsync(staffHasCoreAssignment: false);
        await using var c = new TestSmartParkingDbContext(options);
        var shifts = new FakeShiftDuty((_, _, duty) => duty == StaffDuties.GateOut);
        var auth = new ParkingAuthorizationService(c, shifts);

        Assert.True(await auth.CanOperateGateOutAsync(StaffId, Roles.Staff, LotId));
        Assert.False(await auth.CanOperateGateInAsync(StaffId, Roles.Staff, LotId));
        Assert.False(await auth.CanOperateSlotVerificationAsync(StaffId, Roles.Staff, LotId));
        Assert.True(await auth.CanOperateAreaAsync(StaffId, Roles.Staff, LotId, StaffDuties.GateOut, 1, 11));
        Assert.Contains(shifts.Calls, x => x == (StaffId, LotId, StaffDuties.GateIn));
        Assert.Contains(shifts.Calls, x => x == (StaffId, LotId, StaffDuties.SlotVerifier));

        // Không có ca thì bảng phân công cũ của Core không còn cấp quyền.
        await using var c2 = new TestSmartParkingDbContext(await SeedAsync(staffHasCoreAssignment: true));
        var noShift = new ParkingAuthorizationService(c2, new FakeShiftDuty((_, _, _) => false));
        Assert.False(await noShift.CanOperateGateOutAsync(StaffId, Roles.Staff, LotId));
        Assert.False(await noShift.CanOperateSlotVerificationAsync(StaffId, Roles.Staff, LotId));

        // Admin không phụ thuộc Operations.
        var adminShifts = new FakeShiftDuty((_, _, _) => false);
        Assert.True(await new ParkingAuthorizationService(c, adminShifts).CanOperateGateOutAsync(AdminId, Roles.Admin, LotId));
        Assert.Empty(adminShifts.Calls);
    }

    [Fact]
    public async Task ShiftSource_CheckoutByOnShiftStaff_Succeeds_AndOffShiftStaffIsRejected()
    {
        var options = await SeedAsync();
        await using (var c = new TestSmartParkingDbContext(options))
        {
            var off = new ParkingSessionService(c, new ParkingAuthorizationService(c, new FakeShiftDuty((_, _, _) => false)), NullLogger<ParkingSessionService>.Instance);
            var denied = await off.CheckOutAsync(500, new CheckOutRequestDto { ConfirmedPlateNumber = "51A12345" }, StaffId, Roles.Staff, "127.0.0.1");
            Assert.False(denied.Success);
        }
        await using (var c = new TestSmartParkingDbContext(options))
        {
            var on = new ParkingSessionService(c, new ParkingAuthorizationService(c, new FakeShiftDuty((_, _, d) => d == StaffDuties.GateOut)), NullLogger<ParkingSessionService>.Instance);
            var ok = await on.CheckOutAsync(500, new CheckOutRequestDto { ConfirmedPlateNumber = "51A12345" }, StaffId, Roles.Staff, "127.0.0.1");
            Assert.True(ok.Success, ok.Message);
        }
    }

    [Fact]
    public async Task OperationsShiftDutyClient_SignsRequest_AndFailsClosedOnEveryError()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, new { allowed = true, shiftId = "s1" })));
        var client = new OperationsShiftDutyClient(new HttpClient(handler) { BaseAddress = new Uri("http://ops/") }, OpsSecret, NullLogger<OperationsShiftDutyClient>.Instance);

        Assert.True(await client.IsOnDutyAsync(StaffId, LotId, StaffDuties.GateIn, default));
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("http://ops/api/internal/shift-duty/check", request.RequestUri!.ToString());
        var ts = long.Parse(request.Headers.GetValues(OperationsShiftDutyClient.TimestampHeader).Single());
        Assert.Equal(InternalRequestSignature.Compute(OpsSecret, ts, body), request.Headers.GetValues(OperationsShiftDutyClient.SignatureHeader).Single());
        using (var doc = JsonDocument.Parse(body))
        {
            Assert.Equal(StaffId, doc.RootElement.GetProperty("staffId").GetInt64());
            Assert.Equal(LotId, doc.RootElement.GetProperty("lotId").GetInt32());
            Assert.Equal(StaffDuties.GateIn, doc.RootElement.GetProperty("duty").GetString());
        }

        foreach (var respond in new Func<HttpRequestMessage, Task<HttpResponseMessage>>[]
        {
            _ => Task.FromResult(Json(HttpStatusCode.OK, new { allowed = false, reason = "NO_SHIFT" })),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json") }),
            _ => throw new HttpRequestException("down"),
            _ => throw new TaskCanceledException("timeout")
        })
        {
            var failing = new OperationsShiftDutyClient(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("http://ops/") }, OpsSecret, NullLogger<OperationsShiftDutyClient>.Instance);
            Assert.False(await failing.IsOnDutyAsync(StaffId, LotId, StaffDuties.GateIn, default));
        }
    }

    [Fact]
    public void StaffDutySource_DefaultsToCoreAssignments_AndRejectsUnsafeConfiguration()
    {
        using (var provider = new ServiceCollection().AddInfrastructureServices(Config(new())).BuildServiceProvider())
            Assert.Null(provider.GetService<IStaffShiftDutyClient>());

        var shifts = Config(new() { ["StaffDuty:Source"] = "OperationsShifts", ["OperationsApi:BaseUrl"] = "http://ops/", ["OperationsApi:SharedSecret"] = OpsSecret });
        using (var provider = new ServiceCollection().AddLogging().AddInfrastructureServices(shifts).BuildServiceProvider())
        using (var scope = provider.CreateScope())
            Assert.IsType<OperationsShiftDutyClient>(scope.ServiceProvider.GetRequiredService<IStaffShiftDutyClient>());

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructureServices(
            Config(new() { ["StaffDuty:Source"] = "OperationsShifts", ["OperationsApi:SharedSecret"] = "short" })));
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructureServices(
            Config(new() { ["StaffDuty:Source"] = "Everyone" })));
    }

    // ------------------------------------------------------------------ 6. nhận diện lúc ra qua Recognition API
    [Fact]
    public async Task HttpExitRecognitionLookup_ParsesRecord_AndReportsUnavailableOnErrors()
    {
        var record = new { id = 9, purpose = "EXIT", parkingLotId = LotId, staffUserId = StaffId, matchedParkingSessionId = 500, confirmedPlateNumber = "51A12345", confirmedAt = DateTime.UtcNow };
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, record)));
        var ok = await new HttpExitRecognitionLookup(new HttpClient(handler) { BaseAddress = new Uri("http://rec/") }, NullLogger<HttpExitRecognitionLookup>.Instance).FindAsync(9, default);
        Assert.True(ok.ServiceAvailable);
        Assert.Equal("EXIT", ok.Record!.Purpose);
        Assert.Equal(500, ok.Record.MatchedParkingSessionId);
        Assert.Equal("http://rec/internal/recognition/9", handler.Requests.Single().Request.RequestUri!.ToString());

        var missing = await new HttpExitRecognitionLookup(new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))) { BaseAddress = new Uri("http://rec/") }, NullLogger<HttpExitRecognitionLookup>.Instance).FindAsync(9, default);
        Assert.True(missing.ServiceAvailable);
        Assert.Null(missing.Record);

        foreach (var respond in new Func<HttpRequestMessage, Task<HttpResponseMessage>>[]
        {
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            _ => throw new HttpRequestException("down")
        })
        {
            var down = await new HttpExitRecognitionLookup(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("http://rec/") }, NullLogger<HttpExitRecognitionLookup>.Instance).FindAsync(9, default);
            Assert.False(down.ServiceAvailable);
        }
    }

    [Fact]
    public async Task Checkout_WithExitRecognition_UsesLookup_AndFailsClosedWhenServiceIsDown()
    {
        var options = await SeedAsync();
        var request = new CheckOutRequestDto { ConfirmedPlateNumber = "51A12345", ExitRecognitionId = 9 };

        async Task<(bool Success, string Message)> CheckoutWith(IExitRecognitionLookup? lookup)
        {
            await using var c = new TestSmartParkingDbContext(options);
            var r = await new ParkingSessionService(c, new ParkingAuthorizationService(c), NullLogger<ParkingSessionService>.Instance, lookup)
                .CheckOutAsync(500, request, AdminId, Roles.Admin, "127.0.0.1");
            return (r.Success, r.Message);
        }

        var down = await CheckoutWith(new FakeRecognition(new ExitRecognitionLookupResult(false, null)));
        Assert.False(down.Success);
        Assert.Contains("Recognition Service", down.Message);

        Assert.False((await CheckoutWith(null)).Success);
        Assert.False((await CheckoutWith(new FakeRecognition(new ExitRecognitionLookupResult(true, null)))).Success);
        Assert.False((await CheckoutWith(new FakeRecognition(new ExitRecognitionLookupResult(true,
            new ExitRecognitionRecord(9, "ENTRY", LotId, AdminId, 500, "51A12345", DateTime.UtcNow))))).Success);

        await using (var c = new TestSmartParkingDbContext(options))
            Assert.Equal(SessionStatuses.Paid, (await c.ParkingSessions.AsNoTracking().SingleAsync(s => s.Id == 500)).Status);

        var ok = await CheckoutWith(new FakeRecognition(new ExitRecognitionLookupResult(true,
            new ExitRecognitionRecord(9, "EXIT", LotId, AdminId, 500, "51A-123.45", DateTime.UtcNow))));
        Assert.True(ok.Success, ok.Message);
    }

    [Fact]
    public void ExitRecognitionLookup_UsesHttpWhenRecognitionServiceIsConfigured_AndRequiresAKey()
    {
        var http = Config(new() { ["RecognitionService:BaseUrl"] = "http://rec/", ["RecognitionService:InternalKey"] = OpsSecret });
        using (var provider = new ServiceCollection().AddLogging().AddInfrastructureServices(http).BuildServiceProvider())
        using (var scope = provider.CreateScope())
            Assert.IsType<HttpExitRecognitionLookup>(scope.ServiceProvider.GetRequiredService<IExitRecognitionLookup>());

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructureServices(
            Config(new() { ["RecognitionService:BaseUrl"] = "http://rec/" })));
    }

    // ------------------------------------------------------------------ 2. dữ kiện tính phí cho Payment
    private static async Task<IActionResult> BillingContextAsync(DbContextOptions<SmartParkingDbContext> options, string raw, bool sign = true)
    {
        await using var c = new TestSmartParkingDbContext(options);
        var svc = new ParkingSessionService(c, new ParkingAuthorizationService(c), NullLogger<ParkingSessionService>.Instance);
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PaymentService:SharedSecret"] = PaymentSecret }).Build();
        var ctl = new InternalPaymentClearancesController(svc, cfg);
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        http.Request.Headers[PaymentServiceSignature.TimestampHeader] = ts.ToString();
        http.Request.Headers[PaymentServiceSignature.SignatureHeader] = sign ? PaymentServiceSignature.Compute(PaymentSecret, ts, raw) : "deadbeef";
        ctl.ControllerContext = new ControllerContext { HttpContext = http };
        return await ctl.BillingContext(default);
    }

    [Fact]
    public async Task BillingContext_SignedRequest_ReturnsCoreFactsWithLongSessionId_ReadOnly()
    {
        var options = await SeedAsync();
        var ok = Assert.IsType<OkObjectResult>(await BillingContextAsync(options, "{\"sessionId\":500}"));
        var dto = Assert.IsType<PaymentBillingContextDto>(ok.Value);
        Assert.Equal(500L, dto.SessionId);
        Assert.Equal(LotId, dto.LotId);
        Assert.Equal(11, dto.ZoneId);
        Assert.Equal(7L, dto.PolicyId);
        Assert.Equal("51A12345", dto.PlateNumber);
        Assert.Equal(SessionStatuses.Paid, dto.Status);
        Assert.Equal("VERIFIED", dto.VerificationStatus);
        Assert.False(dto.Ticketed);
        Assert.Equal(new DateTime(2026, 11, 20, 1, 0, 0, DateTimeKind.Utc), dto.EnteredAt);
        Assert.Equal(new DateTime(2026, 11, 20, 1, 5, 0, DateTimeKind.Utc), dto.VerifiedAt);
        Assert.Equal(35000m, dto.TotalFee);

        await using var c = new TestSmartParkingDbContext(options);
        Assert.Equal(0, await c.AuditLogs.CountAsync());
        Assert.Equal(SessionStatuses.Paid, (await c.ParkingSessions.SingleAsync()).Status);
    }

    [Fact]
    public async Task BillingContext_RejectsUnsignedUnknownAndMalformedRequests()
    {
        var options = await SeedAsync();
        Assert.Equal(401, Assert.IsType<UnauthorizedObjectResult>(await BillingContextAsync(options, "{\"sessionId\":500}", sign: false)).StatusCode);
        Assert.IsType<NotFoundObjectResult>(await BillingContextAsync(options, "{\"sessionId\":999}"));
        Assert.IsType<BadRequestObjectResult>(await BillingContextAsync(options, "{\"sessionId\":0}"));
        Assert.IsType<BadRequestObjectResult>(await BillingContextAsync(options, "{\"id\":500}"));
        Assert.IsType<BadRequestObjectResult>(await BillingContextAsync(options, "not json"));
    }
}
