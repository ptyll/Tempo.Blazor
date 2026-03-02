using Tempo.Blazor.Models;

namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Provides schedule events for a given date range.
/// Implement this interface for server-side data loading.
/// </summary>
public interface IScheduleDataProvider
{
    /// <summary>Fetches events that overlap with the specified date range.</summary>
    Task<IReadOnlyList<TmScheduleEvent>> GetEventsAsync(
        TmScheduleQuery query, CancellationToken ct = default);
}
