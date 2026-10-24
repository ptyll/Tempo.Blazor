using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// Resolves the relative-time formatter the "Saved 5 min ago" badge uses, tolerating a host that registers
/// less than <c>AddTempoBlazor()</c> does.
/// </summary>
internal static class EmailRelativeTime
{
    /// <summary>
    /// The host's <see cref="ITmRelativeTimeFormatter"/> when it registered one; otherwise the core
    /// implementation over the host's <see cref="ITmLocalizer"/> and <paramref name="clock"/>; otherwise
    /// <see langword="null"/> (a test host with no core localization at all). The badge then says plain
    /// "Saved" - the relative phrase is optional chrome, never a reason to throw or to show a key.
    /// </summary>
    public static ITmRelativeTimeFormatter? Resolve(IServiceProvider services, TimeProvider clock)
    {
        if (services.GetService(typeof(ITmRelativeTimeFormatter)) is ITmRelativeTimeFormatter registered)
        {
            return registered;
        }

        return services.GetService(typeof(ITmLocalizer)) is ITmLocalizer core
            ? new TmRelativeTimeFormatter(core, clock)
            : null;
    }
}
