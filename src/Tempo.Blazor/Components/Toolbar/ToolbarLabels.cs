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

    /// <summary>
    /// Labels show like <see cref="IconsWithText"/>, but when the bar runs out of room (<see cref="ToolbarOverflow.Menu"/>) the
    /// text of the icon+text buttons goes FIRST - they become icon buttons (the text stays the accessible name and tooltip) -
    /// and only when that is not enough do buttons move into "More". The decision is measured on every layout pass by
    /// <c>tm-toolbar.js</c> (the root carries <c>data-tm-labels-collapsed</c> while the labels are hidden), so a widened bar
    /// takes its labels back. Needs <see cref="ToolbarOverflow.Menu"/>; without it this behaves like <see cref="IconsWithText"/>.
    /// </summary>
    ShrinkBeforeOverflow,
}
