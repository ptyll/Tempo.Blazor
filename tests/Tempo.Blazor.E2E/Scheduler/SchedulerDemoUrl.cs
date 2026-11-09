namespace Tempo.Blazor.E2E.Scheduler;

/// <summary>
/// The <c>/scheduler</c> demo page URL, with an opt-in date override.
/// </summary>
/// <remarks>
/// The demo page anchors its sample week to Wednesday of the current week, so the layout no longer
/// follows the real weekday. <c>TM_E2E_DEMO_NOW</c> replaces "the current week" with the week of the
/// given instant through the demo's own <c>?now=</c> clock contract (the WASM host registers a pinned
/// <c>TimeProvider</c>), which is how the weekend cases get replayed on any day:
/// <code>TM_E2E_DEMO_NOW=2026-11-14T12:00:00Z dotnet test ... --filter "FullyQualifiedName~SchedulerResponsive"</code>
/// pins a Saturday, <c>2026-11-15T12:00:00Z</c> a Sunday. A query the caller already supplies wins.
/// </remarks>
internal static class SchedulerDemoUrl
{
    /// <summary>The scheduler demo page path.</summary>
    public const string Path = "/scheduler";

    private const string WeekdayOverride = "TM_E2E_DEMO_NOW";

    /// <summary>
    /// The absolute page URL. <paramref name="query"/> is used as given (a leading <c>?</c> included);
    /// without one the pinned clock from <see cref="WeekdayOverride"/> is appended when it is set.
    /// </summary>
    public static string For(string baseUrl, string query = "")
        => string.IsNullOrEmpty(query)
            ? $"{baseUrl}{Path}{PinnedClockQuery}"
            : $"{baseUrl}{Path}{query}";

    private static string PinnedClockQuery
    {
        get
        {
            var now = Environment.GetEnvironmentVariable(WeekdayOverride);
            return string.IsNullOrWhiteSpace(now)
                ? string.Empty
                : $"?now={Uri.EscapeDataString(now)}";
        }
    }
}
