using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// The untrusted-markup boundary of the WYSIWYG canvas (Fáze 9A).
/// <para>
/// Block content reaches the canvas from an MJML import, a JSON payload or a live edit, so it is HTML the
/// editor did not author. Two rules apply, and neither may be relaxed:
/// </para>
/// <list type="number">
///   <item>
///   <b>Markup goes through <see cref="IHtmlContentSanitizer"/> and nothing else.</b> The sanitized output
///   is then reduced to the tag stream alone — every attribute is dropped, and an <c>&lt;a&gt;</c> becomes
///   an inert <c>&lt;span&gt;</c>. A canvas click selects a block; it must never navigate, and a hostile
///   <c>style</c> attribute must never paint over the editor. Without a registered sanitizer the content is
///   shown as ESCAPED TEXT (fail closed), never as markup.
///   </item>
///   <item>
///   <b>A template token is a chip, never markup and never a substituted value.</b> Each span becomes an
///   HTML-encoded chip, so a payload like <c>{{ "&lt;img onerror=…&gt;" }}</c> can only ever become text.
///   </item>
/// </list>
/// <para>
/// <b>Order matters (review fix).</b> The sanitizer runs on the WHOLE content (its masker keeps the code
/// spans verbatim), and the tokens are then <i>masked positionally before</i> the tag reduction — every span
/// becomes a placeholder such as <c>U+E0000U+E001</c> — and restored to a chip afterwards. Two failures the
/// earlier "cut first, then reduce" order caused:
/// </para>
/// <list type="bullet">
///   <item>
///   Cutting first handed the reducer fragments like <c>!</c><c>&lt;/strong&gt;</c>, an orphan closing tag
///   its fail-closed balance rule escaped and painted as literal <c>&lt;/strong&gt;</c>.
///   </item>
///   <item>
///   A token inside an ATTRIBUTE (<c>&lt;a href="{{ url }}"&gt;</c>) broke the tag around it: the reducer saw
///   <c>&lt;a href="&lt;span class="…"&gt;…&lt;/span&gt;"&gt;</c> and the <c>&lt;a</c> opening leaked into the
///   text as <c>&amp;lt;a</c>. Masked first, the placeholder is just another attribute character, so the tag
///   is reduced normally and the placeholder disappears WITH the dropped attribute.
///   </item>
/// </list>
/// </summary>
internal static partial class EmailCanvasMarkup
{
    /// <summary>The token pattern — the same spans the pipeline's masker treats as code.</summary>
    private const string TokenExpression = @"\{\{.*?\}\}|\{%.*?%\}";

    /// <summary>Characters of an <c>mj-raw</c> block shown in its placeholder.</summary>
    internal const int RawPreviewLength = 140;

    /// <summary>Opening character of a positional mask (a private-use code point no author text carries).</summary>
    private const char MaskOpen = '\uE000';

    /// <summary>Closing character of a positional mask.</summary>
    private const char MaskClose = '\uE001';

    /// <summary>What a mask character that did NOT come from <see cref="ToMarkup"/> becomes.</summary>
    private const char ReplacementCharacter = '\uFFFD';

    /// <summary>
    /// Turns authored text into canvas markup: sanitized (attribute-free, anchor-free) HTML plus one inert
    /// chip per template token. FAIL CLOSED: with no sanitizer the whole content is escaped text.
    /// </summary>
    public static string ToMarkup(string? content, IHtmlContentSanitizer? sanitizer)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        if (sanitizer is null)
        {
            // No sanitizer: the author still has to SEE the text, but it can never become markup.
            return WebUtility.HtmlEncode(content);
        }

        // SanitizeTemplateContent, not Sanitize: the pipeline's own masker protects the code spans from
        // being HTML-encoded (which would destroy the template) and restores them verbatim afterwards.
        var sanitized = sanitizer.SanitizeTemplateContent(content);

        // 1. A mask character that survived means a FORGED placeholder (an attacker can spell U+E000 through
        //    a character reference, and the sanitizer decodes it). It can never be ours, so it becomes
        //    U+FFFD — the same normalisation the HTML parser applies to a character it cannot keep.
        var neutralised = NeutralizeMaskCharacters(sanitized);

        // 2. Mask every token positionally: the whole token becomes ONE opaque placeholder, so no later
        //    step can split it, encode it or take it for markup.
        var tokens = new List<string>(4);
        var masked = MaskTokens(neutralised, tokens);

