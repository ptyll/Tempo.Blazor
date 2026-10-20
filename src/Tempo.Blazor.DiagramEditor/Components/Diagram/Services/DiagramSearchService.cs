using System.Text.RegularExpressions;
using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram.Services;

/// <summary>
/// Searches a diagram document for nodes and edges matching a free-text query.
/// </summary>
public static class DiagramSearchService
{
    /// <summary>
    /// Searches the active page of <paramref name="document"/> for nodes and edges whose
    /// <see cref="DiagramNode.Id"/>, <see cref="DiagramNode.StencilId"/>,
    /// <c>Data["label"]</c>, <c>Data["name"]</c> or any other data value contains
    /// <paramref name="query"/> (case-insensitive).
    /// </summary>
    public static List<DiagramSearchResult> Search(DiagramDocument? document, string? query)
        => Search(document, query, useRegex: false);

    /// <summary>
    /// Searches the active page of <paramref name="document"/> for nodes and edges matching
    /// <paramref name="query"/> using plain-text or regex search.
    /// </summary>
    public static List<DiagramSearchResult> Search(DiagramDocument? document, string? query, bool useRegex)
        => SearchPage(document?.ActivePage, query, useRegex);

    /// <summary>
    /// Searches all pages of <paramref name="document"/>.
    /// </summary>
    public static List<DiagramSearchResult> SearchAllPages(DiagramDocument? document, string? query)
        => SearchAllPages(document, query, useRegex: false);

