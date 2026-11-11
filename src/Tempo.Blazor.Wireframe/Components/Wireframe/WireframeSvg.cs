using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Static helpers that write common wireframe SVG shapes into a <see cref="RenderTreeBuilder"/>.
/// All coordinates are relative to the element's top-left (0, 0).
/// Colors follow the wireframe neutral palette (grays + one accent).
/// </summary>
internal static class WireframeSvg
{
    // ── Wireframe color palette ───────────────────────────────────────────────
    internal const string Fill = "#f3f4f6";
    internal const string FillDark = "#e5e7eb";
    internal const string FillAccent = "#dbeafe";
    internal const string Border = "#d1d5db";
    internal const string BorderStrong = "#9ca3af";
    internal const string ColorText = "#374151";
    internal const string ColorMuted = "#6b7280";
    internal const string ColorLight = "#9ca3af";
    internal const string Accent = "#3b82f6";

    // ── F helper ─────────────────────────────────────────────────────────────
    internal static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // ── Markup emit ──────────────────────────────────────────────────────────
    //
    // Every AddMarkupContent chunk is parsed into its OWN DOM template by Blazor, so a fragment
    // MUST be a complete, standalone well-formed XML piece — an unclosed "<g transform=…>" frame
    // is auto-closed by the browser and its children become SIBLINGS, collapsing every
    // transformed sketch in the editor. Container nodes therefore go through OpenGroup/CloseGroup
    // (real Blazor elements), and Markup guards (DEBUG) against split group fragments regressing.
    //
    // Sequence numbers live inside an OpenRegion so repeated renders produce identical frames
    // and Blazor can diff them without rebuilding the DOM.

    internal static void Markup(RenderTreeBuilder b, string html)
    {
        RejectUnbalancedGroupMarkup(html);
        b.OpenRegion(0);
        b.AddMarkupContent(0, html);
        b.CloseRegion();
    }

    /// <summary>Opens a real Blazor <c>&lt;g&gt;</c> element (transform first, then opacity).</summary>
    internal static void OpenGroup(RenderTreeBuilder b, string transform, string? opacity = null)
    {
        b.OpenRegion(0);
        b.OpenElement(0, "g");
        b.AddAttribute(1, "transform", SafeRawAttribute(transform, "translate(0,0)"));
        if (opacity is not null)
            b.AddAttribute(2, "opacity", SafeRawAttribute(opacity, "1"));
    }

    /// <summary>Closes the <c>&lt;g&gt;</c> opened by <see cref="OpenGroup"/>.</summary>
    internal static void CloseGroup(RenderTreeBuilder b)
    {
        b.CloseElement();
        b.CloseRegion();
    }

    /// <summary>
    /// Prepares a value for <c>AddAttribute</c>: values that could break out of the attribute
    /// (markup chars, quotes, event-handler or javascript: payloads) fall back to
    /// <paramref name="fallback"/>; plain values are returned RAW because Blazor encodes
    /// <c>AddAttribute</c> values itself — pre-escaping would double-encode them.
    /// </summary>
    internal static string SafeRawAttribute(string value, string fallback)
        => IsDangerousAttributeValue(value) ? fallback : value;

    private static bool IsDangerousAttributeValue(string value)
        => StencilPackRenderer.ContainsDangerousMarkup(value)
           || value.IndexOfAny(['<', '>', '"', '\'']) >= 0;

    /// <summary>
    /// DEBUG-only guard: throws when a markup fragment contains an unbalanced <c>&lt;g&gt;</c>,
    /// which the browser would auto-close per-frame, collapsing transformed sketches. The method
    /// is annotated <c>[Conditional("DEBUG")]</c>, so it is a no-op in Release builds.
    /// </summary>
    [Conditional("DEBUG")]
    private static void RejectUnbalancedGroupMarkup(string html)
    {
        var depth = 0;
        var index = 0;
        while (index < html.Length)
        {
            var lt = html.IndexOf('<', index);
            if (lt < 0)
                break;

            if (lt + 1 < html.Length && html[lt + 1] == '/')
            {
                if (IsGroupTagName(html, lt + 2))
                {
                    depth--;
                    if (depth < 0)
                        ThrowUnbalanced(html, "stray </g>");
                }
            }
            else if (IsGroupTagName(html, lt + 1))
            {
                var tagEnd = html.IndexOf('>', lt);
                if (tagEnd < 0 || html[tagEnd - 1] != '/')
                    depth++;
            }

            index = lt + 1;
        }

        if (depth != 0)
            ThrowUnbalanced(html, "unclosed <g>");

        static bool IsGroupTagName(string html, int i)
            => i < html.Length
               && html[i] == 'g'
               && (i + 1 >= html.Length || html[i + 1] is ' ' or '>' or '\t' or '\r' or '\n' or '/');

        static void ThrowUnbalanced(string html, string reason)
            => throw new InvalidOperationException(
                $"WireframeSvg.Markup received an unbalanced fragment ({reason}). Blazor parses each " +
                "AddMarkupContent chunk into its own template, so a split <g> auto-closes in the " +
                "browser and its children become siblings. Emit containers via " +
                $"WireframeSvg.OpenGroup/CloseGroup instead. Fragment: {html}");
    }

