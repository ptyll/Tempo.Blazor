using System.Net;
using System.Text;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The BCL-only <see cref="IHtmlContentSanitizer"/> that ships in this package (D12/D22) and is the
/// default whenever <c>Tempo.Blazor.EmailTemplates.Engine</c> is not registered — i.e. the editor in
/// Blazor WebAssembly, which must not pull AngleSharp (or Scriban/Mjml.Net) into the browser.
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>tokenizer with HTML-encode</b>, never a regex strip/replace. The default action is to
/// encode every <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>"</c> and <c>'</c>; only the tags of
/// <see cref="HtmlSanitizerPolicy.AllowedTags"/> are re-emitted, without attributes, with the single
/// documented exception of <c>a[href]</c> after the scheme check (<see cref="HtmlSanitizerPolicy.IsHrefAllowed"/>).
/// <c>style</c> and <c>on*</c> never pass. Unclosed elements are completed, orphan closing tags are
/// encoded (so content can never inject <c>&lt;/mj-text&gt;</c>, <c>&lt;mj-raw&gt;</c> or
/// <c>&lt;!--</c>), character references are decoded and re-encoded, and an unknown, invalid or
/// unterminated construct degrades to escaped text — never to passthrough.
/// </para>
/// <para>
/// The policy is shared with the authoritative AngleSharp implementation, and the output may only
/// differ from it towards something stricter: the WYSIWYG consequence is that a WASM export can lose
/// formatting AngleSharp would keep (a <c>&lt;div&gt;</c> becomes visible text), which is a formatting
/// difference, not a security one.
/// </para>
/// </remarks>
public sealed class ConservativeHtmlContentSanitizer : IHtmlContentSanitizer
{
    /// <summary>The shared, stateless instance.</summary>
    public static ConservativeHtmlContentSanitizer Instance { get; } = new();

    private ConservativeHtmlContentSanitizer()
    {
    }

    /// <inheritdoc />
    public string Sanitize(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(html.Length);
        var open = new List<string>();
        var index = 0;

        while (index < html.Length)
        {
            if (html[index] != '<')
            {
                var next = html.IndexOf('<', index);
                if (next < 0)
                {
                    next = html.Length;
                }

                AppendText(builder, html.AsSpan(index, next - index));
                index = next;
                continue;
            }

            var following = index + 1 < html.Length ? html[index + 1] : '\0';
            switch (following)
            {
                case '!':
                    index = SkipCommentOrDeclaration(html, index);
                    continue;
                case '?':
                    index = SkipToGt(html, index);
                    continue;
                case '/':
                    index = ReadClosingTag(html, index, builder, open);
                    continue;
            }

            if (!char.IsAsciiLetter(following))
            {
                // A lone '<' (or "<3", "< b"): text, encoded.
                AppendText(builder, html.AsSpan(index, 1));
                index++;
                continue;
            }

            if (!TryReadStartTag(html, index, out var tag))
            {
                // EOF inside a tag: the browser drops it, so it is text — encoded, never emitted.
                AppendText(builder, html.AsSpan(index, html.Length - index));
                index = html.Length;
                continue;
            }

            index = tag.End + 1;
            if (HtmlSanitizerPolicy.DroppedWithContent.Contains(tag.Name))
            {
                index = SkipDroppedElement(html, tag);
                continue;
            }

            if (!HtmlSanitizerPolicy.AllowedTags.Contains(tag.Name))
            {
                // Unknown / non-allow-listed element: escaped text, never passthrough (D22).
                AppendText(builder, html.AsSpan(tag.Start, tag.End - tag.Start + 1));
                continue;
            }

            builder.Append('<').Append(tag.Name);
            AppendPolicyAttributes(html, tag, builder);
            builder.Append('>');

            if (HtmlSanitizerPolicy.VoidTags.Contains(tag.Name))
            {
                continue;
            }

            if (tag.SelfClosing)
            {
                builder.Append("</").Append(tag.Name).Append('>');
                continue;
            }

            open.Add(tag.Name);
        }

        for (var i = open.Count - 1; i >= 0; i--)
        {
            builder.Append("</").Append(open[i]).Append('>');
        }

        return builder.ToString();
    }

    /// <inheritdoc />
    public string SanitizeTemplateContent(string content)
        => TemplateContentMasker.Sanitize(content, Sanitize);

