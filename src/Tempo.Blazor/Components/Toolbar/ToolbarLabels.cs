namespace Tempo.Blazor.Components.Toolbar;

/// <summary>Whether the toolbar's buttons show their text next to the icon.</summary>
public enum ToolbarLabels
{
    /// <summary>
    /// The default: labels show, except on a toolbar narrower than <c>TmBreakpoints.Sm</c> (640px), measured on the
    /// toolbar itself (not the viewport and not the shell's threshold), where icon buttons fall back to icon only.
    /// Text-only buttons keep their text.
    /// </summary>
    Auto,

    /// <summary>Icon buttons never show their text; the text is still the accessible name.</summary>
    Icons,

    /// <summary>Labels always show.</summary>
    IconsWithText,
}