    // ── Primitives ────────────────────────────────────────────────────────────

    /// <summary>Rounded rectangle (wireframe box).</summary>
    internal static string Rect(double x, double y, double w, double h,
        string fill = Fill, string stroke = Border, double rx = 4, double strokeWidth = 1)
        => $"<rect x='{F(x)}' y='{F(y)}' width='{F(w)}' height='{F(h)}' rx='{F(rx)}' fill='{fill}' stroke='{stroke}' stroke-width='{F(strokeWidth)}'></rect>";

    /// <summary>Single-line text label, vertically centred at y.</summary>
    internal static string Text(string content, double x, double y,
        double fontSize = 11, string fill = ColorText, string anchor = "start", string fontWeight = "normal",
        string dominantBaseline = "middle")
        => $"<text x='{F(x)}' y='{F(y)}' font-size='{F(fontSize)}' fill='{fill}' text-anchor='{anchor}' dominant-baseline='{dominantBaseline}' font-family='ui-sans-serif,system-ui' font-weight='{fontWeight}'>{Escape(content)}</text>";

    /// <summary>Centred text label inside a bounding box.</summary>
    internal static string TextCentred(string content, double boxW, double boxH,
        double fontSize = 11, string fill = ColorText, string fontWeight = "normal")
        => Text(content, boxW / 2, boxH / 2, fontSize, fill, "middle", fontWeight);

    /// <summary>Small label above an input field.</summary>
    internal static string FieldLabel(string label, bool required = false)
    {
        var text = required ? label + " *" : label;
        return Text(text, 0, -6, 10, ColorMuted);
    }

    /// <summary>Placeholder text inside an input rect.</summary>
    internal static string Placeholder(string text, double h)
        => Text(text, 8, h / 2, 10, ColorLight);

    /// <summary>Chevron-down arrow (for selects/dropdowns).</summary>
    internal static string ChevronDown(double x, double y, double size = 8)
    {
        var h = size * 0.6;
        return $"<polyline points='{F(x)},{F(y)} {F(x + size / 2)},{F(y + h)} {F(x + size)},{F(y)}' fill='none' stroke='{BorderStrong}' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'></polyline>";
    }

