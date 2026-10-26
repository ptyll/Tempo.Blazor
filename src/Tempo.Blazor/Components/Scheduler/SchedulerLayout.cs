using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The lane assignment both the time grid and the timeline use, so the two views cannot drift apart.
/// A lane is reused the moment an event ends, and an event that ends when the next one starts does
/// not overlap it.
/// </summary>
internal static class SchedulerLayout
{
    /// <summary>An event placed in one lane of a row that holds <see cref="TotalLanes"/> lanes.</summary>
    internal readonly record struct PositionedEvent(TmScheduleEvent Event, int Lane, int TotalLanes);

    /// <summary>
    /// Assigns every event a lane. Events are taken in start order, then end order, and the first
    /// lane whose previous event has already ended is reused. The lane count is the widest point of
    /// the whole set, so a later event shares the width of the busiest overlap.
    /// </summary>
    public static IReadOnlyList<PositionedEvent> Assign(IReadOnlyList<TmScheduleEvent> events)
    {
        if (events.Count == 0) return [];

        var sorted = events.OrderBy(e => e.StartLocal).ThenBy(e => e.EndLocal).ToList();
        var ends = new List<DateTime>();
        var placed = new List<(TmScheduleEvent Event, int Lane)>(sorted.Count);

        foreach (var evt in sorted)
        {
            var lane = ends.FindIndex(end => evt.StartLocal >= end);
            if (lane < 0)
            {
                lane = ends.Count;
                ends.Add(evt.EndLocal);
            }
            else
            {
                ends[lane] = evt.EndLocal;
            }

            placed.Add((evt, lane));
        }

        // Each overlap cluster keeps its own width. A lone event after a busy morning is one lane,
        // not a slice of the morning's widest point.
        var result = new PositionedEvent[placed.Count];
        var cluster = new List<int>();
        for (var i = 0; i <= placed.Count; i++)
        {
            var startsNewCluster = i == placed.Count
                || (cluster.Count > 0 && placed[i].Event.StartLocal >= cluster.Max(index => placed[index].Event.EndLocal));
            if (startsNewCluster && cluster.Count > 0)
            {
                var total = cluster.Max(index => placed[index].Lane) + 1;
                foreach (var index in cluster)
                    result[index] = new PositionedEvent(placed[index].Event, placed[index].Lane, total);
                cluster.Clear();
            }

            if (i < placed.Count) cluster.Add(i);
        }

        return result;
    }
}
