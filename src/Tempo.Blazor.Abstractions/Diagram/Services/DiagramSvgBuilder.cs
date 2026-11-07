using System.Globalization;
using System.Text;
using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram.Services;

/// <summary>
/// Builds a static SVG representation of a diagram page using pure SVG elements (no browser, no
/// JavaScript), so the output can be embedded directly, rasterised by Skia (PNG) or embedded by
/// QuestPDF (PDF). Rendering is deterministic given a fixed page, options and palette.
/// </summary>
/// <remarks>
/// Stencil geometry is resolved through a <c>stencilLookup</c> delegate so the same builder can be
/// driven by the full <c>DiagramStencilRegistry</c> (shippable renderer) or a stand-alone stencil
/// map (server export) without either taking a dependency on the other.
/// </remarks>
public static class DiagramSvgBuilder
{
    /// <summary>Builds the SVG string for <paramref name="page"/>.</summary>
    /// <param name="page">The diagram page to render.</param>
    /// <param name="options">Size / padding / grid / background options.</param>
    /// <param name="palette">Default colour palette (theme). Explicit stencil/style colours override it.</param>
    /// <param name="stencilLookup">Resolves a stencil by id; may return null for unknown ids.</param>
    public static string Build(
        DiagramPage page,
        DiagramExportOptions options,
        DiagramSvgPalette palette,
        Func<string, DiagramStencil?> stencilLookup)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(stencilLookup);

        var nodes = page.Nodes;
        var edges = page.Edges;

        bool autoFit = options.Width is null && options.Height is null;
        double minX = nodes.Count > 0 ? nodes.Min(n => n.X) : 0;
        double minY = nodes.Count > 0 ? nodes.Min(n => n.Y) : 0;
        double maxX = nodes.Count > 0 ? nodes.Max(n => n.X + n.W) : page.Width;
        double maxY = nodes.Count > 0 ? nodes.Max(n => n.Y + n.H) : page.Height;

        double padding = autoFit ? options.Padding : 0;
        double svgWidth = options.Width ?? Math.Max(page.Width, maxX - minX + padding * 2);
        double svgHeight = options.Height ?? Math.Max(page.Height, maxY - minY + padding * 2);

        double offsetX = autoFit && nodes.Count > 0
            ? padding - minX
            : 0;
        double offsetY = autoFit && nodes.Count > 0
            ? padding - minY
            : 0;

        string bg = string.IsNullOrWhiteSpace(options.BackgroundColor) ? palette.Background : options.BackgroundColor;

        var sb = new StringBuilder();
        sb.Append($"""<svg xmlns="http://www.w3.org/2000/svg" width="{F(svgWidth)}" height="{F(svgHeight)}">""");
        sb.Append("<defs>");
        AppendMarkers(sb, palette);
        if (options.IncludeGrid)
        {
            sb.Append($"""<pattern id="grid" width="20" height="20" patternUnits="userSpaceOnUse"><circle cx="1" cy="1" r="1" fill="{EscapeSvg(palette.Grid)}"/></pattern>""");
        }
        sb.Append($"""<clipPath id="page-clip"><rect x="0" y="0" width="{F(page.Width)}" height="{F(page.Height)}"/></clipPath>""");
        sb.Append("</defs>");

        sb.Append($"""<rect width="{F(svgWidth)}" height="{F(svgHeight)}" fill="{EscapeSvg(bg)}"/>""");
        if (options.IncludeGrid)
        {
            sb.Append($"""<rect width="{F(svgWidth)}" height="{F(svgHeight)}" fill="url(#grid)"/>""");
        }

        sb.Append($"""<g id="edges" clip-path="url(#page-clip)" transform="translate({F(offsetX)},{F(offsetY)})">""");
        foreach (var edge in edges)
        {
            RenderEdge(sb, edge, nodes, palette);
        }
        sb.Append("</g>");

