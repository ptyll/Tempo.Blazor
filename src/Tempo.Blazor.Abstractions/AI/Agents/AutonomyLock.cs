namespace Tempo.Blazor.AI.Models;

/// <summary>
/// A limit on one action that the surface cannot lift, enforced by somebody other than the reader: a tenant policy, an
/// administrator, the host application itself.
/// <para>
/// A lock always names the action it pins and, when the enforcing party is known, WHO enforces it and WHY. The autonomy
/// dial is not allowed to change a lock — it lists it next to the value so the reader knows why a grade is capped; a
/// lock without a source would be an unexplained disabled control, and one without a reason is printed as the bare fact
/// it is.
/// </para>
/// </summary>
public sealed record AutonomyLock
{
    /// <summary>Creates a lock over one action.</summary>
    /// <param name="actionLabel">The action the limit applies to, in the host's own words ("Send external email").</param>
    public AutonomyLock(string actionLabel)
    {
        ActionLabel = actionLabel ?? string.Empty;
        Source = string.Empty;
    }

    /// <summary>
    /// The action the limit applies to, in the host's words ("Send external email", "Delete a file"). It is a LABEL, not
    /// an id: the surface prints it and never parses it.
    /// </summary>
    public string ActionLabel { get; init; }

    /// <summary>
    /// Who enforces the limit ("Tenant policy", "Compliance profile", "Workspace administrator"). Empty renders no
    /// "enforced by" line instead of a line with a blank source.
    /// </summary>
    public string Source { get; init; }

    /// <summary>
    /// Why the limit exists, in the host's own words. Null or empty renders no reason line.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>Whether the enforcing party is known.</summary>
    public bool HasSource => !string.IsNullOrWhiteSpace(Source);

    /// <summary>Whether the lock carries a reason worth printing.</summary>
    public bool HasReason => !string.IsNullOrWhiteSpace(Reason);
}
