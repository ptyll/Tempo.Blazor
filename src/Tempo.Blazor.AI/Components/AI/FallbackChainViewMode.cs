namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How <c>TmFallbackChain</c> presents the run. Both presentations render the SAME attempts; the flow is never the only
/// way to read the data (WCAG 1.1.1 / 1.4.1), because a reader who cannot follow the arrows — with a screen reader, a
/// keyboard only, or a narrow phone — gets every attempt, its result, its reason and its latency as a plain table.
/// </summary>
public enum FallbackChainViewMode
{
    /// <summary>
    /// The flow (default): one node per attempt in the order it was tried, joined by decorative arrows, ending in the
    /// Answer node the first successful attempt branches to.
    /// </summary>
    Flow = 0,

    /// <summary>
    /// The table alternative: one row per attempt (position, model, result, reason, latency, time). The same data,
    /// without the geometry.
    /// </summary>
    Table = 1,
}
