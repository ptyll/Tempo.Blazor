using System.Globalization;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.Tests.DataTable.Snapshots;

/// <summary>
/// Normalizes rendered markup so golden snapshots stay stable across renders.
/// Only four transforms are applied: <c>blazor:*</c> attributes are removed, void elements
/// are written in one canonical form (<see cref="NormalizeVoidElements"/>),
/// instance-specific ids (GUIDs and <c>tm-*-{hex}</c>) are replaced in first-seen
/// order, and whitespace between tags and inside <c>class</c> attributes is collapsed.
/// </summary>
public static partial class MarkupSnapshot
{
    /// <summary>Normalizes <paramref name="markup"/> for golden comparison.</summary>
    public static string Normalize(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);

        // ICU-based globalization stacks (Linux) render the en-US AM/PM designator after U+202F
        // where the goldens, captured under Windows NLS, carry a plain U+0020 (U+00A0 likewise).
        // Collapse the space variants onto U+0020 so goldens stay stable across platforms.
        var withoutBlazor = BlazorAttribute().Replace(markup
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace('\u202F', ' ')
            .Replace('\u00A0', ' '), string.Empty);
        var withStableIds = ReplaceInstanceIds(NormalizeVoidElements(withoutBlazor));
        var collapsed = CollapseInterTagWhitespace(withStableIds);
        return ClassAttribute().Replace(collapsed, NormalizeClassAttribute);
    }

    /// <summary>
    /// Writes every HTML void element (<c>input</c>, <c>br</c>, <c>img</c>, ...) as <c>&lt;tag attrs /&gt;</c>.
    /// The goldens were captured with a serializer that self-closes void elements; bUnit 2.x
    /// (AngleSharp) emits <c>&lt;input ...&gt;&lt;/input&gt;</c> for the same render tree. The two spellings
    /// describe the identical DOM, so the comparison must not depend on the serializer version.
    /// </summary>
    public static string NormalizeVoidElements(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);

        var selfClosed = VoidElementWithEndTag().Replace(markup, "$1 />");
        return VoidElementOpenTag().Replace(selfClosed, "$1 />");
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

    // <input ...></input> (bUnit 2.x / AngleSharp) -> <input ... />
    [GeneratedRegex(@"(<(?:area|base|br|col|embed|hr|img|input|link|meta|source|track|wbr)(?=[\s/>])(?:[^>""']|""[^""]*""|'[^']*')*?)\s*/?>\s*</(?:area|base|br|col|embed|hr|img|input|link|meta|source|track|wbr)>", RegexOptions.CultureInvariant)]
    private static partial Regex VoidElementWithEndTag();

    // <input ...> or <input ... /> -> <input ... /> (no end tag follows any more)
    [GeneratedRegex(@"(<(?:area|base|br|col|embed|hr|img|input|link|meta|source|track|wbr)(?=[\s/>])(?:[^>""']|""[^""]*""|'[^']*')*?)\s*/?>", RegexOptions.CultureInvariant)]
    private static partial Regex VoidElementOpenTag();

    [GeneratedRegex(@">\s+<", RegexOptions.CultureInvariant)]
    private static partial Regex InterTagWhitespace();

    [GeneratedRegex(@"class=""([^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex ClassAttribute();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();
}
