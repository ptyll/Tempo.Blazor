namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

/// <summary>Zoom of the database timeline. One column is a day, a week or a month.</summary>
public enum TimelineZoomLevel
{
    /// <summary>One column per day (44 px).</summary>
    Day,

    /// <summary>One column per week (100 px, seven days).</summary>
    Week,

    /// <summary>One column per month (110 px, thirty days).</summary>
    Month
}

/// <summary>
/// Pure pixel-to-day maths for the database timeline. The pointer helper reports pixels; this
/// converts them, so a drag never needs a render to know how far it moved.
/// </summary>
public static class TimelineGeometry
{
    /// <summary>Pixels per day at <paramref name="zoom"/>.</summary>
    public static double PixelsPerDay(TimelineZoomLevel zoom) => zoom switch
    {
        TimelineZoomLevel.Day => 44.0,
        TimelineZoomLevel.Week => 100.0 / 7.0,
        TimelineZoomLevel.Month => 110.0 / 30.0,
        _ => 44.0
    };

    /// <summary>Column width at <paramref name="zoom"/>.</summary>
    public static double ColumnWidth(TimelineZoomLevel zoom) => zoom switch
    {
        TimelineZoomLevel.Day => 44.0,
        TimelineZoomLevel.Week => 100.0,
        TimelineZoomLevel.Month => 110.0,
        _ => 44.0
    };

    /// <summary>How many whole days <paramref name="pixels"/> cover at <paramref name="zoom"/>.</summary>
    public static int DaysForPixels(double pixels, TimelineZoomLevel zoom)
    {
        var perDay = PixelsPerDay(zoom);
        if (perDay <= 0) return 0;
        return (int)Math.Round(pixels / perDay, MidpointRounding.AwayFromZero);
    }
}
