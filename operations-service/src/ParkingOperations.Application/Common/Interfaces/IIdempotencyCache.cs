namespace ParkingOperations.Application.Common.Interfaces;

/// <summary>
/// Result of an idempotency lookup or lock acquisition.
/// </summary>
public enum IdempotencyStatus
{
    None,
    Cached,
    InFlight
}

/// <summary>
/// Idempotency cache with in-flight concurrency lock to prevent race conditions
/// when multiple concurrent requests carry the same idempotency key.
/// </summary>
public interface IIdempotencyCache
{
    ValueTask<(bool Found, string? Value)> TryGetAsync(string key, CancellationToken ct = default);
    ValueTask SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Atomically acquires an in-flight execution lock or returns existing cached value.
    /// If an operation with this key is already running, returns (IdempotencyStatus.InFlight, null).
    /// If an operation has already completed and cached, returns (IdempotencyStatus.Cached, cachedValue).
    /// If no operation exists, acquires lock and returns (IdempotencyStatus.None, null).
    /// </summary>
    ValueTask<(IdempotencyStatus Status, string? CachedValue)> AcquireOrGetAsync(string key, TimeSpan inFlightLockTtl, CancellationToken ct = default);

    /// <summary>
    /// Releases the in-flight lock (used on failure or cancellation when result is NOT cached).
    /// </summary>
    ValueTask ReleaseInFlightAsync(string key, CancellationToken ct = default);
}
