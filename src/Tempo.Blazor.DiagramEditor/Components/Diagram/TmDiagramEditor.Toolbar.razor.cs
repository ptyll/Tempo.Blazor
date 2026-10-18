using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Toolbar contract of <see cref="TmDiagramEditor"/> (DiagramEditor redesign, phase 1):
/// preset/flag-driven group visibility and the <c>data-tm-group</c> attribute values.
/// Everything here is stable public API — the group set, the attribute values and the
/// <see cref="ToolbarPreset"/>/<see cref="ToolbarGroups"/> parameters must survive the phase-4
/// toolbar regrouping unchanged. Visibility decisions happen only in C#; hosts (e.g. Modeling)
/// must never target the toolbar's internal DOM with positional CSS selectors.
/// </summary>
public partial class TmDiagramEditor
{
    /// <summary>
    /// Toolbar layout preset. <see cref="DiagramToolbarPreset.Full"/> (the default) renders every
    /// toolbar group in the classic order — the look this component has always had.
    /// <see cref="DiagramToolbarPreset.Viewer"/> renders only the zoom, view, search and export
    /// groups for read-only preview surfaces.
    /// </summary>
    [Parameter] public DiagramToolbarPreset ToolbarPreset { get; set; } = DiagramToolbarPreset.Full;

    /// <summary>
    /// Explicit toolbar group selection. When set (non-<c>null</c>) it overrides
    /// <see cref="ToolbarPreset"/>; <c>null</c> (the default) resolves the visible groups from the
    /// preset. The built-in Read-only toggle additionally requires the host not to pin
    /// <see cref="ReadOnly"/> and the <see cref="DiagramToolbarGroups.ReadOnly"/> flag to be set.
    /// </summary>
    [Parameter] public DiagramToolbarGroups? ToolbarGroups { get; set; }

    /// <summary>
    /// Host content rendered at the very start of the first toolbar row, before the document
    /// title (G9 host slot).
    /// </summary>
    [Parameter] public RenderFragment? HeaderStart { get; set; }

    /// <summary>
    /// Host content rendered at the very end of the last toolbar row, after the built-in groups
    /// (G9 host slot).
    /// </summary>
    [Parameter] public RenderFragment? HeaderActions { get; set; }

    /// <summary>
    /// Groups that make up the <see cref="DiagramToolbarPreset.Viewer"/> layout: zoom, view
    /// (fit-to-view stays in the zoom group), search and export.
    /// </summary>
    private const DiagramToolbarGroups ViewerToolbarGroups =
        DiagramToolbarGroups.Zoom | DiagramToolbarGroups.View
        | DiagramToolbarGroups.Search | DiagramToolbarGroups.Export;

    /// <summary>The groups visible in the first toolbar row before the trailing spacer.</summary>
    private const DiagramToolbarGroups Row1LeadingGroups =
        DiagramToolbarGroups.History | DiagramToolbarGroups.Clipboard | DiagramToolbarGroups.Tools
        | DiagramToolbarGroups.Format | DiagramToolbarGroups.Insert | DiagramToolbarGroups.Arrange;

    /// <summary>The groups the second-row divider separates from canvas-size.</summary>
    private const DiagramToolbarGroups Row2TrailingGroups =
        DiagramToolbarGroups.Layout | DiagramToolbarGroups.Export
        | DiagramToolbarGroups.Templates | DiagramToolbarGroups.Import;

    /// <summary>Effective group selection: explicit flags win over the preset.</summary>
    private DiagramToolbarGroups EffectiveToolbarGroups =>
        ToolbarGroups
        ?? (ToolbarPreset == DiagramToolbarPreset.Viewer
            ? ViewerToolbarGroups
            : DiagramToolbarGroups.All);

    private bool Shows(DiagramToolbarGroups group) => EffectiveToolbarGroups.HasFlag(group);

    private bool ShowsRow1Divider => (EffectiveToolbarGroups & Row1LeadingGroups) != 0;

    private bool ShowsRow2Divider =>
        Shows(DiagramToolbarGroups.CanvasSize) && (EffectiveToolbarGroups & Row2TrailingGroups) != 0;

    /// <summary>
    /// Whether the built-in Read-only/Edit toggle renders: only when the host did not pin
    /// <see cref="ReadOnly"/> and the <c>readonly</c> group is enabled. A host-pinned read-only
    /// editor can no longer be unlocked from the UI (DI1).
    /// </summary>
    private bool ShowsReadOnlyToggle =>
        !ReadOnly && Shows(DiagramToolbarGroups.ReadOnly);

    /// <summary>
    /// The <c>data-tm-group</c> attribute value for a group. These values are a frozen public
    /// contract — do not rename them.
    /// </summary>
    private static string GroupName(DiagramToolbarGroups group) => group switch
    {
        DiagramToolbarGroups.History => "history",
        DiagramToolbarGroups.Clipboard => "clipboard",
        DiagramToolbarGroups.Tools => "tools",
        DiagramToolbarGroups.Format => "format",
        DiagramToolbarGroups.Insert => "insert",
        DiagramToolbarGroups.Arrange => "arrange",
        DiagramToolbarGroups.Search => "search",
        DiagramToolbarGroups.Panels => "panels",
        DiagramToolbarGroups.Zoom => "zoom",
        DiagramToolbarGroups.View => "view",
        DiagramToolbarGroups.CanvasSize => "canvas-size",
        DiagramToolbarGroups.Layout => "layout",
        DiagramToolbarGroups.Export => "export",
        DiagramToolbarGroups.Templates => "templates",
        DiagramToolbarGroups.Import => "import",
        DiagramToolbarGroups.ReadOnly => "readonly",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "Unknown toolbar group."),
    };
}