    /// <summary>Simple SVG icon approximation (circle, magnifier, calendar, clock, …).</summary>
    internal static string Icon(string name, double cx, double cy, double size = 12)
    {
        var r = size / 2;
        return name switch
        {
            "search" or "magnifier" =>
                $"<circle cx='{F(cx - 1)}' cy='{F(cy - 1)}' r='{F(r * 0.65)}' fill='none' stroke='{BorderStrong}' stroke-width='1.5'></circle>" +
                $"<line x1='{F(cx + r * 0.15)}' y1='{F(cy + r * 0.15)}' x2='{F(cx + r * 0.75)}' y2='{F(cy + r * 0.75)}' stroke='{BorderStrong}' stroke-width='1.5' stroke-linecap='round'></line>",
            "calendar" =>
                $"<rect x='{F(cx - r)}' y='{F(cy - r * 0.8)}' width='{F(size)}' height='{F(size * 0.85)}' rx='2' fill='none' stroke='{BorderStrong}' stroke-width='1.5'></rect>" +
                $"<line x1='{F(cx - r * 0.4)}' y1='{F(cy - r * 1.1)}' x2='{F(cx - r * 0.4)}' y2='{F(cy - r * 0.5)}' stroke='{BorderStrong}' stroke-width='1.5'></line>" +
                $"<line x1='{F(cx + r * 0.4)}' y1='{F(cy - r * 1.1)}' x2='{F(cx + r * 0.4)}' y2='{F(cy - r * 0.5)}' stroke='{BorderStrong}' stroke-width='1.5'></line>",
            "clock" =>
                $"<circle cx='{F(cx)}' cy='{F(cy)}' r='{F(r)}' fill='none' stroke='{BorderStrong}' stroke-width='1.5'></circle>" +
                $"<polyline points='{F(cx)},{F(cy)} {F(cx)},{F(cy - r * 0.6)} {F(cx + r * 0.4)},{F(cy)}' fill='none' stroke='{BorderStrong}' stroke-width='1.5' stroke-linecap='round'></polyline>",
            "copy" =>
                $"<rect x='{F(cx - r + 2)}' y='{F(cy - r)}' width='{F(size - 3)}' height='{F(size - 3)}' rx='1' fill='none' stroke='{BorderStrong}' stroke-width='1.5'></rect>" +
                $"<rect x='{F(cx - r)}' y='{F(cy - r + 2)}' width='{F(size - 3)}' height='{F(size - 3)}' rx='1' fill='{Fill}' stroke='{BorderStrong}' stroke-width='1.5'></rect>",
            "upload" =>
                $"<polyline points='{F(cx)},{F(cy + r * 0.6)} {F(cx)},{F(cy - r * 0.4)} {F(cx - r * 0.5)},{F(cy - r * 0.0)}' fill='none' stroke='{BorderStrong}' stroke-width='1.5' stroke-linecap='round'></polyline>" +
                $"<polyline points='{F(cx - r * 0.5)},{F(cy - r * 0.0)} {F(cx)},{F(cy - r * 0.4)} {F(cx + r * 0.5)},{F(cy - r * 0.0)}' fill='none' stroke='{BorderStrong}' stroke-width='1.5' stroke-linecap='round'></polyline>" +
                $"<line x1='{F(cx - r)}' y1='{F(cy + r)}' x2='{F(cx + r)}' y2='{F(cy + r)}' stroke='{BorderStrong}' stroke-width='1.5' stroke-linecap='round'></line>",
            "bell" =>
                $"<path d='M{F(cx)},{F(cy - r)} a{F(r * 0.8)},{F(r * 0.8)} 0 0 1 {F(r * 0.8)},{F(r * 0.8)} v{F(r * 0.4)} h{F(-r * 1.6)} v{F(-r * 0.4)} a{F(r * 0.8)},{F(r * 0.8)} 0 0 1 {F(r * 0.8)},{F(-r * 0.8)}' fill='{Fill}' stroke='{BorderStrong}' stroke-width='1.5'></path>" +
                $"<line x1='{F(cx - r * 0.2)}' y1='{F(cy + r * 0.8)}' x2='{F(cx + r * 0.2)}' y2='{F(cy + r * 0.8)}' stroke='{BorderStrong}' stroke-width='1.5'></line>",
            "spinner" or "loader" =>
                $"<circle cx='{F(cx)}' cy='{F(cy)}' r='{F(r)}' fill='none' stroke='{FillDark}' stroke-width='2'></circle>" +
                $"<path d='M{F(cx)},{F(cy - r)} a{F(r)},{F(r)} 0 0 1 {F(r)},{F(r)}' fill='none' stroke='{Accent}' stroke-width='2' stroke-linecap='round'></path>",
            "check" =>
                $"<polyline points='{F(cx - r * 0.6)},{F(cy)} {F(cx - r * 0.1)},{F(cy + r * 0.6)} {F(cx + r * 0.7)},{F(cy - r * 0.6)}' fill='none' stroke='{Accent}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'></polyline>",
            "x" or "close" =>
                $"<line x1='{F(cx - r * 0.6)}' y1='{F(cy - r * 0.6)}' x2='{F(cx + r * 0.6)}' y2='{F(cy + r * 0.6)}' stroke='{BorderStrong}' stroke-width='1.5' stroke-linecap='round'></line>" +
                $"<line x1='{F(cx + r * 0.6)}' y1='{F(cy - r * 0.6)}' x2='{F(cx - r * 0.6)}' y2='{F(cy + r * 0.6)}' stroke='{BorderStrong}' stroke-width='1.5' stroke-linecap='round'></line>",
            "image" or "photo" =>
                $"<rect x='{F(cx - r)}' y='{F(cy - r)}' width='{F(size)}' height='{F(size)}' rx='2' fill='{FillDark}' stroke='{Border}'></rect>" +
                $"<circle cx='{F(cx - r * 0.3)}' cy='{F(cy - r * 0.3)}' r='{F(r * 0.25)}' fill='{ColorMuted}'></circle>" +
                $"<polyline points='{F(cx - r)},{F(cy + r * 0.3)} {F(cx - r * 0.2)},{F(cy - r * 0.2)} {F(cx + r * 0.4)},{F(cy + r * 0.4)} {F(cx + r * 0.7)},{F(cy)} {F(cx + r)},{F(cy + r)}' fill='none' stroke='{ColorMuted}' stroke-width='1'></polyline>",
            "lock" =>
                $"<rect x='{F(cx - r * 0.6)}' y='{F(cy - r * 0.2)}' width='{F(r * 1.2)}' height='{F(r)}' rx='2' fill='none' stroke='{BorderStrong}' stroke-width='1.5'></rect>" +
                $"<circle cx='{F(cx)}' cy='{F(cy - r * 0.3)}' r='{F(r * 0.25)}' fill='none' stroke='{BorderStrong}' stroke-width='1.5'></circle>",
            "user" or "avatar" =>
                $"<circle cx='{F(cx)}' cy='{F(cy - r * 0.2)}' r='{F(r * 0.45)}' fill='{FillDark}' stroke='{Border}' stroke-width='1'></circle>" +
                $"<path d='M{F(cx - r)},{F(cy + r)} a{F(r)},{F(r * 0.5)} 0 0 1 {F(r * 2)},0' fill='{FillDark}' stroke='{Border}' stroke-width='1'></path>",
            _ =>
                // Generic circle fallback
                $"<circle cx='{F(cx)}' cy='{F(cy)}' r='{F(r)}' fill='none' stroke='{BorderStrong}' stroke-width='1.5'></circle>"
        };
    }

