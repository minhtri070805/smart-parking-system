using Application.Interfaces.PayOS;
using PaymentAttemptModel = Application.Models.PayOS.PayOSPaymentAttempt;
using PayOSPaymentAttemptEntity = Infrastructure.Features.PayOS.Persistence.PayOSPaymentAttempt;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Features.PayOS.Persistence.Repositories;

public sealed class PayOSPaymentAttemptRepository(PayOSPaymentDbContext db) : IPayOSPaymentAttemptRepository
{
    public async Task<PaymentAttemptModel?> FindByUserAndIdempotencyKeyAsync(long userId, string idempotencyKey, CancellationToken ct)
    {
        var entity = await db.PaymentAttempts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.IdempotencyKey == idempotencyKey, ct);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<PaymentAttemptModel?> FindByOrderCodeAsync(long orderCode, CancellationToken ct)
    {
        var entity = await db.PaymentAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.OrderCode == orderCode, ct);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<PaymentAttemptModel?> FindByIdAsync(long id, CancellationToken ct)
    {
        var entity = await db.PaymentAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<bool> TryAddAsync(PaymentAttemptModel attempt, CancellationToken ct)
    {
        var entity = new PayOSPaymentAttemptEntity();
        CopyToEntity(attempt, entity);
        db.PaymentAttempts.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            attempt.Id = entity.Id;
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(entity).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<PaymentAttemptModel?> StorePaymentLinkAsync(long id, string paymentLinkId, string checkoutUrl,
        DateTime updatedAtUtc, CancellationToken ct)
    {
        var markedPending = await db.PaymentAttempts.Where(x => x.Id == id && x.Status == "CREATING")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.PaymentLinkId, paymentLinkId)
                .SetProperty(x => x.CheckoutUrl, checkoutUrl)
                .SetProperty(x => x.Status, "PENDING")
                .SetProperty(x => x.UpdatedAtUtc, updatedAtUtc), ct);
        if (markedPending == 0)
        {
            await db.PaymentAttempts.Where(x => x.Id == id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.PaymentLinkId, paymentLinkId)
                    .SetProperty(x => x.CheckoutUrl, checkoutUrl)
                    .SetProperty(x => x.UpdatedAtUtc, updatedAtUtc), ct);
        }
        return await FindByIdAsync(id, ct);
    }

    public async Task<bool> TryMarkLinkCreationFailedAsync(long id, DateTime updatedAtUtc, CancellationToken ct) =>
        await db.PaymentAttempts.Where(x => x.Id == id && x.Status == "CREATING")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "LINK_CREATION_FAILED")
                .SetProperty(x => x.UpdatedAtUtc, updatedAtUtc), ct) > 0;

    public async Task<IReadOnlyList<PaymentAttemptModel>> FindDueCoreConfirmationsAsync(DateTime nowUtc, int limit, CancellationToken ct)
    {
        var entities = await db.PaymentAttempts.AsNoTracking()
            .Where(x => x.Status == "PAID_PENDING_CORE_CONFIRMATION" &&
                        (x.NextCoreRetryAtUtc == null || x.NextCoreRetryAtUtc <= nowUtc))
            .OrderBy(x => x.NextCoreRetryAtUtc)
            .Take(limit)
            .ToListAsync(ct);
        return entities.Select(ToModel).ToArray();
    }

    public async Task SaveAsync(PaymentAttemptModel attempt, CancellationToken ct)
    {
        var entity = await db.PaymentAttempts.FirstOrDefaultAsync(x => x.Id == attempt.Id, ct)
            ?? throw new InvalidOperationException($"Payment attempt {attempt.Id} was not found.");
        CopyToEntity(attempt, entity);
        await db.SaveChangesAsync(ct);
    }

    private static PaymentAttemptModel ToModel(PayOSPaymentAttemptEntity entity) => new()
    {
        Id = entity.Id,
        OrderCode = entity.OrderCode,
        UserId = entity.UserId,
        PlanId = entity.PlanId,
        VehicleId = entity.VehicleId,
        CorePaymentIntentId = entity.CorePaymentIntentId,
        Amount = entity.Amount,
        Currency = entity.Currency,
        BillingPeriodStartUtc = entity.BillingPeriodStartUtc,
        BillingPeriodEndUtc = entity.BillingPeriodEndUtc,
        Status = entity.Status,
        PaymentLinkId = entity.PaymentLinkId,
        CheckoutUrl = entity.CheckoutUrl,
        ProviderReference = entity.ProviderReference,
        PaidAtUtc = entity.PaidAtUtc,
        CoreConfirmedAtUtc = entity.CoreConfirmedAtUtc,
        CoreConfirmationAttempts = entity.CoreConfirmationAttempts,
        NextCoreRetryAtUtc = entity.NextCoreRetryAtUtc,
        LastCoreErrorCode = entity.LastCoreErrorCode,
        IdempotencyKey = entity.IdempotencyKey,
        CreatedAtUtc = entity.CreatedAtUtc,
        UpdatedAtUtc = entity.UpdatedAtUtc
    };

    private static void CopyToEntity(PaymentAttemptModel model, PayOSPaymentAttemptEntity entity)
    {
        entity.OrderCode = model.OrderCode;
        entity.UserId = model.UserId;
        entity.PlanId = model.PlanId;
        entity.VehicleId = model.VehicleId;
        entity.CorePaymentIntentId = model.CorePaymentIntentId;
        entity.Amount = model.Amount;
        entity.Currency = model.Currency;
        entity.BillingPeriodStartUtc = model.BillingPeriodStartUtc;
        entity.BillingPeriodEndUtc = model.BillingPeriodEndUtc;
        entity.Status = model.Status;
        entity.PaymentLinkId = model.PaymentLinkId;
        entity.CheckoutUrl = model.CheckoutUrl;
        entity.ProviderReference = model.ProviderReference;
        entity.PaidAtUtc = model.PaidAtUtc;
        entity.CoreConfirmedAtUtc = model.CoreConfirmedAtUtc;
        entity.CoreConfirmationAttempts = model.CoreConfirmationAttempts;
        entity.NextCoreRetryAtUtc = model.NextCoreRetryAtUtc;
        entity.LastCoreErrorCode = model.LastCoreErrorCode;
        entity.IdempotencyKey = model.IdempotencyKey;
        entity.CreatedAtUtc = model.CreatedAtUtc;
        entity.UpdatedAtUtc = model.UpdatedAtUtc;
    }
}

