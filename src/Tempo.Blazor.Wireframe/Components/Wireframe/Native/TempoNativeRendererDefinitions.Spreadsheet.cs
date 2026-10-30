using System.Text;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Wireframe.Models;
using static Tempo.Blazor.Components.Wireframe.WireframeSvg;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// The <c>TmSpreadsheet</c> stencil of the native wireframe pack (redesign phase 6/8). Split out of
/// <c>TempoNativeRendererDefinitions.cs</c> when that file crossed the 1200-line source ceiling: the block is
/// self-contained (it shares only the constants and the primitive helpers of this partial class), so the
/// split is a move, not a refactor, and the stencil markup is byte-identical.
/// </summary>
internal static partial class TempoNativeRendererDefinitions
{
    // The spreadsheet stencil mirrors the CONTAINER-driven chrome of TmSpreadsheet (phase 6 of the
    // redesign): desktop keeps the tabbed ribbon with its labelled groups, tablet draws TWO compact
    // command rows without a tab strip, and mobile a compact header, ONE command row and the core
    // mobile action bar instead of the status bar. The band is the additive `layoutMode` prop
    // (auto|desktop|tablet|mobile) shared with the diagram/dashboard stencils: `auto` — and any
    // unknown value — renders the DESKTOP variant, because a stencil communicates the chosen
    // presentation rather than re-flowing the canvas (X11).
    private static void RenderSpreadsheet(WireframeElement el, RenderTreeBuilder b)
    {
        var rows = el.Props.GetInt("rows", 8);
        var cols = el.Props.GetInt("columns", 6);
        var sheets = el.Props.GetInt("sheetCount", 2);
        var showHeader = el.Props.GetBool("showHeader", false);
        var title = el.Props.GetString("title", "Spreadsheet");
        var layout = ResolveDiagramLayout(el);

        var sb = new StringBuilder();
        sb.Append(Rect(0, 0, el.W, el.H, Fill, Border, 4));
        sb.Append($"<g data-layout='{layout}'>");

        double y;
        if (layout == "mobile")
        {
            // A phone has no room for the document header: the COMPACT header replaces it (the two
            // are mutually exclusive on the real component, so exactly one header paints).
            y = AppendSpreadsheetMobileHeader(el, sb, title);
            y = AppendSpreadsheetCompactRows(el, sb, y, rowCount: 1);
        }
        else if (layout == "tablet")
        {
            y = AppendSpreadsheetDocumentHeader(el, sb, title, showHeader);
            y = AppendSpreadsheetCompactRows(el, sb, y, rowCount: 2);
        }
        else
        {
            y = AppendSpreadsheetDocumentHeader(el, sb, title, showHeader);
            y = AppendSpreadsheetRibbon(el, sb, y);
        }

        y = AppendSpreadsheetFormulaBar(el, sb, y);
        y = AppendSpreadsheetGrid(el, sb, y, cols, rows,
            reserve: layout == "mobile" ? SpreadsheetSheetTabsH + SpreadsheetActionBarH : SpreadsheetSheetTabsH + SpreadsheetStatusH);
        y = AppendSpreadsheetSheetTabs(el, sb, y, sheets);

        if (layout == "mobile")
            AppendSpreadsheetActionBar(el, sb);
        else
            AppendSpreadsheetStatusBar(el, sb);

        sb.Append("</g>");
        Svg(b, sb.ToString());
    }

    private const double SpreadsheetSheetTabsH = 22;
    private const double SpreadsheetStatusH = 20;
    private const double SpreadsheetActionBarH = 44;

