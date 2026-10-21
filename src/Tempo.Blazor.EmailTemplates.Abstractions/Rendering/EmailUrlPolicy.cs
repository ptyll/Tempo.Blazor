namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The public read-only view of the pipeline's single URL allow-list (§3.7a, D15), for a UI that has to
/// explain a rejected link <b>before</b> it reaches the markup — the item dialogs of the editor.
/// <para>
/// The list itself lives in the internal <see cref="UrlSchemePolicy"/> and cannot be extended by a host:
/// <c>http</c>, <c>https</c>, <c>mailto</c>, <c>tel</c>, a relative path without an authority and a
/// <c>#fragment</c> pass; every other scheme and every network/UNC path is refused.
/// </para>
/// </summary>
public static class EmailUrlPolicy
{
    /// <summary>The value a refused URL is replaced with in the markup (fail-closed but still a link).</summary>
    public const string BlockedUrlPlaceholder = UrlSchemePolicy.BlockedUrlPlaceholder;

    /// <summary>
    /// Returns the offending scheme in lower case (<c>javascript</c>, <c>data</c>, …) or the pseudo-scheme
    /// <c>network-path</c> for a protocol-relative / UNC value, or <see langword="null"/> when the URL may
    /// be emitted. An empty value is allowed and means "no link".
    /// </summary>
    public static string? FindBlockedScheme(string? url) => UrlSchemePolicy.FindBlockedScheme(url);

    /// <summary>Answers whether <paramref name="url"/> may be emitted into the message.</summary>
    public static bool IsAllowed(string? url) => UrlSchemePolicy.IsAllowed(url);
}