        // 3. Reduce the WHOLE masked string to the allow-listed tag stream, restoring each surviving
        //    placeholder as its chip in the SAME pass — the reduction emits real tags, so a second pass over
        //    its output would encode them. A placeholder that sat inside an attribute is inside a tag that is
        //    dropped as a whole, which is exactly what should happen: the attribute never reaches the canvas,
        //    so neither does the token it carried.
        var reduced = new StringBuilder(masked.Length + (tokens.Count * 32));
        ReduceToTags(reduced, masked, tokens);
        return reduced.ToString();
    }

    /// <summary>A one-line, escaped preview of a raw block's content (it is NEVER rendered as markup).</summary>
    public static string RawPreview(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var collapsed = Whitespace().Replace(content, " ").Trim();
        return WebUtility.HtmlEncode(
            collapsed.Length > RawPreviewLength ? collapsed[..RawPreviewLength] + "…" : collapsed);
    }

    /// <summary>Replaces a mask character the sanitizer let through (a forged placeholder) with U+FFFD.</summary>
    private static string NeutralizeMaskCharacters(string sanitized)
        => sanitized.IndexOfAny([MaskOpen, MaskClose]) < 0
            ? sanitized
            : new string(sanitized.Select(c => c is MaskOpen or MaskClose ? ReplacementCharacter : c).ToArray());

    /// <summary>
    /// Replaces every token span with <c>U+E000{index}U+E001</c> and records the span text, so the reducer
    /// sees one opaque placeholder instead of markup the token body could look like.
    /// </summary>
    private static string MaskTokens(string value, List<string> tokens)
    {
        var matches = TokenPattern().Matches(value);
        if (matches.Count == 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + (matches.Count * 4));
        var cursor = 0;
        foreach (Match match in matches)
        {
            builder.Append(value, cursor, match.Index - cursor);
            builder.Append(MaskOpen)
                .Append(tokens.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(MaskClose);
            tokens.Add(match.Value);
            cursor = match.Index + match.Length;
        }

        builder.Append(value, cursor, value.Length - cursor);
        return builder.ToString();
    }

    /// <summary>
    /// Reduces sanitized markup to a tag stream: allow-listed tags without ANY attribute, an
    /// <c>&lt;a&gt;</c> as an inert <c>span</c>, and anything else dropped. Safe by construction because it
    /// runs on <see cref="IHtmlContentSanitizer"/> output whose code spans are already masked, so a literal
    /// <c>&lt;</c> either starts a tag the sanitizer emitted or is text. A stray <c>&lt;</c> (no closing
    /// <c>&gt;</c>) is encoded, so nothing can slip through as markup.
    /// </summary>
    private static void ReduceToTags(StringBuilder builder, string sanitized, List<string> tokens)
    {
        if (sanitized.Length == 0)
        {
            return;
        }

        var cursor = 0;
        foreach (Match match in TagPattern().Matches(sanitized))
        {
            AppendPlainText(builder, sanitized[cursor..match.Index], tokens);
            cursor = match.Index + match.Length;

            var closing = match.Groups["close"].Length > 0;
            var name = match.Groups["name"].Value.ToLowerInvariant();
            if (!HtmlSanitizerPolicy.AllowedTags.Contains(name))
            {
                continue;
            }

            // An anchor keeps its text and loses its navigation: the canvas selects, it never navigates.
            var tag = name == "a" ? "span" : name;
            if (closing)
            {
                builder.Append("</").Append(tag).Append('>');
            }
            else if (name == "a")
            {
                builder.Append("<span class=\"tm-email-block__link\">");
            }
            else
            {
                builder.Append('<').Append(tag).Append('>');
            }
        }

        AppendPlainText(builder, sanitized[cursor..], tokens);
    }

    /// <summary>
    /// Appends a run of TEXT between two reduced tags: every <c>&lt;</c>/<c>&gt;</c> it still contains is
    /// encoded (after <see cref="ReduceToTags"/> a remaining angle bracket is not an allow-listed tag, so it
    /// must never be able to look like one) and every positional mask becomes its chip.
    /// </summary>
    private static void AppendPlainText(StringBuilder builder, string text, List<string> tokens)
    {
        if (tokens.Count == 0)
        {
            AppendEncoded(builder, text);
            return;
        }

        var cursor = 0;
        foreach (Match match in MaskPattern().Matches(text))
        {
            AppendEncoded(builder, text[cursor..match.Index]);
            cursor = match.Index + match.Length;

            // A mask whose index is out of range cannot have been produced here (step 1 neutralised every
            // foreign placeholder character), so it is dropped rather than trusted.
            if (int.TryParse(match.Groups["index"].Value, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var index)
                && index < tokens.Count)
            {
                AppendToken(builder, tokens[index]);
            }
        }

        AppendEncoded(builder, text[cursor..]);
    }

    /// <summary>Encodes the two characters that could start markup in a text run.</summary>
    private static void AppendEncoded(StringBuilder builder, string text)
    {
        foreach (var c in text)
        {
            if (c == '<') builder.Append("&lt;");
            else if (c == '>') builder.Append("&gt;");
            else builder.Append(c);
        }
    }

    private static void AppendToken(StringBuilder builder, string token)
    {
        // The chip shows the token as written ({{ path }} / {% tag %}) and carries the bare path for a host
        // that wants to do something with it; neither part is ever substituted, and both are ENCODED text —
        // a token's body never becomes markup.
        //
        // Decode ONCE before encoding: the sanitizer's masker keeps the span verbatim, but the surrounding
        // content was entity-normalised, so a token written with an entity ("{{ a &gt; b }}") must not end up
        // double-encoded ("&amp;gt;") in a chip whose whole point is showing what the author wrote.
        var text = WebUtility.HtmlEncode(WebUtility.HtmlDecode(token));
        var path = token.Length >= 4 ? WebUtility.HtmlDecode(token[2..^2]).Trim() : WebUtility.HtmlDecode(token);
        builder.Append("<span class=\"tm-email-block__token\" data-tm-token=\"")
            .Append(WebUtility.HtmlEncode(path))
            .Append("\">")
            .Append(text)
            .Append("</span>");
    }

    /// <summary>The pipeline's code-span pattern (expression or block tag), non-greedy.</summary>
    [GeneratedRegex(TokenExpression, RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TokenPattern();

    /// <summary>One positional mask of <see cref="MaskTokens"/>.</summary>
    [GeneratedRegex(@"\uE000(?<index>[0-9]{1,6})\uE001", RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex MaskPattern();

    /// <summary>An HTML tag of a sanitized fragment.</summary>
    [GeneratedRegex(@"<(?<close>/?)(?<name>[a-zA-Z][a-zA-Z0-9]*)[^>]*>",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TagPattern();

    /// <summary>Any run of whitespace, collapsed to one space in the raw preview.</summary>
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Whitespace();
}
