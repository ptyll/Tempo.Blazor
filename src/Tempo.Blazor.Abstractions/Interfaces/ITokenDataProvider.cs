namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Data provider for the token/variable autocomplete in TmRichEditor.
/// </summary>
public interface ITokenDataProvider
{
    /// <summary>
    /// Searches tokens matching the given query string.
    /// Called as the user types after the {{ trigger.
    /// </summary>
    Task<IEnumerable<IToken>> SearchTokensAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Whether the provider supports creating new tokens from the autocomplete dropdown.
    /// </summary>
    bool SupportsCreation { get; }

    /// <summary>
    /// Invalidates any cached token data so the next search returns fresh results.
    /// Called before RefreshTokensAsync() on the editor to ensure new/updated tokens are available.
    /// </summary>
    void Refresh();
}
