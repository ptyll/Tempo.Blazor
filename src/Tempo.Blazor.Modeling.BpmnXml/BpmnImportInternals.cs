using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>Deterministic helpers shared by the importer internals.</summary>
internal static class BpmnImportInternals
{
    /// <summary>
    /// Stable generated id for XML elements that carry no <c>id</c> attribute:
    /// <c>{type}_{fnv1a-hash of the element path}</c> — same input, same id, across runs.
    /// </summary>
    public static string StableId(string semanticType, string xmlPath)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offsetBasis;
        foreach (var ch in xmlPath)
        {
            hash ^= ch;
            hash *= prime;
        }

        return semanticType + "_" + hash.ToString("x16");
    }

    /// <summary>
    /// Builds an XPath-like path of local names that unambiguously locates the element:
    /// segments carry an <c>[@id='…']</c> predicate when the element has an id, otherwise a
    /// 1-based <c>[n]</c> positional predicate when same-named siblings exist. Deterministic
    /// for a given document (mirrors the corpus guard).
    /// </summary>
    public static string BuildPath(XElement element)
    {
        var parts = new List<string>();
        var current = element;
        while (current is not null)
        {
            var segment = current.Name.LocalName;
            if (current.Attribute("id") is { } id)
            {
                segment += $"[@id='{id.Value}']";
            }
            else
            {
                var position = 1 + current.ElementsBeforeSelf().Count(sibling => sibling.Name == current.Name);
                if (position > 1 || current.ElementsAfterSelf().Any(sibling => sibling.Name == current.Name))
                {
                    segment += "[" + position.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
                }
            }

            parts.Add(segment);
            current = current.Parent;
        }

        parts.Reverse();
        return "/" + string.Join("/", parts);
    }

    /// <summary>Strips a QName prefix (<c>tns:Task_1</c> → <c>Task_1</c>) from a reference value.</summary>
    public static string StripQNamePrefix(string value)
    {
        var colonIndex = value.LastIndexOf(':');
        return colonIndex >= 0 ? value[(colonIndex + 1)..] : value;
    }

    public static bool IsLocal(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    public static IEnumerable<XElement> Children(XElement element, string localName) =>
        element.Elements().Where(child => IsLocal(child, localName));

    public static XElement? Child(XElement element, string localName) =>
        element.Elements().FirstOrDefault(child => IsLocal(child, localName));

    /// <summary>Direct text content of an element (used for textAnnotation bodies and expression text); a null element maps to an empty string.</summary>
    public static string DirectText(XElement? element) =>
        element is null
            ? string.Empty
            : string.Concat(element.Nodes().OfType<XText>().Select(text => text.Value)).Trim();

    public static JsonElement JsonElementFrom<TValue>(TValue value) => JsonSerializer.SerializeToElement(value);

    public static JsonElement StringElement(string? value) => JsonSerializer.SerializeToElement(value);
}
