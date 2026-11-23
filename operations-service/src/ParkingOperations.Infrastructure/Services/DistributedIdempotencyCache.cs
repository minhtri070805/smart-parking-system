using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using ParkingOperations.Application.Common.Interfaces;

namespace ParkingOperations.Infrastructure.Services;

/// <summary>
/// Distributed implementation of IIdempotencyCache backed by IDistributedCache (Redis / Distributed Memory).
/// Designed for multi-instance production deployments.
/// </summary>
public sealed class DistributedIdempotencyCache(
    IDistributedCache cache,
    ILogger<DistributedIdempotencyCache> logger) : IIdempotencyCache
{
    private record InFlightMarker(bool InFlight, DateTimeOffset ExpiresAt);

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const string InFlightPrefix = "inflight:";

    public async ValueTask<(bool Found, string? Value)> TryGetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await cache.GetStringAsync(key, ct);
            return (value is not null, value);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read idempotency key {Key} from distributed cache. Falling back to downstream call.", key);
            return (false, null);
        }
    }

    public async ValueTask SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
            await cache.SetStringAsync(key, value, options, ct);
            await cache.RemoveAsync(InFlightPrefix + key, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set idempotency key {Key} in distributed cache.", key);
        }
    }

    public async ValueTask<(IdempotencyStatus Status, string? CachedValue)> AcquireOrGetAsync(string key, TimeSpan inFlightLockTtl, CancellationToken ct = default)
    {
        try
        {
            var cached = await cache.GetStringAsync(key, ct);
            if (cached is not null)
            {
                return (IdempotencyStatus.Cached, cached);
            }

            var inFlightKey = InFlightPrefix + key;
            var inFlightRaw = await cache.GetStringAsync(inFlightKey, ct);
            if (inFlightRaw is not null)
            {
                return (IdempotencyStatus.InFlight, null);
            }

            var marker = new InFlightMarker(true, DateTimeOffset.UtcNow.Add(inFlightLockTtl));
            var markerJson = JsonSerializer.Serialize(marker, JsonOpts);
            var lockOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = inFlightLockTtl };

            await cache.SetStringAsync(inFlightKey, markerJson, lockOptions, ct);
            return (IdempotencyStatus.None, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking in-flight status for key {Key} in distributed cache.", key);
            return (IdempotencyStatus.None, null);
        }
    }

    public async ValueTask ReleaseInFlightAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await cache.RemoveAsync(InFlightPrefix + key, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to release in-flight lock for key {Key}.", key);
        }
    }
}
