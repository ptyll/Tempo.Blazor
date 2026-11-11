using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Raw-SVG sanitizing helpers of <see cref="StencilPackRenderer"/>: user- or pack-supplied markup
/// (raw <c>svg</c> nodes, icon registry content, data URLs) is parsed as XML with DTD processing
/// prohibited, restricted to the allow-listed SVG elements/attributes, stripped of event handlers
/// and javascript: payloads, and re-emitted as an escaped markup string.
/// </summary>
internal static partial class StencilPackRenderer
{
    private static readonly HashSet<string> AllowedRawSvgElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "svg",
        "g",
        "path",
        "rect",
        "circle",
        "ellipse",
        "line",
        "polyline",
        "polygon",
        "text",
        "tspan",
        "defs",
        "linearGradient",
        "radialGradient",
        "stop",
        "clipPath",
        "mask"
    };

    private static readonly HashSet<string> AllowedRawSvgAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "class",
        "d",
        "points",
        "x",
        "y",
        "x1",
        "y1",
        "x2",
        "y2",
        "cx",
        "cy",
        "r",
        "rx",
        "ry",
        "width",
        "height",
        "viewBox",
        "fill",
        "fill-opacity",
        "stroke",
        "stroke-width",
        "stroke-linecap",
        "stroke-linejoin",
        "stroke-dasharray",
        "stroke-opacity",
        "opacity",
        "transform",
        "font-size",
        "font-family",
        "font-weight",
        "text-anchor",
        "dominant-baseline",
        "dx",
        "dy",
        "offset",
        "stop-color",
        "stop-opacity",
        "clip-path",
        "mask"
    };

    private static bool IsSafeDataUrl(string value)
        => value.TrimStart().StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
           && !ContainsDangerousMarkup(value);

    private static string SafeAttribute(string value, string fallback)
        => ContainsDangerousMarkup(value) ? fallback : EscapeAttribute(value);

    private static string EscapeAttribute(string value)
        => WireframeSvg.Escape(value).Replace("'", "&apos;", StringComparison.Ordinal);

    private static string SanitizeRawSvg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        XDocument document;
        try
        {
            using var reader = XmlReader.Create(
                new StringReader("<root>" + value + "</root>"),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return string.Empty;
        }

        var root = document.Root;
        if (root is null)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var node in root.Nodes())
            AppendSanitizedNode(node, builder);

        var sanitized = builder.ToString();
        return ContainsDangerousMarkup(sanitized) ? string.Empty : sanitized;
    }

    internal static bool ContainsDangerousMarkup(string value)
        => ScriptTagRegex().IsMatch(value)
           || ForeignObjectTagRegex().IsMatch(value)
           || JavascriptUrlRegex().IsMatch(value)
           || EventAttributeRegex().IsMatch(value);

    private static void AppendSanitizedNode(XNode node, StringBuilder builder)
    {
        switch (node)
        {
            case XElement element:
                AppendSanitizedElement(element, builder);
                break;
            case XText text:
                builder.Append(WireframeSvg.Escape(text.Value));
                break;
        }
    }

    private static void AppendSanitizedElement(XElement element, StringBuilder builder)
    {
        var localName = element.Name.LocalName;
        if (!AllowedRawSvgElements.Contains(localName))
            return;

        builder.Append('<').Append(localName);
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            var attributeName = attribute.Name.LocalName;
            var attributeValue = attribute.Value;
            if (!AllowedRawSvgAttributes.Contains(attributeName)
                || ContainsDangerousMarkup(attributeName)
                || ContainsDangerousMarkup(attributeValue))
            {
                continue;
            }

            builder
                .Append(' ')
                .Append(attributeName)
                .Append("='")
                .Append(EscapeAttribute(attributeValue))
                .Append('\'');
        }

        builder.Append('>');
        foreach (var child in element.Nodes())
            AppendSanitizedNode(child, builder);
        builder.Append("</").Append(localName).Append('>');
    }

    [GeneratedRegex(@"<\s*/?\s*script\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex(@"<\s*/?\s*foreignObject\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForeignObjectTagRegex();

    [GeneratedRegex(@"(^|[^A-Za-z0-9_:-])on[a-zA-Z][\w:-]*\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventAttributeRegex();

    [GeneratedRegex(@"javascript\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavascriptUrlRegex();
}
