namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// The direction of a fill that does NOT need the fill-handle drag (WCAG 2.5.7): each value copies the
/// leading row/column of the selection into the rest of it, the way Ctrl+D / Ctrl+R do in Excel. The
/// handle drag itself stays the fast path on a coarse pointer.
/// </summary>
public enum SpreadsheetFillDirection
{
    /// <summary>Copy the top row of the selection down (Ctrl+D).</summary>
    Down,

    /// <summary>Copy the left column of the selection right (Ctrl+R).</summary>
    Right,
}
