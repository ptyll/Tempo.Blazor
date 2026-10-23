namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Where <see cref="TmWireframeEditor"/> presents the pages of the document.
/// </summary>
public enum WireframePageNavigation
{
    /// <summary>
    /// The pages are horizontal tabs above the canvas (the layout before the shell restructure).
    /// The left panel then has the Components and Layers tabs.
    /// </summary>
    Tabs,

    /// <summary>
    /// The pages are a numbered vertical list in the left panel (<see cref="TmWireframePagesPanel"/>),
    /// next to Components and Layers. The horizontal page tabs are not rendered.
    /// </summary>
    Panel,
}
