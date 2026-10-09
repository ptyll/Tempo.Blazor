namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// Identifies a side panel of the <see cref="TmEditorShell"/> — the <see cref="EditorShellPanel.Left"/>
/// toolbox-style panel and the <see cref="EditorShellPanel.Right"/> properties-style panel. Used by
/// <c>CollapsedPanels</c> to render a panel as a slim rail instead of its full width.
/// </summary>
[Flags]
public enum EditorShellPanel
{
    /// <summary>The left panel (a toolbox, a block list, navigation).</summary>
    Left = 1,

    /// <summary>The right panel (an inspector, properties, event detail).</summary>
    Right = 2,
}
