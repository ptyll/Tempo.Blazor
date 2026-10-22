namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The shape of a STYLE-BOUND attribute value the pipeline may write (<c>padding</c>, <c>border</c>,
/// <c>font-family</c>, <c>width</c>, <c>height</c>, <c>line-height</c>, <c>letter-spacing</c>, …).
/// </summary>
/// <remarks>
/// <para>
/// The MJML compiler copies these values verbatim into the element's inline <c>style</c>
/// (<c>style="padding:«value»;…"</c>), so a value carrying <c>;</c>, a quote or a <c>url(…)</c> stops being a
/// length and becomes a declaration of its own — <c>"0;background:url(https://tracker)"</c> is a tracking pixel,
/// and a quote in a font stack can leave the attribute altogether. The allow-list is therefore a CHARACTER set:
/// lengths, numbers, percentages, hex colours, CSS keywords and a comma-separated font stack survive, and
/// anything with parentheses (so no <c>url(</c>, <c>expression(</c>, <c>calc(</c>), quotes, semicolons, braces or
/// backslashes is dropped. A legitimate value the charset refuses is dropped rather than "cleaned up": a
/// partially rewritten declaration is still a declaration.
/// </para>
/// <para>
/// The single quote is the one exception (Fáze 10 security re-review, item 5): a font stack legitimately quotes a
/// family (<c>'Open Sans', Arial</c>), so a <c>font-family</c> value may carry it — but only in an EVEN number,
/// because an unpaired quote would still leave the attribute. Every other style-bound value keeps the quote-free
/// charset, and <c>;</c>, <c>(</c>, <c>\</c> and <c>"</c> stay refused everywhere.
/// </para>
/// </remarks>
internal static class CssStyleValue
{
    /// <summary>The maximum length of a style-bound value (a real font stack is well under this).</summary>
    private const int MaxLength = 160;

    /// <summary>Answers whether <paramref name="value"/> may be copied into an inline style declaration.</summary>
    public static bool IsPlausible(string? value) => IsPlausible(value, null);

    /// <summary>
    /// Answers whether <paramref name="value"/> may be copied into the declaration of
    /// <paramref name="attributeName"/>. Only a <c>font-family</c> value may quote a family, and only with an even
    /// number of quotes.
    /// </summary>
    /// <param name="value">The attribute value the model carried.</param>
    /// <param name="attributeName">The attribute the value belongs to (a font stack is the only quoted one).</param>
    public static bool IsPlausible(string? value, string? attributeName)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength) return false;

        // The charset itself closes every injection: ';' '{' '}' '<' '>' '"' '\\' ':' and '(' ')' are not in
        // it, so a declaration cannot be opened, a URL function cannot appear and an attribute cannot be left.
        var quotesAllowed = string.Equals(attributeName, "font-family", StringComparison.OrdinalIgnoreCase);
        var quotes = 0;
        foreach (var c in trimmed)
        {
            if (c == '\'')
            {
                if (!quotesAllowed) return false;
                quotes++;
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(c) && c is not (' ' or ',' or '.' or '%' or '-' or '+' or '/' or '*' or '#' or '_'))
            {
                return false;
            }
        }

        return !quotesAllowed || quotes % 2 == 0;
    }
}
