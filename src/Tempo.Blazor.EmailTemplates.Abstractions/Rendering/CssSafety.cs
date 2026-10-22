namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The CSS payload rule of the pipeline (§3.7a, Fáze 10 review 6): a stylesheet the pipeline WRITES
/// (<c>mj-style</c>, <c>style</c> in a raw block, a host-set inline style) may not carry the constructs a
/// browser would execute or fetch.
/// </summary>
/// <remarks>
/// <para>
/// The guard's URL pass covers a <c>url(…)</c> whose scheme is refused; these are the remaining constructs an
/// e-mail never needs and a mail client (or a webmail preview) may still honour:
/// </para>
/// <list type="bullet">
/// <item><c>expression(…)</c> — the legacy IE script-in-CSS property;</item>
/// <item><c>behavior:</c> — the legacy IE HTC binding, which loads code from a URL;</item>
/// <item><c>-moz-binding</c> — the Firefox XBL binding, the same idea;</item>
/// <item><c>@import</c> — a second stylesheet fetched from the message, which is a tracking pixel with extra
/// steps and is not supported by a single mail client that matters.</item>
/// </list>
/// <para>
/// Each construct is matched in a COMPACTED copy of the text (Fáze 10 security re-review, item 4): comments are
/// removed with an EMPTY string (<c>ex/**&#47;*&#47;pression(1)</c> is <c>expression(1)</c> to a CSS parser) and
/// every whitespace character is dropped (<c>behavior :url(x.htc)</c> is a <c>behavior:</c> declaration), both on
/// the raw text and on the escape-decoded text (<see cref="CssNormalizer"/>), so <c>\65 xpression(</c> cannot hide
/// either. A payload that fails is dropped ENTIRELY, never rewritten: a partially cleaned stylesheet is still a
/// stylesheet whose remaining rules the author did not write.
/// </para>
/// </remarks>
internal static class CssSafety
{
    /// <summary>The constructs the pipeline refuses in a stylesheet it writes.</summary>
    private static readonly string[] Blocked =
        ["expression(", "behavior:", "-moz-binding", "@import"];

    /// <summary>Answers whether the stylesheet may be written into the message verbatim.</summary>
    public static bool IsSafe(string? css) => IsSafe(css, null);

    /// <summary>
    /// Answers whether the stylesheet may be written into the message verbatim, treating the
    /// <c>@import</c> targets in <paramref name="permittedImports"/> as the ones the PIPELINE itself wrote.
    /// </summary>
    /// <param name="css">The stylesheet (or a single declaration) to judge.</param>
    /// <param name="permittedImports">
    /// Exact <c>@import</c> targets the pipeline emitted itself (the compiled <c>mj-font</c> hrefs). Any other
    /// <c>@import</c> — including one in the same block — still refuses the payload.
    /// </param>
    public static bool IsSafe(string? css, IReadOnlyCollection<string>? permittedImports)
    {
        if (string.IsNullOrEmpty(css)) return true;

        // The guard's own CSS pass first: a url(…)/image-set(…)/@import target with a refused scheme is a
        // stylesheet that would fetch or execute something, and the guard already knows how to read one past
        // comments and CSS escapes.
        if (MarkupGuard.HasBlockedCssReference(css)) return false;

        // The raw text covers what a comment or a space would otherwise split; the normalised text covers what a
        // CSS escape hides. Both are compacted (no comments, no whitespace) before the constructs are searched for.
        return IsCompactSafe(css, permittedImports) && IsCompactSafe(CssNormalizer.Normalize(css), permittedImports);
    }

    /// <summary>Answers whether the compacted form of <paramref name="css"/> carries none of the blocked constructs.</summary>
    private static bool IsCompactSafe(string css, IReadOnlyCollection<string>? permittedImports)
    {
        var compact = Compact(css);
        if (permittedImports is { Count: > 0 })
        {
            foreach (var import in permittedImports)
            {
                if (string.IsNullOrWhiteSpace(import)) continue;

                var target = Compact(import);
                compact = compact
                    .Replace("@importurl(" + target + ")", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("@import\"" + target + "\"", string.Empty, StringComparison.OrdinalIgnoreCase)
                    .Replace("@import'" + target + "'", string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        foreach (var construct in Blocked)
        {
            if (compact.Contains(construct, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The CSS with comments removed to an EMPTY string and every whitespace character dropped — the form a
    /// browser's parser effectively reads for the constructs above.
    /// </summary>
    private static string Compact(string css)
    {
        var builder = new System.Text.StringBuilder(css.Length);
        var index = 0;
        while (index < css.Length)
        {
            if (css[index] == '/' && index + 1 < css.Length && css[index + 1] == '*')
            {
                var close = css.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    // An unterminated comment runs to the end of the stylesheet, as a CSS parser reads it.
                    break;
                }

                index = close + 2;
                continue;
            }

            if (!char.IsWhiteSpace(css[index]))
            {
                builder.Append(css[index]);
            }

            index++;
        }

        return builder.ToString();
    }
}
