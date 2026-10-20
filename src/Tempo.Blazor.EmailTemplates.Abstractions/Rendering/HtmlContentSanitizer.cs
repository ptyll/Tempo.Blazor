using System.Text;
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
public static class HtmlContentSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
        { "b", "i", "u", "strong", "em", "a", "br", "span", "p", "ul", "ol", "li" };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
        { "href", "style", "target", "rel" };

    private static readonly HashSet<string> DroppedWithContent = new(StringComparer.OrdinalIgnoreCase)
        { "script", "style", "iframe", "object", "embed", "link", "meta", "base", "title" };

    private static readonly HtmlParser Parser = new();

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
