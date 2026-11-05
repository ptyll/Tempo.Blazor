namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How <c>TmAgentHandoff</c> presents a chain. Both presentations render the SAME steps; the chain view is never the
/// only way to read the data (WCAG 1.1.1 / 1.4.1), because a reader who cannot follow the arrows — with a screen
/// reader, a keyboard only, or a narrow phone — gets every step, every passed item and every loss as a plain table.
/// </summary>
public enum AgentHandoffViewMode
{
    /// <summary>
    /// The chain (default): one labelled step per handoff, joined by decorative arrows, each with its route, its state
    /// and a Details button that opens the passed and lost lists.
    /// </summary>
    Chain = 0,

    /// <summary>
    /// The table alternative: one row per handoff (step, from, to, time, state, passed on, lost) carrying the item
    /// labels and the reasons as text. The same data, without the geometry.
    /// </summary>
    Table = 1,
}
