namespace Tempo.Blazor.AI.Models;

/// <summary>
/// Execution state of one <see cref="AgentPlanStep"/>. The state decides what <c>TmAgentPlan</c> paints
/// (an icon plus the state in words) and which actions a step offers: a <see cref="Failed"/> step offers
/// "Retry from this step", a <see cref="Running"/> one counts its live elapsed time, and a
/// <see cref="Skipped"/> one is a terminal state that is neither a success nor a failure.
/// </summary>
public enum AgentStepStatus
{
    /// <summary>Queued but not started.</summary>
    Pending = 0,

    /// <summary>Executing right now; the plan counts its elapsed time from
    /// <see cref="AgentPlanStep.StartedAt"/> until <see cref="AgentPlanStep.CompletedAt"/> is set.</summary>
    Running = 1,

    /// <summary>Finished successfully.</summary>
    Done = 2,

    /// <summary>Finished with an error; <see cref="AgentPlanStep.Error"/> carries the reason.</summary>
    Failed = 3,

    /// <summary>Deliberately skipped — a terminal state that neither succeeded nor failed.</summary>
    Skipped = 4,
}
