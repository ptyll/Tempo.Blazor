namespace Tempo.Blazor.Models;

/// <summary>Context data for scheduler slot context menu events.</summary>
public class SchedulerSlotContext
{
    /// <summary>Start of the slot/date range.</summary>
    public DateTime Start { get; set; }

    /// <summary>End of the slot/date range.</summary>
    public DateTime End { get; set; }

    /// <summary>Mouse X coordinate for menu positioning.</summary>
    public double ClientX { get; set; }

    /// <summary>Mouse Y coordinate for menu positioning.</summary>
    public double ClientY { get; set; }

    /// <summary>Resource ID if clicked in resource-grouped view.</summary>
    public string? ResourceId { get; set; }
}

/// <summary>Context data for scheduler event context menu events.</summary>
public class SchedulerEventContext
{
    /// <summary>The event that was clicked.</summary>
    public TmScheduleEvent Event { get; set; } = default!;

    /// <summary>Mouse X coordinate for menu positioning.</summary>
    public double ClientX { get; set; }

    /// <summary>Mouse Y coordinate for menu positioning.</summary>
    public double ClientY { get; set; }
}
