using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Mcp.Wireframe;

/// <summary>One placed component in an implementation-brief region.</summary>
public sealed record BriefElement(
    string Id, string Type, string? Role, double X, double Y, double W, double H, string? Name = null);

/// <summary>A layout region (header / sidebar / content / footer) derived from element geometry.</summary>
public sealed record BriefRegion(string Kind, IReadOnlyList<BriefElement> Elements);

/// <summary>
/// A component type and how many times it is used. <paramref name="Names"/> lists the distinct
/// element names (ordinal order) of the placed elements, or <c>null</c> when none is named.
/// </summary>
public sealed record BriefComponent(string Type, string? Role, int Count, IReadOnlyList<string>? Names = null);

/// <summary>A navigation flow between two elements, derived from a connector.</summary>
public sealed record BriefFlow(
    string FromId,
    string? FromType,
    string? FromRole,
    string ToId,
    string? ToType,
    string? ToRole,
    string? Label,
    string? FromName = null,
    string? ToName = null);

/// <summary>One page rendered as an implementation section.</summary>
public sealed record BriefPage(
    string Name, double Width, double Height,
    IReadOnlyList<BriefRegion> Regions,
    IReadOnlyList<BriefComponent> Components,
    IReadOnlyList<BriefFlow> Flows);

/// <summary>The full implementation brief for a wireframe document.</summary>
public sealed record WireframeBrief(
    string Title,
    IReadOnlyList<BriefPage> Pages,
    IReadOnlyList<BriefComponent> ComponentsUsed);

/// <summary>
/// Deterministically transforms a wireframe into an implementation-oriented brief: pages become
/// sections, geometry becomes layout regions, connectors become navigation flows, and component
/// usage is summarised — the artifact a plan/use-case consumes to build the real page.
/// </summary>
public static class WireframeImplementationBrief
{
    public static WireframeBrief Build(WireframeDocument document)
    {
        var pages = document.Pages.Select(BuildPage).ToList();

        var componentsUsed = document.Pages
            .SelectMany(p => p.Elements)
            .GroupBy(e => (e.Type, e.Role), BriefComponentKeyComparer.Instance)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Type, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Role, StringComparer.Ordinal)
            .Select(g => new BriefComponent(g.Key.Type, g.Key.Role, g.Count(), DistinctNames(g)))
            .ToList();

        return new WireframeBrief(document.Title, pages, componentsUsed);
    }

    private static BriefPage BuildPage(WireframePage page)
    {
        var byRegion = page.Elements
            .GroupBy(e => Classify(e, page.Width, page.Height));

        // Fixed, readable region order.
        var order = new[] { "header", "sidebar", "content", "footer" };
        var regions = order
            .Select(kind => (kind, els: byRegion.FirstOrDefault(g => g.Key == kind)))
            .Where(x => x.els is not null)
            .Select(x => new BriefRegion(x.kind,
                x.els!.OrderBy(e => e.Y).ThenBy(e => e.X)
                    .Select(e => new BriefElement(e.Id, e.Type, e.Role, e.X, e.Y, e.W, e.H, e.Name)).ToList()))
            .ToList();

        var components = page.Elements
            .GroupBy(e => (e.Type, e.Role), BriefComponentKeyComparer.Instance)
            .OrderBy(g => g.Key.Type, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Role, StringComparer.Ordinal)
            .Select(g => new BriefComponent(g.Key.Type, g.Key.Role, g.Count(), DistinctNames(g)))
            .ToList();

        var elementById = page.Elements.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var flows = page.Connectors.Select(c => new BriefFlow(
            c.FromId, elementById.GetValueOrDefault(c.FromId)?.Type, elementById.GetValueOrDefault(c.FromId)?.Role,
            c.ToId, elementById.GetValueOrDefault(c.ToId)?.Type, elementById.GetValueOrDefault(c.ToId)?.Role,
            c.Label,
            elementById.GetValueOrDefault(c.FromId)?.Name,
            elementById.GetValueOrDefault(c.ToId)?.Name)).ToList();

        return new BriefPage(page.Name, page.Width, page.Height, regions, components, flows);
    }

    private static IReadOnlyList<string>? DistinctNames(IEnumerable<WireframeElement> elements)
    {
        var names = elements
            .Select(e => e.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return names.Count == 0 ? null : names;
    }

    /// <summary>Classifies an element into a layout region from its position on the page.</summary>
    public static string Classify(WireframeElement el, double pageWidth, double pageHeight)
    {
        if (el.Y + el.H <= pageHeight * 0.15)
        {
            return "header";
        }
        if (el.Y >= pageHeight * 0.85)
        {
            return "footer";
        }
        if (el.X + el.W <= pageWidth * 0.2)
        {
            return "sidebar";
        }
        return "content";
    }

    private sealed class BriefComponentKeyComparer : IEqualityComparer<(string Type, string? Role)>
    {
        public static BriefComponentKeyComparer Instance { get; } = new();

        public bool Equals((string Type, string? Role) x, (string Type, string? Role) y)
            => string.Equals(x.Type, y.Type, StringComparison.Ordinal)
               && string.Equals(x.Role, y.Role, StringComparison.Ordinal);

        public int GetHashCode((string Type, string? Role) obj)
            => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Type),
                obj.Role is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Role));
    }
}
