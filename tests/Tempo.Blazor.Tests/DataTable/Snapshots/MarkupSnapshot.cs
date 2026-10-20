using System.Globalization;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.Tests.DataTable.Snapshots;

/// <summary>
/// Normalizes rendered markup so golden snapshots stay stable across renders.
/// Only three transforms are applied: <c>blazor:*</c> attributes are removed,
/// instance-specific ids (GUIDs and <c>tm-*-{hex}</c>) are replaced in first-seen
/// order, and whitespace between tags and inside <c>class</c> attributes is collapsed.
/// </summary>
public static partial class MarkupSnapshot
{
    /// <summary>Normalizes <paramref name="markup"/> for golden comparison.</summary>
    public static string Normalize(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);

        var withoutBlazor = BlazorAttribute().Replace(markup, string.Empty);
        var withStableIds = ReplaceInstanceIds(withoutBlazor);
        var collapsed = CollapseInterTagWhitespace(withStableIds);
        return ClassAttribute().Replace(collapsed, NormalizeClassAttribute);
    }

    private static string ReplaceInstanceIds(string markup)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var next = 1;

        return InstanceId().Replace(markup, match =>
        {
            var raw = match.Value;
            if (!seen.TryGetValue(raw, out var replacement))
            {
                replacement = "ID" + next.ToString(CultureInfo.InvariantCulture);
                seen[raw] = replacement;
                next++;
            }

            return replacement;
        });
    }

    private static string CollapseInterTagWhitespace(string markup)
        => InterTagWhitespace().Replace(markup, match =>
            match.Value.IndexOf('\n', StringComparison.Ordinal) >= 0 ? "\n" : " ");

    private static string NormalizeClassAttribute(Match match)
    {
        var classes = WhitespaceRun().Split(match.Groups[1].Value.Trim())
            .Where(part => part.Length > 0);
        return "class=\"" + string.Join(' ', classes) + "\"";
    }

    // blazor:event-name, quoted or bare — bUnit emits both forms on elements that carry a handler.
    [GeneratedRegex(@"\s+blazor:[A-Za-z0-9_:-]+(?:=\""[^""]*""|=[^\s>]*)?", RegexOptions.CultureInvariant)]
    private static partial Regex BlazorAttribute();

    // A GUID, or a library-generated id such as view-manager-toggle-a1b2c3 / tm-overlay-panel-a1b2c3.
    [GeneratedRegex(@"\b(?:[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|(?:[A-Za-z]+-)+[0-9a-fA-F]{6,})\b", RegexOptions.CultureInvariant)]
    private static partial Regex InstanceId();

    [GeneratedRegex(@">\s+<", RegexOptions.CultureInvariant)]
    private static partial Regex InterTagWhitespace();

    [GeneratedRegex(@"class=""([^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex ClassAttribute();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();
}
