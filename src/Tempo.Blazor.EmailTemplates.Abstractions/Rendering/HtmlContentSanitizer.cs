using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Parser;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Sanitizes the inline HTML allowed inside text-bearing blocks: only a small safe whitelist of tags
/// and attributes survives; scripts/styles are dropped with their content, event handlers and unsafe
/// URL schemes are stripped. The link allow-list is <see cref="UrlSchemePolicy"/> (the same
/// <c>http</c>/<c>https</c>/<c>mailto</c>/<c>tel</c> + relative rules the render pipeline applies, D22).
/// Built on AngleSharp's HTML parser so it is robust against evasion.
/// </summary>
/// <remarks>
/// The result is serialized with <see cref="QuoteSafeMarkupFormatter"/>, which also encodes
/// <c>"</c> and <c>'</c> in text nodes. Without it a sanitized value inserted into an *attribute*
/// (<c>title="{{ x | html.trusted }}"</c>) could close the attribute and add an event handler — the
/// sanitizer removes dangerous markup, the formatter makes the remaining text safe in every position.
/// </remarks>
public static partial class HtmlContentSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
        { "b", "i", "u", "strong", "em", "a", "br", "span", "p", "ul", "ol", "li" };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
        { "href", "style", "target", "rel" };

    private static readonly HashSet<string> DroppedWithContent = new(StringComparer.OrdinalIgnoreCase)
        { "script", "style", "iframe", "object", "embed", "link", "meta", "base", "title" };

    private static readonly HtmlParser Parser = new();

    /// <summary>
    /// Sanitizes the authored content of a text-bearing block (<c>mj-text</c>, <c>mj-accordion-text</c>)
    /// that may carry Scriban code. The plain <see cref="Sanitize"/> pass would entity-encode the quotes,
    /// <c>&gt;</c> and <c>&amp;</c> *inside* a code span and destroy the template
    /// (<c>{{ price | math.format "N2" }}</c> → "Invalid token found &amp;"), so each <c>{{ … }}</c> span
    /// is swapped for an inert placeholder, the rest is sanitized, and the spans are restored verbatim.
    /// </summary>
    /// <remarks>
    /// This is safe because of the layering: the *output* of an expression span is HTML-escaped by the
    /// engine and a <c>html.trusted</c> value is sanitized, while everything outside a span stays in the
    /// sanitizer's hands and is rebuilt into the compiled markup under <c>MarkupGuard</c>. A
    /// <c>{% … %}</c> block tag is restored only when it cannot carry markup itself, and a content that
    /// already contains the placeholder characters or a suspicious <c>{%{</c> construct falls back to the
    /// plain sanitizer (fail closed).
    /// </remarks>
    public static string SanitizeTemplateContent(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        if (content.Contains(PlaceholderStart, StringComparison.Ordinal) ||
            content.Contains(PlaceholderEnd, StringComparison.Ordinal) ||
            EscapeBlockPattern().IsMatch(content))
        {
            return Sanitize(content);
        }

        var nonce = Guid.NewGuid().ToString("N");
        var spans = new List<string>();
        var masked = CodeSpanPattern().Replace(content, match =>
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

        var sanitized = Sanitize(masked);
        if (spans.Count == 0)
        {
            return sanitized;
        }

        return PlaceholderPattern().Replace(sanitized, match =>
            string.Equals(match.Groups["nonce"].Value, nonce, StringComparison.Ordinal) &&
            int.TryParse(match.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
            index < spans.Count
                ? spans[index]
                : string.Empty);
    }

    /// <summary>The private-use character that opens a masked span placeholder.</summary>
    private const char PlaceholderStart = '\uE000';

    /// <summary>The private-use character that closes a masked span placeholder.</summary>
    private const char PlaceholderEnd = '\uE001';

    private static bool ContainsMarkup(string value)
        => value.Contains('<', StringComparison.Ordinal) ||
           value.Contains('>', StringComparison.Ordinal) ||
           value.Contains('&', StringComparison.Ordinal);

    /// <summary>A Scriban expression span (<c>{{ … }}</c>) or block tag (<c>{% … %}</c>), non-greedy.</summary>
    [GeneratedRegex(@"\{\{.*?\}\}|\{%.*?%\}", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodeSpanPattern();

    /// <summary>A placeholder emitted by <see cref="SanitizeTemplateContent"/>.</summary>
    [GeneratedRegex(@"[\uE000](?<nonce>[0-9a-f]{32})_(?<index>[0-9]+)[\uE001]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlaceholderPattern();

    /// <summary>
    /// A construct whose masking would be guesswork (<c>{%{</c>): such a content goes through the plain
    /// sanitizer, which fails closed by encoding it.
    /// </summary>
    [GeneratedRegex(@"\{%+\{", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EscapeBlockPattern();

    /// <summary>Returns a sanitized copy of <paramref name="html"/> safe to embed in generated markup.</summary>
    public static string Sanitize(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;

        var document = Parser.ParseDocument("<!DOCTYPE html><html><body></body></html>");
        document.Body!.InnerHtml = html;
        SanitizeChildren(document.Body);

        var builder = new StringBuilder();
        foreach (var node in document.Body.ChildNodes)
        {
            builder.Append(node.ToHtml(QuoteSafeMarkupFormatter.Instance));
        }

        return builder.ToString();
    }

    private static void SanitizeChildren(INode parent)
    {
        foreach (var node in parent.ChildNodes.ToArray())
        {
            switch (node)
            {
                case IComment:
                    node.RemoveFromParent();
                    break;
                case IElement element:
                    SanitizeElement(parent, element);
                    break;
            }
        }
    }

    private static void SanitizeElement(INode parent, IElement element)
    {
        if (DroppedWithContent.Contains(element.LocalName))
        {
            element.Remove();
            return;
        }

        foreach (var attribute in element.Attributes.ToArray())
        {
            if (!IsAttributeAllowed(element, attribute))
                element.RemoveAttribute(attribute.NamespaceUri, attribute.Name);
        }

        SanitizeChildren(element);

        if (!AllowedTags.Contains(element.LocalName))
        {
            // Unwrap unknown formatting tags: keep their (already-sanitized) children.
            while (element.FirstChild is { } child)
                parent.InsertBefore(child, element);
            element.Remove();
        }
    }

    private static bool IsAttributeAllowed(IElement element, IAttr attribute)
    {
        var name = attribute.Name;
        if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase)) return false;
        if (!AllowedAttributes.Contains(name)) return false;

        if (name.Equals("href", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("src", StringComparison.OrdinalIgnoreCase))
            return UrlSchemePolicy.IsAllowed(attribute.Value);

        if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
            return IsSafeStyle(attribute.Value);

        return true;
    }

    private static bool IsSafeStyle(string value)
    {
        var lowered = value.ToLowerInvariant();
        return !lowered.Contains("javascript:", StringComparison.Ordinal) &&
            !lowered.Contains("expression(", StringComparison.Ordinal) &&
            !lowered.Contains("url(", StringComparison.Ordinal);
    }
}

/// <summary>
/// Serializes sanitized content so it is safe in a text node *and* when the whole value lands inside a
/// quoted attribute: <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> and <c>'</c> are always encoded
/// (in text nodes and in attribute values alike).
/// </summary>
internal sealed class QuoteSafeMarkupFormatter : HtmlMarkupFormatter
{
    public static readonly QuoteSafeMarkupFormatter Instance = new();

    private QuoteSafeMarkupFormatter()
    {
    }

    /// <inheritdoc />
    public override string Text(ICharacterData text) => HtmlTextEscaper.Encode(text.Data);

    /// <inheritdoc />
    protected override string Attribute(IAttr attribute)
        => string.Concat(attribute.Name, "=\"", HtmlTextEscaper.Encode(attribute.Value), "\"");
}
