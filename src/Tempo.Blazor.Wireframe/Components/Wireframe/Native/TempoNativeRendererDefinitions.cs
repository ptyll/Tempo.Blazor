using System.Text;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Wireframe.Models;
using static Tempo.Blazor.Components.Wireframe.WireframeSvg;

namespace Tempo.Blazor.Components.Wireframe;

internal static class TempoNativeRendererDefinitions
{
    private static readonly BuiltInComponentSchemas Schemas = new();

    public static IEnumerable<WireframeComponentDef> GetDefinitions()
    {
        yield return DefFromSchema("TmChart", "bar-chart-2", RenderChart);
        yield return DefFromSchema("TmGauge", "activity", RenderGauge);
        yield return DefFromSchema("TmStockChart", "trending-up", RenderStockChart);
        yield return DefFromSchema("TmKanbanBoard", "columns", RenderKanbanBoard);
        yield return DefFromSchema("TmPivotTable", "table", RenderPivotTable);
        yield return DefFromSchema("TmGantt", "bar-chart-2", RenderGantt);
        yield return DefFromSchema("TmWorkflowDesignerCanvas", "git-branch", RenderWorkflowDesignerCanvas);
        yield return DefFromSchema("TmDiagramEditor", "git-branch", RenderDiagramEditor);
        yield return DefFromSchema("TmDashboard", "grid", RenderDashboard);
        yield return DefFromSchema("TmSpreadsheet", "grid", RenderSpreadsheet);
        yield return DefFromSchema("TmDocumentEditor", "file-text", RenderDocumentEditor);
        yield return DefFromSchema("TmNotionEditor", "book-open", RenderNotionEditor);
        yield return DefFromSchema("TmChat", "message-circle", RenderChat);
    }

    private static WireframeComponentDef DefFromSchema(
        string type,
        string? icon,
        Action<WireframeElement, RenderTreeBuilder> render)
    {
        var schema = Schemas.GetSchemas().FirstOrDefault(x => x.Type == type)
                     ?? throw new InvalidOperationException($"No schema found for '{type}'");

        return new WireframeComponentDef
        {
            Type = schema.Type,
            Category = schema.Category,
            DisplayName = schema.DisplayName,
            Icon = icon,
            DefaultWidth = schema.DefaultWidth,
            DefaultHeight = schema.DefaultHeight,
            Props = [.. schema.Props],
            IsBuiltIn = true,
            IsContainer = schema.IsContainer,
            RenderSvg = render,
            SizePresets = schema.SizePresets
        };
    }

    private static void Svg(RenderTreeBuilder builder, string markup)
        => builder.AddMarkupContent(0, markup);

    /// <summary>The same width-based ellipsis the stencil pack renderer applies
    /// (<see cref="StencilPackRenderer"/> ApplyEllipsis, ~0.55em per glyph): caps a label at the
    /// pixels a text node actually has, so header titles never paint under the switcher pill.</summary>
    private static string Ellipsize(string content, double width, double fontSize)
    {
        if (string.IsNullOrEmpty(content) || width <= 0 || fontSize <= 0)
            return content;

        var maxChars = (int)Math.Floor(width / (fontSize * 0.55));
        if (content.Length <= maxChars)
            return content;

        return maxChars <= 1 ? "…" : content[..(maxChars - 1)] + "…";
    }

    private static void RenderChart(WireframeElement el, RenderTreeBuilder b)
    {
        var type = el.Props.GetString("type", "bar");
        var title = el.Props.GetString("title", "Chart Title");
        var dataPoints = el.Props.GetInt("dataPoints", 6);
        var showLegend = el.Props.GetBool("showLegend", false);
        var showGrid = el.Props.GetBool("showGrid", false);
        var stacked = type is "stackedBar" or "stackedHorizontalBar" or "stackedArea";
        var horizontal = el.Props.GetBool("horizontal", false) || type is "horizontalBar" or "stackedHorizontalBar";
        var sb = new StringBuilder();
        sb.Append(Rect(0, 0, el.W, el.H, Fill, Border, 6));
        sb.Append(Text(title, el.W / 2, 16, 12, ColorText, "middle", "500"));
        var legendH = showLegend ? 24.0 : 0;
        var chartX = 32.0;
        var chartY = 28.0;
        var chartW = el.W - chartX - 8;
        var chartH = el.H - chartY - 24 - legendH;
        var fills = new[] { FillAccent, FillDark, "#fef9c3", "#dcfce7", "#fee2e2" };

        if (type == "pie" || type == "donut")
        {
            var cx = el.W / 2;
            var cy = chartY + chartH / 2;
            var r = Math.Min(chartW, chartH) / 2 - 8;
            var angles = new[] { 0.0, 72, 144, 216, 288, 360 };
            for (var i = 0; i < Math.Min(dataPoints, 5); i++)
            {
                var a1 = angles[i] * Math.PI / 180;
                var a2 = angles[i + 1] * Math.PI / 180;
                var x1 = cx + r * Math.Cos(a1);
                var y1 = cy + r * Math.Sin(a1);
                var x2 = cx + r * Math.Cos(a2);
                var y2 = cy + r * Math.Sin(a2);
                var large = angles[i + 1] - angles[i] > 180 ? 1 : 0;
                sb.Append($"<path d='M{F(cx)},{F(cy)} L{F(x1)},{F(y1)} A{F(r)},{F(r)} 0 {large} 1 {F(x2)},{F(y2)} Z' fill='{fills[i % fills.Length]}' stroke='white' stroke-width='1'></path>");
            }

            if (type == "donut")
                sb.Append($"<circle cx='{F(cx)}' cy='{F(cy)}' r='{F(r * 0.5)}' fill='white'></circle>");
        }
        else if (type == "line" || type == "area" || type == "stackedArea")
        {
            sb.Append(VLine(chartX, chartY, chartY + chartH));
            sb.Append(HLine(chartX, chartX + chartW, chartY + chartH));
            if (showGrid)
            {
                for (var g = 1; g <= 4; g++)
                    sb.Append(HLine(chartX, chartX + chartW, chartY + chartH * g / 5, FillDark));
            }

            var heights = new[] { 0.6, 0.8, 0.4, 0.9, 0.5, 0.7, 0.85 };
            if (type == "stackedArea")
            {
                // Three series accumulating on a shared baseline: each band is drawn between the
                // running total underneath it and its own cumulative top.
                const int seriesCount = 3;
                var lower = new double[dataPoints];
                for (var s = 0; s < seriesCount; s++)
                {
                    var upper = new double[dataPoints];
                    var top = new List<string>();
                    var bottom = new List<string>();
                    for (var i = 0; i < dataPoints; i++)
                    {
                        upper[i] = lower[i] + heights[(i + s) % heights.Length] * chartH / seriesCount;
                        var px = chartX + (i + 0.5) * chartW / dataPoints;
                        top.Add($"{F(px)},{F(chartY + chartH - upper[i])}");
                        bottom.Insert(0, $"{F(px)},{F(chartY + chartH - lower[i])}");
                    }

                    sb.Append($"<polygon points='{string.Join(" ", top)} {string.Join(" ", bottom)}' fill='{fills[s % fills.Length]}' stroke='{Accent}' stroke-width='1'></polygon>");
                    lower = upper;
                }
            }
            else
            {
                var pts = new List<string>();
                for (var i = 0; i < dataPoints; i++)
                {
                    var px = chartX + (i + 0.5) * chartW / dataPoints;
                    var py = chartY + chartH - heights[i % heights.Length] * chartH;
                    pts.Add($"{F(px)},{F(py)}");
                }

                if (type == "area" && pts.Count > 0)
                {
                    var firstX = chartX + 0.5 * chartW / dataPoints;
                    var lastX = chartX + (dataPoints - 0.5) * chartW / dataPoints;
                    var baseline = chartY + chartH;
                    sb.Append($"<polygon points='{string.Join(" ", pts)} {F(lastX)},{F(baseline)} {F(firstX)},{F(baseline)}' fill='{FillAccent}' stroke='none'></polygon>");
                }

                sb.Append($"<polyline points='{string.Join(" ", pts)}' fill='none' stroke='{Accent}' stroke-width='2'></polyline>");
            }
        }
        else
        {
            sb.Append(VLine(chartX, chartY, chartY + chartH));
            sb.Append(HLine(chartX, chartX + chartW, chartY + chartH));
            if (showGrid)
            {
                for (var g = 1; g <= 4; g++)
                {
                    sb.Append(horizontal
                        ? VLine(chartX + chartW * g / 5, chartY, chartY + chartH, FillDark)
                        : HLine(chartX, chartX + chartW, chartY + chartH * g / 5, FillDark));
                }
            }

            var heights = new[] { 0.6, 0.8, 0.4, 0.9, 0.5, 0.7 };
            if (horizontal)
            {
                var barH = chartH / dataPoints * 0.65;
                var gap = chartH / dataPoints;
                for (var i = 0; i < dataPoints; i++)
                {
                    var bw = heights[i % heights.Length] * chartW;
                    var by = chartY + i * gap + gap * 0.175;
                    if (stacked)
                    {
                        // Each bar is split into three segments accumulating from the left baseline.
                        var x = chartX;
                        for (var s = 0; s < 3; s++)
                        {
                            var sw = bw / 3;
                            sb.Append(Rect(x, by, sw, barH, fills[s % fills.Length], "#93c5fd", 0));
                            x += sw;
                        }
                    }
                    else
                    {
                        sb.Append(Rect(chartX, by, bw, barH, FillAccent, "#93c5fd", 2));
                    }
                }
            }
            else
            {
                var barW = chartW / dataPoints * 0.65;
                var gap = chartW / dataPoints;
                for (var i = 0; i < dataPoints; i++)
                {
                    var bh = heights[i % heights.Length] * chartH;
                    var bx = chartX + i * gap + gap * 0.175;
                    if (stacked)
                    {
                        // Each bar is split into three segments accumulating on the zero baseline.
                        var y = chartY + chartH;
                        for (var s = 0; s < 3; s++)
                        {
                            var sh = bh / 3;
                            y -= sh;
                            sb.Append(Rect(bx, y, barW, sh, fills[s % fills.Length], "#93c5fd", 0));
                        }
                    }
                    else
                    {
                        sb.Append(Rect(bx, chartY + chartH - bh, barW, bh, FillAccent, "#93c5fd", 2));
                    }
                }
            }
        }

        if (showLegend)
        {
            var lx = el.W / 2 - dataPoints * 36 / 2;
            for (var i = 0; i < Math.Min(dataPoints, 5); i++)
            {
                sb.Append(Rect(lx + i * 36, el.H - 20, 10, 10, fills[i % fills.Length], Border, 2));
                sb.Append(Text($"L{i + 1}", lx + i * 36 + 14, el.H - 15, 8, ColorMuted));
            }
        }

        Svg(b, sb.ToString());
    }

