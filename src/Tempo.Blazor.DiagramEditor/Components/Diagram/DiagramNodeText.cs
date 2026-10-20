using System.Globalization;
using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Shared display-label extraction for diagram nodes: the connect dialog title
/// (<c>ConnectSourceLabel</c>) and the document search (<see cref="Services.DiagramSearchService"/>)
/// must agree on what a node's "text" is, so the key order and the truncation live here.
/// </summary>
internal static class DiagramNodeText
{
    /// <summary>Data keys that hold a node's own text, in priority order.</summary>
    internal static readonly string[] LabelKeys = ["label", "name", "text", "title"];

    /// <summary>
    /// The subset the document search reports as <c>Label</c> results. <c>text</c> stays a
    /// generic data match there (historic behaviour the replace feature relies on), while the
    /// display-label extraction above still prefers it over <c>title</c>.
    /// </summary>
    internal static readonly string[] SearchLabelKeys = ["label", "name", "title"];

    /// <summary>
    /// The node's own text — the first non-whitespace value among <see cref="LabelKeys"/>
    /// (string or JSON string element). Null when the node carries no text; callers fall back
    /// to the stencil's display name.
    /// </summary>
    internal static string? GetDisplayLabel(DiagramNode node)
    {
        foreach (var key in LabelKeys)
        {
            if (!node.Data.TryGetValue(key, out var value))
                continue;
            var text = AsText(value)?.Trim();
            if (!string.IsNullOrEmpty(text))
                return text;
        }
        return null;

        static string? AsText(object? value) => value switch
        {
            string s => s,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } je => je.GetString(),
            _ => null
        };
    }

    /// <summary>
    /// Truncates to <paramref name="maxTextElements"/> Unicode text elements (grapheme-safe —
    /// surrogate pairs and combining sequences are never split), appending "…" when cut.
    /// </summary>
    internal static string Truncate(string text, int maxTextElements = 40)
    {
        var info = new StringInfo(text);
        if (info.LengthInTextElements <= maxTextElements)
            return text;
        return info.SubstringByTextElements(0, maxTextElements - 1) + "…";
    }
}
