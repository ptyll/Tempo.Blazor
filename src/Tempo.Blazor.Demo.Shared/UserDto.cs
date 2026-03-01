namespace Tempo.Blazor.Demo.Shared;

/// <summary>
/// User data transfer object for mention functionality.
/// </summary>
public record UserDto(
    string Id,
    string UserName,
    string DisplayName,
    string? AvatarUrl,
    string Email,
    string Department)
{
    /// <summary>
    /// Gets the full display name with username for search.
    /// </summary>
    public string SearchText => $"{DisplayName} {UserName} {Email}".ToLowerInvariant();
}
