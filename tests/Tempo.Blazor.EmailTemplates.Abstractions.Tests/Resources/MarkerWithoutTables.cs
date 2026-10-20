namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Resources;

/// <summary>
/// Marker type with no embedded JSON table at all — a host's marker whose tables were not packaged. Used
/// to pin that a missing table is an empty table (key not found), never a failure.
/// </summary>
public sealed class MarkerWithoutTables
{
}
