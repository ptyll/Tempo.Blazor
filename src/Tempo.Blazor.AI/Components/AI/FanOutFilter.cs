namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Which slice of a fan-out a <c>TmFanOutGrid</c> shows: everything, or only the tasks in one state.
/// <para>
/// The filter is a single choice (a state filter is not a multi-select search): <see cref="All"/> is the default and the
/// only value that shows every task. The members mirror <see cref="Tempo.Blazor.AI.Models.AgentActivityStatus"/>, so a
/// host never maps between two vocabularies — <see cref="WaitingForApproval"/> is the state that needs the reader.
/// </para>
/// </summary>
public enum FanOutFilter
{
    /// <summary>Every task, whatever its state. The default.</summary>
    All = 0,

    /// <summary>Only the tasks waiting for a worker.</summary>
    Queued = 1,

    /// <summary>Only the tasks a worker is holding right now.</summary>
    Running = 2,

    /// <summary>Only the tasks waiting for a human decision.</summary>
    WaitingForApproval = 3,

    /// <summary>Only the tasks that succeeded.</summary>
    Succeeded = 4,

    /// <summary>Only the tasks that failed (a failed task may still be retryable).</summary>
    Failed = 5,

    /// <summary>Only the tasks that were cancelled.</summary>
    Cancelled = 6,
}
