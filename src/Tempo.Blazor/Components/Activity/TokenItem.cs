namespace Tempo.Blazor.Components.Activity;

/// <summary>
/// Represents a token/variable item in the editor autocomplete.
/// </summary>
public class TokenItem
{
    /// <summary>
    /// Gets or sets the unique key of the token (e.g. "user.email").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the token (e.g. "User Email").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional description of the token.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the optional category for grouping (e.g. "User", "System").
    /// </summary>
    public string? Category { get; set; }
}
