using System.ComponentModel;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// One entry of a spreadsheet context menu: a stable id the host maps to an action, the already-localized
/// label, and the two optional modifiers a menu entry can carry.
/// </summary>
/// <remarks>
/// The model is intentionally free of rendering concerns so <see cref="TmSpreadsheetContextMenu"/> can render
/// the same list as the anchored desktop menu and as the phone's bottom sheet.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SpreadsheetContextMenuItem
{
    /// <summary>Stable action id (kebab case, never <c>tm-</c>-prefixed). Raised back through the menu's selection callback.</summary>
    public required string Id { get; init; }

    /// <summary>The already-localized visible label.</summary>
    public required string Label { get; init; }

    /// <summary>Renders a divider ABOVE this entry.</summary>
    public bool DividerBefore { get; init; }

    /// <summary>Tints the entry as destructive and announces it as such.</summary>
    public bool Danger { get; init; }
}