    /// <summary>The opt-in document header (mirrors <c>TmSpreadsheet.ShowHeader</c>). Returns the new top.</summary>
    private static double AppendSpreadsheetDocumentHeader(
        WireframeElement el, StringBuilder sb, string title, bool showHeader)
    {
        if (!showHeader)
        {
            return 0;
        }

        const double headerH = 34;
        sb.Append("<g data-region='header'>");
        sb.Append(Rect(0, 0, el.W, headerH, Fill, "none", 4));
        sb.Append(Text(Ellipsize(title, el.W * 0.45, 13), 12, headerH / 2, 13, ColorText, "start", "600"));
        sb.Append(Text("\u25be", 12 + Math.Min(el.W * 0.45, title.Length * 7.2) + 8, headerH / 2, 9, ColorMuted));
        var actionX = el.W - 210;
        sb.Append(Rect(actionX, headerH / 2 - 5, 10, 10, FillDark, BorderStrong, 2));
        sb.Append(Text("Open", actionX + 14, headerH / 2, 9, ColorText));
        sb.Append(Rect(actionX + 61, headerH / 2 - 5, 10, 10, FillDark, BorderStrong, 2));
        sb.Append(Text("Download", actionX + 75, headerH / 2, 9, ColorText));
        sb.Append(VLine(actionX + 124, 9, headerH - 9));
        sb.Append(Rect(actionX + 136, headerH / 2 - 5, 10, 10, FillDark, BorderStrong, 2));
        sb.Append(Text("Show grid lines", actionX + 150, headerH / 2, 9, ColorText));
        sb.Append(Pill(actionX + 209, headerH / 2 - 7, 24, 14, Accent, "none"));
        sb.Append($"<circle cx='{F(actionX + 226)}' cy='{F(headerH / 2)}' r='5' fill='{Fill}'></circle>");
        sb.Append(HLine(0, el.W, headerH));
        sb.Append("</g>");
        return headerH;
    }

    /// <summary>The phone compact header: the document title plus the host action slot.</summary>
    private static double AppendSpreadsheetMobileHeader(WireframeElement el, StringBuilder sb, string title)
    {
        const double headerH = 34;
        sb.Append("<g data-region='mobile-header'>");
        sb.Append(Rect(0, 0, el.W, headerH, Fill, "none", 4));
        sb.Append(Text(Ellipsize(title, el.W - 46, 12), 12, headerH / 2, 12, ColorText, "start", "600"));
        sb.Append(Rect(el.W - 26, headerH / 2 - 8, 16, 16, Fill, Border, 3));
        sb.Append(Text("\u22ee", el.W - 18, headerH / 2, 10, ColorMuted, "middle"));
        sb.Append(HLine(0, el.W, headerH));
        sb.Append("</g>");
        return headerH;
    }

