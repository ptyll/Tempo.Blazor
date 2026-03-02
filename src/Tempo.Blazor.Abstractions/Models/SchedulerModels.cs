namespace Tempo.Blazor.Models;

/// <summary>Available scheduler view types.</summary>
public enum TmScheduleViewType
{
    /// <summary>Single day with time axis.</summary>
    Day,
    /// <summary>Seven-day view with time axis.</summary>
    Week,
    /// <summary>Monthly calendar grid.</summary>
    Month,
    /// <summary>Chronological event list.</summary>
    Agenda,
    /// <summary>Horizontal timeline with resources.</summary>
    Timeline
}

/// <summary>Represents a scheduled event or appointment.</summary>
public class TmScheduleEvent
{
    /// <summary>Unique identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Event title.</summary>
    public string Title { get; set; } = "";

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Start date and time.</summary>
    public DateTime Start { get; set; }

    /// <summary>End date and time.</summary>
    public DateTime End { get; set; }

    /// <summary>Whether this is an all-day event.</summary>
    public bool AllDay { get; set; }

    /// <summary>CSS color value for the event indicator.</summary>
    public string? Color { get; set; }

    /// <summary>Optional CSS class to apply to the event element.</summary>
    public string? CssClass { get; set; }

    /// <summary>Resource identifier for resource grouping.</summary>
    public string? ResourceId { get; set; }

    /// <summary>RRULE string for recurring events (RFC 5545).</summary>
    public string? RecurrenceRule { get; set; }

    /// <summary>Exception dates excluded from the recurrence pattern.</summary>
    public List<DateTime>? RecurrenceExceptions { get; set; }

    /// <summary>Whether the event is read-only (cannot be dragged/resized).</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>Arbitrary metadata for consumer use.</summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>Represents a schedulable resource (person, room, etc.).</summary>
public class TmScheduleResource
{
    /// <summary>Unique identifier.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>CSS color value for the resource.</summary>
    public string? Color { get; set; }

    /// <summary>Optional group identifier for nested grouping.</summary>
    public string? GroupId { get; set; }

    /// <summary>Display order within the group.</summary>
    public int SortOrder { get; set; }
}

/// <summary>Query parameters for loading schedule events.</summary>
public record TmScheduleQuery(DateTime RangeStart, DateTime RangeEnd, string? ResourceId = null);
