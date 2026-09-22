using System.Collections.Concurrent;
using Tempo.Blazor.Abstractions.Interfaces;
using Tempo.Blazor.Abstractions.Shared;

namespace Tempo.Blazor.Services;

/// <summary>In-memory <see cref="IPushSubscriptionStore"/> for demos and tests (upsert by endpoint).</summary>
public sealed class InMemoryPushSubscriptionStore : IPushSubscriptionStore
{
    // endpoint -> subscription
    private readonly ConcurrentDictionary<string, TmPushSubscription> _byEndpoint = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task SaveAsync(TmPushSubscription subscription, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (!subscription.IsValid)
            throw new ArgumentException("Push subscription is missing required fields.", nameof(subscription));

        _byEndpoint[subscription.Endpoint] = subscription;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(endpoint))
            _byEndpoint.TryRemove(endpoint, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TmPushSubscription>> GetForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var list = _byEndpoint.Values
            .Where(s => string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult<IReadOnlyList<TmPushSubscription>>(list);
    }

    /// <summary>Total number of stored subscriptions (test/diagnostic helper).</summary>
    public int Count => _byEndpoint.Count;

    /// <summary>Removes every stored subscription (test/diagnostic helper).</summary>
    public void Clear() => _byEndpoint.Clear();
}
