using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Configuration;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// The Q1=A registration snippet from docs/localization-plurals.md, compiled so a snippet that
/// does not build fails the test project. The test compares this body to the documented block.
/// </summary>
internal static class DocumentedUserZoneRegistration
{
    // doc-sample:user-zone
    // builder.Services.AddTempoBlazor();
    // builder.Services.AddScoped<TimeProvider>(sp =>
    // {
    //     var zoneId = sp.GetRequiredService<CircuitZoneCache>().ZoneId; // set once from JS, per circuit
    //     TimeZoneInfo zone;
    //     try { zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId); }
    //     catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; }
    //     catch (InvalidTimeZoneException) { zone = TimeZoneInfo.Utc; }
    //     return new UserTimeProvider(zone);
    // });
    //
    // sealed class UserTimeProvider(TimeZoneInfo zone) : TimeProvider
    // {
    //     public override TimeZoneInfo LocalTimeZone => zone;
    // }

    public static void Register(IServiceCollection builderServices)
    {
        var builder = new HostBuilder(builderServices);
        // doc-sample:user-zone
        builder.Services.AddTempoBlazor();
        builder.Services.AddScoped<TimeProvider>(sp =>
        {
            var zoneId = sp.GetRequiredService<CircuitZoneCache>().ZoneId; // set once from JS, per circuit
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId); }
            catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; }
            catch (InvalidTimeZoneException) { zone = TimeZoneInfo.Utc; }
            return new UserTimeProvider(zone);
        });
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

    private sealed class UserTimeProvider(TimeZoneInfo zone) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => zone;
    }
}
