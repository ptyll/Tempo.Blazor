using System.Text;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Wireframe.Models;
using static Tempo.Blazor.Components.Wireframe.WireframeSvg;

namespace Tempo.Blazor.Components.Wireframe;

internal static partial class TempoNativeRendererDefinitions
{
    // The phone variant of the document editor (redesign phase 8, decision G2): a compact header row (title, state, search / undo / redo / kebab), the
    // tabs row (Home, Insert, Review, View), ONE tools row, the fitted page and the action bar (Comments, Print preview, the primary Save, More).
    // Chosen by a frame narrower than the stencil's default width: a phone-sized frame is a phone layout.
    private static bool IsDocumentEditorPhone(WireframeElement el) => el.W < 480;

    private static void RenderDocumentEditorPhone(WireframeElement el, RenderTreeBuilder b)
    {
        var title = el.Props.GetString("title", "Document");
        var sb = new StringBuilder();
        sb.Append(Rect(0, 0, el.W, el.H, FillDark, Border, 4));

        // header
        sb.Append(Rect(0, 0, el.W, 30, Fill, Border, 4));
        sb.Append(Rect(8, 9, 9, 12, "none", ColorMuted, 1));
        sb.Append(Text(Ellipsize(title, Math.Max(0, el.W - 130), 11), 24, 19, 11, ColorText, "start", "600"));
        for (var a = 0; a < 4; a++)
            sb.Append($"<circle cx='{F(el.W - 12 - a * 20)}' cy='15' r='{(a == 0 ? 2 : 5)}' fill='none' stroke='{Border}' stroke-width='1'></circle>");

        // tabs row
        var tabsY = 30.0;
        sb.Append(Rect(0, tabsY, el.W, 24, Fill, "none", 0));
        sb.Append(HLine(0, el.W, tabsY + 24));
        var tabW = Math.Min(54.0, el.W / 4);
        var tabLabels = new[] { "Home", "Insert", "Review", "View" };
        for (var t = 0; t < tabLabels.Length; t++)
        {
            sb.Append(Text(tabLabels[t], 8 + t * tabW + tabW / 2 - 4, tabsY + 15, 9, t == 0 ? Accent : ColorMuted, "middle", t == 0 ? "600" : "400"));
            if (t == 0)
                sb.Append(Rect(8 + 4, tabsY + 21, tabW - 8, 2, Accent, "none", 1));
        }

        // ONE tools row
        var toolsY = tabsY + 24;
        sb.Append(Rect(0, toolsY, el.W, 30, Fill, "none", 0));
        sb.Append(HLine(0, el.W, toolsY + 30));
        var tools = new[] { "B", "I", "U", "≡", "⋯" };
        for (var i = 0; i < tools.Length; i++)
            sb.Append(Text(tools[i], 14 + i * Math.Min(32.0, (el.W - 28) / tools.Length), toolsY + 19, 11, ColorMuted, "middle", "500"));

        // the page, as wide as the column
        var barH = 36.0;
        var pageY = toolsY + 38;
        sb.Append(Rect(8, pageY, el.W - 16, Math.Max(0, el.H - pageY - barH - 4), "white", Border, 2));
        sb.Append(Text(title, 16, pageY + 20, 12, ColorText, "start", "600"));
        for (var r = 0; r < 8; r++)
        {
            var ly = pageY + 34 + r * 12;
            if (ly + 6 > el.H - barH - 10)
                break;
            sb.Append(Rect(16, ly, (el.W - 32) * (r % 4 == 3 ? 0.55 : 0.92), 5, FillDark, "none", 2));
        }

        // the action bar: three tiles (the third, Save, is the primary) and More
        var barY = el.H - barH;
        sb.Append(Rect(0, barY, el.W, barH, Fill, Border, 0));
        var tile = el.W / 4.5;
        sb.Append(Text("Comments", tile * 0.5, barY + 22, 8, ColorMuted, "middle"));
        sb.Append(Text("Print", tile * 1.5, barY + 22, 8, ColorMuted, "middle"));
        sb.Append(Rect(tile * 2 + 4, barY + 5, tile * 1.25 - 8, barH - 10, Accent, "none", 4));
        sb.Append(Text("Save", tile * 2.625, barY + 22, 8, "white", "middle", "600"));
        sb.Append(Text("More", tile * 3.75 + tile * 0.25, barY + 22, 8, ColorMuted, "middle"));

        Svg(b, sb.ToString());
    }
}
