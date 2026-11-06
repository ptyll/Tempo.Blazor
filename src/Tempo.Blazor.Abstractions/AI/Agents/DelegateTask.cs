namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One unit of work waiting to be handed to an agent: what it is, how long it is expected to take, how urgent it is
/// and which agent holds it right now.
/// <para>
/// The record is a VALUE: the host recomposes it with <c>task with { … }</c> when a task is renamed, re-estimated or
/// assigned, and no component mutates the instance it was given. <see cref="Id"/> is the identity the board keys the
/// assignment overlay on and the value every callback reports, so a blank id is REJECTED at construction — an id the
/// model invented would change on every rebuild of the host's snapshot and silently move a task back to the backlog.
/// </para>
/// <para>
/// Nothing is invented. A missing <see cref="EstimatedDuration"/> means "not estimated" (the board prints no
/// duration and its occupied-until preview cannot project past the agent's own load), a zero or negative one is not
/// an estimate either, a missing <see cref="Priority"/> is not <see cref="DelegateTaskPriority.Normal"/>, and a
/// blank or whitespace <see cref="AssignedAgentId"/> is the same as no assignment at all.
/// </para>
/// </summary>
public sealed record DelegateTask
{
    /// <summary>Creates a delegated task.</summary>
    /// <param name="id">
    /// Stable identity in the host's store — the key the board's assignment overlay uses. Required: a blank value
    /// throws <see cref="ArgumentException"/> instead of inventing an id that changes on the next rebuild.
    /// </param>
    /// <param name="title">What the task is, in the host's own words. Blank is allowed and reads as empty.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null, empty or whitespace.</exception>
    public DelegateTask(string id, string title)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            // An id generated here would look stable but is not: the host rebuilds its snapshot from data, so a row
            // would lose its assignment overlay without any error. Failing loudly is the only way that bug cannot be
            // silent (the same rule MemoryItem follows).
            throw new ArgumentException(
                "A delegated task needs a stable, non-blank id: no component invents one, because an id that changes " +
                "between renders silently drops the task's assignment.",
                nameof(id));
        }

        Id = id;
        Title = string.IsNullOrWhiteSpace(title) ? string.Empty : title;
    }

    /// <summary>Stable identity of the task. Never empty: construction rejects a blank value.</summary>
    public string Id { get; init; }

    /// <summary>The task in the host's own words. A whitespace-only title reads as empty.</summary>
    public string Title { get; init; }

    /// <summary>
    /// How long the task is expected to take. Null (the default), <see cref="TimeSpan.Zero"/> and a negative span all
    /// mean "not estimated": the board shows no duration line and never claims an end time it cannot compute.
    /// </summary>
    public TimeSpan? EstimatedDuration { get; init; }

    /// <summary>
    /// How urgent the task is. Null (the default) means the host has no priority for it, which renders no priority
    /// chip at all — an invented <see cref="DelegateTaskPriority.Normal"/> would be a judgement nobody made.
    /// </summary>
    public DelegateTaskPriority? Priority { get; init; }

    private readonly string? _assignedAgentId;

    /// <summary>
    /// The agent that holds the task, or null for a task still in the backlog. A blank value counts as null, so a
    /// host that maps a missing foreign key to <c>""</c> does not create an agent column that does not exist.
    /// </summary>
    public string? AssignedAgentId
    {
        get => string.IsNullOrWhiteSpace(_assignedAgentId) ? null : _assignedAgentId;
        init => _assignedAgentId = value;
    }

    /// <summary>Whether the host gave the task a usable estimate.</summary>
    public bool HasEstimate => EstimatedDuration is { } estimate && estimate > TimeSpan.Zero;

    /// <summary>Whether the host gave the task a priority.</summary>
    public bool HasPriority => Priority is not null;

    /// <summary>Whether <see cref="Priority"/> is one of the declared grades (a cast integer may not be).</summary>
    public bool HasKnownPriority => Priority is >= DelegateTaskPriority.Low and <= DelegateTaskPriority.Urgent;

    /// <summary>Whether an agent holds the task right now.</summary>
    public bool IsAssigned => AssignedAgentId is not null;

    /// <summary>
    /// Whether the task is ready to be handed to somebody. A priority and an estimate are optional, so every task in
    /// the backlog is assignable; the flag exists so a surface can ask the question in the model's own terms.
    /// </summary>
    public bool IsAssignable => !IsAssigned;
}