    private static void RenderGauge(WireframeElement el, RenderTreeBuilder b)
    {
        var value = el.Props.GetDouble("value", 65.0);
        var min = el.Props.GetDouble("min", 0.0);
        var max = el.Props.GetDouble("max", 100.0);
        var label = el.Props.GetString("label", "");
        var sb = new StringBuilder();
        var cx = el.W / 2;
        var cy = el.H * 0.62;
        var r = Math.Min(el.W, el.H * 2) * 0.42;
        var ratio = max > min ? Math.Clamp((value - min) / (max - min), 0, 1) : 0;
        var startA = Math.PI;
        var endA = 2 * Math.PI;
        var trackX1 = cx + r * Math.Cos(startA);
        var trackY1 = cy + r * Math.Sin(startA);
        var trackX2 = cx + r * Math.Cos(endA);
        var trackY2 = cy + r * Math.Sin(endA);
        sb.Append($"<path d='M {F(trackX1)},{F(trackY1)} A {F(r)},{F(r)} 0 0 1 {F(trackX2)},{F(trackY2)}' fill='none' stroke='{FillDark}' stroke-width='8' stroke-linecap='round'></path>");
        var valueA = startA + ratio * Math.PI;
        var valX2 = cx + r * Math.Cos(valueA);
        var valY2 = cy + r * Math.Sin(valueA);
        var large = ratio > 0.5 ? 1 : 0;
        sb.Append($"<path d='M {F(trackX1)},{F(trackY1)} A {F(r)},{F(r)} 0 {large} 1 {F(valX2)},{F(valY2)}' fill='none' stroke='{Accent}' stroke-width='8' stroke-linecap='round'></path>");
        var needleX = cx + (r - 12) * Math.Cos(valueA);
        var needleY = cy + (r - 12) * Math.Sin(valueA);
        sb.Append($"<line x1='{F(cx)}' y1='{F(cy)}' x2='{F(needleX)}' y2='{F(needleY)}' stroke='{ColorText}' stroke-width='2' stroke-linecap='round'></line>");
        sb.Append($"<circle cx='{F(cx)}' cy='{F(cy)}' r='4' fill='{ColorText}'></circle>");
        sb.Append(Text(F(value), cx, cy + 14, 12, ColorText, "middle", "600"));
        if (!string.IsNullOrEmpty(label))
            sb.Append(Text(label, cx, cy + 26, 9, ColorMuted, "middle"));
        Svg(b, sb.ToString());
    }

