namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// Where a context menu was requested and what it applies to. Raised by
/// <see cref="TmSpreadsheetCanvasGrid"/> and <see cref="TmSpreadsheetSheetTabs"/> when they are told NOT to
/// render their own menu (<c>RenderContextMenu="false"</c>), so the host can render it as a sibling of the
/// layout container.
/// </summary>
/// <remarks>
/// The menu surfaces are <c>position: fixed</c>. The layout container carries
/// <c>container-type: inline-size</c>, which implies <c>contain: layout</c> and therefore makes it the
/// containing block of fixed descendants — a menu rendered inside it would be positioned against the
/// component's box instead of the viewport (and a menu rendered inside the scrolling grid would also be
/// clipped). Hence the host renders them outside the container.
/// </remarks>
public sealed record SpreadsheetContextMenuRequest
{
    /// <summary>Zero-based row of the cell the menu applies to; -1 for a sheet-tab request.</summary>
    public int Row { get; init; } = -1;

    /// <summary>Zero-based column of the cell the menu applies to; -1 for a sheet-tab request.</summary>
    public int Column { get; init; } = -1;

    /// <summary>Zero-based index of the sheet the menu applies to; -1 for a grid request.</summary>
    public int SheetIndex { get; init; } = -1;

    /// <summary>Client (viewport) X of the gesture that opened the menu.</summary>
    public double ClientX { get; init; }

    /// <summary>Client (viewport) Y of the gesture that opened the menu.</summary>
    public double ClientY { get; init; }
}
