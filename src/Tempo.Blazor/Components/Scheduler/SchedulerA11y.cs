using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The keyboard and naming decisions the scheduler views share, so each view does not grow its own.
/// </summary>
internal static class SchedulerA11y
{
    /// <summary>Enter activates on keydown, once: a held key and a modified chord do not.</summary>
    public static bool IsEnterActivation(KeyboardEventArgs e)
        => e.Key == "Enter" && !e.Repeat && !HasModifier(e);

    /// <summary>Space activates on keyup, the half a native button uses, and a modified chord does not.</summary>
    public static bool IsSpaceActivation(KeyboardEventArgs e)
        => e.Key == " " && !HasModifier(e);

    /// <summary>Alt, Ctrl, Meta and Shift belong to the browser; a roving handler must ignore them.</summary>
    public static bool HasModifier(KeyboardEventArgs e) => e.AltKey || e.CtrlKey || e.MetaKey || e.ShiftKey;

    /// <summary>
    /// The accessible name of an event: words, never a dash, in the culture's own time format.
    /// An event that spans days names both dates.
    /// </summary>
    public static string EventLabel(ITmLocalizer loc, TmScheduleEvent e, CultureInfo culture)
    {
        if (e.AllDay)
            return string.Format(loc["TmScheduler_AllDayEventAriaLabel"], e.Title);

        var start = e.StartLocal;
        var end = e.EndLocal;
        if (start.Date != end.Date)
        {
            return string.Format(loc["TmScheduler_MultiDayEventAriaLabel"], e.Title,
                start.ToString("f", culture), end.ToString("f", culture));
        }

        return string.Format(loc["TmScheduler_EventAriaLabel"], e.Title,
            start.ToString("t", culture), end.ToString("t", culture));
    }

    /// <summary>
    /// A day cell's name: the long date plus the plural event count. Zero reads the plain key,
    /// because no shipped plural category means "none".
    /// </summary>
    public static string DayCellLabel(ITmLocalizer loc, DateOnly day, int count, CultureInfo culture)
    {
        var date = day.ToString("D", culture);
        return count == 0
            ? string.Format(loc["TmScheduler_DayCellAria"], date)
            : loc.Plural("TmScheduler_DayCellAria", count, date);
    }

    /// <summary>A slot's name: the culture's time, worded, with the end so the length is audible.</summary>
    public static string SlotLabel(ITmLocalizer loc, DateTime start, DateTime end, CultureInfo culture)
        => string.Format(loc["TmScheduler_SlotAriaLabel"],
            start.ToString("t", culture), end.ToString("t", culture), start.ToString("D", culture));

    /// <summary>
    /// A pointer context menu carries its own point. A keyboard one reports 0,0, so the element's box
    /// is the point the menu can open from. A missing script keeps the reported point.
    /// </summary>
    public static async Task<(double X, double Y)> MenuPointAsync(IJSRuntime js, ElementReference element, MouseEventArgs e)
    {
        if (e.ClientX > 0 || e.ClientY > 0) return (e.ClientX, e.ClientY);
        try
        {
            var box = await js.InvokeAsync<double[]>("TempoBlazor.Scheduler.elementPoint", element);
            return (box[0], box[1]);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException or ObjectDisposedException)
        {
            return (e.ClientX, e.ClientY);
        }
    }

    /// <summary>
    /// The keyboard point of the slot the key was pressed in. A captured reference goes stale when the
    /// roving stop moves, so the slot is found by its start (and resource) inside the grid.
    /// </summary>
    public static async Task<(double X, double Y)> SlotMenuPointAsync(
        IJSRuntime js, ElementReference root, DateTime slotStart, string? resourceId, MouseEventArgs e)
    {
        if (e.ClientX > 0 || e.ClientY > 0) return (e.ClientX, e.ClientY);
        try
        {
            var start = slotStart.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
            var box = await js.InvokeAsync<double[]?>("TempoBlazor.Scheduler.slotPoint", root, start, resourceId);
            return box is { Length: >= 2 } ? (box[0], box[1]) : (e.ClientX, e.ClientY);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException or ObjectDisposedException)
        {
            return (e.ClientX, e.ClientY);
        }
    }
}

/// <summary>Pure grid movement for a roving tabindex. Returns -1 for a key that is not a move.</summary>
internal static class RovingGrid
{
    /// <summary>
    /// The index an arrow, Home or End lands on inside a grid of <paramref name="count"/> cells laid
    /// out in rows of <paramref name="columns"/>. A key that is not a move returns -1, and a move
    /// that would leave the grid stays put.
    /// </summary>
    public static int Move(int index, string key, int columns, int count)
    {
        if (columns < 1 || count < 1) return -1;
        var target = key switch
        {
            "ArrowRight" => index + 1,
            "ArrowLeft" => index - 1,
            "ArrowDown" => index + columns,
            "ArrowUp" => index - columns,
            "Home" => index - index % columns,
            "End" => index - index % columns + columns - 1,
            _ => -1
        };
        if (target < 0) return key is "ArrowRight" or "ArrowLeft" or "ArrowDown" or "ArrowUp" or "Home" or "End"
            ? index : -1;
        return target >= count ? index : target;
    }
}
