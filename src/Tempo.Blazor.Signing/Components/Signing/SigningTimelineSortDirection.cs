namespace Tempo.Blazor.Components.Signing;

/// <summary>
/// Sort direction of <see cref="TmSubmissionStatusTimeline.Events"/>. The default
/// <see cref="Descending"/> keeps the historical newest-first order (the latest event is the one a
/// reviewer looks for first); <see cref="Ascending"/> renders the oldest event first, i.e. the
/// reading order of the ceremony. (Q-S9: the default documents today's behaviour.)
/// </summary>
public enum SigningTimelineSortDirection
{
    /// <summary>Newest event first (the default — today's behaviour).</summary>
    Descending = 0,

    /// <summary>Oldest event first.</summary>
    Ascending = 1
}