    private static void RenderStockChart(WireframeElement el, RenderTreeBuilder b)
    {
        var title = el.Props.GetString("title", "ACME");
        var type = el.Props.GetString("type", "candle");
        var period = el.Props.GetString("period", "1M");
        var sb = new StringBuilder();
        sb.Append(Rect(0, 0, el.W, el.H, Fill, Border, 6));
        sb.Append(Text(title, 12, 16, 12, ColorText, "start", "600"));
        sb.Append(Text("+2.4%  ▲", el.W - 12, 16, 10, "#16a34a", "end", "500"));
        var periods = new[] { "1D", "1W", "1M", "3M", "1Y" };
        var btnW = 28.0;
        var btnsX = el.W - periods.Length * (btnW + 2) - 8;
        var hdrH = 30.0;
        sb.Append(Rect(0, hdrH, el.W, el.H - hdrH, Fill, "none", 0));
        for (var pi = 0; pi < periods.Length; pi++)
        {
            var bx = btnsX + pi * (btnW + 2);
            var isActive = periods[pi] == period;
            sb.Append(Rect(bx, hdrH + 4, btnW, 16, isActive ? FillAccent : FillDark, isActive ? "#93c5fd" : "none", 3));
            sb.Append(Text(periods[pi], bx + btnW / 2, hdrH + 12, 8, isActive ? Accent : ColorMuted, "middle"));
        }

        var chartY = hdrH + 24;
        var chartH = el.H - chartY - 20;
        var chartX = 8.0;
        var chartW = el.W - 16;
        sb.Append(VLine(chartX, chartY, chartY + chartH, Border));
        sb.Append(HLine(chartX, chartX + chartW, chartY + chartH, Border));
        for (var gi = 1; gi <= 3; gi++)
            sb.Append(HLine(chartX, chartX + chartW, chartY + chartH * gi / 4, FillDark));

        var candleCount = 16;
        var candleW = (chartW - 4) / candleCount;
        var rng = 7;
        for (var ci = 0; ci < candleCount; ci++)
        {
            rng = (rng * 1103515245 + 12345) & 0x7fffffff;
            var open = 0.2 + (rng & 0xff) / 512.0;
            rng = (rng * 1103515245 + 12345) & 0x7fffffff;
            var close = 0.2 + (rng & 0xff) / 512.0;
            var high = Math.Max(open, close) + 0.08;
            var low = Math.Min(open, close) - 0.08;
            var bx = chartX + 2 + ci * candleW;
            if (type == "candle")
            {
                var bullish = close > open;
                var cFill = bullish ? "#dcfce7" : "#fee2e2";
                var cStroke = bullish ? "#16a34a" : "#dc2626";
                var bodyY = chartY + (1 - Math.Max(open, close)) * chartH;
                var bodyH = Math.Max(Math.Abs(open - close) * chartH, 2);
                sb.Append($"<line x1='{F(bx + candleW / 2)}' y1='{F(chartY + (1 - high) * chartH)}' x2='{F(bx + candleW / 2)}' y2='{F(chartY + (1 - low) * chartH)}' stroke='{cStroke}' stroke-width='1'></line>");
                sb.Append($"<rect x='{F(bx + 1)}' y='{F(bodyY)}' width='{F(candleW - 2)}' height='{F(bodyH)}' fill='{cFill}' stroke='{cStroke}' stroke-width='0.5'></rect>");
            }
            else if (ci > 0)
            {
                rng = (rng * 1103515245 + 12345) & 0x7fffffff;
                var prevClose = 0.2 + (rng & 0xff) / 512.0;
                sb.Append($"<line x1='{F(bx - candleW)}' y1='{F(chartY + (1 - prevClose) * chartH)}' x2='{F(bx)}' y2='{F(chartY + (1 - close) * chartH)}' stroke='{Accent}' stroke-width='1.5'></line>");
            }
        }

        Svg(b, sb.ToString());
    }

    private static void RenderKanbanBoard(WireframeElement el, RenderTreeBuilder b)
    {
        var cols = el.Props.GetStringList("columns");
        if (cols.Length == 0)
            cols = ["To Do", "In Progress", "Done"];
        var colW = (el.W - (cols.Length - 1) * 8.0) / cols.Length;
        var sb = new StringBuilder();
        for (var i = 0; i < cols.Length; i++)
        {
            var x = i * (colW + 8);
            sb.Append(Rect(x, 0, colW, el.H, FillDark, Border, 6));
            sb.Append(Rect(x + 4, 4, colW - 8, 24, FillDark, "none", 0));
            sb.Append(Text(cols[i], x + colW / 2, 16, 11, ColorText, "middle", "500"));
            for (var c = 0; c < 2; c++)
            {
                var cy = 36 + c * 60.0;
                sb.Append(Rect(x + 4, cy, colW - 8, 52, Fill, Border, 4));
                sb.Append(HLine(x + 4, x + colW - 4, cy + 16));
                sb.Append(Text("Task", x + 12, cy + 8, 10, ColorMuted));
            }
        }

        Svg(b, sb.ToString());
    }

    private static void RenderWorkflowDesignerCanvas(WireframeElement el, RenderTreeBuilder b)
    {
        var title = el.Props.GetString("title", "Workflow");
        var sb = new StringBuilder();
        sb.Append(DashedRect(el.W, el.H, 6));
        sb.Append(Text(title, el.W / 2, 18, 12, ColorMuted, "middle", "500"));
        var nodes = new (double x, double y, string label, string type)[]
        {
            (60, el.H / 2 - 20, "Start", "initial"),
            (el.W / 2 - 60, el.H / 2 - 20, "Process", "intermediate"),
            (el.W - 200, el.H / 2 - 20, "End", "final")
        };
        foreach (var (nx, ny, label, nodeType) in nodes)
        {
            var fill = nodeType == "initial" ? FillAccent : nodeType == "final" ? "#dcfce7" : Fill;
            var rx = nodeType == "initial" ? 20.0 : 8.0;
            sb.Append(Rect(nx, ny, 120, 40, fill, Border, rx));
            sb.Append(Text(label, nx + 60, ny + 20, 11, ColorText, "middle"));
        }

        sb.Append(HLine(180, el.W / 2 - 60, el.H / 2));
        sb.Append(HLine(el.W / 2 + 60, el.W - 200, el.H / 2));
        Svg(b, sb.ToString());
    }

    private static void RenderChat(WireframeElement el, RenderTreeBuilder b)
    {
        var msgCount = el.Props.GetInt("messageCount", 4);
        var showInput = el.Props.GetBool("showInput", true);
        var sb = new StringBuilder();
        sb.Append(Rect(0, 0, el.W, el.H, Fill, Border, 6));
        sb.Append(Rect(0, 0, el.W, 44, FillDark, "none", 6));
        sb.Append(HLine(0, el.W, 44));
        sb.Append($"<circle cx='22' cy='22' r='12' fill='{FillAccent}' stroke='#93c5fd'></circle>");
        sb.Append(Text("A", 22, 22, 10, Accent, "middle", "500"));
        sb.Append(Text("Alice", 40, 18, 11, ColorText, "start", "500"));
        sb.Append(Text("Online", 40, 31, 9, "#22c55e"));
        var contentH = el.H - 44 - (showInput ? 48 : 8);
        var bubbleH = Math.Max(contentH / msgCount - 8, 20);
        for (var i = 0; i < msgCount; i++)
        {
            var by = 52 + i * (bubbleH + 8);
            var mine = i % 2 == 1;
            var bw = el.W * 0.65;
            var bx = mine ? el.W - bw - 8 : 8;
            var fill = mine ? FillAccent : FillDark;
            var stroke = mine ? "#93c5fd" : Border;
            sb.Append(Rect(bx, by, bw, bubbleH, fill, stroke, 8));
            if (!mine)
                sb.Append($"<circle cx='8' cy='{F(by + bubbleH / 2)}' r='0'></circle>");
        }

        if (showInput)
        {
            sb.Append(HLine(0, el.W, el.H - 48));
            sb.Append(Rect(8, el.H - 40, el.W - 52, 32, FillDark, Border, 6));
            sb.Append(Text("Message…", 16, el.H - 24, 10, ColorLight));
            sb.Append(Rect(el.W - 40, el.H - 40, 32, 32, FillAccent, "#93c5fd", 6));
            sb.Append(Icon("send", el.W - 24, el.H - 24, 14));
        }

        Svg(b, sb.ToString());
    }

