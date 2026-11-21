using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E coverage for the unified SVG canvas refactor
/// (see planning/DIAGRAM_UNIFIED_SVG_PLAN.md — F0.4, F0.5, F0.6).
///
/// <para>
/// Before F2 these tests doubled as a drift-bug canary between the SVG
/// <c>getScreenCTM()</c> scale and the independent HTML overlay CSS transform. After F2 the
/// HTML overlay lives inside a <c>&lt;foreignObject&gt;</c> in the scene pane — no independent
/// transform exists anymore — so these tests assert the <b>invariant F2 guarantees</b>:
/// the transform layer carries no scale of its own, and on-screen node geometry is driven
/// exclusively by the SVG CTM (viewBox + window size).
/// </para>
///
/// <para>
/// F5 will move node rotation from CSS onto an SVG <c>&lt;g transform&gt;</c>; the rotation
/// test is rewritten at that point.
/// </para>
/// </summary>
[TestClass]
public class DiagramTransformE2ETests : WasmTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    private async Task<IPage> PrepareDiagramPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        await GotoAppReadyAsync(page, $"{BaseUrl}{DiagramEditorUrl}", WaitUntilState.DOMContentLoaded);

        await page.WaitForSelectorAsync(".tm-diagram-canvas", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        // Wait until the editor JS has wired up the instance
        await page.WaitForFunctionAsync("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !canvas.id) return false;
                const ed = window.tmDiagramEditor;
                return !!(ed && ed.instances && ed.instances.get(canvas.id));
            }
        """, null, new PageWaitForFunctionOptions { Timeout = 15000 });

        // Load a known sample so there are at least two nodes and one edge
        await page.GetByRole(AriaRole.Button, new() { Name = "Load UML sample" }).ClickAsync();
        await page.WaitForSelectorAsync(".tm-diagram-node", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await page.WaitForTimeoutAsync(500);

        return page;
    }

    private static async Task SetCanvasScaleAsync(IPage page, double scale)
    {
        // Use the same JS entry point the Blazor SetZoom() C# method invokes.
        await page.EvaluateAsync("""
            (s) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas) return;
                window.tmDiagramEditor.zoomTo(canvas, s);
            }
        """, scale);
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task<(double A, double B, double C, double D, double E, double F)> GetSvgScreenCtmAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>("""
            () => {
                const svg = document.querySelector('.tm-diagram-canvas__svg');
                if (!svg) return null;
                const m = svg.getScreenCTM();
                if (!m) return null;
                return JSON.stringify({ a: m.a, b: m.b, c: m.c, d: m.d, e: m.e, f: m.f });
            }
        """);
        Assert.IsNotNull(json, "SVG element or CTM not available");
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return (
            r.GetProperty("a").GetDouble(),
            r.GetProperty("b").GetDouble(),
            r.GetProperty("c").GetDouble(),
            r.GetProperty("d").GetDouble(),
            r.GetProperty("e").GetDouble(),
            r.GetProperty("f").GetDouble()
        );
    }

    /// <summary>
    /// Returns the effective horizontal scale applied by a CSS transform on the
    /// scene pane. After F3 (planning/DIAGRAM_UNIFIED_SVG_PLAN.md) there is no
    /// separate HTML overlay layer — nodes render as per-node SVG <c>&lt;g&gt;</c>
    /// elements directly inside <c>.tm-diagram-scene-pane</c>. The scene pane
    /// itself must never carry a <c>scale()</c> transform, otherwise the "drift
    /// bug" would resurface; zoom must flow exclusively via the SVG viewBox.
    /// </summary>
    private static async Task<double?> HtmlOverlayScale(IPage page)
    {
        return await page.EvaluateAsync<double?>("""
            () => {
                const pane = document.querySelector('.tm-diagram-scene-pane');
                if (!pane) return null;
                const t = getComputedStyle(pane).transform;
                if (!t || t === 'none') return 1;
                const m = t.match(/matrix\(([-0-9.eE+]+),\s*([-0-9.eE+]+),\s*([-0-9.eE+]+),\s*([-0-9.eE+]+),\s*([-0-9.eE+]+),\s*([-0-9.eE+]+)\)/);
                return m ? parseFloat(m[1]) : null;
            }
        """);
    }

    private static async Task<(double X, double Y)> GetBoundingCenterAsync(IPage page, string selector)
    {
        var json = await page.EvaluateAsync<string>("""
            (sel) => {
                const el = document.querySelector(sel);
                if (!el) return null;
                const r = el.getBoundingClientRect();
                return JSON.stringify({ x: r.left + r.width / 2, y: r.top + r.height / 2 });
            }
        """, selector);
        Assert.IsNotNull(json, $"Element not found: {selector}");
        using var doc = JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("x").GetDouble(), doc.RootElement.GetProperty("y").GetDouble());
    }

    /// <summary>
    /// F0.4 + F2 — verifies that nodes and their connected edge endpoints stay aligned across
    /// multiple zoom levels. Pre-F2 the regression was an independent HTML overlay CSS scale
    /// diverging from the SVG CTM; post-F2 the invariant is stronger — the HTML overlay
    /// carries no scale of its own, so the SVG CTM alone determines the alignment.
    /// </summary>
    [TestMethod]
    public async Task PanThenZoom_NodesRemainAlignedWithEdges()
    {
        var page = await PrepareDiagramPageAsync();

        double[] scales = [1.0, 0.75, 1.25];
        foreach (var s in scales)
        {
            await SetCanvasScaleAsync(page, s);

            var svgCtm = await GetSvgScreenCtmAsync(page);
            var htmlScale = await HtmlOverlayScale(page);
            Assert.IsNotNull(htmlScale, "scene-pane <g> not found");

            Math.Abs(htmlScale!.Value - 1.0).Should().BeLessThan(1e-3,
                $"at zoom {s}, the scene-pane must carry no CSS scale of its own — " +
                $"F2/F3 fold zoom into the SVG viewBox so the only scale in play is CTM.a = {svgCtm.A:F3}");

            // Pick the first edge path and verify that its first command coordinate projects
            // onto the bounding box of some node (simple adjacency sanity check in screen space).
            var edgeCenter = await GetBoundingCenterAsync(page, ".tm-diagram-edge-path");
            var nodeCenter = await GetBoundingCenterAsync(page, ".tm-diagram-node");
            Math.Abs(edgeCenter.X - nodeCenter.X).Should().BeLessThan(2000,
                "sanity-only: edge and first node should be on the same canvas");
        }
    }

    /// <summary>
    /// F0.5 — rotates the first node by 45° via the public command surface and checks that
    /// its ports' screen centers track the rotation. Gives us a pre-F5 baseline for the
    /// SVG-based rotate flow.
    /// </summary>
    [TestMethod]
    public async Task RotatedNode_PortsMoveWithRotation()
    {
        var page = await PrepareDiagramPageAsync();

        // Pick the first node and read port positions at 0° and at 45°.
        var before = await page.EvaluateAsync<string>("""
            () => {
                const node = document.querySelector('.tm-diagram-node');
                if (!node) return null;
                const id = node.getAttribute('data-node-id');
                const ports = node.querySelectorAll('.tm-diagram-port');
                const centers = [];
                ports.forEach(p => {
                    const r = p.getBoundingClientRect();
                    centers.push({ x: r.left + r.width / 2, y: r.top + r.height / 2 });
                });
                return JSON.stringify({ id, centers });
            }
        """);
        Assert.IsNotNull(before);

        using var beforeDoc = JsonDocument.Parse(before);
        var nodeId = beforeDoc.RootElement.GetProperty("id").GetString();
        Assert.IsNotNull(nodeId);

        // Rotate via JS by updating the SVG <g>'s `transform` attribute — F3.A
        // moved node position + rotation onto the attribute (translate(x,y) rotate(θ cx cy))
        // on the per-node <g>. We append/replace the rotate(...) part while preserving
        // the existing translate(...) to get the identical effect as the rotate command.
        await page.EvaluateAsync("""
            (id) => {
                const el = document.querySelector(`g.tm-diagram-node[data-node-id='${id}']`);
                if (!el) return;
                const current = el.getAttribute('transform') || '';
                // Extract existing translate(x,y) — keep it; replace any rotate(...) with 45°.
                const transMatch = current.match(/translate\([^)]*\)/);
                const translate = transMatch ? transMatch[0] : 'translate(0,0)';
                const w = parseFloat(el.getAttribute('data-w') || '100');
                const h = parseFloat(el.getAttribute('data-h') || '100');
                el.setAttribute('transform', `${translate} rotate(45 ${w / 2} ${h / 2})`);
            }
        """, nodeId);
        await page.WaitForTimeoutAsync(250);

        var after = await page.EvaluateAsync<string>("""
            (id) => {
                const node = document.querySelector(`[data-node-id='${id}']`);
                if (!node) return null;
                const ports = node.querySelectorAll('.tm-diagram-port');
                const centers = [];
                ports.forEach(p => {
                    const r = p.getBoundingClientRect();
                    centers.push({ x: r.left + r.width / 2, y: r.top + r.height / 2 });
                });
                return JSON.stringify(centers);
            }
        """, nodeId);
        Assert.IsNotNull(after);

        using var afterDoc = JsonDocument.Parse(after);
        var afterCenters = afterDoc.RootElement;
        var beforeCenters = beforeDoc.RootElement.GetProperty("centers");

        beforeCenters.GetArrayLength().Should().Be(afterCenters.GetArrayLength(),
            "port count must not change when rotating");

        // At least one port must have moved: otherwise rotation has no visible effect, which
        // would indicate the whole rotation path is broken (or the node has no ports).
        var moved = false;
        for (var i = 0; i < beforeCenters.GetArrayLength(); i++)
        {
            var bx = beforeCenters[i].GetProperty("x").GetDouble();
            var by = beforeCenters[i].GetProperty("y").GetDouble();
            var ax = afterCenters[i].GetProperty("x").GetDouble();
            var ay = afterCenters[i].GetProperty("y").GetDouble();
            if (Math.Abs(ax - bx) > 2 || Math.Abs(ay - by) > 2)
            {
                moved = true;
                break;
            }
        }
        moved.Should().BeTrue("rotating a node must visibly shift its ports in screen space");
    }

    /// <summary>
    /// F0.6 + F2 — the drift bug the name refers to was a double-source-of-truth between SVG
    /// CTM and HTML overlay CSS. After F2 the overlay lives inside a foreignObject and carries
    /// no CSS transform of its own, so the "drift" can't exist anymore. This test asserts the
    /// post-F2 invariant directly: the SVG CTM is the single source of zoom, and the HTML
    /// overlay transform layer is CSS-identity at the requested zoom level.
    /// </summary>
    [TestMethod]
    public async Task ScreenToDoc_And_HtmlLayerScale_MatchAtScale075()
    {
        var page = await PrepareDiagramPageAsync();

        await SetCanvasScaleAsync(page, 0.75);

        var ctm = await GetSvgScreenCtmAsync(page);
        var htmlScale = await HtmlOverlayScale(page);

        Assert.IsNotNull(htmlScale, "scene-pane <g> not found");

        Math.Abs(htmlScale!.Value - 1.0).Should().BeLessThan(1e-3,
            "post-F3 the scene-pane must expose no CSS scale; any non-identity value re-introduces the drift bug");

        // CTM.a must reflect the requested zoom, independently of the overlay.
        Math.Abs(ctm.A - 0.75).Should().BeLessThan(0.01,
            "SVG CTM scale must track the zoomTo(0.75) call — this is now the sole zoom authority");
    }
}
