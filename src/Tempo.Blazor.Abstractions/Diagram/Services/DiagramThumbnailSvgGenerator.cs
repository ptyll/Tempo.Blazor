using System.Globalization;
using System.Text;
using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram.Services;

/// <summary>Options for <see cref="DiagramThumbnailSvgGenerator.Generate(DiagramDocument, DiagramThumbnailOptions)"/>.</summary>
public sealed class DiagramThumbnailOptions
{
    /// <summary>Render node labels (default <c>true</c>; template card previews pass <c>false</c> — the text is illegible at card size).</summary>
    public bool IncludeLabels { get; init; } = true;

    /// <summary>Paint an opaque white background rect (default <c>true</c>; template card previews pass <c>false</c> — the card provides the surface).</summary>
    public bool OpaqueBackground { get; init; } = true;

    /// <summary>Viewport width of the generated SVG (must be positive). Defaults to 300 (template card previews use 320 for a 16:10 box).</summary>
    public int Width { get; init; } = 300;

    /// <summary>Viewport height of the generated SVG (must be positive). Defaults to 200 (template card previews use 200 for a 16:10 box).</summary>
    public int Height { get; init; } = 200;
}

/// <summary>
/// Generates a lightweight SVG thumbnail from a <see cref="DiagramDocument"/>.
/// Used for template previews when no static PNG thumbnail is available.
/// </summary>
public static class DiagramThumbnailSvgGenerator
{
    /// <summary>
    /// Generates an SVG string representing the document (labels on, opaque white background,
    /// 300×200 viewport).
    /// </summary>
    public static string Generate(DiagramDocument document)
        => Generate(document, new DiagramThumbnailOptions());

    /// <summary>
    /// Generates an SVG string representing the document. The viewBox is fitted to the node
    /// bounds plus a fixed padding; the viewport (<see cref="DiagramThumbnailOptions.Width"/> ×
    /// <see cref="DiagramThumbnailOptions.Height"/>) meets it with
    /// <c>preserveAspectRatio="xMidYMid meet"</c>, so any viewport aspect renders the whole
    /// diagram without cropping.
    /// </summary>
    public static string Generate(DiagramDocument document, DiagramThumbnailOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Height);
        document.EnsurePages();

        var page = document.ActivePage;
        var nodes = page.Nodes;
        var edges = page.Edges;

        // Compute bounding box of all nodes
        double minX = 0, minY = 0, maxX = 300, maxY = 200;
        if (nodes.Count > 0)
        {
            minX = nodes.Min(n => n.X);
            minY = nodes.Min(n => n.Y);
            maxX = nodes.Max(n => n.X + n.W);
            maxY = nodes.Max(n => n.Y + n.H);
        }

