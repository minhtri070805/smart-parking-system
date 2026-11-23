using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ParkingOperations.Application.Common.Interfaces;

namespace ParkingOperations.Infrastructure.Services;

/// <summary>
/// In-process idempotency store backed by ConcurrentDictionary with TTL and in-flight locking.
/// </summary>
public sealed class InMemoryIdempotencyCache : IIdempotencyCache, IDisposable
{
    private record Entry(string Value, DateTimeOffset ExpiresAt, bool IsInFlight);

    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.Ordinal);
    private readonly ILogger<InMemoryIdempotencyCache> _logger;
    private readonly Timer _sweepTimer;

    public InMemoryIdempotencyCache(ILogger<InMemoryIdempotencyCache> logger)
    {
        _logger = logger;
        _sweepTimer = new Timer(Sweep, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public ValueTask<(bool Found, string? Value)> TryGetAsync(string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow && !entry.IsInFlight)
                return ValueTask.FromResult((true, (string?)entry.Value));

            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
                _store.TryRemove(key, out Entry? _);
        }

        return ValueTask.FromResult((false, (string?)null));
    }

    public ValueTask SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
    {
        var entry = new Entry(value, DateTimeOffset.UtcNow.Add(ttl), IsInFlight: false);
        _store[key] = entry;
        return ValueTask.CompletedTask;
    }

    public ValueTask<(IdempotencyStatus Status, string? CachedValue)> AcquireOrGetAsync(string key, TimeSpan inFlightLockTtl, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        while (true)
        {
            if (_store.TryGetValue(key, out var existing))
            {
                if (existing.ExpiresAt <= now)
                {
                    _store.TryRemove(key, out Entry? _);
                    continue;
                }

                if (existing.IsInFlight)
                    return ValueTask.FromResult((IdempotencyStatus.InFlight, (string?)null));

                return ValueTask.FromResult((IdempotencyStatus.Cached, (string?)existing.Value));
            }

            var inFlightEntry = new Entry(string.Empty, now.Add(inFlightLockTtl), IsInFlight: true);
            if (_store.TryAdd(key, inFlightEntry))
            {
                return ValueTask.FromResult((IdempotencyStatus.None, (string?)null));
            }
        }
    }

    public ValueTask ReleaseInFlightAsync(string key, CancellationToken ct = default)
    {
        if (_store.TryGetValue(key, out var entry) && entry.IsInFlight)
        {
            _store.TryRemove(key, out Entry? _);
        }
        return ValueTask.CompletedTask;
    }

    private void Sweep(object? _)
    {
        var now = DateTimeOffset.UtcNow;
        var removed = 0;
        foreach (var kvp in _store)
        {
            if (kvp.Value.ExpiresAt <= now && _store.TryRemove(kvp.Key, out Entry? _))
                removed++;
        }

        if (removed > 0)
            _logger.LogDebug("IdempotencyCache sweep removed {Count} expired entries.", removed);
    }

    public void Dispose() => _sweepTimer.Dispose();
}
