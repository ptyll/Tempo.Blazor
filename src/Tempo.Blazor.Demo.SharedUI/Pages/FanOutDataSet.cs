namespace Tempo.Blazor.Demo.SharedUI.Pages;

/// <summary>
/// The dataset the <c>/ai-agents</c> demo's fan-out section shows (plan 7ba95ed6, Fáze 17).
/// <para>
/// <see cref="Live"/> is the page's own eighteen-unit run — the list every other walk of the page reads. The three
/// fixtures are the shapes a BROWSER guard needs and a live sample cannot guarantee on demand: <see cref="Zero"/>
/// (the localized empty state), <see cref="One"/> (a single tile and its detail) and <see cref="TwoHundred"/> (more
/// units than the grid's <c>VisibleTaskLimit</c>, so the windowed rows become observable). A fixture is a frozen
/// snapshot: while one is selected the page never re-derives the live sample, so nothing moves under the reader.
/// </para>
/// </summary>
internal enum FanOutDataSet
{
    /// <summary>The page's own run, derived from the demo clock (eighteen units).</summary>
    Live,

    /// <summary>No units at all — the grid's empty state.</summary>
    Zero,

    /// <summary>A single unit — one tile, its detail and the summary of a one-task fan-out.</summary>
    One,

    /// <summary>Two hundred units — the grid mounts a bounded window of whole rows.</summary>
    TwoHundred,
}
