using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The auto-assign strategy of a delegate board: which agent takes the next unassigned task.
/// <para>
/// The rule is deliberately boring and completely testable: an agent that still has a free slot is a candidate, and
/// among the candidates the one that becomes FREE FIRST wins — a reader pointing at the board sees the same order the
/// strategy uses, because each column prints when its agent is free. A tie keeps the host's declaration order, so the
/// result never depends on a hash or an unstable sort.
/// </para>
/// <para>
/// The strategy is pure: no clock of its own (the caller passes <c>now</c>), no state, no component. The temporary
/// load of a PLAN is tracked in a local map, so assigning two tasks in one pass fills two different agents instead of
/// piling both on the first one.
/// </para>
/// </summary>
internal static class DelegateAutoAssigner
{
    /// <summary>
    /// The agents that can take work right now, best first: earliest <see cref="DelegateAgent.FreeAt"/> first, and the
    /// host's declaration order for a tie. A full agent is never in the list.
    /// </summary>
    /// <param name="agents">The board's columns, in the host's order.</param>
    /// <param name="now">The instant the availability is judged at, from the component's clock.</param>
    /// <returns>The candidate agents, best first. Empty when every agent is full.</returns>
    public static IReadOnlyList<DelegateAgent> Candidates(IReadOnlyList<DelegateAgent>? agents, DateTimeOffset now)
    {
        if (agents is null || agents.Count == 0)
        {
            return [];
        }

        var candidates = new List<(DelegateAgent Agent, int Order)>();
        for (var index = 0; index < agents.Count; index++)
        {
            var agent = agents[index];
            if (agent is not null && agent.HasCapacity)
            {
                candidates.Add((agent, index));
            }
        }

        if (candidates.Count <= 1)
        {
            return [.. candidates.Select(candidate => candidate.Agent)];
        }

        // OrderBy is a stable sort in .NET, so the declaration order is what a tie keeps.
        return
        [
            .. candidates
                .OrderBy(candidate => candidate.Agent.FreeAt(now))
                .Select(candidate => candidate.Agent)
        ];
    }

    /// <summary>The single best agent for the next task, or null when every agent is full.</summary>
    /// <param name="agents">The board's columns, in the host's order.</param>
    /// <param name="now">The instant the availability is judged at.</param>
    /// <returns>The first free agent by free time, or null.</returns>
    public static DelegateAgent? FirstFree(IReadOnlyList<DelegateAgent>? agents, DateTimeOffset now)
    {
        var candidates = Candidates(agents, now);
        return candidates.Count == 0 ? null : candidates[0];
    }

    /// <summary>
    /// The whole plan of one auto-assign pass: every task, paired with the agent that should take it, in the order the
    /// tasks were given. A task with no room anywhere is simply absent from the plan — the caller reports the
    /// remainder instead of inventing a target.
    /// <para>
    /// Each assignment consumes one of the target's free slots for the rest of the pass, so a plan never hands two
    /// tasks to an agent that has one slot.
    /// </para>
    /// </summary>
    /// <param name="tasks">The tasks to place, best first (the caller orders them by priority).</param>
    /// <param name="agents">The board's columns, in the host's order.</param>
    /// <param name="now">The instant the availability is judged at.</param>
    /// <returns>One item per task that found a target.</returns>
    public static IReadOnlyList<DelegateAutoAssignment> Plan(
        IReadOnlyList<DelegateTask>? tasks,
        IReadOnlyList<DelegateAgent>? agents,
        DateTimeOffset now)
    {
        if (tasks is null || tasks.Count == 0 || agents is null || agents.Count == 0)
        {
            return [];
        }

        // The pass keeps its own load: the host's ActiveCount never changes during one render, so a second task
        // would land on an agent whose single slot the first task just took.
        var taken = new Dictionary<string, int>(StringComparer.Ordinal);
        var plan = new List<DelegateAutoAssignment>();

        foreach (var task in tasks)
        {
            if (task is null)
            {
                continue;
            }

            DelegateAgent? best = null;
            foreach (var candidate in Candidates(agents, now))
            {
                var already = taken.TryGetValue(candidate.Id, out var count) ? count : 0;
                if (already >= candidate.RemainingSlots)
                {
                    continue;
                }

                if (best is null || candidate.FreeAt(now) < best.FreeAt(now))
                {
                    best = candidate;
                }
            }

            if (best is null)
            {
                continue;
            }

            taken[best.Id] = (taken.TryGetValue(best.Id, out var used) ? used : 0) + 1;
            plan.Add(new DelegateAutoAssignment(task, best));
        }

        return plan;
    }

    /// <summary>
    /// The order an auto-assign pass offers the backlog in: the most urgent first, and a task the host gave no
    /// priority keeps its declaration order AFTER the graded ones — an ungraded task is not silently promoted to
    /// <see cref="DelegateTaskPriority.Urgent"/>, and it is not demoted below an explicit
    /// <see cref="DelegateTaskPriority.Low"/> either: it simply has no grade to sort by.
    /// </summary>
    /// <param name="tasks">The backlog, in the host's order.</param>
    /// <returns>A copy in assignment order; the input list is never touched.</returns>
    public static IReadOnlyList<DelegateTask> InAssignmentOrder(IReadOnlyList<DelegateTask>? tasks)
    {
        if (tasks is null || tasks.Count == 0)
        {
            return [];
        }

        var graded = new List<DelegateTask>();
        var ungraded = new List<DelegateTask>();
        foreach (var task in tasks)
        {
            if (task is null)
            {
                continue;
            }

            (task.HasKnownPriority ? graded : ungraded).Add(task);
        }

        // OrderByDescending is stable, so two tasks of the same grade keep the host's order.
        return [.. graded.OrderByDescending(task => task.Priority!.Value), .. ungraded];
    }
}

/// <summary>One task an auto-assign pass would hand to an agent.</summary>
/// <param name="Task">The task being placed.</param>
/// <param name="Agent">The agent that takes it.</param>
internal sealed record DelegateAutoAssignment(DelegateTask Task, DelegateAgent Agent);
