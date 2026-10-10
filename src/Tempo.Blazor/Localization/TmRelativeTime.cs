namespace Tempo.Blazor.Localization;

/// <summary>
/// Resolves the relative-time formatter for a component. A host that called <c>AddTempoBlazor()</c>
/// gets the registered <see cref="ITmRelativeTimeFormatter"/> (and so can replace it); a host — or a test
/// context — that only registered an <see cref="ITmLocalizer"/> gets the default formatter over that
/// localizer and the registered <see cref="TimeProvider"/> (or the system clock). Never throws for a
/// missing registration.
/// </summary>
internal static class TmRelativeTime
{
    internal static ITmRelativeTimeFormatter Resolve(IServiceProvider services, ITmLocalizer localizer)
        => services.GetService(typeof(ITmRelativeTimeFormatter)) as ITmRelativeTimeFormatter
           ?? new TmRelativeTimeFormatter(localizer, services.GetService(typeof(TimeProvider)) as TimeProvider);
}