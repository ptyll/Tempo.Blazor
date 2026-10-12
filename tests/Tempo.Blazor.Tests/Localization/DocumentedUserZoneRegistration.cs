using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// The Q1=A registration snippet from docs/localization-plurals.md, compiled so a snippet that
/// does not build fails the test project. The test compares the marked registration block to the
/// documented block, and resolves it under <c>ValidateScopes</c> beside a singleton
/// <see cref="TimeProvider"/> consumer.
/// </summary>
internal static class DocumentedUserZoneRegistration
{
    public static void Register(IServiceCollection builderServices)
    {
        var builder = new HostBuilder(builderServices);
        // doc-sample:user-zone
        builder.Services.AddTempoBlazor();
        builder.Services.AddScoped<CircuitZoneCache>();
        builder.Services.AddScoped<ITmRelativeTimeFormatter>(sp => new TmRelativeTimeFormatter(
            sp.GetRequiredService<ITmLocalizer>(),
            new UserTimeProvider(sp.GetRequiredService<CircuitZoneCache>())));
    }

    private sealed class HostBuilder(IServiceCollection services)
    {
        public IServiceCollection Services { get; } = services;
    }

    internal sealed class CircuitZoneCache
    {
        public string ZoneId { get; set; } = "";

        /// <summary>Set by the host once JS reports the zone. Read on every format, never captured.</summary>
        public TimeZoneInfo? Zone { get; set; }
    }

    // Nested types are private by default, so the declaration text is byte-for-byte the documented one.
    sealed class UserTimeProvider(CircuitZoneCache cache) : TimeProvider
    {
        // Read lazily: the zone arrives from JS after the first render.
        public override TimeZoneInfo LocalTimeZone => cache.Zone ?? TimeZoneInfo.Utc;
    }
}