    private static void RenderSpreadsheet(WireframeElement el, RenderTreeBuilder b)
    {
        var rows = el.Props.GetInt("rows", 8);
        var cols = el.Props.GetInt("columns", 6);
        var sheets = el.Props.GetInt("sheetCount", 2);
        var sb = new StringBuilder();
        sb.Append(Rect(0, 0, el.W, el.H, Fill, Border, 4));
        sb.Append(Rect(0, 0, el.W, 28, FillDark, "none", 4));
        sb.Append(HLine(0, el.W, 28));
        sb.Append(Rect(4, 4, 36, 20, Fill, Border, 2));
        sb.Append(Text("A1", 22, 14, 9, ColorText, "middle"));
        sb.Append(VLine(40, 0, 28, Border));
        sb.Append(Text("fx  =SUM(A1:A5)", 48, 14, 9, ColorMuted));
        var headerH = 20.0;
        var rowNumW = 28.0;
        var gridY = 28.0;
        var gridH = el.H - 28 - 24;
        var colW = (el.W - rowNumW) / cols;
        var rowH = (gridH - headerH) / rows;
        sb.Append(Rect(rowNumW, gridY, el.W - rowNumW, headerH, FillDark, "none", 0));
        sb.Append(HLine(0, el.W, gridY + headerH));
        for (var c = 0; c < cols; c++)
        {
            var cx = rowNumW + c * colW;
            sb.Append(VLine(cx, gridY, gridY + gridH));
            sb.Append(Text(((char)('A' + c)).ToString(), cx + colW / 2, gridY + headerH / 2, 9, ColorMuted, "middle"));
        }

        for (var r = 0; r < rows; r++)
        {
            var ry = gridY + headerH + r * rowH;
            sb.Append(HLine(0, el.W, ry + rowH));
            sb.Append(Text((r + 1).ToString(), rowNumW / 2, ry + rowH / 2, 9, ColorMuted, "middle"));
            if (r == 0)
            {
                for (var c = 0; c < cols; c++)
                {
                    var cx = rowNumW + c * colW;
                    sb.Append(Rect(cx + 1, ry + 1, colW - 1, rowH - 1, FillAccent, "none", 0));
                    sb.Append(Text(c == 0 ? "Name" : c == 1 ? "Value" : "—", cx + 4, ry + rowH / 2, 8, Accent));
                }
            }
        }

        sb.Append(VLine(rowNumW, gridY, gridY + gridH));
        sb.Append(HLine(0, el.W, gridY + gridH));
        var tabY = el.H - 24;
        sb.Append(Rect(0, tabY, el.W, 24, FillDark, "none", 0));
        sb.Append(HLine(0, el.W, tabY));
        for (var s = 0; s < sheets; s++)
        {
            sb.Append(Rect(4 + s * 62, tabY + 4, 56, 16, s == 0 ? Fill : FillDark, s == 0 ? Border : "none", 3));
            sb.Append(Text($"Sheet {s + 1}", 32 + s * 62, tabY + 12, 8, s == 0 ? ColorText : ColorMuted, "middle"));
        }

        sb.Append(Text("+", el.W - 16, tabY + 12, 10, ColorMuted, "middle"));
        Svg(b, sb.ToString());
    }

    private static void RenderGantt(WireframeElement el, RenderTreeBuilder b)
    {
        var taskCount = el.Props.GetInt("taskCount", 5);
        var period = el.Props.GetString("period", "week");
        var showProgressLabel = el.Props.GetBool("showProgressLabel", false);
        var toolbarLabels = el.Props.GetBool("toolbarLabels", false);
        var tabs = el.Props.GetString("sidebarMode", "rail") == "tabs";
        // Phase 11: the opt-in rollup band under the chart (five miniature cards).
        var showSummaryBand = el.Props.GetBool("showSummaryBand", false);
        var summaryH = showSummaryBand ? 34.0 : 0.0;
        var sb = new StringBuilder();
        sb.Append(Rect(0, 0, el.W, el.H, Fill, Border, 4));

        // Redesign (desktop): an optional text tab strip (SidebarMode=Tabs) over a grouped toolbar row;
        // with ToolbarLabels the buttons are pills with text, otherwise icon squares.
        var top = 0.0;
        if (tabs)
        {
            var tabNames = new[] { "Tasks", "Workload", "History", "Notifications", "Reports" };
            for (var i = 0; i < tabNames.Length; i++)
            {
                var tx0 = 12 + i * 64;
                sb.Append(Text(tabNames[i], tx0, 12, 8, i == 0 ? Accent : ColorMuted));
            }

            sb.Append(Rect(8, 22, 40, 2, Accent, "none", 1));
            sb.Append(HLine(0, el.W, 24));
            top = 24;
        }

        var toolbarH = 24.0;
        sb.Append(Rect(8, top + 4, toolbarLabels ? 48 : 20, 16, Accent, "none", 3));
        if (toolbarLabels)
            sb.Append(Text("+ Add task", 12, top + 12, 7, "#ffffff"));
        var tbX = toolbarLabels ? 64.0 : 32.0;
        var tbLabels = new[] { "Expand", "Collapse", "Day", "Week", "Month", "Filter", "Export" };
        for (var i = 0; i < tbLabels.Length && tbX < el.W - 40; i++)
        {
            var tbW = toolbarLabels ? 8 + tbLabels[i].Length * 4.5 : 16;
            sb.Append(Rect(tbX, top + 4, tbW, 16, Fill, Border, 3));
            if (toolbarLabels)
                sb.Append(Text(tbLabels[i], tbX + tbW / 2, top + 12, 7, ColorText, "middle"));
            tbX += tbW + 6 + (i == 1 || i == 4 ? 6 : 0);
        }

        sb.Append(HLine(0, el.W, top + toolbarH));
        top += toolbarH;

        var listW = el.W * 0.3;
        var chartW = el.W - listW;
        var headerH = 28.0;
        var rowH = (el.H - top - headerH - summaryH) / taskCount;
        sb.Append(Rect(0, top, el.W, headerH, FillDark, "none", 4));
        sb.Append(HLine(0, el.W, top + headerH));
        sb.Append(Text("Task", 12, top + headerH / 2, 10, ColorMuted));
        var ticks = period == "week" ? 7 : period == "month" ? 4 : 12;
        for (var t = 0; t < ticks; t++)
        {
            var tx = listW + t * chartW / ticks;
            sb.Append(VLine(tx, top, el.H, Border));
            sb.Append(Text($"W{t + 1}", tx + chartW / ticks / 2, top + headerH / 2, 8, ColorMuted, "middle"));
        }

        sb.Append(VLine(listW, top, el.H, BorderStrong));
        var barConfigs = new[] { (0.0, 0.4), (0.1, 0.55), (0.3, 0.3), (0.5, 0.4), (0.6, 0.35) };
        for (var t = 0; t < taskCount; t++)
        {
            var ry = top + headerH + t * rowH;
            if (t > 0)
                sb.Append(HLine(0, el.W, ry));
            sb.Append(Text($"Task {t + 1}", 12, ry + rowH / 2, 10, ColorText));
            var (bStart, bLen) = barConfigs[t % barConfigs.Length];
            var bx = listW + bStart * chartW;
            var bw = bLen * chartW;
            var isDone = t < 2;
            // Redesign: the first row is the project summary bar (a darker tone), the rest are
            // accent-tinted bars with a solid progress fill; done rows keep the green tone.
            if (t == 0)
            {
                sb.Append(Rect(bx, ry + rowH * 0.2, bw, rowH * 0.6, ColorMuted, "none", 3));
                continue;
            }

            sb.Append(Rect(bx, ry + rowH * 0.2, bw, rowH * 0.6, isDone ? "#dcfce7" : FillAccent, isDone ? "#86efac" : "#93c5fd", 3));
            sb.Append(Rect(bx, ry + rowH * 0.2, bw * (isDone ? 1.0 : 0.45), rowH * 0.6, isDone ? "#22c55e" : Accent, "none", 3));
            if (showProgressLabel)
                sb.Append(Text(isDone ? "100%" : "45%", bx + bw + 4, ry + rowH / 2, 8, ColorMuted));
        }

        // Today: a solid line with a pill in the header, as in the redesigned chart.
        var todayX = listW + chartW * 0.38;
        sb.Append(VLine(todayX, top + headerH, el.H - summaryH, "#ef4444"));
        sb.Append(Rect(todayX - 14, top + headerH - 8, 28, 16, "#ef4444", "none", 8));
        sb.Append(Text("Today", todayX, top + headerH, 7, "#ffffff", "middle", "500"));

        // Phase 11: the summary band — one strip of five miniature cards under the chart.
        if (showSummaryBand)
        {
            var bandY = el.H - summaryH;
            sb.Append(HLine(0, el.W, bandY));
            var labels = new[] { "Status", "Time", "Miles.", "Budget", "WBS" };
            var cardW = (el.W - 16 - 4 * 6) / labels.Length;
            for (var i = 0; i < labels.Length; i++)
            {
                var cx = 8 + i * (cardW + 6);
                sb.Append(Rect(cx, bandY + 5, cardW, 24, Fill, Border, 3));
                sb.Append(Text(labels[i], cx + 4, bandY + 12, 6, ColorMuted));
                sb.Append(Rect(cx + 4, bandY + 17, cardW * (0.25 + i * 0.12), 5, Accent, "none", 2));
            }
        }

        Svg(b, sb.ToString());
    }

