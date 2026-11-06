namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One committed move of a task on a delegate board: the task, the agent it came from and the agent it went to.
/// <para>
/// The board raises at most ONE of these per reader action and changes nothing itself — the host applies the move to
/// its own data and re-renders. The same record describes an assignment
/// (<see cref="From"/> null, <see cref="To"/> set), a return to the backlog (<see cref="To"/> null) and a
/// hand-over between two agents (both set), so a host that only cares about "who holds it now" reads
/// <see cref="AssigneeId"/> and ignores the rest.
/// </para>
/// </summary>
public sealed record DelegateMoveRequest
{
    /// <summary>Creates a move request.</summary>
    /// <param name="task">The task that moved. Required.</param>
    /// <param name="from">The agent that held it (null when it came from the backlog).</param>
    /// <param name="to">The agent that holds it now (null when it went back to the backlog).</param>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is null.</exception>
    public DelegateMoveRequest(DelegateTask task, DelegateAgent? from, DelegateAgent? to)
    {
        Task = task ?? throw new ArgumentNullException(nameof(task));
        From = from;
        To = to;
    }

    /// <summary>The task that moved, as the host supplied it.</summary>
    public DelegateTask Task { get; init; }

    /// <summary>The agent the task came from, or null when it came from the backlog.</summary>
    public DelegateAgent? From { get; init; }

    /// <summary>The agent that holds the task now, or null when it went back to the backlog.</summary>
    public DelegateAgent? To { get; init; }

    /// <summary>The agent id the host should store on the task now, or null for the backlog.</summary>
    public string? AssigneeId => To?.Id;

    /// <summary>Whether the task was handed to an agent (from the backlog or from another agent).</summary>
    public bool IsAssignment => To is not null;

    /// <summary>Whether the task went back to the backlog.</summary>
    public bool IsUnassignment => To is null;

    /// <summary>
    /// Whether the task moved between two DIFFERENT agents. A "move" to the agent that already holds it is not a
    /// change and the board never reports one.
    /// </summary>
    public bool IsHandover => From is not null && To is not null
        && !string.Equals(From.Id, To.Id, StringComparison.Ordinal);
}
