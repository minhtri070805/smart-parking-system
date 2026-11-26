using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartParking.Application.Common.Security;
using SmartParking.Domain.Entities;
using SmartParking.Persistence.Data;

namespace SmartParking.Api.Controllers;

[ApiController]
[Route("api/internal/subscription-payments")]
[AllowAnonymous]
public sealed class InternalSubscriptionPaymentsController(
    SmartParkingDbContext db,
    IConfiguration configuration,
    ILogger<InternalSubscriptionPaymentsController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [HttpPost("prepare")]
    public async Task<IActionResult> Prepare(CancellationToken ct)
    {
        var (error, raw) = await ReadSignedBodyAsync(ct);
        if (error is not null) return error;
        if (!configuration.GetValue<bool>("Subscriptions:Enabled"))
            return StatusCode(503, new { code = "SUBSCRIPTIONS_DISABLED" });

        var request = Deserialize<PrepareSubscriptionPaymentRequest>(raw);
        if (request is null || request.OrderCode <= 0 || request.UserId <= 0 || request.PlanId <= 0 || request.VehicleId <= 0)
            return BadRequest(new { code = "INVALID_BODY" });

        var existing = await db.SubscriptionPaymentIntents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrderCode == request.OrderCode, ct);
        if (existing is not null)
            return existing.OwnerUserId == request.UserId && existing.PlanId == request.PlanId && existing.VehicleId == request.VehicleId
                ? Ok(ToPrepareResponse(existing, replay: true))
                : Conflict(new { code = "ORDER_CODE_CONFLICT" });

        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SubscriptionPaymentIntents
            .Where(x => x.Status == "PENDING_PAYMENT" && x.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "CANCELLED"), ct);

        if (!await db.Users.AnyAsync(x => x.Id == request.UserId && x.IsActive, ct))
            return NotFound(new { code = "USER_NOT_FOUND" });

        var plan = await db.SubscriptionPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.PlanId && x.Status == "APPROVED", ct);
        if (plan is null || plan.EffectiveFromUtc > now || (plan.EffectiveToUtc.HasValue && plan.EffectiveToUtc <= now))
            return BadRequest(new { code = "PLAN_UNAVAILABLE" });
        if (plan.Currency != "VND" || plan.PriceAmount <= 0 || plan.PriceAmount > 100_000_000m || decimal.Truncate(plan.PriceAmount) != plan.PriceAmount)
            return BadRequest(new { code = "PLAN_NOT_PAYOS_COMPATIBLE", message = "PayOS subscriptions require a whole VND amount up to 100,000,000." });

        var vehicleOwned = await db.Vehicles.AnyAsync(x => x.Id == request.VehicleId && x.UserId == request.UserId && x.IsActive, ct);
        if (!vehicleOwned) return BadRequest(new { code = "VEHICLE_NOT_OWNED_OR_INACTIVE" });
        if (!await db.ParkingLots.AnyAsync(x => x.Id == plan.LotId && x.IsActive, ct))
            return BadRequest(new { code = "INVALID_LOT" });

        var inProgress = await db.SubscriptionPaymentIntents.AnyAsync(x => x.VehicleId == request.VehicleId &&
            x.LotId == plan.LotId && x.Status == "PENDING_PAYMENT" && x.ExpiresAtUtc > now, ct);
        if (inProgress) return Conflict(new { code = "PAYMENT_ALREADY_IN_PROGRESS" });

        // Old API requests could only create unpaid PENDING passes. They never granted access.
        await db.ParkingPasses.Where(x => x.VehicleId == request.VehicleId && x.LotId == plan.LotId &&
                x.Status == "PENDING" && x.PaymentInvoiceId == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "CANCELLED"), ct);

        var activePass = await db.ParkingPasses
            .Where(x => x.VehicleId == request.VehicleId && x.LotId == plan.LotId && x.Status == "ACTIVE")
            .OrderByDescending(x => x.EndsAtUtc)
            .FirstOrDefaultAsync(ct);
        if (activePass is not null && activePass.EndsAtUtc <= now)
        {
            activePass.Status = "EXPIRED";
            activePass.UpdatedAtUtc = now;
            activePass.Revision++;
            await db.SaveChangesAsync(ct);
            activePass = null;
        }
        if (activePass is not null && (activePass.OwnerUserId != request.UserId || activePass.PlanId != plan.Id))
            return Conflict(new { code = "PLAN_CHANGE_NOT_SUPPORTED", message = "Renewals must use the active subscription plan and vehicle." });

        var billingStart = activePass is not null && activePass.EndsAtUtc > now ? activePass.EndsAtUtc : now;
        var intent = new SubscriptionPaymentIntent
        {
            OrderCode = request.OrderCode,
            OwnerUserId = request.UserId,
            VehicleId = request.VehicleId,
            PlanId = plan.Id,
            LotId = plan.LotId,
            Amount = plan.PriceAmount,
            Currency = plan.Currency,
            TermMonths = plan.TermMonths,
            BillingPeriodStartUtc = billingStart,
            BillingPeriodEndUtc = billingStart.AddMonths(plan.TermMonths),
            Status = "PENDING_PAYMENT",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(24)
        };
        db.SubscriptionPaymentIntents.Add(intent);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { code = "PAYMENT_ALREADY_IN_PROGRESS" });
        }
        return Ok(ToPrepareResponse(intent, replay: false));
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(CancellationToken ct)
    {
        var (error, raw) = await ReadSignedBodyAsync(ct);
        if (error is not null) return error;
        var request = Deserialize<ConfirmSubscriptionPaymentRequest>(raw);
        if (request is null || request.OrderCode <= 0 || request.Amount <= 0 || request.Amount > 100_000_000m ||
            request.Currency != "VND" || string.IsNullOrWhiteSpace(request.ProviderReference) || request.ProviderReference.Length > 100)
            return BadRequest(new { code = "INVALID_BODY" });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var intent = await db.SubscriptionPaymentIntents
            .FromSqlInterpolated($"SELECT * FROM subscription_payment_intents WHERE order_code = {request.OrderCode} FOR UPDATE")
            .FirstOrDefaultAsync(ct);
        if (intent is null) return NotFound(new { code = "PAYMENT_INTENT_NOT_FOUND" });
        if (intent.Status == "CONFIRMED")
        {
            if (intent.ProviderReference != request.ProviderReference || intent.Amount != request.Amount || intent.Currency != request.Currency)
                return Conflict(new { code = "PAYMENT_REFERENCE_CONFLICT" });
            return Ok(ToConfirmResponse(intent, replay: true));
        }
        if (intent.Status != "PENDING_PAYMENT") return Conflict(new { code = "PAYMENT_INTENT_NOT_PENDING" });
        if (intent.Amount != request.Amount || intent.Currency != request.Currency)
            return BadRequest(new { code = "PAYMENT_AMOUNT_MISMATCH" });
        if (await db.SubscriptionPaymentIntents.AnyAsync(x => x.Id != intent.Id && x.ProviderReference == request.ProviderReference, ct))
            return Conflict(new { code = "PAYMENT_REFERENCE_ALREADY_USED" });

        var now = DateTime.UtcNow;
        var paidAt = request.PaidAtUtc == default || request.PaidAtUtc > now.AddMinutes(5)
            ? now
            : request.PaidAtUtc.UtcDateTime;
        var plan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == intent.PlanId, ct);
        if (plan is null || plan.PriceAmount != intent.Amount || plan.Currency != intent.Currency)
            return Conflict(new { code = "PLAN_SNAPSHOT_MISMATCH" });

        var activePass = await db.ParkingPasses
            .Where(x => x.VehicleId == intent.VehicleId && x.LotId == intent.LotId && x.Status == "ACTIVE")
            .OrderByDescending(x => x.EndsAtUtc)
            .FirstOrDefaultAsync(ct);
        ParkingPass pass;
        if (activePass is not null && activePass.EndsAtUtc > paidAt)
        {
            if (activePass.OwnerUserId != intent.OwnerUserId || activePass.PlanId != intent.PlanId)
                return Conflict(new { code = "ACTIVE_PASS_CONFLICT" });
            activePass.EndsAtUtc = activePass.EndsAtUtc > intent.BillingPeriodEndUtc
                ? activePass.EndsAtUtc
                : intent.BillingPeriodEndUtc;
            activePass.PriceAmount = intent.Amount;
            activePass.Currency = intent.Currency;
            activePass.UpdatedAtUtc = now;
            activePass.Revision++;
            pass = activePass;
        }
        else
        {
            if (activePass is not null)
            {
                activePass.Status = "EXPIRED";
                activePass.UpdatedAtUtc = now;
                activePass.Revision++;
                await db.SaveChangesAsync(ct);
            }
            pass = new ParkingPass
            {
                OwnerUserId = intent.OwnerUserId,
                VehicleId = intent.VehicleId,
                PlanId = intent.PlanId,
                PlanVersion = plan.Version,
                LotId = intent.LotId,
                StartsAtUtc = intent.BillingPeriodStartUtc,
                EndsAtUtc = intent.BillingPeriodEndUtc,
                PriceAmount = intent.Amount,
                Currency = intent.Currency,
                IdempotencyKey = $"PAYOS-{intent.OrderCode}",
                Status = "ACTIVE"
            };
            db.ParkingPasses.Add(pass);
        }

        if (pass.Id == 0)
            await db.SaveChangesAsync(ct);

        intent.Status = "CONFIRMED";
        intent.ProviderReference = request.ProviderReference.Trim();
        intent.ParkingPassId = pass.Id;
        intent.ConfirmedAtUtc = now;
        db.AuditLogs.Add(new AuditLog
        {
            ActorId = intent.OwnerUserId,
            Action = "SUBSCRIPTION_PAYMENT_CONFIRMED",
            TargetEntity = "SubscriptionPaymentIntent",
            TargetId = intent.OrderCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NewValues = $"ProviderReference={request.ProviderReference};Amount={intent.Amount} {intent.Currency};ParkingPassId={pass.Id}"
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Confirmed PayOS subscription order {OrderCode} for user {UserId}.", intent.OrderCode, intent.OwnerUserId);
        return Ok(ToConfirmResponse(intent, replay: false));
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(CancellationToken ct)
    {
        var (error, raw) = await ReadSignedBodyAsync(ct);
        if (error is not null) return error;
        var request = Deserialize<CancelSubscriptionPaymentRequest>(raw);
        if (request is null || request.OrderCode <= 0) return BadRequest(new { code = "INVALID_BODY" });
        var intent = await db.SubscriptionPaymentIntents.FirstOrDefaultAsync(x => x.OrderCode == request.OrderCode, ct);
        if (intent is null) return NotFound(new { code = "PAYMENT_INTENT_NOT_FOUND" });
        if (intent.Status == "CONFIRMED") return Conflict(new { code = "PAYMENT_ALREADY_CONFIRMED" });
        if (intent.Status == "PENDING_PAYMENT") intent.Status = "CANCELLED";
        await db.SaveChangesAsync(ct);
        return Ok(new { orderCode = intent.OrderCode, status = intent.Status });
    }

    private async Task<(IActionResult? Error, string Raw)> ReadSignedBodyAsync(CancellationToken ct)
    {
        var secret = configuration[$"{PaymentServiceSignature.SectionName}:SharedSecret"];
        if (!PaymentServiceSignature.IsSecretUsable(secret))
            return (StatusCode(503, new { code = "PAYMENT_INTEGRATION_NOT_CONFIGURED" }), "");
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        if (!PaymentServiceSignature.Verify(secret, Request.Headers[PaymentServiceSignature.TimestampHeader],
                Request.Headers[PaymentServiceSignature.SignatureHeader], raw, DateTimeOffset.UtcNow))
            return (Unauthorized(new { code = "INVALID_SIGNATURE" }), "");
        return (null, raw);
    }

    private static T? Deserialize<T>(string raw)
    {
        try { return JsonSerializer.Deserialize<T>(raw, Json); }
        catch (JsonException) { return default; }
    }

    private static object ToPrepareResponse(SubscriptionPaymentIntent intent, bool replay) => new
    {
        PaymentIntentId = intent.Id,
        intent.OrderCode,
        intent.OwnerUserId,
        intent.PlanId,
        intent.VehicleId,
        intent.LotId,
        intent.Amount,
        intent.Currency,
        intent.TermMonths,
        intent.BillingPeriodStartUtc,
        intent.BillingPeriodEndUtc,
        intent.ExpiresAtUtc,
        replay
    };

    private static object ToConfirmResponse(SubscriptionPaymentIntent intent, bool replay) => new
    {
        intent.OrderCode,
        intent.ParkingPassId,
        intent.Status,
        intent.ConfirmedAtUtc,
        replay
    };
}

public sealed record PrepareSubscriptionPaymentRequest(long OrderCode, long UserId, long PlanId, long VehicleId);
public sealed record ConfirmSubscriptionPaymentRequest(long OrderCode, decimal Amount, string Currency, string ProviderReference, DateTimeOffset PaidAtUtc);
public sealed record CancelSubscriptionPaymentRequest(long OrderCode);
