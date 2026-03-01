namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Data provider for the @mention autocomplete in TmRichEditor and TmSimpleEditor.
/// </summary>
public interface IMentionDataProvider
{
    /// <summary>
    /// Searches users matching the given query string.
    /// Called with debouncing as the user types after the @ trigger.
    /// </summary>
    Task<IEnumerable<IMentionUser>> SearchUsersAsync(string query, CancellationToken ct = default);
}