    /// <summary>
    /// Emits the attributes the conservative implementation may keep — the names come from
    /// <see cref="HtmlSanitizerPolicy.ConservativeAttributes"/>, not from a hardcoded <c>"href"</c>, so the
    /// implementation and the documented policy cannot drift. Every value is decoded once for the verdict
    /// (so <c>&amp;#106;avascript:</c> is judged as <c>javascript:</c>), normalised, refused by the URL policy
    /// if need be, and encoded — a value the policy allows keeps its entities single-encoded.
    /// </summary>
    private static void AppendPolicyAttributes(string html, StartTag tag, StringBuilder builder)
    {
        if (!tag.Name.Equals("a", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var name in HtmlSanitizerPolicy.ConservativeAttributes)
        {
            if (!TryGetAttribute(html, tag, name, out var raw))
            {
                continue;
            }

            var decoded = NormalizeText(WebUtility.HtmlDecode(raw));
            var emitted = HtmlSanitizerPolicy.IsHrefAllowed(decoded)
                ? decoded
                : UrlSchemePolicy.BlockedUrlPlaceholder;
            builder.Append(' ').Append(name).Append("=\"").Append(HtmlTextEscaper.Encode(emitted)).Append('"');
        }
    }

    /// <summary>
    /// Encodes one text run: character references are decoded and re-encoded so
    /// <c>&amp;#60;b&amp;#62;</c> normalizes to <c>&amp;lt;b&amp;gt;</c> instead of becoming a tag.
    /// A run without <c>&amp;</c> skips the decode (the common case stays allocation-free).
    /// </summary>
    private static void AppendText(StringBuilder builder, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return;
        }

        var value = text.IndexOf('&') < 0 ? text.ToString() : WebUtility.HtmlDecode(text.ToString());
        builder.Append(HtmlTextEscaper.Encode(NormalizeText(value)));
    }

    /// <summary>
    /// Replaces the characters a browser does not keep verbatim with <c>U+FFFD</c>: NUL and the other C0
    /// controls (tab, LF and CR are legitimate markup whitespace) and a lone surrogate. This is the same
    /// normalisation the HTML parser applies, and it closes the smuggling gap where dropping a byte inside
    /// <c>java\0script:</c> turns a refused value into an executable one.
    /// </summary>
    private static string NormalizeText(string value)
    {
        var first = FirstCharacterToReplace(value);
        if (first < 0)
        {
            return value;
        }

        var chars = value.ToCharArray();
        for (var i = first; i < chars.Length; i++)
        {
            if (IsControlOrLoneSurrogate(value, i))
            {
                chars[i] = ReplacementCharacter;
            }
        }

        return new string(chars);
    }

    private static int FirstCharacterToReplace(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (IsControlOrLoneSurrogate(value, i))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsControlOrLoneSurrogate(string value, int index)
    {
        var c = value[index];
        if (c < ' ' && c is not ('\t' or '\n' or '\r'))
        {
            return true;
        }

        if (char.IsHighSurrogate(c))
        {
            return index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]);
        }

        if (char.IsLowSurrogate(c))
        {
            return index == 0 || !char.IsHighSurrogate(value[index - 1]);
        }

