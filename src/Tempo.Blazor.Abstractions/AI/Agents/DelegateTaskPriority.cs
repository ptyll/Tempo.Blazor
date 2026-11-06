namespace Tempo.Blazor.AI.Models;

/// <summary>
/// How urgent a delegated task is, as the board paints and orders it. The words are the surface's own (the model
/// carries no text), so the same value reads "High" in English and "Vysoká" in Czech.
/// <para>
/// A host that has no priority scale for its tasks leaves
/// <see cref="DelegateTask.Priority"/> <c>null</c>: an INVENTED <see cref="Normal"/> would claim a judgement nobody
/// made, and the board would then sort by a value the host never stored.
/// </para>
/// </summary>
public enum DelegateTaskPriority
{
    /// <summary>Nice to have: the task may wait for every other one.</summary>
    Low = 0,

    /// <summary>The ordinary case.</summary>
    Normal = 1,

    /// <summary>Should go before the ordinary work.</summary>
    High = 2,

    /// <summary>Blocks somebody: takes the first free slot there is.</summary>
    Urgent = 3,
}
