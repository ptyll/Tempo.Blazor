namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How <c>TmAgentLanes</c> presents a run. Both presentations render the SAME snapshot; the graphical one is never the
/// only way to read the data (WCAG 1.1.1 / 1.4.1), because a reader who cannot use a time axis — with a screen reader,
/// a keyboard only, or a narrow phone — gets every block and every handoff as a plain table.
/// </summary>
public enum AgentLanesViewMode
{
    /// <summary>
    /// The timeline (default): one lane per agent, a block per piece of work, handoff arrows and the "now" line. The
    /// blocks are native buttons, so a reader walks them with Tab and selects with Enter.
    /// </summary>
    Timeline = 0,

    /// <summary>
    /// The table alternative: every block as a row (agent, activity, kind, start, end, state, marker) plus a second
    /// table of the handoffs. The same data, without the geometry.
    /// </summary>
    Table = 1,
}
