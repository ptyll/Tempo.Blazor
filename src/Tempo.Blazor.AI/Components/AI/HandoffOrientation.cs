namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How <c>TmAgentHandoff</c> lays its chain out. The two orientations render the SAME ordered list — vertical by
/// default, horizontal for a handoff shown along a toolbar or a wide card — so a reader loses no fact and no test has
/// to know two markups.
/// </summary>
public enum HandoffOrientation
{
    /// <summary>The chain runs top to bottom (default): one step under the last, joined by arrows.</summary>
    Vertical = 0,

    /// <summary>The chain runs start to end across the surface, joined by arrows pointing along the reading direction.</summary>
    Horizontal = 1,
}
