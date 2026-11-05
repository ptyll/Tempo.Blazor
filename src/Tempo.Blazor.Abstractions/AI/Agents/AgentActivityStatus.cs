namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What an agent is doing right now, in the one vocabulary every agent visualization shares.
/// <para>
/// The state is always painted three ways at once — a tone colour, an icon and a localized WORD — because a
/// colour alone is not information a colour-blind or screen-reader reader can use (WCAG 1.4.1). The tone and the
/// icon come from the AI package's shared <c>AgentStatusPresentation</c> mapping, so a lane, a graph node and a
/// fan-out tile can never disagree about what "running" looks like.
/// </para>
/// <para>
/// The set is deliberately transport-neutral: a host maps its own backend vocabulary (a queued turn, a leased
/// attempt, a permission wait, a finished run) onto these seven values and the visualizations stay backend-free.
/// </para>
/// </summary>
public enum AgentActivityStatus
{
    /// <summary>The agent exists but has nothing to do. The neutral state, and the one an unknown backend value falls back to.</summary>
    Idle = 0,

    /// <summary>Work is assigned to the agent and it has not started yet — a turn in the queue, a task waiting for a free slot.</summary>
    Queued = 1,

    /// <summary>The agent is working right now. The only state whose duration is still growing.</summary>
    Running = 2,

    /// <summary>
    /// The agent stopped and is waiting for a human decision (an approval, a permission grant, a review verdict). A
    /// surface paints this in the attention tone, because nothing moves until the reader acts.
    /// </summary>
    WaitingForApproval = 3,

    /// <summary>The agent finished its work successfully. A terminal state.</summary>
    Succeeded = 4,

    /// <summary>The agent failed. A terminal state; the failure reason belongs to the host's own data.</summary>
    Failed = 5,

    /// <summary>The agent was stopped before it finished. Terminal, and neither a success nor a failure.</summary>
    Cancelled = 6,
}
