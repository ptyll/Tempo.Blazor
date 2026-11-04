namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One step of an ongoing agent run, as <c>TmAgentMonitor</c> draws it on its timeline: what the agent is
/// doing, how it ended, and when.
/// <para>
/// The monitor keeps the run's TOTALS (elapsed time and cost) on its own parameters, not on the steps: a
/// step that reports its own cost would force every host to keep a running sum in two places, and a step
/// that reports none would make the timeline silently disagree with the header.
/// </para>
/// </summary>
public sealed record AgentMonitorStep
{
    /// <summary>Creates a pending step.</summary>
    /// <param name="title">What the step did, as the reader should see it. Null is stored as an empty
    /// string and a blank title renders the localized "Untitled step" fallback.</param>
    /// <param name="id">Stable identity of the step, used as the timeline item's <c>@key</c>. Null or
    /// whitespace generates one, so two identical steps are still two items.</param>
    public AgentMonitorStep(string title, string? id = null)
    {
        Title = title ?? string.Empty;
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
    }

    /// <summary>Stable identity of the step. Never empty: a missing id is generated at construction.</summary>
    public string Id { get; init; }

    /// <summary>What the step did, as the reader should see it.</summary>
    public string Title { get; init; }

    /// <summary>What kind of step this is; the timeline draws its icon (or the host's
    /// <c>IconResolver</c> result) and the localized kind as a visually hidden word. Default
    /// <see cref="AgentStepKind.Thinking"/>.</summary>
    public AgentStepKind Kind { get; init; } = AgentStepKind.Thinking;

    /// <summary>Current state of the step. Default <see cref="AgentStepStatus.Pending"/>.</summary>
    public AgentStepStatus Status { get; init; } = AgentStepStatus.Pending;

    /// <summary>A short line under the title (the tool name, the recipient, the decision). Null renders no
    /// detail line.</summary>
    public string? Detail { get; init; }

    /// <summary>When the step started, from the host's clock. Null means the monitor cannot time it.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When the step finished. Null on a step that is still running (or never started).</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Whether the step reached a terminal state (<see cref="AgentStepStatus.Done"/>,
    /// <see cref="AgentStepStatus.Failed"/> or <see cref="AgentStepStatus.Skipped"/>).</summary>
    public bool IsFinished =>
        Status is AgentStepStatus.Done or AgentStepStatus.Failed or AgentStepStatus.Skipped;

    /// <summary>
    /// How long the step took, or null while one end of its window is missing. A running step therefore
    /// reports null here — use <see cref="Elapsed"/> for that.
    /// </summary>
    public TimeSpan? Duration
        => StartedAt is { } start && CompletedAt is { } end && end >= start ? end - start : null;

    /// <summary>
    /// How much time the step has consumed at <paramref name="now"/>: its recorded
    /// <see cref="Duration"/> once it finished, otherwise the time since <see cref="StartedAt"/> — never
    /// negative.
    /// </summary>
    /// <param name="now">The current moment, from the caller's <see cref="System.TimeProvider"/>.</param>
    /// <returns>The elapsed time, or null when the step never started.</returns>
    public TimeSpan? Elapsed(DateTimeOffset now)
    {
        if (Duration is { } recorded)
        {
            return recorded;
        }

        if (StartedAt is not { } start)
        {
            return null;
        }

        return now > start ? now - start : TimeSpan.Zero;
    }
}
