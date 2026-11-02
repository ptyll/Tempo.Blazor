namespace Tempo.Blazor.AI.Models;

/// <summary>
/// Lifecycle state of a permission request rendered by <c>TmPermissionRequest</c>.
/// <para>
/// The component derives the state from its <c>Grant</c> parameter and from its own decisions; the
/// host reads it through the <c>State</c> property and, when it needs to persist every transition,
/// through <c>GrantChanged</c>. <see cref="Pending"/> is the only state in which the request offers
/// Allow and Deny; every other state collapses the request into a single result row.
/// </para>
/// </summary>
public enum PermissionRequestState
{
    /// <summary>Waiting for the reader; the request shows its scopes and its actions.</summary>
    Pending,

    /// <summary>A permission is in force; the row shows its scope, the remaining time and Revoke.</summary>
    Granted,

    /// <summary>The reader denied the request.</summary>
    Denied,

    /// <summary>The granted permission ran out; there is nothing left to revoke.</summary>
    Expired,
}
