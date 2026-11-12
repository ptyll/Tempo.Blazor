namespace Tempo.Blazor.Components.Dashboard;

/// <summary>
/// Chrome of the <c>TmDashboard</c> widget card (TG-009): the header with the category icon tile,
/// title, description, collapse chevron and host <c>WidgetHeaderActions</c>, plus the edit-mode
/// drag handle / move buttons and remove control.
/// Switching modes never changes the grid placement (columns/rows) or the widget order — but
/// entering edit mode adds the header bar above the card content, so the content shifts down by
/// roughly the header height while the widget keeps its exact grid cells.
/// </summary>
public enum WidgetChrome
{
    /// <summary>
    /// The header renders in every mode (default) — the pre-TG-009 behaviour. View mode shows the
    /// icon tile, title, description, host actions and the collapse chevron; edit mode adds the
    /// drag handle (desktop) or the move group (compact layouts) and the remove control.
    /// </summary>
    Always,

    /// <summary>
    /// The full header renders only in edit mode. In view mode the widget renders no header at
    /// all — no icon, title, collapse chevron, drag handle or host <c>WidgetHeaderActions</c>;
    /// the <c>WidgetTemplate</c> owns its own title and links. The widget stays a
    /// <c>role="region"</c> named by a visually hidden title through <c>aria-labelledby</c>, and a
    /// widget persisted in the collapsed state still renders its content (there is no collapse
    /// control to honour). Edit mode is unchanged.
    /// </summary>
    EditOnly,

    /// <summary>
    /// No decorative chrome anywhere: the view mode renders like <see cref="EditOnly"/> (region
    /// named by the visually hidden title), and edit mode replaces the full header with a minimal
    /// functional strip — the drag handle (desktop) or the move group (compact layouts) plus the
    /// remove control — so widgets stay movable and removable. The icon tile, title, description,
    /// collapse chevron and host <c>WidgetHeaderActions</c> never render.
    /// </summary>
    Never,
}
