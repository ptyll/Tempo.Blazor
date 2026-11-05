namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One applied call in an agent graph, with the monotonic <see cref="Sequence"/> the shared live contract needs.
/// <para>
/// The event is deliberately tiny: a host that learns "the planner called the web search again" sends an edge id and a
/// sequence, and the surface folds it into the edge (a thicker stroke, a fresh "last called" time) and pulses the edge
/// once. That is exactly what a <c>AgentTurnToolAuthorizationDto</c>-style feed produces, whatever its transport (a
/// hub, a one-second poll, a server-sent event stream).
/// </para>
/// <para>
/// A batch may arrive more than once — the shared stream drops what was already applied — so the host never
/// de-duplicates itself.
/// </para>
/// </summary>
public sealed record AgentGraphCall
{
    private int _count = 1;

    /// <summary>Creates a call event.</summary>
    /// <param name="sequence">
    /// The event's position in the run's change log. It must grow by ONE per applied change, because the shared stream
    /// treats "the last applied sequence plus one" as its definition of contiguity and rejects a batch with a hole
    /// rather than papering over it.
    /// </param>
    public AgentGraphCall(long sequence) => Sequence = sequence;

    /// <summary>The event's position in the run's change log; strictly increasing per applied change.</summary>
    public long Sequence { get; init; }

    /// <summary>
    /// The id of the <see cref="AgentGraphEdge"/> the call travelled along. A null, blank or unknown id is IGNORED by
    /// the fold — an edge is never invented from a call, because its two endpoints would be guesses.
    /// </summary>
    public string? EdgeId { get; init; }

    /// <summary>
    /// When the call happened, or <c>null</c> when the host does not stamp it. An older instant than the one the edge
    /// already carries is never written back, so "last called" cannot move backwards.
    /// </summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>
    /// How many calls this one event reports. Default 1, which is what a per-call feed sends; a batched feed may report
    /// a page of identical calls at once. A negative value is clamped to zero, so an edge never shrinks.
    /// </summary>
    public int Count
    {
        get => _count;
        init => _count = value < 0 ? 0 : value;
    }
}
