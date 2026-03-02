namespace Tempo.Blazor.Models;

/// <summary>Describes an available export format (e.g. CSV, XLSX).</summary>
public record TmExportFormat(string Value, string Label);

/// <summary>Describes an entity type available for export.</summary>
public record TmExportEntity(string Value, string Label);

/// <summary>Represents a user's export request with selected format and entity types.</summary>
public record TmExportRequest(string Format, IReadOnlyList<string> SelectedEntityTypes);