        sb.Append("""<g id="nodes" clip-path="url(#page-clip)">""");
        foreach (var node in nodes.OrderBy(n => n.ZIndex))
        {
            RenderNode(sb, node, stencilLookup, palette, offsetX, offsetY);
        }
        sb.Append("</g>");

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void AppendMarkers(StringBuilder sb, DiagramSvgPalette palette)
    {
        string solid = EscapeSvg(palette.MarkerSolid);
        string hollowFill = EscapeSvg(palette.MarkerHollowFill);
        string hollowStroke = EscapeSvg(palette.MarkerHollowStroke);
        sb.Append($"""<marker id="arrow-association" markerWidth="10" markerHeight="10" refX="9" refY="5" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L10,5 L0,10 L1,5 Z" fill="{solid}"/></marker>""");
        sb.Append($"""<marker id="arrow-inheritance" markerWidth="12" markerHeight="10" refX="11" refY="5" orient="auto" markerUnits="strokeWidth"><path d="M0,0 L11,5 L0,10 Z" fill="{hollowFill}" stroke="{hollowStroke}"/></marker>""");
        sb.Append($"""<marker id="arrow-composition" markerWidth="10" markerHeight="10" refX="9" refY="5" orient="auto" markerUnits="strokeWidth"><path d="M0,5 L5,0 L10,5 L5,10 Z" fill="{solid}"/></marker>""");
        sb.Append($"""<marker id="arrow-aggregation" markerWidth="10" markerHeight="10" refX="9" refY="5" orient="auto" markerUnits="strokeWidth"><path d="M0,5 L5,0 L10,5 L5,10 Z" fill="{hollowFill}" stroke="{hollowStroke}"/></marker>""");
    }

    private static void RenderNode(StringBuilder sb, DiagramNode node, Func<string, DiagramStencil?> stencilLookup, DiagramSvgPalette palette, double ox, double oy)
    {
        var stencil = stencilLookup(node.StencilId);
        var layout = stencil?.Layout ?? new DiagramStencilLayout { BackgroundShape = "rectangle" };
        var shape = layout.BackgroundShape ?? "rectangle";

        // A node's OWN style wins over the stencil layout (an author who recoloured one node must
        // see that colour in an export), and the palette is the last resort. A CSS custom-property
        // reference (the shipped BPMN stencils paint with var(--tm-diagram-paper, …)) cannot resolve
        // in a standalone SVG — the serialized file has no stylesheet — so a var() paint falls back
        // to the palette instead of rendering as an invalid (black) paint.
        string fill = ResolveNodePaint(node.Style.Fill, layout.Fill, palette.DefaultNodeFill);
        string stroke = ResolveNodePaint(node.Style.Stroke, layout.Stroke, palette.DefaultNodeStroke);
        double strokeWidth = node.Style.StrokeWidth ?? layout.StrokeWidth ?? 1.5;

        sb.Append($"""<g transform="translate({F(ox + node.X)},{F(oy + node.Y)}) rotate({F(node.Rotation)},{F(node.W / 2)},{F(node.H / 2)})">""");

        if (shape == "weak-entity")
        {
            sb.Append($"""<rect x="-2" y="-2" width="{F(node.W + 4)}" height="{F(node.H + 4)}" fill="{EscapeSvg(fill)}" stroke="{EscapeSvg(stroke)}" stroke-width="{F(strokeWidth)}"/>""");
            sb.Append($"""<rect x="2" y="2" width="{F(node.W - 4)}" height="{F(node.H - 4)}" fill="none" stroke="{EscapeSvg(stroke)}" stroke-width="{F(strokeWidth)}"/>""");
        }
        else if (shape == "ellipse" || shape == "rounded" || shape == "rectangle")
        {
            double rx = shape == "ellipse" ? node.W / 2 : (shape == "rounded" ? 8 : 0);
            double ry = shape == "ellipse" ? node.H / 2 : (shape == "rounded" ? 8 : 0);
            sb.Append($"""<rect x="0" y="0" width="{F(node.W)}" height="{F(node.H)}" rx="{F(rx)}" ry="{F(ry)}" fill="{EscapeSvg(fill)}" stroke="{EscapeSvg(stroke)}" stroke-width="{F(strokeWidth)}"/>""");
        }
        else if (shape == "diamond")
        {
            sb.Append($"""<polygon points="{F(node.W / 2)},0 {F(node.W)},{F(node.H / 2)} {F(node.W / 2)},{F(node.H)} 0,{F(node.H / 2)}" fill="{EscapeSvg(fill)}" stroke="{EscapeSvg(stroke)}" stroke-width="{F(strokeWidth)}"/>""");
        }
        else if (shape == "document")
        {
            double h80 = node.H * 0.8;
            string d = $"M0,0 H{F(node.W)} V{F(h80)} Q{F(node.W * 0.85)},{F(node.H)} {F(node.W * 0.7)},{F(h80)} Q{F(node.W * 0.55)},{F(node.H)} {F(node.W * 0.4)},{F(h80)} Q{F(node.W * 0.25)},{F(node.H)} {F(node.W * 0.1)},{F(h80)} Q0,{F(node.H)} 0,{F(h80)} Z";
            sb.Append($"""<path d="{d}" fill="{EscapeSvg(fill)}" stroke="{EscapeSvg(stroke)}" stroke-width="{F(strokeWidth)}"/>""");
        }
        else
        {
            sb.Append($"""<rect x="0" y="0" width="{F(node.W)}" height="{F(node.H)}" fill="{EscapeSvg(fill)}" stroke="{EscapeSvg(stroke)}" stroke-width="{F(strokeWidth)}"/>""");
        }

        RenderNodeContent(sb, node, layout, palette);
        sb.Append("</g>");
    }

