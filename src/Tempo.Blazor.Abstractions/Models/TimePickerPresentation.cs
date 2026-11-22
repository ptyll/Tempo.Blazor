namespace Tempo.Blazor.Models;

/// <summary>
/// How <c>TmTimePicker</c> presents the time-of-day value. Additive; the default
/// <see cref="Segments"/> keeps the pre-2.13 DOM.
/// </summary>
public enum TimePickerPresentation
{
    /// <summary>
    /// Today's <c>TmTimeInput</c> segments with no overlay. The default, so existing call sites
    /// keep their DOM.
    /// </summary>
    Segments = 0,

    /// <summary>
    /// Segment readout plus a clock button that opens <c>TmTimeSelector</c> in a
    /// <c>TmOverlayPanel</c> (popover or bottom sheet).
    /// </summary>
    Popup = 1,

    /// <summary>
    /// On a mobile viewport, a trigger button plus a sheet; otherwise <see cref="Popup"/>.
    /// Not the default — it would change the DOM of every existing phone call site.
    /// </summary>
    Auto = 2,
}