        const double padding = 20;
        var viewBoxX = minX - padding;
        var viewBoxY = minY - padding;
        var viewBoxW = Math.Max(maxX - minX + padding * 2, 1);
        var viewBoxH = Math.Max(maxY - minY + padding * 2, 1);

        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{viewBoxX.ToString(CultureInfo.InvariantCulture)} {viewBoxY.ToString(CultureInfo.InvariantCulture)} {viewBoxW.ToString(CultureInfo.InvariantCulture)} {viewBoxH.ToString(CultureInfo.InvariantCulture)}\" width=\"{options.Width}\" height=\"{options.Height}\" preserveAspectRatio=\"xMidYMid meet\">");
        if (options.OpaqueBackground)
            sb.AppendLine("<rect x=\"" + viewBoxX.ToString(CultureInfo.InvariantCulture) + "\" y=\"" + viewBoxY.ToString(CultureInfo.InvariantCulture) + "\" width=\"" + viewBoxW.ToString(CultureInfo.InvariantCulture) + "\" height=\"" + viewBoxH.ToString(CultureInfo.InvariantCulture) + "\" fill=\"#ffffff\" />");

        // Render edges first (behind nodes)
        foreach (var edge in edges)
        {
            var source = nodes.FirstOrDefault(n => n.Id == edge.SourceNodeId);
            var target = nodes.FirstOrDefault(n => n.Id == edge.TargetNodeId);
            if (source is null || target is null) continue;

            var sx = source.X + source.W / 2;
            var sy = source.Y + source.H / 2;
            var tx = target.X + target.W / 2;
            var ty = target.Y + target.H / 2;

            sb.AppendLine($"<line x1=\"{sx.ToString(CultureInfo.InvariantCulture)}\" y1=\"{sy.ToString(CultureInfo.InvariantCulture)}\" x2=\"{tx.ToString(CultureInfo.InvariantCulture)}\" y2=\"{ty.ToString(CultureInfo.InvariantCulture)}\" stroke=\"#6b7280\" stroke-width=\"2\" />");
        }

        foreach (var node in nodes)
        {
            RenderNode(sb, node, options.IncludeLabels);
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static void RenderNode(StringBuilder sb, DiagramNode node, bool includeLabels)
    {
        var fill = node.Style.Fill ?? GetDefaultFill(node.StencilId);
        var stroke = node.Style.Stroke ?? GetDefaultStroke(node.StencilId);
        var strokeWidth = node.Style.StrokeWidth ?? 1;
        var radius = node.Style.Radius ?? GetDefaultRadius(node.StencilId);

        var x = node.X;
        var y = node.Y;
        var w = node.W;
        var h = node.H;
        var cx = x + w / 2;
        var cy = y + h / 2;

        var shape = GetShapeType(node.StencilId);

        switch (shape)
        {
            case NodeShape.Ellipse:
                sb.AppendLine($"<ellipse cx=\"{cx.ToString(CultureInfo.InvariantCulture)}\" cy=\"{cy.ToString(CultureInfo.InvariantCulture)}\" rx=\"{(w / 2).ToString(CultureInfo.InvariantCulture)}\" ry=\"{(h / 2).ToString(CultureInfo.InvariantCulture)}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{strokeWidth.ToString(CultureInfo.InvariantCulture)}\" />");
                break;

            case NodeShape.Rhombus:
                {
                    var points = $"{cx.ToString(CultureInfo.InvariantCulture)},{y.ToString(CultureInfo.InvariantCulture)} { (x + w).ToString(CultureInfo.InvariantCulture)},{cy.ToString(CultureInfo.InvariantCulture)} {cx.ToString(CultureInfo.InvariantCulture)},{(y + h).ToString(CultureInfo.InvariantCulture)} {x.ToString(CultureInfo.InvariantCulture)},{cy.ToString(CultureInfo.InvariantCulture)}";
                    sb.AppendLine($"<polygon points=\"{points}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{strokeWidth.ToString(CultureInfo.InvariantCulture)}\" />");
                }
                break;

            case NodeShape.Text:
                if (includeLabels)
                    sb.AppendLine($"<text x=\"{cx.ToString(CultureInfo.InvariantCulture)}\" y=\"{cy.ToString(CultureInfo.InvariantCulture)}\" dominant-baseline=\"middle\" text-anchor=\"middle\" font-size=\"14\" fill=\"#111827\">{EscapeSvgText(GetNodeLabel(node))}</text>");
                break;

            case NodeShape.Rectangle:
            default:
                sb.AppendLine($"<rect x=\"{x.ToString(CultureInfo.InvariantCulture)}\" y=\"{y.ToString(CultureInfo.InvariantCulture)}\" width=\"{w.ToString(CultureInfo.InvariantCulture)}\" height=\"{h.ToString(CultureInfo.InvariantCulture)}\" rx=\"{radius.ToString(CultureInfo.InvariantCulture)}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{strokeWidth.ToString(CultureInfo.InvariantCulture)}\" />");
                var label = includeLabels ? GetNodeLabel(node) : string.Empty;
                if (!string.IsNullOrWhiteSpace(label) && shape != NodeShape.Text)
                {
                    sb.AppendLine($"<text x=\"{cx.ToString(CultureInfo.InvariantCulture)}\" y=\"{cy.ToString(CultureInfo.InvariantCulture)}\" dominant-baseline=\"middle\" text-anchor=\"middle\" font-size=\"{(Math.Min(14, h / 3)).ToString(CultureInfo.InvariantCulture)}\" fill=\"#111827\">{EscapeSvgText(label)}</text>");
                }
                break;
        }
    }

    private static NodeShape GetShapeType(string? stencilId)
    {
        if (string.IsNullOrEmpty(stencilId)) return NodeShape.Rectangle;
        var s = stencilId.ToLowerInvariant();
        if (s.Contains("ellipse") || s.Contains("circle")) return NodeShape.Ellipse;
        if (s.Contains("rhombus") || s.Contains("diamond")) return NodeShape.Rhombus;
        if (s.Contains("text") || s.Contains("label")) return NodeShape.Text;
        return NodeShape.Rectangle;
    }

    private static string GetDefaultFill(string? stencilId)
    {
        if (string.IsNullOrEmpty(stencilId)) return "#f3f4f6";
        var s = stencilId.ToLowerInvariant();
        if (s.Contains("uml.class")) return "#eff6ff";
        if (s.Contains("erd.entity")) return "#f0fdf4";
        if (s.Contains("start") || s.Contains("end")) return "#dbeafe";
        return "#f3f4f6";
    }

    private static string GetDefaultStroke(string? stencilId)
    {
        if (string.IsNullOrEmpty(stencilId)) return "#374151";
        var s = stencilId.ToLowerInvariant();
        if (s.Contains("uml.class")) return "#2563eb";
        if (s.Contains("erd.entity")) return "#16a34a";
        return "#374151";
    }

    private static double GetDefaultRadius(string? stencilId)
    {
        if (string.IsNullOrEmpty(stencilId)) return 0;
        var s = stencilId.ToLowerInvariant();
        if (s.Contains("start") || s.Contains("end") || s.Contains("terminator")) return 16;
        return 2;
    }

    private static string GetNodeLabel(DiagramNode node)
    {
        if (node.Data.TryGetValue("label", out var label) && label is not null)
            return label.ToString() ?? "";
        if (node.Data.TryGetValue("name", out var name) && name is not null)
            return name.ToString() ?? "";
        return "";
    }

    private static string EscapeSvgText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private enum NodeShape
    {
        Rectangle,
        Ellipse,
        Rhombus,
        Text
    }
}
