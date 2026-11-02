using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.AI.Services;

/// <summary>
/// Supplies the <c>@</c>-mention suggestions of a composer. The composer calls it with the text the reader
/// typed after the <c>@</c>, debounced (<c>MentionDebounce</c>) and with a cancellation token that is
/// cancelled as soon as the query changes, the list closes or the component is disposed — so a slow search
/// never repaints a stale list and a host can pass the token straight to its API call.
/// </summary>
/// <remarks>
/// The interface lives in <c>Tempo.Blazor.Abstractions</c> on purpose: a backend or an API client can
/// implement it without a Blazor reference. An implementation must honour the cancellation token and should
/// be cheap for short queries; an exception is caught by the composer, which closes the list and announces a
/// localized failure instead of breaking the editor.
/// </remarks>
public interface IAiMentionProvider
{
    /// <summary>
    /// Returns the mentions matching <paramref name="query"/> — the text after the <c>@</c> (possibly empty,
    /// which asks for the default suggestions).
    /// </summary>
    /// <param name="query">Text typed after the trigger; never null.</param>
    /// <param name="cancellationToken">Cancelled when the query changed or the list closed.</param>
    /// <returns>The matching mentions; an empty list closes the suggestion list.</returns>
    Task<IReadOnlyList<AiMentionItem>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
