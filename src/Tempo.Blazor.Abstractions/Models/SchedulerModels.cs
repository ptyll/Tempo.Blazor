using Tempo.Blazor.Abstractions.Shared;

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

/// <summary>How an all-day event is painted. Solid is the historical fill; Tinted uses the shared event surface.</summary>
public enum TmAllDayEventStyle
{
    /// <summary>Solid fill of the event colour. The default, and the markup from before this option existed.</summary>
    Solid,

    /// <summary>Tinted surface with a coloured left stripe, the same card as a timed event.</summary>
    Tinted
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

    /// <summary>
    /// Start date and time, timezone-aware. Assigning a <see cref="DateTime"/> still works via the
    /// implicit conversion (the machine-local offset is used); prefer an explicit
    /// <see cref="DateTimeOffset"/> when the event belongs to a specific timezone.
    /// </summary>
    public DateTimeOffset Start { get; set; }

    /// <summary>End date and time, timezone-aware. See <see cref="Start"/> for the DateTime conversion note.</summary>
    public DateTimeOffset End { get; set; }

    /// <summary>Start wall-clock time (offset dropped). Convenience for legacy DateTime consumers.</summary>
    public DateTime StartLocal => Start.DateTime;

    /// <summary>End wall-clock time (offset dropped). Convenience for legacy DateTime consumers.</summary>
    public DateTime EndLocal => End.DateTime;

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

/// <summary>
/// Represents a scheduler-specific resource snapshot. Use <see cref="ToResource"/>
/// and <see cref="FromResource"/> to bridge to the shared <see cref="TmResource"/> model.
/// </summary>
public class TmScheduleResource
{
    /// <summary>Unique identifier.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>CSS color value for the resource.</summary>
    public string? Color { get; set; }

    /// <summary>Optional resource type, for example <c>person</c>, <c>team</c>, <c>room</c>, or <c>equipment</c>.</summary>
    public string? ResourceType { get; set; }

    /// <summary>Optional group identifier for nested grouping.</summary>
    public string? GroupId { get; set; }

    /// <summary>Display order within the group.</summary>
    public int SortOrder { get; set; }

    /// <summary>Optional provider/source discriminator for applications with multiple resource sources.</summary>
    public string? SourceKey { get; set; }

    /// <summary>Optional tenant, workspace, or application scope identifier.</summary>
    public string? TenantId { get; set; }

    /// <summary>Creates a shared resource snapshot from this scheduler resource.</summary>
    public TmResource ToResource()
        => new()
        {
            Id = Id,
            DisplayName = Name,
            ResourceType = ResourceType,
            Color = Color,
            GroupId = GroupId,
            SortOrder = SortOrder,
            SourceKey = SourceKey,
            TenantId = TenantId
        };

    /// <summary>Creates a scheduler resource snapshot from a shared resource model.</summary>
    /// <param name="resource">Resource to copy.</param>
    public static TmScheduleResource FromResource(TmResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return new TmScheduleResource
        {
            Id = resource.Id,
            Name = resource.DisplayName,
            Color = resource.Color,
            ResourceType = resource.ResourceType,
            GroupId = resource.GroupId,
            SortOrder = resource.SortOrder,
            SourceKey = resource.SourceKey,
            TenantId = resource.TenantId
        };
    }
}

/// <summary>Query parameters for loading schedule events.</summary>
public record TmScheduleQuery(DateTime RangeStart, DateTime RangeEnd, string? ResourceId = null);
