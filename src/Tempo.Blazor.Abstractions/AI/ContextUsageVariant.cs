namespace Tempo.Blazor.AI.Models;

/// <summary>
/// How the context window is presented:
/// <see cref="Bar"/> (one stacked horizontal bar, the default),
/// <see cref="Grid"/> (a hundred cells, one per percent of the budget, so the free headroom is visible) or
/// <see cref="Treemap"/> (the sections as areas, through the core <c>TmChart</c>).
/// <para>
/// The presentation only changes the PICTURE. The legend table, the meter and the empty state are identical in
/// every variant, so a host can switch it per layout without losing a fact or a control.
/// </para>
/// </summary>
public enum ContextUsageVariant
{
    /// <summary>One stacked bar whose segments are the sections. The default.</summary>
    Bar = 0,

    /// <summary>A hundred cells, one per percent of the budget; used cells carry the tone of their section.</summary>
    Grid = 1,

    /// <summary>A treemap of the sections (and their items) drawn by the core <c>TmChart</c>.</summary>
    Treemap = 2,
}