    /// <summary>The desktop ribbon: the tab strip plus the labelled command groups.</summary>
    private static double AppendSpreadsheetRibbon(WireframeElement el, StringBuilder sb, double y)
    {
        // ── Ribbon tabs (Home is active in the mockup; Rozložení/Revize/Vzorce are intentionally absent) ──
        const double tabsH = 26;
        string[] tabs = ["Home", "Insert", "Data", "View"];
        sb.Append("<g data-region='ribbon-tabs'>");
        sb.Append(Rect(0, y, el.W, tabsH, FillDark, "none", 0));
        var tabX = 12.0;
        for (var i = 0; i < tabs.Length; i++)
        {
            var active = i == 0;
            var w = tabs[i].Length * 6.2 + 18;
            sb.Append(Text(tabs[i], tabX, y + tabsH / 2, 9, active ? Accent : ColorMuted, "start", active ? "600" : "normal"));
            if (active)
            {
                sb.Append(Rect(tabX, y + tabsH - 2, w - 18, 2, Accent, "none", 0));
            }
            tabX += w;
        }
        sb.Append(HLine(0, el.W, y + tabsH));
        sb.Append("</g>");
        y += tabsH;

        // ── Ribbon groups with visible labels and big / small commands ──
        const double ribbonH = 62;
        string[] groups = ["Clipboard", "Font", "Alignment", "Number", "Cells", "Insert"];
        double[] weights = [1.55, 1.35, 1.05, 1.15, 1.25, 0.95];
        sb.Append("<g data-region='ribbon-groups'>");
        var innerX = 8.0;
        var innerW = el.W - 16;
        var weightSum = 0.0;
        foreach (var weight in weights)
        {
            weightSum += weight;
        }

        for (var g = 0; g < groups.Length; g++)
        {
            var groupW = innerW * weights[g] / weightSum;
            var gx = innerX;
            if (g > 0)
            {
                sb.Append(VLine(gx - 4, y + 6, y + ribbonH - 16));
            }

            if (g == 0)
            {
                // Large Undo / Redo, a column of small Cut / Copy, then large Paste.
                sb.Append(LargeButton(gx + 25, y + 20, "Undo", canUndo: true));
                sb.Append(LargeButton(gx + 25 + 40, y + 20, "Redo", canUndo: false));
                sb.Append(SmallButton(gx + 78, y + 12, "Cut"));
                sb.Append(SmallButton(gx + 78, y + 30, "Copy"));
                sb.Append(LargeButton(gx + 148, y + 20, "Paste", canUndo: true));
            }
            else if (g == 1)
            {
                sb.Append(Rect(gx + 4, y + 8, Math.Max(40, groupW * 0.34), 16, Fill, Border, 3));
                sb.Append(Text("Inter", gx + 10, y + 16, 8, ColorText));
                sb.Append(Rect(gx + 6 + Math.Max(40, groupW * 0.34), y + 8, 30, 16, Fill, Border, 3));
                sb.Append(Text("11", gx + 12 + Math.Max(40, groupW * 0.34), y + 16, 8, ColorText));
                sb.Append(Text("B", gx + 8, y + 34, 10, ColorText, "start", "700"));
                sb.Append(Text("I", gx + 26, y + 34, 10, ColorMuted, "start", "italic"));
                sb.Append(Text("U", gx + 44, y + 34, 10, ColorMuted, "start", "600"));
                sb.Append(Text("A", gx + 64, y + 34, 10, Accent, "start", "700"));
                sb.Append(Rect(gx + 63, y + 38, 12, 2, Accent, "none", 0));
            }
            else if (g == 2)
            {
                // Alignment: horizontal row + vertical row (incl. merge cells).
                for (var i = 0; i < 3; i++)
                {
                    sb.Append(AlignGlyph(gx + 8 + i * 18, y + 14, i));
                    sb.Append(AlignGlyph(gx + 8 + i * 18, y + 32, i + 3));
                }
                sb.Append(MergeGlyph(gx + 62, y + 32));
            }
            else if (g == 3)
            {
                sb.Append(Rect(gx + 4, y + 8, Math.Max(46, groupW * 0.6), 16, Fill, Border, 3));
                sb.Append(Text("Currency", gx + 10, y + 16, 8, ColorText));
                sb.Append(Text("%", gx + 8, y + 34, 9, ColorText));
                sb.Append(Text("000", gx + 26, y + 34, 8, ColorText));
                sb.Append(Text("+.0", gx + 50, y + 34, 8, ColorText));
                sb.Append(Text("-.00", gx + 70, y + 34, 8, ColorText));
            }
            else if (g == 4)
            {
                sb.Append(LargeButton(gx + 24, y + 20, "Insert row", canUndo: true));
                sb.Append(LargeButton(gx + 24 + 44, y + 20, "Insert column", canUndo: true));
                sb.Append(LargeButton(gx + 24 + 88, y + 20, "Delete", canUndo: false));
            }
            else
            {
                sb.Append(LargeButton(gx + 26, y + 20, "Insert link", canUndo: true));
                sb.Append(LargeButton(gx + 26 + 52, y + 20, "Insert image", canUndo: true));
            }

            sb.Append(Text(groups[g], gx + groupW / 2 - 4, y + ribbonH - 8, 8, ColorMuted, "middle"));
            innerX += groupW;
        }
        sb.Append("</g>");

        y += ribbonH;
        sb.Append(HLine(0, el.W, y));
        return y;
    }

