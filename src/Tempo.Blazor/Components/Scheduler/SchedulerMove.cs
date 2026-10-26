using System.Globalization;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The one commit path a scheduler event moves or resizes through. HTML5 drop, the touch
/// <c>NotifyMoveComplete</c> and the Alt+Arrow keyboard alternative all call it, so the read-only
/// guard, the range clamp and the display-zone offset live in exactly one place.
/// </summary>
internal static class SchedulerMove
{
    /// <summary>
    /// Moves <paramref name="evt"/> onto <paramref name="date"/> at <paramref name="minuteOfDay"/>,
    /// keeping its duration. <paramref name="minuteOfDay"/> of -1 keeps the time of day (a month has
    /// no minute axis). A null resource leaves the resource alone; the timeline's synthetic
    /// <c>_default</c> row is the same as no resource.
    /// </summary>
    public static bool Move(
        TmScheduleEvent evt, string date, int minuteOfDay, string? resourceId, bool readOnly,
        TimeZoneInfo? timeZone = null, int rangeStart = 0, int rangeMinutes = 24 * 60)
    {
        if (readOnly || evt.IsReadOnly) return false;
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return false;

        var duration = evt.End - evt.Start;
        var minute = minuteOfDay < 0
            ? (int)evt.StartLocal.TimeOfDay.TotalMinutes
            : ClampMinute(minuteOfDay, duration, rangeStart, rangeMinutes);
        var time = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minute));
        var local = day.ToDateTime(time);
        var zone = timeZone ?? TimeZoneInfo.Local;
        evt.Start = new DateTimeOffset(local, zone.GetUtcOffset(local));
        evt.End = evt.Start + duration;
        if (resourceId is not null && resourceId != "_default")
            evt.ResourceId = resourceId;
        return true;
    }

    /// <summary>
    /// One keyboard step. A plain chord moves by one slot (vertical) or one day (horizontal); a
    /// shifted chord resizes the END by one slot. A month has no slots, so a shifted chord does
    /// nothing and a vertical step is a week.
    /// </summary>
    public static bool Step(
        TmScheduleEvent evt, int x, int y, bool shift, int slotMinutes, bool readOnly, bool hasMinuteAxis,
        TimeZoneInfo? timeZone = null, int rangeStart = 0, int rangeMinutes = 24 * 60, bool horizontalResize = false)
    {
        if (readOnly || evt.IsReadOnly) return false;
        var slot = Math.Max(1, slotMinutes);

        if (shift)
        {
            // Time grid resizes with Up/Down (the bottom edge); timeline resizes with Left/Right
            // (the right edge). The other axis is not a resize.
            var steps = horizontalResize ? x : y;
            if (!hasMinuteAxis || steps == 0 || (horizontalResize ? y : x) != 0) return false;
            var endLocal = evt.EndLocal.AddMinutes(steps * slot);
            if (endLocal < evt.StartLocal.AddMinutes(slot))
                endLocal = evt.StartLocal.AddMinutes(slot);
            var zone = timeZone ?? TimeZoneInfo.Local;
            evt.End = new DateTimeOffset(endLocal, zone.GetUtcOffset(endLocal));
            return true;
        }

        var duration = evt.End - evt.Start;
        var start = evt.StartLocal.AddDays(hasMinuteAxis ? x : x + y * 7);
        if (!hasMinuteAxis && x == 0 && y == 0) return false;
        if (hasMinuteAxis)
        {
            var minute = (int)start.TimeOfDay.TotalMinutes + y * slot;
            var clamped = ClampMinute(minute, duration, rangeStart, rangeMinutes);
            if (clamped == (int)evt.StartLocal.TimeOfDay.TotalMinutes && start.Date == evt.StartLocal.Date)
                return false;
            start = start.Date.AddMinutes(clamped);
        }

        var display = timeZone ?? TimeZoneInfo.Local;
        evt.Start = new DateTimeOffset(start, display.GetUtcOffset(start));
        evt.End = evt.Start + duration;
        return true;
    }

    /// <summary>
    /// The start minute that keeps the whole event inside
    /// <c>[rangeStart, rangeStart + rangeMinutes]</c>, snapped to a minute and never past midnight.
    /// </summary>
    public static int ClampMinute(int minute, TimeSpan duration, int rangeStart, int rangeMinutes)
    {
        var length = (int)Math.Round(duration.TotalMinutes);
        if (length < 1) length = 1;
        // The event keeps its length, so the last legal start is the window end minus that length.
        // A full day ends at midnight; a clipped day ends at the visible range.
        var window = rangeMinutes < 1 ? 1 : rangeMinutes;
        var windowEnd = rangeStart + window;
        if (windowEnd > 24 * 60) windowEnd = 24 * 60;
        var latest = windowEnd - length;
        if (latest < rangeStart) latest = rangeStart;
        var result = minute;
        if (result < rangeStart) result = rangeStart;
        if (result > latest) result = latest;
        return result;
    }

    /// <summary>The announcement for a move: the title and the time, in the current UI culture.</summary>
    public static string MovedText(ITmLocalizer loc, TmScheduleEvent evt, string? resourceName = null, bool dateOnly = false)
    {
        var when = evt.AllDay || dateOnly
            ? evt.StartLocal.ToString("D", CultureInfo.CurrentUICulture)
            : evt.StartLocal.ToString("g", CultureInfo.CurrentUICulture);
        return resourceName is null
            ? string.Format(CultureInfo.CurrentUICulture, loc["TmScheduler_EventMoved"], evt.Title, when)
            : string.Format(CultureInfo.CurrentUICulture, loc["TmScheduler_EventMovedToResource"], evt.Title, resourceName, when);
    }

    /// <summary>The announcement for a resize: the new start and end, in the current UI culture.</summary>
    public static string ResizedText(ITmLocalizer loc, TmScheduleEvent evt)
        => string.Format(
            CultureInfo.CurrentUICulture,
            loc["TmScheduler_EventResized"],
            evt.StartLocal.ToString("t", CultureInfo.CurrentUICulture),
            evt.EndLocal.ToString("t", CultureInfo.CurrentUICulture));
}
