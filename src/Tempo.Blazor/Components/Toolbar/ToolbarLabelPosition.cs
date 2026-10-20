namespace Tempo.Blazor.Components.Toolbar;

/// <summary>Where a <see cref="TmToolbarButton"/> shows its text label relative to the icon.</summary>
public enum ToolbarLabelPosition
{
    /// <summary>Icon and label on one line (the default).</summary>
    Inline,

    /// <summary>The label sits under the icon — the ribbon style of the tablet mockups.</summary>
    Below,

    /// <summary>No visible label. The text stays the accessible name (aria-label) of the button.</summary>
    Hidden,
}
