namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// How a <see cref="TmSidePanel"/> presents itself at a given container width. Distinct from the
/// F3 <c>PanelPresentation</c> (the anchored popover vs sheet choice): a side panel docks in-flow
/// or composes <c>TmDrawer</c> as a sheet — it is never an anchored popover.
/// </summary>
public enum SidePanelPresentation
{
    /// <summary>
    /// Follows the container's layout: docked in-flow below the desktop breakpoint (1024px), a
    /// modal side sheet on a tablet container, a modal bottom sheet on a mobile container.
    /// </summary>
    Auto,

    /// <summary>The panel renders docked in-flow at every width. <see cref="TmSidePanel.Open"/>
    /// controls whether it renders at all.</summary>
    Docked,

    /// <summary>The panel renders as a modal sheet at every width: a side sheet on desktop and
    /// tablet containers, a bottom sheet on a mobile container.</summary>
    Sheet,
}
