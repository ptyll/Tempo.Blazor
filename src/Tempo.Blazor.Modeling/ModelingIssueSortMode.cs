namespace Tempo.Blazor.Modeling;

/// <summary>
/// Sort mode of the modeling issues list (Fáze 11). <see cref="Input"/> is the default and
/// keeps the provider's input order untouched — the pre-Fáze 11 behavior; the two other
/// modes reorder the rows the panel renders without ever mutating the host's
/// <c>Issues</c> collection.
/// </summary>
public enum ModelingIssueSortMode
{
    /// <summary>Input order — the panel never re-sorts (the default).</summary>
    Input,

    /// <summary>Severity groups: Error &gt; Warning &gt; Info, stable within each group
    /// (rows of the same severity keep their input relative order).</summary>
    Severity,

    /// <summary>By the source element/relationship display name (unmatched source ids fall
    /// back to the id itself); issues without a source render last.</summary>
    Source
}