    /// <summary>
    /// Searches all pages of <paramref name="document"/> using plain-text or regex search.
    /// </summary>
    public static List<DiagramSearchResult> SearchAllPages(DiagramDocument? document, string? query, bool useRegex)
    {
        var results = new List<DiagramSearchResult>();
        if (document?.Pages is null || string.IsNullOrWhiteSpace(query))
            return results;

        var term = query.Trim();
        for (int pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
        {
            var page = document.Pages[pageIndex];
            var pageResults = SearchPage(page, term, useRegex);
            foreach (var r in pageResults)
            {
                r.PageIndex = pageIndex;
            }
            results.AddRange(pageResults);
        }
        return results;
    }

    /// <summary>
    /// Attempts to compile <paramref name="pattern"/> into a case-insensitive regex.
    /// </summary>
    public static bool TryCreateRegex(string pattern, out Regex? regex, out string? error)
    {
        regex = null;
        error = null;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = "Pattern is empty.";
            return false;
        }

        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (RegexParseException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Applies a regex replacement to the field identified by <paramref name="result"/>.
    /// Returns information about what changed, or <c>null</c> if the field is not replaceable.
    /// </summary>
    public static DiagramSearchReplaceResult? ReplaceInResult(
        DiagramDocument document,
        DiagramSearchResult result,
        Regex regex,
        string replacement)
    {
        if (result.MatchType is DiagramSearchMatchType.Id or DiagramSearchMatchType.StencilId)
            return null;

        var page = result.PageIndex.HasValue
            ? document.Pages[result.PageIndex.Value]
            : document.ActivePage;

        if (page is null)
            return null;

        if (result.NodeId is not null)
        {
            var node = page.Nodes.FirstOrDefault(n => n.Id == result.NodeId);
            if (node is null)
                return null;

            string? dataKey = result.DataKey;
            if (result.MatchType == DiagramSearchMatchType.Label && dataKey is not null)
            {
                var oldValue = node.Data.TryGetValue(dataKey, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
                var newValue = regex.Replace(oldValue, replacement);
                if (oldValue == newValue)
                    return null;
                return new DiagramSearchReplaceResult(node.Id, dataKey, oldValue, newValue);
            }

            if (result.MatchType == DiagramSearchMatchType.Data && dataKey is not null)
            {
                var oldValue = node.Data.TryGetValue(dataKey, out var v2) ? v2?.ToString() ?? string.Empty : string.Empty;
                var newValue = regex.Replace(oldValue, replacement);
                if (oldValue == newValue)
                    return null;
                return new DiagramSearchReplaceResult(node.Id, dataKey, oldValue, newValue);
            }

            return null;
        }

        if (result.EdgeId is not null)
        {
            var edge = page.Edges.FirstOrDefault(e => e.Id == result.EdgeId);
            if (edge is null)
                return null;

            if (result.MatchType == DiagramSearchMatchType.Label && result.DataKey == "Label")
            {
                var oldValue = edge.Label ?? string.Empty;
                var newValue = regex.Replace(oldValue, replacement);
                if (oldValue == newValue)
                    return null;
                return new DiagramSearchReplaceResult(edge.Id, "Label", oldValue, newValue);
            }

            return null;
        }

        return null;
    }

    private static List<DiagramSearchResult> SearchPage(DiagramPage? page, string? query, bool useRegex)
    {
        var results = new List<DiagramSearchResult>();
        if (page is null || string.IsNullOrWhiteSpace(query))
            return results;

        var term = query.Trim();
        var regex = useRegex && TryCreateRegex(term, out var r, out _) ? r : null;

        foreach (var node in page.Nodes)
        {
            var nodeResults = SearchNode(node, term, regex);
            results.AddRange(nodeResults);
        }

        foreach (var edge in page.Edges)
        {
            var edgeResults = SearchEdge(edge, term, regex);
            results.AddRange(edgeResults);
        }

        return results;
    }

    private static IEnumerable<DiagramSearchResult> SearchNode(DiagramNode node, string term, Regex? regex)
    {
        if (Matches(node.Id, term, regex))
        {
            yield return new DiagramSearchResult
            {
                NodeId = node.Id,
                MatchType = DiagramSearchMatchType.Id,
                MatchedText = node.Id,
            };
            yield break;
        }

        if (Matches(node.StencilId, term, regex))
        {
            yield return new DiagramSearchResult
            {
                NodeId = node.Id,
                MatchType = DiagramSearchMatchType.StencilId,
                MatchedText = node.StencilId,
            };
            yield break;
        }

        // Label-like data keys — the same priority order the connect dialog title uses
        // (DiagramNodeText); "text" intentionally stays a generic data match below.
        foreach (var key in DiagramNodeText.SearchLabelKeys)
        {
            var value = node.Data.TryGetValue(key, out var v) ? v?.ToString() : null;
            if (Matches(value, term, regex))
            {
                yield return new DiagramSearchResult
                {
                    NodeId = node.Id,
                    MatchType = DiagramSearchMatchType.Label,
                    MatchedText = value ?? string.Empty,
                    DataKey = key,
                };
                yield break;
            }
        }

        // Any data value
        foreach (var kvp in node.Data)
        {
            var value = kvp.Value?.ToString();
            if (Matches(value, term, regex))
            {
                yield return new DiagramSearchResult
                {
                    NodeId = node.Id,
                    MatchType = DiagramSearchMatchType.Data,
                    MatchedText = value ?? string.Empty,
                    DataKey = kvp.Key,
                };
                yield break;
            }
        }
    }

    private static IEnumerable<DiagramSearchResult> SearchEdge(DiagramEdge edge, string term, Regex? regex)
    {
        if (Matches(edge.Id, term, regex))
        {
            yield return new DiagramSearchResult
            {
                EdgeId = edge.Id,
                MatchType = DiagramSearchMatchType.Id,
                MatchedText = edge.Id,
            };
            yield break;
        }

        if (Matches(edge.Label, term, regex))
        {
            yield return new DiagramSearchResult
            {
                EdgeId = edge.Id,
                MatchType = DiagramSearchMatchType.Label,
                MatchedText = edge.Label ?? string.Empty,
                DataKey = "Label",
            };
            yield break;
        }

        if (Matches(edge.ConnectorType, term, regex))
        {
            yield return new DiagramSearchResult
            {
                EdgeId = edge.Id,
                MatchType = DiagramSearchMatchType.Data,
                MatchedText = edge.ConnectorType ?? string.Empty,
            };
            yield break;
        }
    }

    private static bool Matches(string? text, string term, Regex? regex)
    {
        if (text is null)
            return false;

        if (regex is not null)
            return regex.IsMatch(text);

        return text.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Describes a replaceable change identified by a search result.</summary>
public sealed class DiagramSearchReplaceResult
{
    public DiagramSearchReplaceResult(string targetId, string dataKey, string oldValue, string newValue)
    {
        TargetId = targetId;
        DataKey = dataKey;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <summary>Node or edge identifier.</summary>
    public string TargetId { get; }

    /// <summary>Dictionary key or "Label" for edge labels.</summary>
    public string DataKey { get; }

    /// <summary>Original field value.</summary>
    public string OldValue { get; }

    /// <summary>Replaced field value.</summary>
    public string NewValue { get; }
}