    private static void RenderPivotTable(WireframeElement el, RenderTreeBuilder b)
    {
        var rows = el.Props.GetInt("rows", 4);
        var cols = el.Props.GetInt("columns", 4);
        var sb = new StringBuilder();
        sb.Append(Rect(0, 0, el.W, el.H, Fill, Border, 4));
        var rowLabelW = el.W * 0.22;
        var colH = 24.0;
        var rowH = (el.H - colH - 28) / rows;
        var colW = (el.W - rowLabelW) / cols;
        sb.Append(Rect(0, 0, el.W, 28, FillDark, "none", 4));
        sb.Append(HLine(0, el.W, 28));
        sb.Append(Text("Pivot", 10, 14, 10, ColorText, "start", "500"));
        sb.Append(Rect(el.W - 120, 4, 54, 20, FillAccent, "#93c5fd", 3));
        sb.Append(Text("Rows ▾", el.W - 93, 14, 8, Accent, "middle"));
        sb.Append(Rect(el.W - 62, 4, 54, 20, FillDark, Border, 3));
        sb.Append(Text("Cols ▾", el.W - 35, 14, 8, ColorMuted, "middle"));
        sb.Append(Rect(rowLabelW, 28, el.W - rowLabelW, colH, FillDark, "none", 0));
        sb.Append(HLine(0, el.W, 28 + colH));
        for (var c = 0; c < cols; c++)
        {
            var cx = rowLabelW + c * colW;
            sb.Append(VLine(cx, 28, el.H));
            sb.Append(Text($"Col {c + 1}", cx + colW / 2, 28 + colH / 2, 8, ColorMuted, "middle", "500"));
        }

        for (var r = 0; r < rows; r++)
        {
            var ry = 28 + colH + r * rowH;
            sb.Append(HLine(0, el.W, ry + rowH));
            sb.Append(Rect(0, ry, rowLabelW, rowH, FillDark, "none", 0));
            sb.Append(Text($"Row {r + 1}", rowLabelW / 2, ry + rowH / 2, 8, ColorMuted, "middle"));
            for (var c = 0; c < cols; c++)
            {
                var cx = rowLabelW + c * colW;
                var val = (r + 1) * (c + 1) * 12 + r * 7;
                sb.Append(Text(val.ToString(), cx + colW / 2, ry + rowH / 2, 9, ColorText, "middle"));
            }
        }

        sb.Append(VLine(rowLabelW, 28, el.H, BorderStrong));
        Svg(b, sb.ToString());
    }

    // X11: the wireframe prop "auto" (and any unknown value) ALWAYS renders the desktop variant —
    // the stencil communicates the chosen presentation, it does not re-flow the canvas layout.
    private static string ResolveDiagramLayout(WireframeElement el)
        => el.Props.GetString("layoutMode", "auto") is { } mode && mode is "desktop" or "tablet" or "mobile"
            ? mode
            : "desktop";

    private static void RenderDiagramEditor(WireframeElement el, RenderTreeBuilder b)
    {
        var title = el.Props.GetString("title", "Diagram");
        var nodeCount = el.Props.GetInt("nodeCount", 4);
        var layout = ResolveDiagramLayout(el);
        var viewer = el.Props.GetString("toolbarPreset", "full") == "viewer";
        var readOnly = el.Props.GetBool("readOnly", false);
        var showMinimap = el.Props.GetBool("showMinimap", true);

        var sb = new StringBuilder();
        sb.Append(Rect(0, 0, el.W, el.H, Fill, Border, 4));
        sb.Append($"<g data-layout='{layout}' data-preset='{(viewer ? "viewer" : "full")}'>");

        if (layout == "mobile")
        {
            // The host-pinned viewer state hides every editing surface on mobile, exactly like
            // ReadOnly="true" on the real component (no action bar, no panels sheet, a Read-only
            // header label).
            var hostViewer = readOnly || viewer;
            RenderDiagramEditorMobile(el, sb, title, nodeCount, hostViewer);
        }
        else
        {
            RenderDiagramEditorDesktopTablet(el, sb, title, nodeCount, layout, viewer, readOnly, showMinimap);
        }

        sb.Append("</g>");
        Svg(b, sb.ToString());
    }

