namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Outcome of a <c>TmCommandPalette</c> <c>SearchProvider</c> fetch. Callers must distinguish
/// <see cref="Empty"/> (the fetch succeeded and found nothing) from <see cref="Transient"/> (the
/// fetch itself failed, e.g. a network blip) — conflating the two hides real outages behind a
/// silent "no results" message.
/// </summary>
public enum CommandPaletteFetchState
{
    /// <summary>The fetch succeeded and returned the matching items.</summary>
    Ok,

    /// <summary>The fetch succeeded but found no matching items.</summary>
    Empty,

    /// <summary>The fetch failed for a reason that may resolve on retry (timeout, network error, 5xx, …).</summary>
    Transient
}

/// <summary>
/// Result of a <c>TmCommandPalette</c> <c>SearchProvider</c> invocation. Lives in Abstractions
/// (no UI dependency) so a backend or API project can return it without referencing the Blazor
/// package. An empty <paramref name="Items"/> list is expected when <paramref name="State"/> is
/// not <see cref="CommandPaletteFetchState.Ok"/>.
/// </summary>
/// <param name="Items">Matching items. Empty (and unused) when <paramref name="State"/> is not <see cref="CommandPaletteFetchState.Ok"/>.</param>
/// <param name="State">Whether the search found results, found none, or failed transiently.</param>
public sealed record CommandPaletteSearchResult(IReadOnlyList<ICommandPaletteAction> Items, CommandPaletteFetchState State)
{
    /// <summary>The search succeeded with <paramref name="items"/>.</summary>
    public static CommandPaletteSearchResult Found(IEnumerable<ICommandPaletteAction> items)
        => new(items.ToList(), CommandPaletteFetchState.Ok);

    /// <summary>The search succeeded but found nothing.</summary>
    public static CommandPaletteSearchResult NoResults { get; } =
        new(Array.Empty<ICommandPaletteAction>(), CommandPaletteFetchState.Empty);

    /// <summary>The search failed transiently; a retry may succeed.</summary>
    public static CommandPaletteSearchResult Failed { get; } =
        new(Array.Empty<ICommandPaletteAction>(), CommandPaletteFetchState.Transient);
}
