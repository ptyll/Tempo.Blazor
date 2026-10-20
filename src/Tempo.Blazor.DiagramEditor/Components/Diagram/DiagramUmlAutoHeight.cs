using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>Content-driven height of UML stencils (header + one row per list item).</summary>
internal static class DiagramUmlAutoHeight
{
    private const double Grid = 10;

    /// <summary>
    /// The height the stencil's content needs (header + list sections), or <see langword="null"/>
    /// when there is nothing to fit — a non-UML stencil, or a UML stencil whose data carries no
    /// list section (attributes/methods/values): the stencil default height IS the floor, so a
    /// content-less stencil keeps whatever height it already has. Shared by <see cref="Fit"/> and
    /// <see cref="Compute"/> so every growth path measures the same content.
    /// </summary>
    internal static double? ContentHeight(string? stencilId, Dictionary<string, object> data)
    {
        var id = stencilId?.ToLowerInvariant() ?? "";
        if (!id.StartsWith("uml.")) return null;

        double headerHeight = id switch
        {
            "uml.abstract-class" or "uml.component" => 38, // stereotype + name
            _ => 30 // name only
        };

        double content = headerHeight;
        bool hasListSection = false;

        foreach (var key in new[] { "attributes", "methods", "values" })
        {
            if (!data.TryGetValue(key, out var listValue)) continue;
            var count = CountListItems(listValue);
            hasListSection = true;
            content += 1; // divider
            content += 16 + Math.Max(1, count) * 18; // padding + rows (min 1 row for empty section)
        }

        return hasListSection ? content : null;
    }

    /// <summary>
    /// The height a property edit should leave the node at: the content-driven need rounded up to
    /// the 10-unit grid, never below the node's current (default) height — the same grow-only,
    /// no-minimum-floor logic <see cref="Fit"/> uses. Non-UML nodes and content-less stencils
    /// return their <paramref name="node"/>'s current height (no resize).
    /// </summary>
    internal static double Compute(DiagramNode node, Dictionary<string, object> data)
    {
        var content = ContentHeight(node.StencilId, data);
        if (content is null) return node.H;

        var needed = Math.Ceiling(content.Value / Grid) * Grid;
        return Math.Max(node.H, needed);
    }

    /// <summary>
    /// Grows (never shrinks) a UML node to its list content, rounded up to the 10-unit grid.
    /// Content-driven only: a stencil whose data carries no list section (attributes/methods/values)
    /// keeps its stencil default height — there is nothing to fit — and there is no minimum-height
    /// floor beyond that default (review fix): the stencil default IS the floor.
    /// </summary>
    internal static void Fit(DiagramNode node)
    {
        var content = ContentHeight(node.StencilId, node.Data);
        if (content is null) return;

        var needed = Math.Ceiling(content.Value / Grid) * Grid;
        if (needed > node.H) node.H = needed;
    }

    /// <summary>Applies <see cref="Fit"/> to every node on every page (template creation).</summary>
    internal static void FitAll(DiagramDocument doc)
    {
        doc.EnsurePages();
        foreach (var page in doc.Pages)
            foreach (var node in page.Nodes)
                Fit(node);
    }

    private static int CountListItems(object? value)
    {
        if (value is null) return 0;
        if (value is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.Array)
                return je.EnumerateArray().Count(e => !string.IsNullOrWhiteSpace(e.ToString()));
            var s = je.ToString();
            return string.IsNullOrWhiteSpace(s) ? 0 : 1;
        }
        if (value is IEnumerable<string> strList)
            return strList.Count(s => !string.IsNullOrWhiteSpace(s));
        if (value is System.Collections.IEnumerable enumerable && value is not string)
            return enumerable.Cast<object>().Count(o => o is not null && !string.IsNullOrWhiteSpace(o.ToString()));
        if (value is string str)
            return str.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                      .Count(l => !string.IsNullOrWhiteSpace(l));
        return 1;
    }
}
