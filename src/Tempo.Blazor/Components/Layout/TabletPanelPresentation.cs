namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// How an editor shell presents its Left and Right panels on a <see cref="Tempo.Blazor.Abstractions.Layout.TmLayoutMode.Tablet"/> layout.
/// Desktop and mobile layouts are not affected by this choice.
/// </summary>
public enum TabletPanelPresentation
{
    /// <summary>
    /// The panels stay docked columns (a slim rail when collapsed, at most ONE expanded, resizable).
    /// This is the behaviour of every earlier version.
    /// </summary>
    Docked = 0,

    /// <summary>
    /// The panels leave the layout: the Left panel is an overlay drawer from the left edge and the Right panel a
    /// non-modal side sheet from the right edge, over a canvas that keeps its full width. At most ONE is open
    /// (opening one closes the other), there are no rails and no resize separators, and Escape closes the open one.
    /// </summary>
    Overlay = 1,
}