namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One answer to a permission request: the reader either granted a <see cref="PermissionGrant"/> or
/// denied it.
/// <para>
/// <c>TmPermissionRequest</c> hands this to its <c>OnDecision</c> callback and only leaves its pending
/// state when the callback completes without throwing, so a failed save never looks like a decision.
/// A grant always carries the grant it produced — the scope and the expiry the host has to persist —
/// and a denial never does, because there is nothing to remember about a permission that was refused.
/// </para>
/// </summary>
public sealed record PermissionDecision
{
    /// <summary>Creates a decision.</summary>
    /// <param name="isGranted">True for a grant, false for a denial.</param>
    /// <param name="grant">The granted permission; required when
    /// <paramref name="isGranted"/> is true and rejected when it is false.</param>
    /// <param name="decidedAt">Moment of the decision, when the host knows it.</param>
    /// <exception cref="ArgumentException">A grant without a <paramref name="grant"/>, or a denial
    /// that carries one.</exception>
    public PermissionDecision(
        bool isGranted,
        PermissionGrant? grant = null,
        DateTimeOffset? decidedAt = null)
    {
        if (isGranted && grant is null)
        {
            throw new ArgumentException(
                "A granted decision must carry the grant it produced.", nameof(grant));
        }

        if (!isGranted && grant is not null)
        {
            throw new ArgumentException(
                "A denied decision grants nothing, so it must not carry a grant.", nameof(grant));
        }

        IsGranted = isGranted;
        Grant = grant;
        DecidedAt = decidedAt;
    }

    /// <summary>Whether the reader granted the permission.</summary>
    public bool IsGranted { get; }

    /// <summary>Whether the reader denied the permission.</summary>
    public bool IsDenied => !IsGranted;

    /// <summary>The granted permission, or null for a denial.</summary>
    public PermissionGrant? Grant { get; }

    /// <summary>Moment of the decision, when the host knows it.</summary>
    public DateTimeOffset? DecidedAt { get; }

    /// <summary>
    /// The scope of a granted decision, taken from its grant. A denial reports
    /// <see cref="PermissionScope.Once"/> because it grants nothing at all — read
    /// <see cref="IsDenied"/> to tell the two apart.
    /// </summary>
    public PermissionScope Scope => Grant?.Scope ?? PermissionScope.Once;

    /// <summary>Creates a decision that grants <paramref name="grant"/>.</summary>
    /// <param name="grant">The permission the reader granted.</param>
    /// <param name="decidedAt">Moment of the decision.</param>
    /// <returns>A granted decision.</returns>
    /// <exception cref="ArgumentException"><paramref name="grant"/> is null.</exception>
    public static PermissionDecision Allow(PermissionGrant grant, DateTimeOffset? decidedAt = null)
        => new(true, grant, decidedAt);

    /// <summary>Creates a denial.</summary>
    /// <param name="decidedAt">Moment of the decision.</param>
    /// <returns>A denied decision.</returns>
    public static PermissionDecision Deny(DateTimeOffset? decidedAt = null)
        => new(false, null, decidedAt);
}
