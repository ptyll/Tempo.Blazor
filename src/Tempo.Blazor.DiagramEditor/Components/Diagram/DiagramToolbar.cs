namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Stable public API of the <see cref="TmDiagramEditor"/> toolbar contract (DiagramEditor
/// redesign, phase 1). The group set, the <c>data-tm-group</c> attribute values and both enums
/// must survive toolbar regrouping (phase 4) unchanged — hosts and the Modeling package key off
/// them instead of positional CSS selectors.
/// </summary>
public enum DiagramToolbarPreset
{
    /// <summary>
    /// Every toolbar group — the set of groups, not a historical order. Since the phase-4
    /// desktop restructure the command groups share one TmToolbar row, the search and panel
    /// toggles live in the header, the view/canvas-size groups in the header "⋯" menu and the
    /// zoom/read-only groups in the status bar. This is the default and keeps every command.
    /// </summary>
    Full = 0,

    /// <summary>
    /// Preview-friendly subset for read-only viewer surfaces: the zoom, view, search and export
    /// groups. The read-only toggle never renders in this preset.
    /// </summary>
    Viewer = 1,
}

/// <summary>
/// Frozen set of <see cref="TmDiagramEditor"/> toolbar groups. Each flag maps to exactly one
/// <c>data-tm-group</c> attribute value on the toolbar DOM (see
/// <see cref="TmDiagramEditor.ToolbarGroups"/>): <c>history</c>, <c>clipboard</c>, <c>tools</c>,
/// <c>format</c>, <c>insert</c>, <c>arrange</c>, <c>search</c>, <c>panels</c>, <c>zoom</c>,
/// <c>view</c>, <c>canvas-size</c>, <c>layout</c>, <c>export</c>, <c>templates</c>,
/// <c>import</c>, <c>readonly</c>.
/// </summary>
[Flags]
public enum DiagramToolbarGroups
{
    /// <summary>No toolbar group.</summary>
    None = 0,

    /// <summary>Undo / Redo group (<c>data-tm-group="history"</c>).</summary>
    History = 1 << 0,

    /// <summary>Cut / Copy / Paste / Duplicate / Delete group (<c>data-tm-group="clipboard"</c>).</summary>
    Clipboard = 1 << 1,

    /// <summary>Select / Pan / Edge tool modes (<c>data-tm-group="tools"</c>).</summary>
    Tools = 1 << 2,

    /// <summary>Format painter group (<c>data-tm-group="format"</c>).</summary>
    Format = 1 << 3,

    /// <summary>Insert table / text dropdown (<c>data-tm-group="insert"</c>).</summary>
    Insert = 1 << 4,

    /// <summary>Align / distribute dropdown (<c>data-tm-group="arrange"</c>).</summary>
    Arrange = 1 << 5,

    /// <summary>Search toggle (<c>data-tm-group="search"</c>).</summary>
    Search = 1 << 6,

    /// <summary>Toolbox / properties / layers / minimap panel toggles (<c>data-tm-group="panels"</c>).</summary>
    Panels = 1 << 7,

    /// <summary>Zoom out / level / in / fit-to-view group (<c>data-tm-group="zoom"</c>).</summary>
    Zoom = 1 << 8,

    /// <summary>Grid / snap / page view / ruler toggles (<c>data-tm-group="view"</c>).</summary>
    View = 1 << 9,

    /// <summary>Ruler unit and canvas size group (<c>data-tm-group="canvas-size"</c>).</summary>
    CanvasSize = 1 << 10,

    /// <summary>Auto layout dropdown (<c>data-tm-group="layout"</c>).</summary>
    Layout = 1 << 11,

    /// <summary>Export dropdown (<c>data-tm-group="export"</c>).</summary>
    Export = 1 << 12,

    /// <summary>Template gallery button (<c>data-tm-group="templates"</c>).</summary>
    Templates = 1 << 13,

    /// <summary>Import dropdown (<c>data-tm-group="import"</c>).</summary>
    Import = 1 << 14,

    /// <summary>Read-only / Edit toggle (<c>data-tm-group="readonly"</c>).</summary>
    ReadOnly = 1 << 15,

    /// <summary>Every group — the <see cref="DiagramToolbarPreset.Full"/> layout.</summary>
    All = History | Clipboard | Tools | Format | Insert | Arrange | Search | Panels
        | Zoom | View | CanvasSize | Layout | Export | Templates | Import | ReadOnly,
}
