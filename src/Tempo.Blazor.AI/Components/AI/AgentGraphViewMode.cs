namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Which presentation of the same agent graph is shown.
/// <para>
/// <see cref="Graph"/> is the drawn network — nodes, curves, call counts as thickness. <see cref="Table"/> is the TEXT
/// alternative with exactly the same data: every connection as a row, so a screen reader, a keyboard-only reader or a
/// reader on a narrow phone can reach the call counts without interpreting a picture (WCAG 1.1.1).
/// </para>
/// </summary>
public enum AgentGraphViewMode
{
    /// <summary>The drawn network. The default.</summary>
    Graph = 0,

    /// <summary>The table of every connection with its call count — the text alternative.</summary>
    Table = 1,
}
