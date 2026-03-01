namespace Tempo.Blazor.Components.Pickers;

/// <summary>An event displayed on the calendar view.</summary>
public sealed record CalendarEvent
{
    /// <summary>Date of the event.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Event title.</summary>
    public required string Title { get; init; }

    /// <summary>Color indicator (CSS color value).</summary>
    public string? Color { get; init; }

    /// <summary>Whether the event spans the entire day.</summary>
    public bool AllDay { get; init; } = true;
}
