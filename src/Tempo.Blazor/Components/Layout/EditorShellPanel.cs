namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// Identifies a side panel of the <see cref="TmEditorShell"/> — the <see cref="EditorShellPanel.Left"/>
/// toolbox-style panel and the <see cref="EditorShellPanel.Right"/> properties-style panel. Used by
/// <c>CollapsedPanels</c> to render a panel as a slim rail instead of its full width, and by
/// <c>ActiveMobilePanel</c> to name the panel a mobile layout shows.
/// </summary>
[Flags]
public enum EditorShellPanel
{
    /// <summary>No panel: nothing collapsed, or — for <c>ActiveMobilePanel</c> — the shell's default
    /// (the canvas tab of the Tabs presentation, the first open panel of the sheet).</summary>
    None = 0,

    /// <summary>The left panel (a toolbox, a block list, navigation).</summary>
    Left = 1,

    /// <summary>The right panel (an inspector, properties, event detail).</summary>
    Right = 2,
}
