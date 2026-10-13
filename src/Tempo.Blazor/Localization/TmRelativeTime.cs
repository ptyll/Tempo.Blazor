namespace Tempo.Blazor.Localization;

/// <summary>
/// Resolves the relative-time formatter for a component. A host that called <c>AddTempoBlazor()</c>
/// gets the registered <see cref="ITmRelativeTimeFormatter"/> (and so can replace it); a host — or a test
/// context — that only registered an <see cref="ITmLocalizer"/> gets the default formatter over that
/// localizer and the registered <see cref="TimeProvider"/> (or the system clock). Never throws for a
/// missing registration.
/// </summary>
/// <remarks>
/// Public so every component — including split packages that call <c>AddTempoBlazor()</c> — resolves
/// the formatter through one helper instead of copying the fallback. When no formatter is registered,
/// each call returns a new instance — cache it per component.
/// </remarks>
public static class TmRelativeTime
{
    /// <summary>
    /// Returns the <see cref="ITmRelativeTimeFormatter"/> to render a relative-time stamp with:
    /// the one registered in <paramref name="services"/> when present, otherwise a default
    /// <see cref="TmRelativeTimeFormatter"/> over <paramref name="localizer"/> and the
    /// <see cref="TimeProvider"/> registered in <paramref name="services"/> (the system clock when
    /// none is). Never throws for a missing registration, so a component renders localized relative
    /// time even in a bare test context that registered only an <see cref="ITmLocalizer"/>.
    /// </summary>
    /// <param name="services">The component's service provider (a Blazor component passes <c>Services</c>).</param>
    /// <param name="localizer">The component's localizer; the fallback formatter reads <c>Tm_Time_*</c> from it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="localizer"/> is null.</exception>
    public static ITmRelativeTimeFormatter Resolve(IServiceProvider services, ITmLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(localizer);
        return services.GetService(typeof(ITmRelativeTimeFormatter)) as ITmRelativeTimeFormatter
            ?? new TmRelativeTimeFormatter(localizer, services.GetService(typeof(TimeProvider)) as TimeProvider);
    }
}