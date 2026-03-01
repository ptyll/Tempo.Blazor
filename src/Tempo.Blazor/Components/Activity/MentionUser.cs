namespace Tempo.Blazor.Components.Activity;

/// <summary>
/// Represents a user mention in the editor.
/// </summary>
public class MentionUser
{
    /// <summary>
    /// Gets or sets the unique identifier of the user.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the username for the mention (e.g., "john_doe").
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the user (e.g., "John Doe").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional avatar URL of the user.
    /// </summary>
    public string? AvatarUrl { get; set; }
}
