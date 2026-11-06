namespace Tempo.Blazor.AI.Models;

/// <summary>
/// How a scheduled run is doing. The board paints it as a WORD plus an icon, never as a colour alone, and the same
/// value also decides whether the run still competes for window capacity (only
/// <see cref="ScheduledRunStatus.Planned"/> and <see cref="ScheduledRunStatus.Running"/> do).
/// </summary>
public enum ScheduledRunStatus
{
    /// <summary>Waiting for its window. The DEFAULT: an unclassified run is planned work, never a finished one.</summary>
    Planned = 0,

    /// <summary>Running right now.</summary>
    Running = 1,

    /// <summary>Finished successfully.</summary>
    Succeeded = 2,

    /// <summary>Finished with an error.</summary>
    Failed = 3,

    /// <summary>Deliberately not scheduled for now.</summary>
    Paused = 4,
}
