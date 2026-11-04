using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tempo.Blazor.Demo.SharedUI.Services;

/// <summary>
/// Registration for the demo's clock.
/// </summary>
/// <remarks>
/// <para>
/// The shared demo pages read the clock the way the library components do — through an injected
/// <see cref="TimeProvider"/> — because a generated sample that hardcodes <c>DateTimeOffset.UtcNow</c> cannot be
/// pinned by a test or by a screenshot run. A bare <c>@inject TimeProvider</c> throws in a host that never
/// registered the service, so every demo host goes through this method instead of registering it ad hoc.
/// </para>
/// <para>
/// The library itself must NOT register a clock (<c>AddTempoBlazor</c> deliberately does not): a component
/// resolves one defensively, and an application owns its own time policy.
/// </para>
/// </remarks>
public static class DemoClockServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TimeProvider.System"/> as the demo's clock unless the host already registered one.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddTempoDemoClock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd, not Add: the WASM demo registers a real scoped clock (its ?now= pinned instance), and a
        // second descriptor would silently replace it depending on registration order.
        services.TryAddScoped(_ => TimeProvider.System);
        return services;
    }
}
