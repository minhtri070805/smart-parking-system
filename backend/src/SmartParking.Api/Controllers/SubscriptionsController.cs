using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Domain.Constants;
using SmartParking.Domain.Entities;

namespace SmartParking.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class SubscriptionsController(IApplicationDbContext db) : ControllerBase
{
    [HttpGet("subscription-plans")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPlans([FromQuery] int? lotId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var plans = await db.SubscriptionPlans.AsNoTracking()
            .Where(x => x.Status == "APPROVED" && x.EffectiveFromUtc <= now && (x.EffectiveToUtc == null || x.EffectiveToUtc > now))
            .Where(x => !lotId.HasValue || x.LotId == lotId.Value)
            .OrderBy(x => x.LotId).ThenBy(x => x.PriceAmount)
            .Select(x => new { x.Id, x.Code, x.Name, x.Version, x.LotId, x.TermMonths, x.PriceAmount, x.Currency, x.EffectiveFromUtc, x.EffectiveToUtc })
            .ToListAsync(ct);
        return Ok(new { data = plans });
    }

    [HttpGet("admin/subscription-plans")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GetAllPlansForAdmin(
        [FromQuery] string? status,
        [FromQuery] int? lotId,
        CancellationToken ct)
    {
        var query = db.SubscriptionPlans.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToUpperInvariant();
            if (normalizedStatus is not ("DRAFT" or "APPROVED" or "RETIRED"))
                return BadRequest(new { code = "INVALID_PLAN_STATUS", allowedValues = new[] { "DRAFT", "APPROVED", "RETIRED" } });
            query = query.Where(x => x.Status == normalizedStatus);
        }
        if (lotId.HasValue) query = query.Where(x => x.LotId == lotId.Value);
        var plans = await query
            .OrderBy(x => x.Code).ThenBy(x => x.Version)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.Version,
                x.LotId,
                LotName = x.Lot.Name,
                x.TermMonths,
                x.PriceAmount,
                x.Currency,
                x.EffectiveFromUtc,
                x.EffectiveToUtc,
                x.Status,
                x.CreatedBy,
                x.CreatedAtUtc,
                x.ApprovedBy,
                x.ApprovedAtUtc
            })
            .ToListAsync(ct);
        return Ok(new { count = plans.Count, data = plans });
    }

    [HttpPost("subscription-plans")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> CreatePlan([FromBody] CreateSubscriptionPlanRequest request, CancellationToken ct)
    {
        if (request.PriceAmount <= 0 || request.TermMonths != 1 || string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 50 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
            return BadRequest(new { code = "INVALID_PLAN", message = "A plan requires a code, name, positive price and one-month term." });
        var lotExists = await db.ParkingLots.AnyAsync(x => x.Id == request.LotId && x.IsActive, ct);
        if (!lotExists)
            return BadRequest(new { code = "INVALID_LOT", message = "Plans can only target an active parking lot." });
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var version = (await db.SubscriptionPlans.Where(x => x.Code == normalizedCode).Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
        var plan = new SubscriptionPlan
        {
            Code = normalizedCode,
            Name = request.Name.Trim(),
            Version = version,
            LotId = request.LotId,
            TermMonths = 1,
            PriceAmount = request.PriceAmount,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "VND" : request.Currency.Trim().ToUpperInvariant(),
            EffectiveFromUtc = request.EffectiveFromUtc.UtcDateTime,
            EffectiveToUtc = request.EffectiveToUtc?.UtcDateTime,
            CreatedBy = CurrentUserId(),
            Status = "DRAFT"
        };
        if (plan.Currency.Length != 3 || (plan.EffectiveToUtc.HasValue && plan.EffectiveToUtc <= plan.EffectiveFromUtc))
            return BadRequest(new { code = "INVALID_PLAN", message = "Currency or effective date range is invalid." });
        db.SubscriptionPlans.Add(plan);
        db.AuditLogs.Add(new AuditLog { ActorId = plan.CreatedBy, Action = "SUBSCRIPTION_PLAN_CREATED", TargetEntity = "SubscriptionPlan", NewValues = $"{plan.Code}:v{plan.Version}" });
        await db.SaveChangesAsync(ct);
        return Created($"/api/v1/subscription-plans/{plan.Id}", new { data = ToPlanResponse(plan) });
    }

    [HttpPut("subscription-plans/{id:long}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> UpdateDraftPlan(long id, [FromBody] UpdateSubscriptionPlanRequest request, CancellationToken ct)
    {
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (plan is null) return NotFound(new { code = "PLAN_NOT_FOUND" });
        if (plan.Status != "DRAFT") return Conflict(new { code = "PLAN_IMMUTABLE", message = "Only DRAFT plans can be edited. Create a new version for an approved plan." });
        if (request.PriceAmount <= 0 || request.TermMonths != 1 || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
            return BadRequest(new { code = "INVALID_PLAN", message = "A plan requires a name, positive price and one-month term." });

        var lotExists = await db.ParkingLots.AnyAsync(x => x.Id == request.LotId && x.IsActive, ct);
        if (!lotExists)
            return BadRequest(new { code = "INVALID_LOT", message = "Plans can only target an active parking lot." });

        var currency = string.IsNullOrWhiteSpace(request.Currency) ? "VND" : request.Currency.Trim().ToUpperInvariant();
        var startsAtUtc = request.EffectiveFromUtc.UtcDateTime;
        var endsAtUtc = request.EffectiveToUtc?.UtcDateTime;
        if (currency.Length != 3 || (endsAtUtc.HasValue && endsAtUtc <= startsAtUtc))
            return BadRequest(new { code = "INVALID_PLAN", message = "Currency or effective date range is invalid." });

        var oldValues = $"Name={plan.Name};LotId={plan.LotId};TermMonths={plan.TermMonths};PriceAmount={plan.PriceAmount};Currency={plan.Currency};EffectiveFromUtc={plan.EffectiveFromUtc:O};EffectiveToUtc={plan.EffectiveToUtc:O}";
        plan.Name = request.Name.Trim();
        plan.LotId = request.LotId;
        plan.TermMonths = 1;
        plan.PriceAmount = request.PriceAmount;
        plan.Currency = currency;
        plan.EffectiveFromUtc = startsAtUtc;
        plan.EffectiveToUtc = endsAtUtc;
        db.AuditLogs.Add(new AuditLog
        {
            ActorId = CurrentUserId(),
            Action = "SUBSCRIPTION_PLAN_UPDATED",
            TargetEntity = "SubscriptionPlan",
            TargetId = id.ToString(),
            OldValues = oldValues,
            NewValues = $"Name={plan.Name};LotId={plan.LotId};TermMonths={plan.TermMonths};PriceAmount={plan.PriceAmount};Currency={plan.Currency};EffectiveFromUtc={plan.EffectiveFromUtc:O};EffectiveToUtc={plan.EffectiveToUtc:O}"
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { data = ToPlanResponse(plan) });
    }

    [HttpPost("subscription-plans/{id:long}/approve")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> ApprovePlan(long id, CancellationToken ct)
    {
        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (plan is null) return NotFound(new { code = "PLAN_NOT_FOUND" });
        if (plan.Status != "DRAFT") return Conflict(new { code = "PLAN_NOT_DRAFT" });
        plan.Status = "APPROVED";
        plan.ApprovedBy = CurrentUserId();
        plan.ApprovedAtUtc = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLog { ActorId = CurrentUserId(), Action = "SUBSCRIPTION_PLAN_APPROVED", TargetEntity = "SubscriptionPlan", TargetId = id.ToString(), NewValues = $"{plan.Code}:v{plan.Version}" });
        await db.SaveChangesAsync(ct);
        return Ok(new { data = ToPlanResponse(plan) });
    }

    [HttpGet("parking-passes/my")]
    [Authorize(Roles = Roles.User)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var ownerId = CurrentUserId();
        var passes = await db.ParkingPasses.AsNoTracking().Where(x => x.OwnerUserId == ownerId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new { x.Id, x.VehicleId, x.PlanId, x.PlanVersion, x.LotId, x.StartsAtUtc, x.EndsAtUtc, x.Status, x.PriceAmount, x.Currency, x.CreatedAtUtc })
            .ToListAsync(ct);
        return Ok(new { data = passes });
    }

    [HttpGet("parking-passes/{id:long}")]
    [Authorize]
    public async Task<IActionResult> GetPass(long id, CancellationToken ct)
    {
        var ownerId = CurrentUserId();
        var isAdmin = User.IsInRole(Roles.Admin);
        var pass = await db.ParkingPasses.AsNoTracking().Where(x => x.Id == id && (isAdmin || x.OwnerUserId == ownerId))
            .Select(x => new { x.Id, x.OwnerUserId, x.VehicleId, x.PlanId, x.PlanVersion, x.LotId, x.StartsAtUtc, x.EndsAtUtc, x.Status, x.PriceAmount, x.Currency, x.CreatedAtUtc, x.Revision })
            .FirstOrDefaultAsync(ct);
        return pass is null ? NotFound() : Ok(new { data = pass });
    }

    [HttpPost("parking-passes/{id:long}/suspend")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Suspend(long id, [FromBody] SuspendParkingPassRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500) return BadRequest(new { code = "REASON_REQUIRED" });
        var pass = await db.ParkingPasses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (pass is null) return NotFound();
        if (pass.Status != "ACTIVE") return Conflict(new { code = "PASS_NOT_ACTIVE" });
        pass.Status = "SUSPENDED";
        pass.UpdatedAtUtc = DateTime.UtcNow;
        pass.Revision++;
        db.AuditLogs.Add(new AuditLog { ActorId = CurrentUserId(), Action = "PARKING_PASS_SUSPENDED", TargetEntity = "ParkingPass", TargetId = id.ToString(), OldValues = "ACTIVE", NewValues = request.Reason.Trim() });
        await db.SaveChangesAsync(ct);
        return Ok(new { data = new { pass.Id, pass.Status, pass.Revision } });
    }

    private long CurrentUserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedAccessException("A valid user id claim is required.");

    private static object ToPlanResponse(SubscriptionPlan plan) => new
    {
        plan.Id,
        plan.Code,
        plan.Name,
        plan.Version,
        plan.LotId,
        plan.TermMonths,
        plan.PriceAmount,
        plan.Currency,
        plan.EffectiveFromUtc,
        plan.EffectiveToUtc,
        plan.Status,
        plan.CreatedAtUtc,
        plan.ApprovedBy,
        plan.ApprovedAtUtc
    };

}

public sealed record CreateSubscriptionPlanRequest(string Code, string Name, int LotId, int TermMonths, decimal PriceAmount, string Currency, DateTimeOffset EffectiveFromUtc, DateTimeOffset? EffectiveToUtc);
public sealed record UpdateSubscriptionPlanRequest(string Name, int LotId, int TermMonths, decimal PriceAmount, string Currency, DateTimeOffset EffectiveFromUtc, DateTimeOffset? EffectiveToUtc);
public sealed record SuspendParkingPassRequest(string Reason);
