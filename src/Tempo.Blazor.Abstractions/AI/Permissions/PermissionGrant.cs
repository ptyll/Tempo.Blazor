namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The permission the reader actually granted: a <see cref="PermissionScope"/> plus, for a scope that
/// can run out, both ends of its window.
/// <para>
/// The record is a value: <c>TmPermissionRequest</c> compares the <c>Grant</c> parameter against the one
/// it last saw, so a host that re-renders with the same values cannot resurrect a permission the reader
/// revoked. A timed grant must name its <see cref="ExpiresAt"/> (a "for a while" permission without an
/// end cannot be honoured) and a grant that never expires must not carry one.
/// </para>
/// </summary>
public sealed record PermissionGrant
{
    /// <summary>Creates a grant.</summary>
    /// <param name="scope">How long the permission lasts.</param>
    /// <param name="expiresAt">Moment the permission ends. Required for
    /// <see cref="PermissionScope.ForDuration"/> and optional for
    /// <see cref="PermissionScope.Session"/>; rejected for <see cref="PermissionScope.Once"/> and
    /// <see cref="PermissionScope.Always"/>, which never expire.</param>
    /// <param name="grantedAt">Moment the permission was granted; together with
    /// <paramref name="expiresAt"/> it is the window the granted row reads back to the reader.</param>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is
    /// <see cref="PermissionScope.ForDuration"/> without an <paramref name="expiresAt"/>, the scope never
    /// expires but an <paramref name="expiresAt"/> was supplied, or
    /// <paramref name="expiresAt"/> is not after <paramref name="grantedAt"/>.</exception>
    public PermissionGrant(
        PermissionScope scope,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? grantedAt = null)
    {
        if (scope == PermissionScope.ForDuration && expiresAt is null)
        {
            throw new ArgumentException(
                "A grant for a duration must name the moment it expires.", nameof(expiresAt));
        }

        if (scope is PermissionScope.Once or PermissionScope.Always && expiresAt is not null)
        {
            throw new ArgumentException(
                $"A grant with the {scope} scope never expires, so it must not carry an expiry.",
                nameof(expiresAt));
        }

        if (expiresAt is { } end && grantedAt is { } start && end <= start)
        {
            throw new ArgumentException(
                "A grant must expire after it was given.", nameof(expiresAt));
        }

        Scope = scope;
        ExpiresAt = expiresAt;
        GrantedAt = grantedAt;
    }

    /// <summary>How long the permission lasts.</summary>
    public PermissionScope Scope { get; }

    /// <summary>Moment the permission ends, or null when it does not expire.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>Moment the permission was granted, when the host records it.</summary>
    public DateTimeOffset? GrantedAt { get; }

    /// <summary>
    /// The window the permission was granted for — <see cref="ExpiresAt"/> minus
    /// <see cref="GrantedAt"/> — or null when either end is unknown. This is what makes the granted row
    /// read "Allowed for 1 h · 42 min left" instead of only a countdown.
    /// </summary>
    public TimeSpan? Duration => ExpiresAt is { } end && GrantedAt is { } start ? end - start : null;

    /// <summary>Whether the permission has run out at <paramref name="now"/>.</summary>
    /// <param name="now">The current moment, from the host's <see cref="System.TimeProvider"/>.</param>
    /// <returns>True when <see cref="ExpiresAt"/> has passed; a grant without an expiry never
    /// expires.</returns>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } end && end <= now;

    /// <summary>Time left until the permission ends, never negative.</summary>
    /// <param name="now">The current moment, from the host's <see cref="System.TimeProvider"/>.</param>
    /// <returns>The remaining time, or null when the grant does not expire.</returns>
    public TimeSpan? Remaining(DateTimeOffset now)
        => ExpiresAt is { } end ? (end > now ? end - now : TimeSpan.Zero) : null;
}
