using System.Globalization;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How a delegate board words its data: the priority and duration of a task, the capacity and availability of an
/// agent, and the projected end time the occupied-until preview needs.
/// <para>
/// One place for the decisions that must not drift between the column header, the row and the live announcement — and
/// pure, so the projection can be tested without a renderer or a clock of its own.
/// </para>
/// </summary>
internal static class DelegatePresentation
{
    /// <summary>The localized word for a priority grade; an unknown value names itself as unranked.</summary>
    /// <param name="loc">The surface's localizer.</param>
    /// <param name="priority">The grade to name.</param>
    /// <returns>The localized word.</returns>
    public static string PriorityLabel(ITmLocalizer loc, DelegateTaskPriority? priority)
        => priority switch
        {
            DelegateTaskPriority.Low => loc["TmDelegateBoard_PriorityLow"],
            DelegateTaskPriority.Normal => loc["TmDelegateBoard_PriorityNormal"],
            DelegateTaskPriority.High => loc["TmDelegateBoard_PriorityHigh"],
            DelegateTaskPriority.Urgent => loc["TmDelegateBoard_PriorityUrgent"],
            _ => loc["TmDelegateBoard_PriorityUnknown"],
        };

    /// <summary>
    /// The stable slug of a priority grade, used as the <c>data-priority</c> hook so a host (or a test) never has to
    /// match a localized word. An unknown value is <c>unknown</c> and a missing one <c>none</c>.
    /// </summary>
    /// <param name="priority">The grade to name.</param>
    /// <returns>The slug.</returns>
    public static string PrioritySlug(DelegateTaskPriority? priority)
        => priority switch
        {
            DelegateTaskPriority.Low => "low",
            DelegateTaskPriority.Normal => "normal",
            DelegateTaskPriority.High => "high",
            DelegateTaskPriority.Urgent => "urgent",
            null => "none",
            _ => "unknown",
        };

    /// <summary>
    /// A duration as a reader counts it: minutes under an hour, whole hours for an exact hour, hours and minutes
    /// otherwise. Zero and negative durations are "no estimate" and render nothing.
    /// </summary>
    /// <param name="loc">The surface's localizer.</param>
    /// <param name="duration">The duration to word.</param>
    /// <returns>The localized duration, or an empty string when there is nothing to measure.</returns>
    public static string DurationText(ITmLocalizer loc, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return string.Empty;
        }

        var minutes = (int)Math.Round(duration.TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes < 1)
        {
            minutes = 1;
        }

        var hours = minutes / 60;
        var remainder = minutes % 60;

        if (hours == 0)
        {
            return loc["TmDelegateBoard_Minutes", minutes.ToString(CultureInfo.CurrentCulture)];
        }

        return remainder == 0
            ? loc["TmDelegateBoard_Hours", hours.ToString(CultureInfo.CurrentCulture)]
            : loc["TmDelegateBoard_HoursMinutes",
                hours.ToString(CultureInfo.CurrentCulture),
                remainder.ToString(CultureInfo.CurrentCulture)];
    }

    /// <summary>The task's duration line, or an empty string when the host gave no estimate.</summary>
    /// <param name="loc">The surface's localizer.</param>
    /// <param name="task">The task to describe.</param>
    /// <returns>The localized sentence, or an empty string.</returns>
    public static string EstimateText(ITmLocalizer loc, DelegateTask task)
    {
        var duration = task.HasEstimate ? DurationText(loc, task.EstimatedDuration!.Value) : string.Empty;
        return duration.Length == 0 ? string.Empty : loc["TmDelegateBoard_Estimate", duration];
    }

    /// <summary>A moment as a reader reads a clock, in the supplied culture's short time pattern.</summary>
    /// <param name="value">The moment to format.</param>
    /// <param name="culture">The culture to format in.</param>
    /// <returns>The formatted time.</returns>
    public static string TimeText(DateTimeOffset value, CultureInfo culture)
        => value.ToString("t", culture);

    /// <summary>The agent's load as "used of capacity": the HOST's numbers, the same pair capacity is judged by.</summary>
    /// <param name="loc">The surface's localizer.</param>
    /// <param name="agent">The agent to describe.</param>
    /// <returns>The localized load text.</returns>
    public static string CapacityText(ITmLocalizer loc, DelegateAgent agent)
        => loc["TmDelegateBoard_Capacity",
            agent.Load.ToString(CultureInfo.CurrentCulture),
            agent.EffectiveMaxConcurrency.ToString(CultureInfo.CurrentCulture)];

    /// <summary>
    /// When an agent would be free again if it took the supplied task: its own free time plus the task's estimate.
    /// <para>
    /// The projection is HONEST about what it knows. A task without an estimate adds nothing (so an already busy agent
    /// still projects its own free time, and a free one projects nothing at all), and the caller renders "Free now"
    /// instead of a made-up end time.
    /// </para>
    /// </summary>
    /// <param name="agent">The agent that would take the task.</param>
    /// <param name="task">The task being considered, or null when there is none.</param>
    /// <param name="now">The instant the projection starts from, from the component's clock.</param>
    /// <returns>The projected free instant and what the projection is based on.</returns>
    public static DelegateOccupancyProjection Project(DelegateAgent agent, DelegateTask? task, DateTimeOffset now)
    {
        var from = agent.FreeAt(now);
        var estimate = task is { HasEstimate: true } ? task.EstimatedDuration!.Value : TimeSpan.Zero;
        var until = from + estimate;
        var usesEstimate = estimate > TimeSpan.Zero;
        return new DelegateOccupancyProjection(until, agent.IsBusyAt(now) || usesEstimate, usesEstimate);
    }
}

/// <summary>
/// What the occupied-until preview can claim: the instant an agent would be free, whether that instant is known at all
/// and whether the task's own estimate went into it.
/// </summary>
/// <param name="Until">The projected free instant.</param>
/// <param name="IsKnown">Whether there is anything to say (the agent is busy now, or the task carries an estimate).</param>
/// <param name="UsesEstimate">Whether the task's estimate was added to the agent's own free time.</param>
internal sealed record DelegateOccupancyProjection(DateTimeOffset Until, bool IsKnown, bool UsesEstimate);
