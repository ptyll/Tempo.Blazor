namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Where a <c>TmFanOutGrid</c> renders the detail of the focused (or hovered) tile. The default
/// <see cref="Inline"/> keeps the detail as a region below the grid (the historical behaviour);
/// <see cref="Popover"/> anchors the same content to the tile in an overlay panel that closes on
/// Escape and hands the focus back — the better shape for a narrow, measured grid.
/// </summary>
public enum FanOutDetailPresentation
{
    /// <summary>The detail follows the tile as a region below the grid. The default.</summary>
    Inline = 0,

    /// <summary>The detail opens in an overlay panel anchored to the tile, with Escape and focus return.</summary>
    Popover = 1,
}