    /// <summary>Dashed border rectangle (for drop zones, empty states).</summary>
    internal static string DashedRect(double w, double h, double rx = 6)
        => $"<rect width='{F(w)}' height='{F(h)}' rx='{F(rx)}' fill='{Fill}' stroke='{Border}' stroke-width='1.5' stroke-dasharray='6 3'></rect>";

    /// <summary>Horizontal divider line.</summary>
    internal static string HLine(double x1, double x2, double y, string stroke = Border)
        => $"<line x1='{F(x1)}' y1='{F(y)}' x2='{F(x2)}' y2='{F(y)}' stroke='{stroke}' stroke-width='1'></line>";

    /// <summary>Vertical divider line.</summary>
    internal static string VLine(double x, double y1, double y2, string stroke = Border)
        => $"<line x1='{F(x)}' y1='{F(y1)}' x2='{F(x)}' y2='{F(y2)}' stroke='{stroke}' stroke-width='1'></line>";

    /// <summary>Pill shape (fully-rounded rect).</summary>
    internal static string Pill(double x, double y, double w, double h,
        string fill = FillDark, string stroke = Border)
        => $"<rect x='{F(x)}' y='{F(y)}' width='{F(w)}' height='{F(h)}' rx='{F(h / 2)}' fill='{fill}' stroke='{stroke}' stroke-width='1'></rect>";

    /// <summary>Renders a rows-×-columns placeholder grid (e.g. for tables, galleries).</summary>
    internal static string Grid(double w, double h, int cols, int rows,
        string fill = Fill, string headerFill = FillDark)
    {
        var sb = new StringBuilder();
        var rowH = h / (rows + 1); // +1 for header
        var colW = w / cols;

        // Header row
        sb.Append(Rect(0, 0, w, rowH, headerFill, Border, 0));
        for (var c = 0; c < cols; c++)
        {
            var tx = c * colW + colW / 2;
            sb.Append(Text($"Col {c + 1}", tx, rowH / 2, 9, ColorMuted, "middle"));
        }

        // Data rows
        for (var r = 0; r < rows; r++)
        {
            var ry = rowH * (r + 1);
            sb.Append(HLine(0, w, ry + rowH));
            for (var c = 1; c < cols; c++)
                sb.Append(VLine(c * colW, ry, ry + rowH));
        }

        // Outer border
        sb.Append(Rect(0, 0, w, h, "none", Border, 0));
        return sb.ToString();
    }

    // ── Input field composite ─────────────────────────────────────────────────

    /// <summary>Standard input field: rect + optional label above + placeholder inside.</summary>
    internal static string InputField(double w, double h, string label, string placeholder,
        bool required = false, bool hasIcon = false, bool hasChevron = false,
        bool disabled = false, bool readOnly = false)
    {
        var sb = new StringBuilder();
        if (disabled) sb.Append("<g opacity='0.45'>");
        if (!string.IsNullOrEmpty(label))
            sb.Append(FieldLabel(label, required));
        var rectFill = disabled ? FillDark : Fill;
        var rectBorder = readOnly ? "none" : Border;
        sb.Append(Rect(0, 0, w, h, rectFill, rectBorder));
        if (readOnly)
            sb.Append($"<rect x='0' y='0' width='{F(w)}' height='{F(h)}' fill='none' stroke='{Border}' stroke-width='1' stroke-dasharray='4 2' rx='3'></rect>");
        if (hasIcon)
            sb.Append(Icon("search", h / 2, h / 2, h * 0.55));
        var textX = hasIcon ? h * 0.8 : 8.0;
        if (!string.IsNullOrEmpty(placeholder))
            sb.Append(Text(placeholder, textX, h / 2, 10, ColorLight));
        if (hasChevron)
            sb.Append(ChevronDown(w - 16, h / 2 - 4));
        if (disabled) sb.Append("</g>");
        return sb.ToString();
    }

    // ── XML escaping ──────────────────────────────────────────────────────────

    internal static string Escape(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