        return false;
    }

    /// <summary>The character a normalised control/lone surrogate becomes (the Unicode replacement character).</summary>
    private const char ReplacementCharacter = '\uFFFD';

    /// <summary>Skips a comment or a declaration up to and including its terminator.</summary>
    private static int SkipCommentOrDeclaration(string html, int index)
    {
        if (html.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
        {
            var end = html.IndexOf("-->", index + 4, StringComparison.Ordinal);
            // An unterminated comment owns the rest of the document; a browser drops it (fail closed).
            return end < 0 ? html.Length : end + 3;
        }

        // <!DOCTYPE …>, <![CDATA[ … ]]> and friends: skip to the first '>'.
        return SkipToGt(html, index);
    }

    private static int SkipToGt(string html, int index)
    {
        var end = html.IndexOf('>', index + 1);
        return end < 0 ? html.Length : end + 1;
    }

    /// <summary>
    /// Reads a closing tag. A tag that closes the currently open element is emitted; anything else
    /// (an orphan, a mismatched close, an element that was never emitted) becomes escaped text — so
    /// <c>&lt;/mj-text&gt;</c> can never reach the generated MJML.
    /// </summary>
    private static int ReadClosingTag(string html, int index, StringBuilder builder, List<string> open)
    {
        var cursor = index + 2;
        while (cursor < html.Length && char.IsWhiteSpace(html[cursor]))
        {
            cursor++;
        }

        var nameStart = cursor;
        while (cursor < html.Length && IsTagNameChar(html[cursor]))
        {
            cursor++;
        }

        var name = html[nameStart..cursor].ToLowerInvariant();
        var end = html.IndexOf('>', Math.Max(cursor, index + 2));
        if (name.Length == 0 || end < 0)
        {
            AppendText(builder, html.AsSpan(index, html.Length - index));
            return html.Length;
        }

        if (HtmlSanitizerPolicy.AllowedTags.Contains(name) && open.Count > 0 && open[^1] == name)
        {
            builder.Append("</").Append(name).Append('>');
            open.RemoveAt(open.Count - 1);
        }
        else
        {
            AppendText(builder, html.AsSpan(index, end - index + 1));
        }

        return end + 1;
    }

    /// <summary>
    /// Drops a <see cref="HtmlSanitizerPolicy.DroppedWithContent"/> element. A void one (meta, link,
    /// base, embed) is just the tag; the others are dropped through their closing tag, and a missing
    /// closing tag drops the rest of the input (that is what a browser would run as raw text).
    /// </summary>
    private static int SkipDroppedElement(string html, StartTag tag)
    {
        if (HtmlSanitizerPolicy.VoidDroppedTags.Contains(tag.Name))
        {
            return tag.End + 1;
        }

        var closeStart = HtmlTagTokenizer.FindClosingTag(html, tag.Name, tag.End + 1);
        if (closeStart < 0)
        {
            return html.Length;
        }

        var closeEnd = html.IndexOf('>', closeStart);
        return closeEnd < 0 ? html.Length : closeEnd + 1;
    }

    private static bool TryGetAttribute(string html, StartTag tag, string name, out string value)
    {
        value = string.Empty;
        if (tag.Attributes is null)
        {
            return false;
        }

        foreach (var attribute in tag.Attributes)
        {
            if (!attribute.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = attribute.Value ?? string.Empty;
            return true;
        }

        return false;
    }

    /// <summary>One scanned start tag; positions are indices into the scanned markup.</summary>
    private readonly record struct StartTag(
        int Start,
        int End,
        string Name,
        List<(string Name, string? Value)>? Attributes,
        bool SelfClosing);

    /// <summary>
    /// Reads a start tag with the WHATWG tokenizer states (tag name → before attribute name → attribute
    /// name → before value → quoted/unquoted value). An unterminated tag — including an unterminated
    /// quoted value — returns <see langword="false"/> so the caller escapes the rest instead of guessing.
    /// </summary>
    private static bool TryReadStartTag(string html, int start, out StartTag tag)
    {
        tag = default;

        var index = start + 1;
        var nameStart = index;
        while (index < html.Length && IsTagNameChar(html[index]))
        {
            index++;
        }

        var name = html[nameStart..index].ToLowerInvariant();
        List<(string, string?)>? attributes = null;
        var selfClosing = false;

        while (true)
        {
            while (index < html.Length && char.IsWhiteSpace(html[index]))
            {
                index++;
            }

            if (index >= html.Length)
            {
                return false;
            }

            if (html[index] == '>')
            {
                index++;
                break;
            }

            if (html[index] == '/')
            {
                if (index + 1 < html.Length && html[index + 1] == '>')
                {
                    selfClosing = true;
                    index += 2;
                    break;
                }

                // A stray '/' that is not followed by '>': WHATWG treats it as a parse error, ignores it and
                // keeps reading attributes. Taking it as "self-closing" emptied the element and moved its
                // content outside it (<b/ >x used to render as <b></b>x instead of <b>x</b>).
                index++;
                continue;
            }

            var attributeNameStart = index;
            while (index < html.Length &&
                   !char.IsWhiteSpace(html[index]) &&
                   html[index] is not ('/' or '>' or '='))
            {
                index++;
            }

            if (index == attributeNameStart)
            {
                index++;
                continue;
            }

            var attributeName = html[attributeNameStart..index];
            while (index < html.Length && char.IsWhiteSpace(html[index]))
            {
                index++;
            }

            if (index >= html.Length)
            {
                return false;
            }

            if (html[index] != '=')
            {
                (attributes ??= []).Add((attributeName, null));
                continue;
            }

            index++;
            while (index < html.Length && char.IsWhiteSpace(html[index]))
            {
                index++;
            }

            if (index >= html.Length)
            {
                return false;
            }

            if (html[index] is '"' or '\'')
            {
                var quote = html[index];
                var valueStart = index + 1;
                var close = html.IndexOf(quote, valueStart);
                if (close < 0)
                {
                    return false;
                }

                (attributes ??= []).Add((attributeName, html[valueStart..close]));
                index = close + 1;
                continue;
            }

            var unquotedStart = index;
            while (index < html.Length && !char.IsWhiteSpace(html[index]) && html[index] != '>')
            {
                index++;
            }

            (attributes ??= []).Add((attributeName, html[unquotedStart..index]));
        }

        tag = new StartTag(start, index - 1, name, attributes, selfClosing);
        return true;
    }

    private static bool IsTagNameChar(char c)
        => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.';
}
