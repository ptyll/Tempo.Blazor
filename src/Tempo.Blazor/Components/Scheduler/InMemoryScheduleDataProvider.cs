using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// Client-side <see cref="IScheduleDataProvider"/> that filters an in-memory event list
/// by date range and optionally by resource.
/// </summary>
public sealed class InMemoryScheduleDataProvider : IScheduleDataProvider
{
    private readonly IReadOnlyList<TmScheduleEvent> _events;

    /// <param name="events">The full in-memory event list.</param>
    public InMemoryScheduleDataProvider(IReadOnlyList<TmScheduleEvent> events)
    {
        _events = events;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TmScheduleEvent>> GetEventsAsync(
        TmScheduleQuery query, CancellationToken ct = default)
    {
        var all = new List<TmScheduleEvent>();

        foreach (var evt in _events)
        {
            if (!string.IsNullOrWhiteSpace(evt.RecurrenceRule))
            {
                // Expand recurring events into individual occurrences
                var expanded = RecurrenceEngine.ExpandRecurrence(evt, query.RangeStart, query.RangeEnd);
                all.AddRange(expanded);
            }
            else if (evt.Start < query.RangeEnd && evt.End > query.RangeStart)
            {
                all.Add(evt);
            }
        }

        IEnumerable<TmScheduleEvent> result = all;

        if (query.ResourceId is not null)
        {
            result = result.Where(e =>
                string.Equals(e.ResourceId, query.ResourceId, StringComparison.Ordinal));
        }

        return Task.FromResult<IReadOnlyList<TmScheduleEvent>>(result.ToList());
    }
}