    private static void RenderDiagramEditorDesktopTablet(
        WireframeElement el, StringBuilder sb, string title, int nodeCount,
        string layout, bool viewer, bool readOnly, bool showMinimap)
    {
        // Header: page tabs + document title + icon-only panel toggles.
        const double headerH = 30;
        sb.Append("<g data-region='header'>");
        sb.Append(Rect(0, 0, el.W, headerH, FillDark, "none", 4));
        sb.Append(HLine(0, el.W, headerH));
        sb.Append(Rect(8, 6, 56, 18, Fill, Border, 3));
        sb.Append(Text("Page 1", 36, 15, 9, ColorText, "middle"));
        sb.Append(Text(title, 76, 15, 10, ColorText, "start", "500"));
        var toggleX = el.W - 20;
        // The viewer preset docks neither the palette nor the minimap, so their header toggles
        // are hidden; the inspector toggle stays (the viewer can still inspect the document).
        var toggles = viewer ? new[] { "☰" } : new[] { "▦", "☰", "▢" };
        for (var t = 0; t < toggles.Length; t++)
        {
            sb.Append(Rect(toggleX - 22, 5, 20, 20, Fill, Border, 3));
            sb.Append(Text(toggles[t], toggleX - 12, 15, 9, ColorMuted, "middle"));
            toggleX -= 26;
        }

        sb.Append(Text("⋯", toggleX - 8, 15, 12, ColorMuted, "middle"));
        sb.Append("</g>");

        // Command row (one core toolbar row; the viewer preset keeps only zoom/view/search/export).
        var top = headerH;
        sb.Append("<g data-region='command-row'>");
        sb.Append(Rect(0, top, el.W, 30, Fill, "none", 0));
        sb.Append(HLine(0, el.W, top + 30));
        var tools = viewer
            ? new[] { "🔍", "⤢", "⭳" }
            : new[] { "↶", "↷", "✂", "⧉", "⌖", "✋", "↗", "＋", "⧩", "⭳" };
        var toolX = 10.0;
        foreach (var tool in tools)
        {
            sb.Append(Rect(toolX, top + 5, 20, 20, Fill, Border, 3));
            sb.Append(Text(tool, toolX + 10, top + 15, 9, ColorMuted, "middle"));
            toolX += 24;
            if (toolX > el.W - 30) break;
        }

        sb.Append(Text("⋯", el.W - 12, top + 15, 12, ColorMuted, "middle"));
        sb.Append("</g>");
        top += 30;

        // Side panels: full preset docks the palette and the inspector on desktop; on tablet the
        // palette collapses to a 44px rail and the inspector becomes a floating drawer.
        var leftW = 0.0;
        var rightW = 0.0;
        if (!viewer)
            leftW = layout == "desktop" ? Math.Max(64, el.W * 0.16) : 44;
        if (layout == "desktop" && !viewer)
            rightW = Math.Max(80, el.W * 0.22);
        if (leftW + rightW > el.W * 0.6) { leftW = 0; rightW = 0; }   // too narrow: canvas only
        var statusH = 22.0;
        var bottom = Math.Max(top, el.H - statusH);
        var bodyH = Math.Max(0, bottom - top);

        // Palette (docked grid on desktop, rail on tablet).
        if (leftW > 0)
        {
            sb.Append("<g data-region='palette'>");
            sb.Append(Rect(0, top, leftW, bodyH, FillDark, "none", 0));
            sb.Append(VLine(leftW, top, bottom));
            if (layout == "desktop")
            {
                sb.Append(Text("Shapes", leftW / 2, top + 12, 9, ColorMuted, "middle", "500"));
                var tiles = Math.Max(1, (int)(leftW / 32));
                var tile = Math.Min(26.0, (leftW - 8) / tiles - 4);
                for (var g = 0; g < 6; g++)
                {
                    var gx = 4 + (g % tiles) * (tile + 4);
                    var gy = top + 20 + (g / tiles) * (tile + 4);
                    if (gy + tile > bottom - 4) break;
                    sb.Append(Rect(gx, gy, tile, tile, g == 0 ? FillAccent : Fill, g == 0 ? "#93c5fd" : Border, 3));
                }
            }
            else
            {
                sb.Append(Text("▦", leftW / 2, top + 14, 10, ColorMuted, "middle"));
                sb.Append(Rect(6, top + 26, leftW - 12, 20, Fill, Border, 3));
                sb.Append(Rect(6, top + 52, leftW - 12, 20, Fill, Border, 3));
            }

            sb.Append("</g>");
        }

        var canvasX = leftW;
        var canvasW = Math.Max(0, el.W - leftW - rightW);
        var ch = bodyH;

        // Canvas with the paper page and a few nodes.
        sb.Append("<g data-region='canvas'>");
        sb.Append(Rect(canvasX, top, canvasW, ch, "white", "none", 0));
        var pageW = Math.Min(canvasW - 16, ch * 0.9);
        var pageH = Math.Min(ch - 12, pageW / 1.4);
        if (pageW > 20 && pageH > 20)
        {
            var pageX = canvasX + (canvasW - pageW) / 2;
            var pageY = top + (ch - pageH) / 2;
            sb.Append(Rect(pageX, pageY, pageW, pageH, "white", Border, 2));
            var nodeW = Math.Min(80, pageW * 0.32);
            var nodeH = 30.0;
            var nodePositions = new (double x, double y)[]
            {
                (pageX + pageW * 0.12, pageY + pageH * 0.2),
                (pageX + pageW * 0.6, pageY + pageH * 0.2),
                (pageX + pageW * 0.12, pageY + pageH * 0.62),
                (pageX + pageW * 0.6, pageY + pageH * 0.62)
            };
            for (var n = 0; n < Math.Min(nodeCount, nodePositions.Length); n++)
            {
                var (nx, ny) = nodePositions[n];
                if (nodeW < 12 || ny + nodeH > pageY + pageH - 4) continue;
                sb.Append(Rect(nx, ny, nodeW, nodeH, n == 0 ? FillAccent : Fill, n == 0 ? "#93c5fd" : Border, 6));
                sb.Append(Text($"Node {n + 1}", nx + nodeW / 2, ny + nodeH / 2, 9, n == 0 ? Accent : ColorText, "middle"));
            }

            if (nodeCount >= 2 && nodeW >= 12)
            {
                var (x1, y1) = (nodePositions[0].x + nodeW, nodePositions[0].y + nodeH / 2);
                var (x2, y2) = (nodePositions[1].x, nodePositions[1].y + nodeH / 2);
                sb.Append($"<line x1='{F(x1)}' y1='{F(y1)}' x2='{F(x2)}' y2='{F(y2)}' stroke='{BorderStrong}' stroke-width='1.5' marker-end='url(#arr)'></line>");
            }
        }

        sb.Append("</g>");

        // Inspector: docked column with Properties | Layers tabs on desktop, floating drawer on tablet.
        if (layout == "desktop" && rightW > 0)
        {
            var minimapH = showMinimap ? Math.Min(60, bodyH * 0.3) : 0;
            var inspectorH = Math.Max(0, bodyH - minimapH);
            sb.Append("<g data-region='inspector'>");
            sb.Append(Rect(el.W - rightW, top, rightW, inspectorH, FillDark, "none", 0));
            sb.Append(VLine(el.W - rightW, top, top + inspectorH));
            sb.Append(Text("Properties", el.W - rightW + rightW * 0.3, top + 12, 9, ColorText, "middle", "500"));
            sb.Append(Text("Layers", el.W - rightW + rightW * 0.75, top + 12, 9, ColorMuted, "middle", "500"));
            sb.Append(HLine(el.W - rightW + 6, el.W - 6, top + 20));
            for (var p = 0; p < 4; p++)
            {
                var py = top + 26 + p * 22;
                if (py + 16 > top + inspectorH - 4) break;
                sb.Append(Rect(el.W - rightW + 6, py, rightW - 12, 16, Fill, Border, 2));
            }

            sb.Append("</g>");

            if (showMinimap)
            {
                sb.Append("<g data-region='minimap'>");
                sb.Append(Rect(el.W - rightW, top + inspectorH, rightW, minimapH, Fill, Border, 0));
                sb.Append(Text("Minimap", el.W - rightW + 8, top + inspectorH + 10, 8, ColorMuted, "start"));
                sb.Append(Rect(el.W - rightW + 8, top + inspectorH + 16, rightW - 16, Math.Max(0, minimapH - 24), FillDark, Border, 2));
                sb.Append("</g>");
            }
        }
        else if (layout == "tablet" && !viewer)
        {
            // Floating inspector drawer over the canvas edge + the collapsible minimap overlay.
            var drawerW = Math.Min(Math.Max(70, el.W * 0.25), Math.Max(0, el.W - leftW - 16));
            if (drawerW >= 60)
            {
                var drawerH = Math.Max(0, bodyH - 16);
                sb.Append("<g data-region='inspector-drawer'>");
                sb.Append(Rect(el.W - drawerW - 6, top + 8, drawerW, drawerH, Fill, BorderStrong, 4));
                sb.Append(Text("Properties", el.W - drawerW - 6 + drawerW * 0.3, top + 22, 9, ColorText, "middle", "500"));
                sb.Append(Text("Layers", el.W - drawerW - 6 + drawerW * 0.75, top + 22, 9, ColorMuted, "middle", "500"));
                sb.Append(HLine(el.W - drawerW, el.W - 12, top + 30));
                for (var p = 0; p < 3; p++)
                    sb.Append(Rect(el.W - drawerW, top + 36 + p * 22, drawerW - 12, 16, FillDark, Border, 2));
                sb.Append("</g>");
            }

            if (showMinimap)
            {
                var minimapH = Math.Min(60, bodyH * 0.3);
                var minimapW = Math.Min(160, Math.Max(0, canvasW - 24));
                if (minimapW >= 60 && minimapH >= 30)
                {
                    sb.Append("<g data-region='minimap'>");
                    sb.Append(Rect(canvasX + canvasW - minimapW - 8, bottom - minimapH - 8, minimapW, minimapH,
                        Fill, BorderStrong, 4));
                    sb.Append(Text("▾ Minimap", canvasX + canvasW - minimapW, bottom - minimapH + 4, 8, ColorMuted, "start"));
                    sb.Append(Rect(canvasX + canvasW - minimapW, bottom - minimapH + 12, minimapW - 16, Math.Max(0, minimapH - 22),
                        FillDark, Border, 2));
                    sb.Append("</g>");
                }
            }
        }

        // Status bar: zoom controls + the editing state label.
        sb.Append("<g data-region='status-bar'>");
        sb.Append(Rect(0, bottom, el.W, statusH, FillDark, "none", 0));
        sb.Append(HLine(0, el.W, bottom));
        sb.Append(Text("− 100 % +", 12, bottom + statusH / 2, 9, ColorMuted, "start"));
        var state = readOnly || viewer ? "Read-only" : "Editing";
        sb.Append(Text(state, el.W - 12, bottom + statusH / 2, 9, ColorMuted, "end", "500"));
        sb.Append("</g>");
    }