    /// <summary>
    /// The compact command rows of the tablet (two rows) and phone (one row) chrome: icon chips, no
    /// tab strip and no group labels — the real compact toolbar reaches the same commands through
    /// <c>data-tm-group</c>/<c>data-command-id</c> without naming them.
    /// </summary>
    private static double AppendSpreadsheetCompactRows(
        WireframeElement el, StringBuilder sb, double y, int rowCount)
    {
        const double rowH = 30;
        // The trailing "More" chip of every row (its label is centred at el.W - 19). A phone is 340-390 px
        // wide, so a row CANNOT hold its whole command set: a chip that would reach "More" is skipped
        // instead of being painted under it (the phase-8 UX review read the overlap as broken controls).
        const double moreWidth = 30;
        var limit = el.W - moreWidth;
        bool Fits(double left, double width) => left + width <= limit;

        sb.Append($"<g data-region='command-row' data-row-count='{rowCount}'>");
        sb.Append(Rect(0, y, el.W, rowH * rowCount, Fill, "none", 0));

        for (var row = 0; row < rowCount; row++)
        {
            var top = y + row * rowH;
            if (row > 0)
            {
                sb.Append(HLine(0, el.W, top));
            }

            var x = 8.0;
            var cy = top + rowH / 2;
            if (row == 0)
            {
                // History.
                x = CommandChip(sb, x, cy, "\u21b6", active: true, limit: limit);
                x = CommandChip(sb, x, cy, "\u21b7", active: false, limit: limit);
                x += 4;
                if (Fits(x, 1))
                {
                    sb.Append(VLine(x, top + 7, top + rowH - 7));
                }

                x += 8;

                // Font family / size + the four character styles.
                if (Fits(x, 62))
                {
                    sb.Append(Rect(x, cy - 10, 62, 20, Fill, Border, 3));
                    sb.Append(Text("Inter", x + 6, cy, 8, ColorText));
                }

                x += 66;
                if (Fits(x, 30))
                {
                    sb.Append(Rect(x, cy - 10, 30, 20, Fill, Border, 3));
                    sb.Append(Text("11", x + 8, cy, 8, ColorText));
                }

                x += 36;
                x = CommandChip(sb, x, cy, "B", weight: "700", limit: limit);
                x = CommandChip(sb, x, cy, "I", weight: "italic", limit: limit);
                x = CommandChip(sb, x, cy, "U", weight: "600", limit: limit);
                x = CommandChip(sb, x, cy, "A", color: Accent, limit: limit);
                x += 4;
                if (Fits(x, 1))
                {
                    sb.Append(VLine(x, top + 7, top + rowH - 7));
                }

                x += 8;

                // Alignment + merge.
                for (var i = 0; i < 3; i++)
                {
                    if (Fits(x, 20))
                    {
                        sb.Append(Rect(x, cy - 10, 20, 20, Fill, Border, 3));
                        sb.Append(AlignGlyph(x + 4, cy - 5, i));
                    }

                    x += 23;
                }

                if (Fits(x, 20))
                {
                    sb.Append(Rect(x, cy - 10, 20, 20, Fill, Border, 3));
                    sb.Append(MergeGlyph(x + 4, cy - 5));
                }
            }
            else
            {
                // Number formats.
                x = CommandChip(sb, x, cy, "000", limit: limit);
                x = CommandChip(sb, x, cy, "%", limit: limit);
                x = CommandChip(sb, x, cy, ".00", limit: limit);
                x += 4;
                if (Fits(x, 1))
                {
                    sb.Append(VLine(x, top + 7, top + rowH - 7));
                }

                x += 8;

                // Rows / columns / link / image (the compact-row command set).
                x = CommandChip(sb, x, cy, "\uff0b", limit: limit);
                x = CommandChip(sb, x, cy, "\u2261", limit: limit);
                x = CommandChip(sb, x, cy, "\u26b8", limit: limit);
                x += 4;
                if (Fits(x, 1))
                {
                    sb.Append(VLine(x, top + 7, top + rowH - 7));
                }

                x += 8;

                // Data: filter / sort.
                x = CommandChip(sb, x, cy, "\u2315", limit: limit);
                x = CommandChip(sb, x, cy, "\u2195", limit: limit);
                x += 4;
                if (Fits(x, 1))
                {
                    sb.Append(VLine(x, top + 7, top + rowH - 7));
                }

                x += 8;
                x = CommandChip(sb, x, cy, "\u26ad", limit: limit);
                CommandChip(sb, x, cy, "\u25a3", limit: limit);
            }

            // The "More" overflow of every compact row.
            sb.Append(Rect(el.W - 30, cy - 10, 22, 20, Fill, Border, 3));
            sb.Append(Text("\u22ef", el.W - 19, cy, 11, ColorMuted, "middle"));
        }

        sb.Append(HLine(0, el.W, y + rowH * rowCount));
        sb.Append("</g>");
        return y + rowH * rowCount;
    }

    /// <summary>
    /// One chip of a compact command row. Returns the next left edge. A chip that would reach
    /// <paramref name="limit"/> (the trailing "More" chip) draws NOTHING and returns <paramref name="x"/>
    /// unchanged, so a narrow row stops instead of painting under "More" — the compact chrome's real
    /// overflow behaviour, where the commands that do not fit live in the More menu.
    /// </summary>
    private static double CommandChip(
        StringBuilder sb, double x, double cy, string glyph,
        bool active = false, string weight = "normal", string? color = null,
        double limit = double.PositiveInfinity)
    {
        var w = Math.Max(22, glyph.Length * 5.0 + 10);
        if (x + w > limit)
        {
            return x;
        }

        sb.Append(Rect(x, cy - 10, w, 20, active ? FillAccent : Fill, active ? Accent : Border, 3));
        sb.Append(Text(glyph, x + w / 2, cy, 8, color ?? (active ? Accent : ColorText), "middle", weight));
        return x + w + 2;
    }

