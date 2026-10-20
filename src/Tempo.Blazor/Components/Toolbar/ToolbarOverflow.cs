namespace Tempo.Blazor.Components.Toolbar;

/// <summary>What <see cref="TmToolbar"/> does when its buttons do not fit.</summary>
public enum ToolbarOverflow
{
    /// <summary>The default: nothing collapses; the buttons render as written (no behaviour change for existing toolbars).</summary>
    None,

    /// <summary>
    /// Buttons that do not fit move into a "More" menu, lowest <see cref="ToolbarButtonPriority"/> first;
    /// <see cref="ToolbarButtonPriority.OverflowOnly"/> buttons always live there. The menu is a
    /// dropdown that presents as a bottom sheet on a phone-sized viewport.
    /// </summary>
    Menu,
}