    private static void RenderDiagramEditorMobile(
        WireframeElement el, StringBuilder sb, string title, int nodeCount, bool hostViewer)
    {
        // Compact header with the page tabs and the document title.
        const double headerH = 34;
        sb.Append("<g data-region='header'>");
        sb.Append(Rect(0, 0, el.W, headerH, FillDark, "none", 4));
        sb.Append(HLine(0, el.W, headerH));
        sb.Append(Rect(8, 8, 52, 18, Fill, Border, 3));
        sb.Append(Text("Page 1", 34, 17, 9, ColorText, "middle"));
        sb.Append(Text(title, 70, 17, 10, ColorText, "start", "500"));
        if (hostViewer)
            sb.Append(Text("Read-only", el.W - 10, 17, 9, ColorMuted, "end"));
        sb.Append("</g>");

        // The action bar (Select | Shapes | Properties | More) drives the mobile surfaces; a
        // host-pinned read-only viewer hides it (and the panels sheet) entirely.
        var barH = 44.0;
        var canvasH = Math.Max(0, el.H - headerH - (hostViewer ? 0 : barH));
        sb.Append("<g data-region='canvas'>");
        sb.Append(Rect(0, headerH, el.W, canvasH, "white", "none", 0));
        var pageW = Math.Min(el.W - 16, canvasH * 0.9);
        var pageH = Math.Min(canvasH - 10, pageW / 1.4);
        if (pageW > 20 && pageH > 20)
        {
            var pageX = (el.W - pageW) / 2;
            var pageY = headerH + (canvasH - pageH) / 2;
            sb.Append(Rect(pageX, pageY, pageW, pageH, "white", Border, 2));
            var nodeW = Math.Min(80, pageW * 0.32);
            var nodeH = 30.0;
            var positions = new (double x, double y)[]
            {
                (pageX + pageW * 0.12, pageY + pageH * 0.2),
                (pageX + pageW * 0.6, pageY + pageH * 0.2),
                (pageX + pageW * 0.12, pageY + pageH * 0.62),
                (pageX + pageW * 0.6, pageY + pageH * 0.62)
            };
            for (var n = 0; n < Math.Min(nodeCount, positions.Length); n++)
            {
                var (nx, ny) = positions[n];
                if (nodeW < 12 || ny + nodeH > pageY + pageH - 4) continue;
                sb.Append(Rect(nx, ny, nodeW, nodeH, n == 0 ? FillAccent : Fill, n == 0 ? "#93c5fd" : Border, 6));
                sb.Append(Text($"Node {n + 1}", nx + nodeW / 2, ny + nodeH / 2, 9, n == 0 ? Accent : ColorText, "middle"));
            }
        }

        sb.Append("</g>");

        if (!hostViewer)
        {
            sb.Append("<g data-region='action-bar'>");
            sb.Append(Rect(0, el.H - barH, el.W, barH, FillDark, "none", 0));
            sb.Append(HLine(0, el.W, el.H - barH));
            var labels = new[] { "Select", "Shapes", "Properties", "More" };
            var tileW = el.W / labels.Length;
            for (var i = 0; i < labels.Length; i++)
            {
                var tx = i * tileW;
                sb.Append(Rect(tx + 3, el.H - barH + 5, Math.Max(0, tileW - 6), barH - 10,
                    i == 0 ? FillAccent : Fill, i == 0 ? "#93c5fd" : Border, 4));
                sb.Append(Text(labels[i], tx + tileW / 2, el.H - barH / 2, 8,
                    i == 0 ? Accent : ColorText, "middle", i == 0 ? "500" : "normal"));
            }

            sb.Append("</g>");
        }
    }

