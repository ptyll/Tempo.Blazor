namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents a user that can be @mentioned in TmRichEditor and TmSimpleEditor.
/// </summary>
public interface IMentionUser
{
    /// <summary>Unique identifier for the user.</summary>
    string Id { get; }

    /// <summary>Username for the @mention trigger (e.g. "john.doe").</summary>
    string UserName { get; }

    /// <summary>Full display name shown in the autocomplete list.</summary>
    string DisplayName { get; }

    /// <summary>Optional avatar image URL.</summary>
    string? AvatarUrl { get; }
}
