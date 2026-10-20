namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The single source of the HTML allow-lists (D12): which tags survive inline HTML sanitization, which
/// attributes may stay on them, which elements are dropped together with their content, and which are
/// void. Both <see cref="IHtmlContentSanitizer"/> implementations read this policy — the AngleSharp one
/// in <c>Tempo.Blazor.EmailTemplates.Engine</c> and the BCL-only
/// <see cref="ConservativeHtmlContentSanitizer"/> in this package — so "the sanitizer allows less" can
/// never become "the sanitizer allows something else".
/// </summary>
/// <remarks>
/// The link allow-list is <see cref="UrlSchemePolicy"/> (the same <c>http</c>/<c>https</c>/<c>mailto</c>/
/// <c>tel</c> + relative rules the render pipeline applies, D15/D22); a host cannot extend it.
/// </remarks>
public static class HtmlSanitizerPolicy
{
    /// <summary>Tags that may appear in sanitized inline HTML.</summary>
    public static IReadOnlySet<string> AllowedTags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "b", "i", "u", "strong", "em", "a", "br", "span", "p", "ul", "ol", "li" };

    /// <summary>
    /// Attributes the authoritative (AngleSharp) implementation may keep. The conservative
    /// implementation is stricter: it emits only <see cref="ConservativeAttributes"/>.
    /// </summary>
    public static IReadOnlySet<string> AllowedAttributes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "href", "style", "target", "rel" };

    /// <summary>
    /// Attributes the conservative implementation may emit — the single documented exception to
    /// "allow-listed tags are emitted without attributes" (D22): <c>a[href]</c> after the scheme check.
    /// </summary>
    public static IReadOnlySet<string> ConservativeAttributes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "href" };

    /// <summary>Elements removed together with their content (they can execute or load).</summary>
    public static IReadOnlySet<string> DroppedWithContent { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "script", "style", "iframe", "object", "embed", "link", "meta", "base", "title" };

    /// <summary>Void elements of the allow-list: emitted without a closing tag.</summary>
    public static IReadOnlySet<string> VoidTags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "br" };

    /// <summary>
    /// Dropped elements that have no closing tag, so only the tag itself is removed instead of
    /// everything up to a closing tag that never comes.
    /// </summary>
    public static IReadOnlySet<string> VoidDroppedTags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "link", "meta", "base", "embed" };

    /// <summary>
    /// Answers whether <paramref name="href"/> may be emitted. The value is judged exactly as a browser
    /// would read it (character references decoded, tab/CR/LF removed), so <c>&amp;#106;avascript:</c>
    /// is refused just like <c>javascript:</c>.
    /// </summary>
    public static bool IsHrefAllowed(string? href) => UrlSchemePolicy.IsAllowed(href);

    /// <summary>Answers whether a <c>style</c> attribute value may be kept.</summary>
    public static bool IsSafeStyle(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        var lowered = value.ToLowerInvariant();
        return !lowered.Contains("javascript:", StringComparison.Ordinal) &&
            !lowered.Contains("expression(", StringComparison.Ordinal) &&
            !lowered.Contains("url(", StringComparison.Ordinal);
    }
}