    // 2ee91e41: like the diagram editor (U18/X11), the dashboard stencil honors layoutMode —
    // "auto" (and any unknown value) renders the desktop variant; tablet reflows the widget grid
    // to two columns and mobile to a single stacked column with a compact switcher chip. The
    // stencil communicates the chosen presentation, it does not re-flow the canvas layout.
    private static void RenderDashboard(WireframeElement el, RenderTreeBuilder b)
    {
        var title = el.Props.GetString("title", "Dashboard");
        var layout = ResolveDiagramLayout(el);
        var showSwitcher = el.Props.GetBool("showSwitcher", true);
        var editable = el.Props.GetBool("editable", false);
        var showAddWidget = el.Props.GetBool("showAddWidget", false);

        var sb = new StringBuilder();
        sb.Append(Rect(0, 0, el.W, el.H, Fill, Border, 8));
        sb.Append($"<g data-layout='{layout}'>");

        // Header: the document title plus, on the right, the dashboard switcher pill and — in
        // edit mode — the Edit affordance, like the real header actions. The pill width follows
        // the element width in every layout (42% capped at 268) and the pill gives up entirely
        // below 60px — below that it would crowd out the title it names. The title ellipsizes
        // to whatever the pill leaves behind.
        var pillW = Math.Min(268, Math.Max(0, el.W * 0.42));
        var showPill = showSwitcher && pillW >= 60;
        var pillX = Math.Max(0, el.W - 18.0 - pillW);
        var headerRight = el.W - 18.0;

        sb.Append("<g data-region='header'>");
        var titleMaxW = (showPill ? pillX : headerRight) - 8 - 18;
        sb.Append(Text(Ellipsize(title, titleMaxW, 14), 18, 22, 14, ColorText, "start", "700"));
        if (showPill)
        {
            sb.Append("<g data-region='switcher'>");
            sb.Append(Rect(pillX, 8, pillW, 28, Fill, Border, 6));
            sb.Append(Rect(pillX + 8, 17, 10, 10, FillAccent, "none", 2));
            sb.Append(Text(Ellipsize(title, pillW - 24 - 14, 9), pillX + 24, 23, 9, ColorText));
            sb.Append(Text("▾", pillX + pillW - 14, 23, 9, ColorMuted, "middle"));
            sb.Append("</g>");
            headerRight = pillX - 8;
        }

        if (editable)
            sb.Append(Text("Edit", headerRight, 22, 9, Accent, "end", "600"));

        sb.Append("</g>");

        // Widget grid: the column count comes from the props (default 3), tablet caps it at 2
        // and mobile stacks a single column; the tile count is columns × rows (rows default 2),
        // clamped 1..24. The optional Add-widget tile replaces the last cell, mirroring the
        // edit-mode drop target.
        var desktopColumns = Math.Max(1, el.Props.GetInt("columns", 3));
        var columns = layout == "mobile" ? 1 : layout == "tablet" ? Math.Min(desktopColumns, 2) : desktopColumns;
        var tileCount = Math.Clamp(columns * Math.Max(1, el.Props.GetInt("rows", 2)), 1, 24);
        var widgetCount = Math.Max(0, tileCount - (showAddWidget ? 1 : 0));
        var rows = (int)Math.Ceiling(tileCount / (double)columns);
        const double margin = 18;
        const double top = 48;
        const double bottomPad = 14;
        const double gap = 12;
        var cellW = (el.W - 2 * margin - (columns - 1) * gap) / columns;
        var cellH = (el.H - top - bottomPad - (rows - 1) * gap) / rows;
        var gridFits = cellW > 24 && cellH > 24;

        sb.Append($"<g data-region='widget-grid' data-columns='{columns}'>");
        if (gridFits)
        {
            for (var i = 0; i < widgetCount; i++)
            {
                var cx = margin + i % columns * (cellW + gap);
                var cy = top + i / columns * (cellH + gap);
                sb.Append("<g data-region='widget'>");
                sb.Append(Rect(cx, cy, cellW, cellH, "white", Border, 6));
                if (layout == "mobile")
                {
                    sb.Append(Rect(cx + 10, cy + cellH / 2 - 6, 12, 12, FillAccent, "none", 3));
                    sb.Append(Text($"Widget {i + 1}", cx + 28, cy + cellH / 2, 9, ColorText, "start", "600"));
                    if (cellH > 52)
                    {
                        sb.Append(Rect(cx + 10, cy + cellH - 22, cellW * 0.5, 6, FillAccent, "none", 3));
                        sb.Append(Rect(cx + 10, cy + cellH - 11, cellW * 0.7, 5, FillDark, "none", 2));
                    }
                }
                else
                {
                    sb.Append(Rect(cx + 12, cy + 12, 16, 16, FillAccent, "none", 4));
                    sb.Append(Text($"Widget {i + 1}", cx + 36, cy + 20, 10, ColorText, "start", "600"));
                    if (cellH > 88)
                    {
                        sb.Append(Rect(cx + 12, cy + 40, cellW * 0.45, 16, FillAccent, "none", 4));
                        sb.Append(Rect(cx + 12, cy + 68, cellW * 0.62, 8, FillDark, "none", 4));
                    }
                }

                sb.Append("</g>");
            }

            if (showAddWidget)
            {
                var last = tileCount - 1;
                var cx = margin + last % columns * (cellW + gap);
                var cy = top + last / columns * (cellH + gap);
                sb.Append("<g data-region='add-widget'>");
                sb.Append(Rect(cx, cy, cellW, cellH, "none", BorderStrong, 6));
                sb.Append(Text("+ Add widget", cx + cellW / 2, cy + cellH / 2, 10, ColorMuted, "middle"));
                sb.Append("</g>");
            }
        }

        sb.Append("</g>");
        sb.Append("</g>");
        Svg(b, sb.ToString());
    }

    private static void RenderDocumentEditor(WireframeElement el, RenderTreeBuilder b)
    {
        var title = el.Props.GetString("title", "Document");
        var showRuler = el.Props.GetBool("showRuler", true);
        var sb = new StringBuilder();
        sb.Append(Rect(0, 0, el.W, el.H, FillDark, Border, 4));
        sb.Append(Rect(0, 0, el.W, 36, Fill, Border, 4));
        sb.Append(HLine(0, el.W, 36));
        foreach (var (tool, tx) in new[] { ("B", 10.0), ("I", 28.0), ("U", 46.0), ("|", 60.0), ("H1", 68.0), ("H2", 82.0), ("|", 96.0), ("≡", 104.0), ("⋮≡", 116.0) })
        {
            if (tool == "|")
                sb.Append(VLine(tx, 6, 30));
            else
                sb.Append(Text(tool, tx, 18, 10, ColorMuted, "middle", "500"));
        }

        var contentY = 36.0;
        if (showRuler)
        {
            sb.Append(Rect(0, 36, el.W, 16, FillDark, "none", 0));
            sb.Append(HLine(0, el.W, 52));
            for (var r = 0; r < 8; r++)
                sb.Append($"<line x1='{F(r * el.W / 8)}' y1='36' x2='{F(r * el.W / 8)}' y2='44' stroke='{Border}' stroke-width='1'></line>");
            contentY = 52;
        }

        var pageW = el.W * 0.75;
        var pageX = (el.W - pageW) / 2;
        sb.Append(Rect(pageX, contentY + 8, pageW, el.H - contentY - 16, "white", Border, 2));
        sb.Append(Text(title, pageX + 10, contentY + 28, 13, ColorText, "start", "600"));
        for (var r = 0; r < 6; r++)
        {
            var ly = contentY + 44 + r * 14;
            if (ly + 8 > el.H - 16)
                break;
            var lw = r % 4 == 3 ? pageW * 0.55 : pageW * 0.88;
            sb.Append(Rect(pageX + 10, ly, lw, 6, FillDark, "none", 2));
        }

        Svg(b, sb.ToString());
    }

    private static void RenderNotionEditor(WireframeElement el, RenderTreeBuilder b)
    {
        var title = el.Props.GetString("title", "Page Title");
        var showSidebar = el.Props.GetBool("showSidebar", true);
        var sb = new StringBuilder();
        sb.Append(Rect(0, 0, el.W, el.H, Fill, Border, 4));
        var sideW = showSidebar ? el.W * 0.22 : 0;
        if (showSidebar)
        {
            sb.Append(Rect(0, 0, sideW, el.H, FillDark, "none", 4));
            sb.Append(VLine(sideW, 0, el.H, Border));
            for (var i = 0; i < 5; i++)
            {
                var y = 12 + i * 26;
                sb.Append(Rect(6, y, sideW - 12, 20, i == 0 ? FillAccent : Fill, i == 0 ? "#93c5fd" : "none", 3));
                sb.Append(Text($"Page {i + 1}", sideW / 2, y + 10, 9, i == 0 ? Accent : ColorMuted, "middle"));
            }
        }

        var contentX = sideW + 8;
        sb.Append(Text(title, contentX, 28, 15, ColorText, "start", "600"));
        var blocks = new[] { (28.0, 1.0), (8.0, 0.9), (8.0, 0.7), (8.0, 1.2), (8.0, 0.85) };
        var by = 46.0;
        foreach (var (bh, bwRatio) in blocks)
        {
            if (by + bh > el.H - 8)
                break;
            sb.Append(Rect(contentX, by, (el.W - contentX - 12) * bwRatio, bh, FillDark, "none", 2));
            by += bh + 8;
        }

        Svg(b, sb.ToString());
    }
}
