using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tempo.Blazor.Configuration;

/// <summary>
/// Extension methods for registering Tempo.Blazor signing services.
/// </summary>
public static class SigningServiceCollectionExtensions
{
    /// <summary>
    /// Registers services required by the signing component group.
    /// </summary>
    public static IServiceCollection AddTempoBlazorSigning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // The runner's autosave debounce runs on an injectable clock so tests advance a
        // FakeTimeProvider instead of sleeping. TryAdd: a host that already pinned its own
        // TimeProvider (like the demo) keeps it.
        services.TryAddSingleton(TimeProvider.System);
        services.AddTempoBlazor();
        return services;
    }
}