    /// <summary>
    /// The paint of one node boundary: the node's own style first, then the stencil layout, then the
    /// palette. A <c>var(--…)</c> reference (the shipped stencils' token paints) is emitted as the
    /// palette default — a standalone SVG has no stylesheet to resolve it, and an unresolved paint
    /// renders black.
    /// </summary>
    private static string ResolveNodePaint(string? nodePaint, string? layoutPaint, string palettePaint)
    {
        var paint = nodePaint ?? layoutPaint;
        if (string.IsNullOrWhiteSpace(paint) || IsTokenPaint(paint!))
        {
            return palettePaint;
        }

        return paint!;
    }

    /// <summary>Whether a paint value references a CSS custom property (unresolvable in an export).</summary>
    private static bool IsTokenPaint(string paint)
        => paint.TrimStart().StartsWith("var(", StringComparison.OrdinalIgnoreCase);

    private static void RenderNodeContent(StringBuilder sb, DiagramNode node, DiagramStencilLayout layout, DiagramSvgPalette palette)
    {
        double currentY = 4;

        var sections = layout.Sections;
        if (sections.Count == 0) return;

        foreach (var section in sections)
        {
            var pad = section.Padding ?? 0;
            currentY += pad;

            switch (section.Type)
            {
                case "divider":
                    {
                        double y = currentY + 0.5;
                        sb.Append($"""<line x1="4" y1="{F(y)}" x2="{F(node.W - 4)}" y2="{F(y)}" stroke="{EscapeSvg(palette.Divider)}" stroke-width="1"/>""");
                        currentY += 1 + pad;
                        break;
                    }
                case "text":
                case "icon":
                    {
                        var text = GetSectionText(section, node);
                        var ts = section.TextStyle;
                        double fs = ts?.FontSize ?? 12;
                        string anchor = GetTextAnchor(ts?.TextAlign ?? StencilTextAlign.Left);
                        double x = GetTextX(ts?.TextAlign ?? StencilTextAlign.Left, node.W, pad);
                        double y = currentY + fs;
                        AppendTextElement(sb, text, x, y, ts, node, palette, anchor);
                        currentY += fs + pad;
                        break;
                    }
                case "list":
                    {
                        var items = GetSectionList(section, node).ToList();
                        var ts = section.TextStyle;
                        double fs = ts?.FontSize ?? 12;
                        double lineHeight = fs * 1.25;
                        string anchor = GetTextAnchor(ts?.TextAlign ?? StencilTextAlign.Left);
                        double x = GetTextX(ts?.TextAlign ?? StencilTextAlign.Left, node.W, pad);
                        sb.Append($"""<text x="{F(x)}" y="{F(currentY + lineHeight)}" font-size="{F(fs)}" text-anchor="{anchor}" font-family="{EscapeSvg(ts?.FontFamily ?? "sans-serif")}" fill="{EscapeSvg(ts?.Color ?? node.Style.Color ?? palette.DefaultText)}" font-weight="{(ts?.IsBold == true ? "700" : "400")}" font-style="{(ts?.IsItalic == true ? "italic" : "normal")}">""");
                        for (int i = 0; i < items.Count; i++)
                        {
                            string dy = i == 0 ? "0" : F(lineHeight);
                            sb.Append($"""<tspan x="{F(x)}" dy="{dy}">{EscapeSvg(items[i])}</tspan>""");
                        }
                        sb.Append("</text>");
                        currentY += items.Count * lineHeight + pad;
                        break;
                    }
            }
        }
    }

