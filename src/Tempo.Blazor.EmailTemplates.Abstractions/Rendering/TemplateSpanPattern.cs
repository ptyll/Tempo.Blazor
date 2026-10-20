using System.Text.RegularExpressions;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The single definition of what a Scriban code span is: an expression (<c>{{ … }}</c>) or a block tag
/// (<c>{% … %}</c>), non-greedy. Both the sanitizer masking (<see cref="TemplateContentMasker"/>) and the
/// no-substitution export (<see cref="TemplateSpanEncoder"/>) read this pattern, so "which text is a
/// template token" can never drift between the two — a difference would either survive unencoded as
/// markup (the encoder) or be HTML-encoded inside the token (the masker).
/// </summary>
internal static partial class TemplateSpanPattern
{
    /// <summary>The expression/block-tag pattern shared by the masker and the encoder.</summary>
    internal static Regex CodeSpan()
        => CodeSpanRegex();

    [GeneratedRegex(@"\{\{.*?\}\}|\{%.*?%\}", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodeSpanRegex();
}
