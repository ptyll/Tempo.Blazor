namespace Tempo.Blazor.Models;

/// <summary>Represents a single property change with old and new values.</summary>
public record TmChangeInfo(string Property, string? OldValue, string? NewValue);