    private static void AppendTextElement(StringBuilder sb, string text, double x, double y, DiagramStencilTextStyle? ts, DiagramNode node, DiagramSvgPalette palette, string anchor)
    {
        double fs = ts?.FontSize ?? 12;
        if (node.Data.TryGetValue("__mathSvg", out var svgObj) && svgObj is string svgHtml && !string.IsNullOrWhiteSpace(svgHtml))
        {
            sb.Append($"""<g transform="translate({F(x)},{F(y - fs)})">{svgHtml}</g>""");
            return;
        }
        sb.Append($"""<text x="{F(x)}" y="{F(y)}" font-size="{F(fs)}" text-anchor="{anchor}" font-family="{EscapeSvg(ts?.FontFamily ?? "sans-serif")}" fill="{EscapeSvg(ts?.Color ?? node.Style.Color ?? palette.DefaultText)}" font-weight="{(ts?.IsBold == true ? "700" : "400")}" font-style="{(ts?.IsItalic == true ? "italic" : "normal")}">{EscapeSvg(text)}</text>""");
    }

    private static string GetTextAnchor(StencilTextAlign align)
        => align switch
        {
            StencilTextAlign.Center => "middle",
            StencilTextAlign.Right => "end",
            _ => "start"
        };

    private static double GetTextX(StencilTextAlign align, double width, double padding)
        => align switch
        {
            StencilTextAlign.Center => width / 2,
            StencilTextAlign.Right => width - padding,
            _ => padding
        };

    private static void RenderEdge(StringBuilder sb, DiagramEdge edge, List<DiagramNode> nodes, DiagramSvgPalette palette)
    {
        var source = nodes.FirstOrDefault(n => n.Id == edge.SourceNodeId);
        var target = nodes.FirstOrDefault(n => n.Id == edge.TargetNodeId);
        if (source is null || target is null) return;

        var sp = GetPortPosition(source, edge.SourcePortId);
        var tp = GetPortPosition(target, edge.TargetPortId);

        string pathD;
        if (edge.SourceNodeId == edge.TargetNodeId)
        {
            // Loop edge: U-shaped cubic bezier
            double loopSize = 40.0;
            double midX = (sp.X + tp.X) / 2;
            double midY = (sp.Y + tp.Y) / 2;
            double dirX = tp.X - sp.X;
            double dirY = tp.Y - sp.Y;
            double len = Math.Sqrt(dirX * dirX + dirY * dirY);
            double perpX, perpY;
            if (len > 0.001)
            {
                perpX = -dirY / len * loopSize;
                perpY = dirX / len * loopSize;
            }
            else
            {
                perpX = 0;
                perpY = -loopSize;
            }
            double cx1 = midX + perpX * 0.5;
            double cy1 = midY + perpY * 0.5;
            double cx2 = midX + perpX * 0.5;
            double cy2 = midY + perpY * 0.5;
            pathD = $"M{F(sp.X)},{F(sp.Y)} C{F(cx1)},{F(cy1)} {F(cx2)},{F(cy2)} {F(tp.X)},{F(tp.Y)}";
        }
        else if (edge.Routing == "curved")
        {
            double mx = (sp.X + tp.X) / 2;
            pathD = $"M{F(sp.X)},{F(sp.Y)} C{F(mx)},{F(sp.Y)} {F(mx)},{F(tp.Y)} {F(tp.X)},{F(tp.Y)}";
        }
        else if (edge.Routing == "orthogonal")
        {
            if (Math.Abs(sp.X - tp.X) < 0.01)
            {
                pathD = $"M{F(sp.X)},{F(sp.Y)} L{F(tp.X)},{F(tp.Y)}";
            }
            else if (Math.Abs(sp.Y - tp.Y) < 0.01)
            {
                pathD = $"M{F(sp.X)},{F(sp.Y)} L{F(tp.X)},{F(tp.Y)}";
            }
            else
            {
                double midX = (sp.X + tp.X) / 2;
                pathD = $"M{F(sp.X)},{F(sp.Y)} L{F(midX)},{F(sp.Y)} L{F(midX)},{F(tp.Y)} L{F(tp.X)},{F(tp.Y)}";
            }
        }
        else
        {
            pathD = $"M{F(sp.X)},{F(sp.Y)} L{F(tp.X)},{F(tp.Y)}";
        }

        string stroke = edge.Style.Stroke ?? palette.DefaultNodeStroke;
        double strokeWidth = edge.Style.StrokeWidth ?? 1.5;
        string dash = !string.IsNullOrWhiteSpace(edge.Style.StrokeDasharray)
            ? $" stroke-dasharray=\"{EscapeSvg(edge.Style.StrokeDasharray)}\""
            : edge.ConnectorType == "dependency"
                ? " stroke-dasharray=\"5,5\""
                : "";

        string markerEnd = edge.ConnectorType switch
        {
            "inheritance" => "url(#arrow-inheritance)",
            "composition" => "url(#arrow-composition)",
            "aggregation" => "url(#arrow-aggregation)",
            _ => "url(#arrow-association)"
        };

        sb.Append($"""<path d="{pathD}" fill="none" stroke="{EscapeSvg(stroke)}" stroke-width="{F(strokeWidth)}" marker-end="{markerEnd}"{dash}/>""");

        if (!string.IsNullOrWhiteSpace(edge.Label))
        {
            double lx = (sp.X + tp.X) / 2;
            double ly = (sp.Y + tp.Y) / 2;
            sb.Append($"""<text x="{F(lx)}" y="{F(ly - 4)}" font-size="11" text-anchor="middle" fill="{EscapeSvg(stroke)}">{EscapeSvg(edge.Label)}</text>""");
        }
    }

