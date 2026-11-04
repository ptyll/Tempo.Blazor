namespace Tempo.Blazor.AI.Models;

/// <summary>
/// A snapshot of one context window: the budget it is measured against, the sections it is made of and the share
/// of the prompt the provider served from its cache. It is a SNAPSHOT — the component never mutates it and never
/// polls anything, so a host pushes a new instance as the conversation grows (SignalR, polling, a server-side
/// channel).
/// <para>
/// <see cref="BudgetTokens"/> is the model's context window. A value of <c>0</c> or less means the budget is
/// UNKNOWN: nothing is measurable, so the component reports the used amount with no percentage, claims no
/// over-budget verdict and divides by nothing.
/// </para>
/// </summary>
public sealed record ContextUsageSnapshot
{
    /// <summary>
    /// The context window the usage is measured against, in tokens. Default <c>0</c> (unknown) — no percentage is
    /// claimed and no over-budget verdict is possible.
    /// </summary>
    public int BudgetTokens { get; init; }

    /// <summary>
    /// The parts of the context window, in the host's reading order. Default <c>null</c> or empty renders the
    /// component's localized empty state. The order is preserved everywhere: the bar, the grid, the treemap and
    /// the legend.
    /// </summary>
    public IReadOnlyList<ContextUsageSection>? Sections { get; init; }

    /// <summary>
    /// The share of the prompt the provider served from its cache, as a fraction between <c>0</c> and <c>1</c>
    /// (0.62 = 62 %). Default <c>null</c> — not reported, so no badge is rendered. The value is clamped into the
    /// range; a host with tokens instead of a ratio divides them itself.
    /// </summary>
    public double? CacheHit { get; init; }
}
