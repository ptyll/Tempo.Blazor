namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// How a <see cref="TmSidePanel"/> presents itself at a given container width. Distinct from the
/// F3 <c>PanelPresentation</c> (the anchored popover vs sheet choice): a side panel docks in-flow
/// or composes <c>TmDrawer</c> as a sheet — it is never an anchored popover.
/// </summary>
public enum SidePanelPresentation
{
    /// <summary>
    /// Docked in-flow when the panel's CONTAINER resolves Desktop (1024px and up by default); a
    /// modal sheet otherwise — a side sheet, or a bottom sheet when the VIEWPORT resolves Mobile.
    /// </summary>
    Auto,

    /// <summary>The panel renders docked in-flow at every width. <see cref="TmSidePanel.Open"/>
    /// controls whether it renders at all.</summary>
    Docked,

    /// <summary>The panel renders as a modal sheet at every width: a side sheet, or a bottom sheet
    /// when the viewport resolves Mobile.</summary>
    Sheet,
}