    private static DiagramPoint GetPortPosition(DiagramNode node, string? portId)
    {
        if (portId is not null)
        {
            var port = node.Ports.FirstOrDefault(p => p.Id == portId);
            if (port is not null)
                return GetPortSidePosition(node, port.Side, port.Offset);
        }
        return new DiagramPoint(node.X + node.W / 2, node.Y + node.H / 2);
    }

    private static DiagramPoint GetPortSidePosition(DiagramNode node, PortSide side, double offset)
    {
        return side switch
        {
            PortSide.Top => new DiagramPoint(node.X + node.W * offset, node.Y),
            PortSide.Right => new DiagramPoint(node.X + node.W, node.Y + node.H * offset),
            PortSide.Bottom => new DiagramPoint(node.X + node.W * offset, node.Y + node.H),
            PortSide.Left => new DiagramPoint(node.X, node.Y + node.H * offset),
            _ => new DiagramPoint(node.X + node.W / 2, node.Y + node.H / 2)
        };
    }

    private static string GetSectionText(DiagramStencilSection section, DiagramNode node)
    {
        if (section.DataKey is not null && node.Data.TryGetValue(section.DataKey, out var value))
        {
            var text = value?.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return section.DefaultText ?? "";
    }

    private static IEnumerable<string> GetSectionList(DiagramStencilSection section, DiagramNode node)
    {
        if (section.DataKey is not null && node.Data.TryGetValue(section.DataKey, out var value))
        {
            if (value is System.Text.Json.JsonElement je)
            {
                if (je.ValueKind == System.Text.Json.JsonValueKind.Array)
                    return je.EnumerateArray().Select(e => e.ToString()).ToList();
                return [je.ToString()];
            }
            if (value is IEnumerable<string> strs)
                return strs;
            if (value is System.Collections.IEnumerable enumerable && value is not string)
                return enumerable.Cast<object>().Select(o => o?.ToString() ?? "").ToList();
            var s = value?.ToString();
            if (!string.IsNullOrWhiteSpace(s)) return [s];
        }
        var def = section.DefaultText;
        if (!string.IsNullOrWhiteSpace(def)) return [def];
        return [];
    }

    private static string EscapeSvg(string? text)
    {
        if (text is null) return "";
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
