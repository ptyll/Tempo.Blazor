using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The Scriban-span masking shared by both <see cref="IHtmlContentSanitizer"/> implementations
/// (§3.0 point 1 / D22). A text block may carry <c>{{ … }}</c> expressions and <c>{% … %}</c> block
/// tags; running the sanitizer over them would HTML-encode the quotes, <c>&gt;</c> and <c>&amp;</c>
/// *inside* a code span and destroy the template (<c>{{ price | math.format "N2" }}</c> became an
/// "Invalid token found &amp;"). Each span is therefore swapped for an inert private-use placeholder,
/// the markup between the spans is sanitized by the caller's delegate, and the spans are restored
/// verbatim.
/// </summary>
/// <remarks>
/// <para>
/// Safe because of the layering: the *output* of an expression is HTML-escaped by the template engine
/// and a <c>html.trusted</c> value is sanitized, while everything outside a span is rebuilt into the
/// compiled markup under <c>MarkupGuard</c>. A block tag is restored only when it cannot carry markup
/// itself; a content that already contains the placeholder characters or a <c>{%{</c> construct falls
/// back to the plain sanitizer (fail closed). One implementation for both sanitizers means the WASM
/// (conservative) and server (AngleSharp) render pipeline mask exactly the same spans.
/// </para>
/// <para>
/// The restore is fail-closed against a *forged* placeholder. A content cannot contain U+E000/U+E001
/// literally (the pre-check catches that), but it can carry them as character references
/// (<c>&amp;#xE000;</c>, <c>&amp;#57344;</c>, or without the terminating <c>;</c>), which the sanitizer
/// decodes — and then removing the "unexpected" placeholder is itself the attack: deleting
/// <c>java&amp;#xE000;…_0&amp;#xE001;script:</c> assembles a live <c>javascript:</c> href, and deleting
/// the placeholder inside <c>u…l(java…script:…)</c> assembles a <c>url(...)</c> style expression after
/// the policy already judged the value. Therefore: a placeholder whose nonce or index is not the one this
/// call emitted is never silently dropped — it marks the content invalid, and invalid content (a missing
/// or duplicated placeholder included) yields <see cref="StripPlaceholderCharacters"/> over the *unmasked*
/// sanitizer output. The private-use range is replaced with U+FFFD, so no placeholder character can ever
/// reach the generated markup.
/// </para>
/// </remarks>
internal static partial class TemplateContentMasker
{
    /// <summary>The private-use character that opens a masked span placeholder.</summary>
    internal const char PlaceholderStart = '\uE000';

    /// <summary>The private-use character that closes a masked span placeholder.</summary>
    internal const char PlaceholderEnd = '\uE001';

    /// <summary>The character a neutralised placeholder character becomes (the Unicode replacement character).</summary>
    internal const char ReplacementCharacter = '\uFFFD';

    /// <summary>Sanitizes <paramref name="content"/> with the spans masked by placeholders.</summary>
    public static string Sanitize(string content, Func<string, string> sanitize)
    {
        ArgumentNullException.ThrowIfNull(sanitize);

        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        // A content that already carries a placeholder character (or a construct whose masking would be
        // guesswork) is not masked at all: it goes through the plain sanitizer and every placeholder
        // character it contained is neutralised. A forged placeholder can therefore never be "restored".
        if (content.Contains(PlaceholderStart, StringComparison.Ordinal) ||
            content.Contains(PlaceholderEnd, StringComparison.Ordinal) ||
            EscapeBlockPattern().IsMatch(content))
        {
            return StripPlaceholderCharacters(sanitize(content));
        }

        var nonce = Guid.NewGuid().ToString("N");
        var spans = new List<string>();
        var masked = TemplateSpanPattern.CodeSpan().Replace(content, match =>
        {
            // A block tag (`{% … %}`) is control flow, not expression output: restore it only when it
            // cannot smuggle markup on its own (a `{% raw %}` payload sits outside the tag and is
            // sanitized anyway, but a tag carrying `<`/`>`/`&` stays with the sanitizer).
            if (match.Value.Length > 1 && match.Value[1] == '%' && ContainsMarkup(match.Value))
            {
                return match.Value;
            }

            spans.Add(match.Value);
            return PlaceholderStart + nonce + "_" + (spans.Count - 1).ToString(CultureInfo.InvariantCulture) + PlaceholderEnd;
        });

        var sanitized = sanitize(masked);
        if (spans.Count == 0)
        {
            // Nothing was masked, but the sanitizer decodes character references: a placeholder smuggled in
            // as `&#xE000;…&#xE001;` would now sit in the output as a live private-use character.
            return StripPlaceholderCharacters(sanitized);
        }

        var restored = new StringBuilder(sanitized.Length);
        var cursor = 0;
        var seen = 0;
        var valid = true;

        foreach (Match match in PlaceholderPattern().Matches(sanitized))
        {
            restored.Append(sanitized, cursor, match.Index - cursor);
            cursor = match.Index + match.Length;
            seen++;

            // Only a placeholder this call emitted may be expanded. Anything else is an attack artifact:
            // do not delete it here (deleting is what splices `java…script:` together) — mark invalid.
            if (!string.Equals(match.Groups["nonce"].Value, nonce, StringComparison.Ordinal) ||
                !int.TryParse(match.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index >= spans.Count)
            {
                valid = false;
                continue;
            }

            restored.Append(spans[index]);
        }

        restored.Append(sanitized, cursor, sanitized.Length - cursor);

        // Fail closed: a forged placeholder, a placeholder the sanitizer dropped (it must never remove one),
        // or a placeholder the restore did not account for all mean "this content cannot be trusted to be
        // masked". Sanitize it unmasked, without ever restoring a span, and neutralise the private-use range.
        if (!valid || seen != spans.Count)
        {
            return StripPlaceholderCharacters(sanitize(content));
        }

        var output = restored.ToString();
        return ContainsPlaceholder(output)
            ? StripPlaceholderCharacters(sanitize(content))
            : output;
    }

    /// <summary>Replaces every placeholder character (U+E000/U+E001) with U+FFFD.</summary>
    private static string StripPlaceholderCharacters(string value)
        => value
            .Replace(PlaceholderStart, ReplacementCharacter)
            .Replace(PlaceholderEnd, ReplacementCharacter);

    private static bool ContainsPlaceholder(string value)
        => value.Contains(PlaceholderStart, StringComparison.Ordinal) ||
           value.Contains(PlaceholderEnd, StringComparison.Ordinal);

    private static bool ContainsMarkup(string value)
        => value.Contains('<', StringComparison.Ordinal) ||
           value.Contains('>', StringComparison.Ordinal) ||
           value.Contains('&', StringComparison.Ordinal);


    /// <summary>A placeholder emitted by <see cref="Sanitize"/>.</summary>
    [GeneratedRegex(@"[\uE000](?<nonce>[0-9a-f]{32})_(?<index>[0-9]+)[\uE001]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlaceholderPattern();

    /// <summary>
    /// A construct whose masking would be guesswork (<c>{%{</c>): such a content goes through the plain
    /// sanitizer, which fails closed by encoding it.
    /// </summary>
    [GeneratedRegex(@"\{%+\{", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EscapeBlockPattern();
}
