namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>Definition of a Kanban board column.</summary>
public sealed record KanbanColumn(string Id, string Title, string? Color = null, int? MaxItems = null);

/// <summary>Event fired when a card is moved between columns.</summary>
public sealed record KanbanMoveEvent<TItem>(TItem Item, string FromColumn, string ToColumn);
