namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents a token/variable that can be inserted into TmRichEditor via {{ trigger.
/// </summary>
public interface IToken
{
    /// <summary>Unique key for the token (e.g. "user.email", "company.name").</summary>
    string Key { get; }

    /// <summary>Display name shown in the autocomplete list and in the editor chip.</summary>
    string DisplayName { get; }

    /// <summary>Optional description shown in the autocomplete list.</summary>
    string? Description { get; }

    /// <summary>Optional category for grouping tokens (e.g. "User", "System").</summary>
    string? Category { get; }
}