    /// <summary>Name box, permanent fx and the value (the formula bar is present in every layout).</summary>
    private static double AppendSpreadsheetFormulaBar(WireframeElement el, StringBuilder sb, double y)
    {
        const double barH = 26;
        sb.Append("<g data-region='formula-bar'>");
        sb.Append(Rect(0, y, el.W, barH, Fill, "none", 0));
        sb.Append(Rect(8, y + 4, 54, 18, Fill, Border, 3));
        sb.Append(Text("A1", 18, y + 13, 9, ColorText));
        sb.Append(Text("\u25be", 50, y + 13, 7, ColorMuted));
        sb.Append(Text("fx", 74, y + 13, 9, ColorMuted, "start", "italic"));
        sb.Append(VLine(88, y + 5, y + barH - 5));
        sb.Append(Text("=SUM(A1:A5)", 96, y + 13, 9, ColorMuted));
        sb.Append(Rect(0, y, el.W, barH, "none", Border, 0));
        sb.Append(HLine(0, el.W, y + barH));
        sb.Append("</g>");
        return y + barH;
    }

    /// <summary>The grid; <paramref name="reserve"/> is the height kept for the tabs and the bottom chrome.</summary>
    private static double AppendSpreadsheetGrid(
        WireframeElement el, StringBuilder sb, double y, int cols, int rows, double reserve)
    {
        const double headerH = 18;
        const double rowNumW = 26;
        var gridY = y;
        var gridH = Math.Max(40, el.H - y - reserve);
        var colW = (el.W - rowNumW) / cols;
        var rowH = Math.Max(8, (gridH - headerH) / rows);

        sb.Append("<g data-region='grid'>");
        sb.Append(Rect(rowNumW, gridY, el.W - rowNumW, headerH, FillDark, "none", 0));
        sb.Append(HLine(0, el.W, gridY + headerH));
        for (var c = 0; c < cols; c++)
        {
            var cx = rowNumW + c * colW;
            sb.Append(VLine(cx, gridY, gridY + gridH));
            sb.Append(Text(((char)('A' + c % 26)).ToString(), cx + colW / 2, gridY + headerH / 2, 8, ColorMuted, "middle"));
        }

        for (var r = 0; r < rows; r++)
        {
            var ry = gridY + headerH + r * rowH;
            sb.Append(HLine(0, el.W, ry + rowH));
            sb.Append(Text((r + 1).ToString(), rowNumW / 2, ry + rowH / 2, 8, ColorMuted, "middle"));
            if (r == 0)
            {
                for (var c = 0; c < cols; c++)
                {
                    var cx = rowNumW + c * colW;
                    sb.Append(Rect(cx + 1, ry + 1, colW - 1, rowH - 1, FillAccent, "none", 0));
                    sb.Append(Text(c == 0 ? "Name" : c == 1 ? "Value" : "\u2014", cx + 4, ry + rowH / 2, 8, Accent));
                }
            }
        }

        sb.Append(VLine(rowNumW, gridY, gridY + gridH));
        sb.Append(HLine(0, el.W, gridY + gridH));
        sb.Append("</g>");
        return gridY + gridH;
    }

    /// <summary>The sheet tabs (active tab underlined) plus the add button. Returns the bottom edge.</summary>
    private static double AppendSpreadsheetSheetTabs(
        WireframeElement el, StringBuilder sb, double y, int sheets)
    {
        const double tabBarH = SpreadsheetSheetTabsH;
        sb.Append("<g data-region='sheet-tabs'>");
        sb.Append(Rect(0, y, el.W, tabBarH, FillDark, "none", 0));
        sb.Append(HLine(0, el.W, y));
        sb.Append(Text("\u2039", 10, y + tabBarH / 2, 9, ColorMuted, "middle"));
        for (var sIdx = 0; sIdx < sheets; sIdx++)
        {
            var tx = 22 + sIdx * 66;
            var active = sIdx == 0;
            sb.Append(Text($"Sheet {sIdx + 1}", tx, y + tabBarH / 2, 8, active ? ColorText : ColorMuted, "start", active ? "600" : "normal"));
            if (active)
            {
                sb.Append(Rect(tx - 4, y + tabBarH - 2, 58, 2, Accent, "none", 0));
            }
        }
        sb.Append(Text("+", el.W - 16, y + tabBarH / 2, 10, ColorMuted, "middle"));
        sb.Append("</g>");
        return y + tabBarH;
    }

