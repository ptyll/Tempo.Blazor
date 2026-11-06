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
    /// Toolbar group set. <see cref="DiagramToolbarPreset.Full"/> (the default) renders every group —
    /// a set of commands, not a screen order: the command groups share one toolbar row, search and
    /// panel toggles live in the header, view/canvas-size in the header "⋯" menu and zoom/read-only
    /// in the status bar. <see cref="DiagramToolbarPreset.Viewer"/> renders only the zoom, view,
    /// search and export groups for read-only preview surfaces.
    /// </summary>
    [Parameter] public DiagramToolbarPreset ToolbarPreset { get; set; } = DiagramToolbarPreset.Full;

    /// <summary>
    /// Explicit toolbar group selection. When set (non-<c>null</c>) it overrides
    /// <see cref="ToolbarPreset"/>; <c>null</c> (the default) resolves the visible groups from the
    /// preset. The built-in Read-only toggle additionally requires the host not to pin
    /// <see cref="ReadOnly"/> and the <see cref="DiagramToolbarGroups.ReadOnly"/> flag to be set.
    /// </summary>
    [Parameter] public DiagramToolbarGroups? ToolbarGroups { get; set; }

    /// <summary>Host content rendered at the very start of the header bar, before the document title (G9 host slot).</summary>
    [Parameter] public RenderFragment? HeaderStart { get; set; }

    /// <summary>Host content rendered at the very end of the header bar, after the built-in controls (G9 host slot).</summary>
    [Parameter] public RenderFragment? HeaderActions { get; set; }

    /// <summary>
    /// Optional host-owned history (additive, phase 3 review A5). When bound, EVERY undo/redo entry
    /// — the toolbar pair, the context menu and the canvas' Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y — calls
    /// the host instead of this editor's own per-page stack, so an embedded host (the Modeling
    /// editor with its semantic command stack) keeps ONE undo history. <c>true</c> = redo.
    /// The default (<c>null</c>) keeps today's behavior byte-identical.
    /// </summary>
    [Parameter] public EventCallback<bool> OnHistoryRequested { get; set; }

    /// <summary>Whether the host owns undo/redo (see <see cref="OnHistoryRequested"/>).</summary>
    private bool HostOwnsHistory => OnHistoryRequested.HasDelegate;

    /// <summary>Routes one history request to the host when it owns the stack; false = do it locally.</summary>
    private async Task<bool> RequestHistoryAsync(bool isRedo)
    {
        if (!HostOwnsHistory)
        {
            return false;
        }

        await OnHistoryRequested.InvokeAsync(isRedo);
        return true;
    }

    /// <summary>
    /// Groups that make up the <see cref="DiagramToolbarPreset.Viewer"/> layout: zoom, view
    /// (fit-to-view stays in the zoom group), search and export.
    /// </summary>
    private const DiagramToolbarGroups ViewerToolbarGroups =
        DiagramToolbarGroups.Zoom | DiagramToolbarGroups.View
        | DiagramToolbarGroups.Search | DiagramToolbarGroups.Export;

    /// <summary>Effective group selection: explicit flags win over the preset.</summary>
    private DiagramToolbarGroups EffectiveToolbarGroups =>
        ToolbarGroups
        ?? (ToolbarPreset == DiagramToolbarPreset.Viewer
            ? ViewerToolbarGroups
            : DiagramToolbarGroups.All);

    private bool Shows(DiagramToolbarGroups group) => EffectiveToolbarGroups.HasFlag(group);

    /// <summary>
    /// Whether the built-in Read-only/Edit toggle renders: only when the host did not pin
    /// <see cref="ReadOnly"/> and the <c>readonly</c> group is enabled. A host-pinned read-only
    /// editor can no longer be unlocked from the UI (DI1).
    /// </summary>
    private bool ShowsReadOnlyToggle =>
        !ReadOnly && Shows(DiagramToolbarGroups.ReadOnly);
}
