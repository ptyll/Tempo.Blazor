namespace Tempo.Blazor.Components.Toolbar;

/// <summary>
/// How important a <see cref="TmToolbarButton"/> is when its <see cref="TmToolbar"/> runs out of room
/// (<see cref="ToolbarOverflow.Menu"/>). Generalises the DocumentEditor's <c>ToolbarItemPriority</c>
/// (the editor migrates onto the core mechanism in its own plan). The priority is an overflow RANK
/// only — buttons always render in the order they are written. The values are NOT ranks: never compare or cast
/// them (a <see cref="Pinned"/> button is the highest-ranked and numerically the last); the toolbar maps them to a rank
/// internally.
/// </summary>
public enum ToolbarButtonPriority
{
    /// <summary>The default. Stays on the toolbar until every Secondary button has moved into the "More" menu.</summary>
    Primary,

    /// <summary>Moves into the "More" menu before any Primary button does.</summary>
    Secondary,

    /// <summary>Always lives in the "More" menu and is never rendered on the toolbar itself.</summary>
    OverflowOnly,

    /// <summary>Never moves into the "More" menu: stays on the bar at every width (the trailing Save / primary call to action).</summary>
    Pinned,
}
