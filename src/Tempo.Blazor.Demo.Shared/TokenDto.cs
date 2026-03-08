namespace Tempo.Blazor.Demo.Shared;

/// <summary>
/// Token/variable data transfer object for template token functionality.
/// </summary>
public record TokenDto(
    string Key,
    string DisplayName,
    string? Description,
    string? Category)
{
    /// <summary>
    /// Gets combined text for search filtering.
    /// </summary>
    public string SearchText => $"{Key} {DisplayName} {Description}".ToLowerInvariant();
}