    /// <summary>The desktop/tablet status bar (zoom + aggregations).</summary>
    private static void AppendSpreadsheetStatusBar(WireframeElement el, StringBuilder sb)
    {
        var statusH = SpreadsheetStatusH;
        var statusY = el.H - statusH;
        sb.Append("<g data-region='status-bar'>");
        sb.Append(Rect(0, statusY, el.W, statusH, Fill, "none", 0));
        sb.Append(HLine(0, el.W, statusY));
        sb.Append(Text("Ready", 10, statusY + statusH / 2, 8, ColorMuted));
        sb.Append(Text("Sum: 0   Count: 0   Average: 0", el.W - 190, statusY + statusH / 2, 8, ColorMuted));
        sb.Append(Text("100 %", el.W - 42, statusY + statusH / 2, 8, ColorMuted));
        sb.Append("</g>");
    }

    /// <summary>
    /// The core mobile action bar: three visible entries plus the "More" overflow — on a phone the
    /// zoom controls and the aggregations live in that overflow instead of a status bar.
    /// </summary>
    private static void AppendSpreadsheetActionBar(WireframeElement el, StringBuilder sb)
    {
        var barH = SpreadsheetActionBarH;
        var barY = el.H - barH;
        string[] items = ["Insert", "Format", "Image", "More"];
        sb.Append("<g data-region='action-bar'>");
        sb.Append(Rect(0, barY, el.W, barH, Fill, "none", 0));
        sb.Append(HLine(0, el.W, barY));
        var slotW = el.W / items.Length;
        for (var i = 0; i < items.Length; i++)
        {
            var cx = slotW * i + slotW / 2;
            var isMore = items[i] == "More";
            sb.Append(Rect(cx - 9, barY + 8, 18, 18, isMore ? FillDark : Fill, Border, 4));
            sb.Append(Text(isMore ? "\u22ef" : "\u25a3", cx, barY + 17, 10, isMore ? ColorMuted : ColorText, "middle"));
            sb.Append(Text(items[i], cx, barY + 34, 8, isMore ? ColorMuted : ColorText, "middle", isMore ? "normal" : "500"));
        }
        sb.Append("</g>");
    }

    /// <summary>A ribbon large button: an icon square above a two-line label.</summary>
    private static string LargeButton(double x, double y, string label, bool canUndo)
        => Rect(x - 18, y - 14, 36, 22, FillDark, Border, 3) +
           Rect(x - 8, y - 10, 8, 6, canUndo ? BorderStrong : Border, "none", 1) +
           Rect(x + 2, y - 10, 6, 6, canUndo ? BorderStrong : Border, "none", 1) +
           Text(label, x, y + 14, 7, canUndo ? ColorText : ColorMuted, "middle");

    /// <summary>A ribbon small button: icon and label on one line.</summary>
    private static string SmallButton(double x, double y, string label)
        => Rect(x, y, 6, 6, BorderStrong, "none", 1) +
           Text(label, x + 10, y + 3, 7, ColorText);

    /// <summary>One horizontal or vertical alignment glyph (indexes 0-2 horizontal, 3-5 vertical).</summary>
    private static string AlignGlyph(double x, double y, int variant)
    {
        var sb = new StringBuilder();
        var lengths = variant % 3 == 0 ? new[] { 12, 9, 12, 7 } : variant % 3 == 1 ? new[] { 12, 7, 12, 9 } : new[] { 12, 12, 12, 12 };
        for (var i = 0; i < 4; i++)
        {
            var lineW = lengths[i];
            var lx = variant % 3 == 1 ? x + (12 - lineW) / 2 : variant % 3 == 2 ? x + 12 - lineW : x;
            sb.Append($"<line x1='{F(lx)}' y1='{F(y + i * 3)}' x2='{F(lx + lineW)}' y2='{F(y + i * 3)}' stroke='{BorderStrong}' stroke-width='1.2'></line>");
        }
        return sb.ToString();
    }

    /// <summary>The merge-cells glyph of the Alignment group.</summary>
    private static string MergeGlyph(double x, double y)
        => $"<rect x='{F(x)}' y='{F(y)}' width='12' height='10' rx='1' fill='none' stroke='{BorderStrong}' stroke-width='1.2'></rect>" +
           $"<line x1='{F(x + 6)}' y1='{F(y)}' x2='{F(x + 6)}' y2='{F(y + 10)}' stroke='{Accent}' stroke-width='1.2' stroke-dasharray='2 2'></line>";
}
