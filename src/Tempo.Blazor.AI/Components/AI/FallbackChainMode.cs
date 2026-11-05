namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How <c>TmFallbackChain</c> is presented. <see cref="View"/> reports the run the host handed over;
/// <see cref="Edit"/> lets a reader change the chain — its order, its members and the resolution policy — and reports
/// every change through the binding, never by mutating the host's list.
/// </summary>
public enum FallbackChainMode
{
    /// <summary>The run view (default): the attempts in the order they were tried, ending in the Answer node.</summary>
    View = 0,

    /// <summary>The editor: the models in fallback order through a sortable list, plus add, remove and the policy.</summary>
    Edit = 1,
}
