namespace Tempo.Blazor.AI.Models;

/// <summary>
/// How long a permission the reader granted lasts.
/// <para>
/// The scope is the answer to "may the agent do this, and for how long" — the one thing a permission
/// request asks besides the tool itself. <c>TmPermissionRequest</c> offers the scopes the host lists in
/// its <c>ScopeOptions</c>, so an app that never wants to hand out a permanent permission simply does
/// not offer <see cref="Always"/>.
/// </para>
/// </summary>
public enum PermissionScope
{
    /// <summary>Allow this one invocation; the permission is consumed by the call it was asked for.</summary>
    Once,

    /// <summary>Allow it for a chosen span of time (the component asks for the span when the reader
    /// picks this scope).</summary>
    ForDuration,

    /// <summary>Allow it until the current session ends.</summary>
    Session,

    /// <summary>Allow it from now on — the agent never asks again for this tool and target.</summary>
    Always,
}
