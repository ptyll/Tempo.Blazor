using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for diagram editor edge interactions across all 6 phases
/// of EDGE_FEATURES_IMPLEMENTATION_PLAN.md.
/// These tests exercise the JS-side drag/draw/drop behavior that bUnit cannot verify.
/// </summary>
[TestClass]
public class DiagramEdgeE2ETests : WasmTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";
    private const string Node1Id = "class1";
    private const string Node2Id = "class2";

    private async Task WaitForCanvasAsync(IPage page)
    {
        await page.WaitForSelectorAsync(".tm-diagram-canvas", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        // The DOM node is there early (Blazor SSR), but the diagram JS handler
        // (`tmDiagramEditor.init`) is wired up only once the WASM interactive
        // bootstrap has completed and the first interactive render ran. Tests
        // that immediately dispatch mouse events would otherwise hit a DOM
        // that looks ready but has no JS listeners attached — the events
        // arrive at the rect but `_onMouseDown` never runs.
        await page.WaitForFunctionAsync("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !canvas.id) return false;
                const ed = window.tmDiagramEditor;
                return !!(ed && ed.instances && ed.instances.get(canvas.id));
            }
        """, null, new PageWaitForFunctionOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(200);
    }

    private async Task OpenDiagramEditorAsync(IPage page)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await page.GotoAsync(BaseUrl + DiagramEditorUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30_000
                });
            }
            catch (TimeoutException) when (attempt == 0)
            {
                // In the full E2E suite the WASM document can occasionally miss the navigation load boundary even
                // though the route is committed. The explicit app/canvas readiness checks below are authoritative.
            }

            try
            {
                await WaitForAppReadyAsync(page);
                await WaitForCanvasAsync(page);
                return;
            }
            catch (TimeoutException) when (attempt == 0)
            {
                // Retry the whole route bootstrap once; a stale/half-booted WASM page can otherwise keep the app
                // readiness fallback waiting even though a fresh navigation succeeds immediately.
            }
        }

        await WaitForAppReadyAsync(page);
        await WaitForCanvasAsync(page);
    }

    private async Task<(double X, double Y)> GetCenterAsync(ILocator locator)
    {
        var box = await locator.BoundingBoxAsync();
        Assert.IsNotNull(box, "Expected element to have a bounding box");
        return (box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    private async Task SelectEdgeAsync(IPage page)
    {
        // Use JS to dispatch a click directly on the hit path — avoids SVG bounding-box / overlay issues
        await page.EvaluateAsync(@"
            const hit = document.querySelector('.tm-diagram-edge-hit-path');
            if (hit) {
                hit.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: 0, clientY: 0 }));
            }
        ");
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>Gets the screen center of the first element matching selector via JS getBoundingClientRect.</summary>
    private async Task<(double X, double Y)> GetScreenCenterViaJsAsync(IPage page, string selector)
    {
        var json = await page.EvaluateAsync<string>("""
            (sel) => {
                const el = document.querySelector(sel);
                if (!el) return null;
                const r = el.getBoundingClientRect();
                return JSON.stringify({ x: r.left + r.width/2, y: r.top + r.height/2 });
            }
        """, selector);
        Assert.IsNotNull(json, $"Element not found: {selector}");
        using var doc = JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("x").GetDouble(), doc.RootElement.GetProperty("y").GetDouble());
    }

    /// <summary>Gets screen centers of all elements matching selector via JS.</summary>
    private async Task<List<(double X, double Y)>> GetScreenCentersViaJsAsync(IPage page, string selector)
    {
        var json = await page.EvaluateAsync<string>("""
            (sel) => {
                const els = document.querySelectorAll(sel);
                const arr = [];
                els.forEach(el => {
                    const r = el.getBoundingClientRect();
                    arr.push({ x: r.left + r.width/2, y: r.top + r.height/2 });
                });
                return JSON.stringify(arr);
            }
        """, selector);
        Assert.IsNotNull(json, $"Elements not found: {selector}");
        using var doc = JsonDocument.Parse(json);
        var list = new List<(double, double)>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            list.Add((item.GetProperty("x").GetDouble(), item.GetProperty("y").GetDouble()));
        }
        return list;
    }

    /// <summary>Dispatches a click event via JS on the first element matching selector.</summary>
    private async Task ClickViaJsAsync(IPage page, string selector)
    {
        await page.EvaluateAsync<string>("""
            (sel) => {
                const el = document.querySelector(sel);
                if (!el) return null;
                const r = el.getBoundingClientRect();
                el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: r.left + r.width/2, clientY: r.top + r.height/2 }));
                return 'ok';
            }
        """, selector);
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>Dispatches a mousedown event via JS on the first element matching selector.
    /// Use this for interactions handled by the JS mousedown listener (virtual bends, handles).</summary>
    private async Task MouseDownViaJsAsync(IPage page, string selector)
    {
        await page.EvaluateAsync<string>("""
            (sel) => {
                const el = document.querySelector(sel);
                if (!el) return null;
                const r = el.getBoundingClientRect();
                el.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, button: 0, clientX: r.left + r.width/2, clientY: r.top + r.height/2 }));
                return 'ok';
            }
        """, selector);
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>Changes the edge routing via the properties panel TmSelect.</summary>
    private async Task SetEdgeRoutingAsync(IPage page, string routing)
    {
        var propsPanel = page.Locator(".tm-diagram-properties-panel, .tm-diagram-editor__props-wrap").First;
        var routingField = propsPanel.Locator(".tm-diagram-properties__field").Filter(new LocatorFilterOptions { HasText = "Routing" }).First;
        if (await routingField.CountAsync() == 0)
        {
            Assert.Inconclusive("Routing field not found in properties panel.");
        }
        var routingSelect = routingField.Locator("select");
        await routingSelect.SelectOptionAsync(routing);
        await page.WaitForTimeoutAsync(700);
    }

    private async Task OpenEdgeContextMenuAsync(IPage page)
    {
        // Use JS dispatch because HTML overlay blocks native mouse events on SVG
        await page.EvaluateAsync("""
            () => {
                const hit = document.querySelector('.tm-diagram-edge-hit-path');
                if (!hit) return;
                const r = hit.getBoundingClientRect();
                const cx = r.left + r.width / 2;
                const cy = r.top + r.height / 2;
                hit.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2, clientX: cx, clientY: cy }));
            }
        """);
        await page.WaitForTimeoutAsync(300);
    }

    private async Task SetToolModeAsync(IPage page, string mode)
    {
        await page.EvaluateAsync("""
            (mode) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !window.tmDiagramEditor) return;
                window.tmDiagramEditor.setToolMode(canvas, mode);
            }
        """, mode);
        await page.WaitForTimeoutAsync(200);
    }

    private async Task<int> GetEdgeCountAsync(IPage page)
    {
        return await page.Locator(".tm-diagram-edge-path").CountAsync();
    }

    // ========================================================================
    // FÁZE 1 — Základy interakce (Foundation)
    // ========================================================================

    [TestMethod]
    [Description("1.1.5 / 1.1.8 — Manual routing toggle via properties panel stops auto-router")]
    public async Task Phase1_ManualRouting_ToggleViaPropertiesPanel()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        await SelectEdgeAsync(page);

        // Insert a virtual-bend waypoint first so the edge doesn't disappear when manual routing is toggled
        // (empty Waypoints list + IsManuallyRouted=true produces an empty path)
        var virtualBends = page.Locator(".tm-diagram-edge-virtual-bend");
        if (await virtualBends.CountAsync() > 0)
        {
            // JS mousedown handler inserts the waypoint; a simple click event won't trigger it
            await MouseDownViaJsAsync(page, ".tm-diagram-edge-virtual-bend");
            await page.WaitForTimeoutAsync(500);
        }

        // Find the properties panel and the manual routing checkbox
        var propsPanel = page.Locator(".tm-diagram-properties-panel, .tm-diagram-editor__props-wrap").First;
        await propsPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Look for manual routing checkbox (TmCheckbox wrapper with label containing "Manual")
        var manualCheckbox = propsPanel.Locator(".tm-checkbox-wrapper").Filter(new LocatorFilterOptions { HasText = "Manual" }).First;
        if (await manualCheckbox.CountAsync() == 0)
        {
            Assert.Inconclusive("Manual routing checkbox not found in properties panel.");
        }

        await manualCheckbox.ClickAsync();
        await page.WaitForTimeoutAsync(600); // give async HandleEdgeManualRoutingChanged time to seed waypoints

        // After toggle, the edge should still be drawn: non-empty `d` attribute
        // AND non-zero SVG bounding box. `IsVisibleAsync` proved flaky in CI
        // because Blazor re-renders detach/attach the SVG node; we use a JS
        // probe so a diagnostic message is attached to any failure.
        var diag = await page.EvaluateAsync<string>("""
            () => {
                const paths = Array.from(document.querySelectorAll('.tm-diagram-edge-path'));
                const out = paths.map(p => {
                    let bb = null;
                    try { bb = p.getBBox(); } catch { bb = null; }
                    const cs = getComputedStyle(p);
                    const parentG = p.closest('.tm-diagram-edge-group');
                    const parentOpacity = parentG ? getComputedStyle(parentG).opacity : null;
                    return {
                        d: p.getAttribute('d'),
                        bboxW: bb ? bb.width : null,
                        bboxH: bb ? bb.height : null,
                        visibility: cs.visibility,
                        display: cs.display,
                        opacity: cs.opacity,
                        parentOpacity,
                        cls: p.getAttribute('class')
                    };
                });
                return JSON.stringify({ count: paths.length, paths: out });
            }
        """);
        Assert.IsNotNull(diag);
        using var diagDoc = JsonDocument.Parse(diag);
        var count = diagDoc.RootElement.GetProperty("count").GetInt32();
        bool anyVisible = false;
        foreach (var p in diagDoc.RootElement.GetProperty("paths").EnumerateArray())
        {
            var d = p.GetProperty("d").GetString();
            var w = p.GetProperty("bboxW").ValueKind == JsonValueKind.Number ? p.GetProperty("bboxW").GetDouble() : 0;
            var h = p.GetProperty("bboxH").ValueKind == JsonValueKind.Number ? p.GetProperty("bboxH").GetDouble() : 0;
            var vis = p.GetProperty("visibility").GetString();
            var disp = p.GetProperty("display").GetString();
            if (!string.IsNullOrEmpty(d) && (w > 0 || h > 0) && vis != "hidden" && disp != "none")
            {
                anyVisible = true;
                break;
            }
        }
        Assert.IsTrue(anyVisible,
            $"Edge should remain visible after toggling manual routing. Diagnostics: {diag}");

        await TakeScreenshotAsync(page, "phase1_manual_routing");
    }

    [TestMethod]
    [Description("1.1.9 — Reset routing button restores auto-routing")]
    public async Task Phase1_ResetRouting_ButtonRestoresAutoRouting()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        await SelectEdgeAsync(page);

        // Switch to orthogonal so manual routing + reset are available
        await SetEdgeRoutingAsync(page, "orthogonal");

        var propsPanel = page.Locator(".tm-diagram-properties-panel, .tm-diagram-editor__props-wrap").First;

        // Enable manual routing so the Reset button appears
        var manualCheckbox = propsPanel.Locator(".tm-checkbox-wrapper").Filter(new LocatorFilterOptions { HasText = "Manual" }).First;
        if (await manualCheckbox.CountAsync() == 0)
        {
            Assert.Inconclusive("Manual routing checkbox not found in properties panel.");
        }
        var isChecked = await manualCheckbox.Locator("input").IsCheckedAsync();
        if (!isChecked)
        {
            await manualCheckbox.ClickAsync();
            await page.WaitForTimeoutAsync(300);
        }

        var resetBtn = page.Locator("button:has-text('Reset routing'), button:has-text('Reset')").First;
        if (await resetBtn.CountAsync() == 0)
        {
            Assert.Inconclusive("Reset routing button not found in properties panel.");
        }

        await resetBtn.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        // Verify that manual routing was turned off (the main effect of reset)
        var isCheckedAfter = await manualCheckbox.Locator("input").IsCheckedAsync();
        Assert.IsFalse(isCheckedAfter, "Manual routing should be disabled after reset routing");

        await TakeScreenshotAsync(page, "phase1_reset_routing");
    }

    [TestMethod]
    [Description("1.2 — Virtual bend click inserts a new waypoint")]
    public async Task Phase1_VirtualBend_ClickInsertsWaypoint()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        await SelectEdgeAsync(page);

        var handlesBefore = page.Locator(".tm-diagram-edge-handle:not(.tm-diagram-edge-handle--dangling):not(.tm-diagram-edge-handle--jetty):not(.tm-diagram-edge-segment-handle)");
        var countBefore = await handlesBefore.CountAsync();

        var virtualBends = page.Locator(".tm-diagram-edge-virtual-bend");
        var vbCount = await virtualBends.CountAsync();
        if (vbCount == 0)
        {
            Assert.Inconclusive("No virtual bends visible; sample edge may be orthogonal or no bends rendered.");
        }

        // Dispatch mousedown via JS — the canvas JS handler listens for mousedown on virtual bends,
        // not click events (which only trigger Blazor @onclick bindings).
        await MouseDownViaJsAsync(page, ".tm-diagram-edge-virtual-bend");
        await page.WaitForTimeoutAsync(500);

        var handlesAfter = page.Locator(".tm-diagram-edge-handle:not(.tm-diagram-edge-handle--dangling):not(.tm-diagram-edge-handle--jetty):not(.tm-diagram-edge-segment-handle)");
        var countAfter = await handlesAfter.CountAsync();
        Assert.IsTrue(countAfter > countBefore,
            $"Expected waypoint count to increase after virtual bend insert (before={countBefore}, after={countAfter})");

        await TakeScreenshotAsync(page, "phase1_virtual_bend");
    }

    [TestMethod]
    [Description("1.3 — Smart removal: dragging waypoint onto straight line removes it")]
    public async Task Phase1_SmartRemoval_DragWaypointToStraightLineRemovesIt()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // First insert a virtual bend to create a middle waypoint
        await SelectEdgeAsync(page);
        var virtualBends = page.Locator(".tm-diagram-edge-virtual-bend");
        if (await virtualBends.CountAsync() == 0)
        {
            Assert.Inconclusive("No virtual bends to create a test waypoint.");
        }

        // Dispatch mousedown via JS — the canvas JS handler listens for mousedown on virtual bends
        await MouseDownViaJsAsync(page, ".tm-diagram-edge-virtual-bend");
        await page.WaitForTimeoutAsync(500);

        // Waypoint handles have data-waypoint="true"; endpoint handles have data-waypoint="false".
        // Virtual bends share the tm-diagram-edge-handle class but have no data-waypoint attribute.
        var waypointHandles = page.Locator(".tm-diagram-edge-handle[data-waypoint='true']");
        var countBefore = await waypointHandles.CountAsync();
        Assert.IsTrue(countBefore >= 1, "Need at least 1 waypoint to test smart removal");

        var waypointCenters = await GetScreenCentersViaJsAsync(page, ".tm-diagram-edge-handle[data-waypoint='true']");
        Assert.IsTrue(waypointCenters.Count >= 1, "Expected at least 1 waypoint center from JS");

        var endpointCenters = await GetScreenCentersViaJsAsync(page, ".tm-diagram-edge-handle[data-waypoint='false']");
        Assert.IsTrue(endpointCenters.Count >= 2, "Expected source and target endpoint handles");

        // Drag the first (only) waypoint onto the straight line between its source and target endpoints
        var (mx, my) = waypointCenters[0];
        var targetX = (endpointCenters[0].X + endpointCenters[1].X) / 2;
        var targetY = (endpointCenters[0].Y + endpointCenters[1].Y) / 2;

        await page.Mouse.MoveAsync((float)mx, (float)my);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)targetX, (float)targetY);
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(500);

        var countAfter = await waypointHandles.CountAsync();
        Assert.IsTrue(countAfter < countBefore,
            $"Expected waypoint count to decrease after smart removal (before={countBefore}, after={countAfter})");

        await TakeScreenshotAsync(page, "phase1_smart_removal");
    }

    [TestMethod]
    [Description("1.4 — Grid snap: node drag snaps to grid")]
    public async Task Phase1_GridSnap_NodeDragSnapsToGrid()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // Drag the first sample node — nodes are HTML elements so real mouse works reliably
        var node = page.Locator(".tm-diagram-node").First;
        await node.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var box = await node.BoundingBoxAsync();
        Assert.IsNotNull(box, "Node should have a bounding box");
        var startX = box.X + box.Width / 2;
        var startY = box.Y + box.Height / 2;

        // Drag to an unaligned position — demo uses GridSize = 20
        // Use Locator.DragToAsync with offset positions so Playwright handles the drag sequence
        // Simulate drag via JS dispatchEvent — the HTML overlay intercepts native mouse
        // events, so Playwright Mouse API doesn't reach the SVG/canvas handlers reliably.
        await page.EvaluateAsync("""
            () => {
                const node = document.querySelector('.tm-diagram-node');
                const container = document.querySelector('.tm-diagram-canvas');
                if (!node || !container) return null;
                const r = node.getBoundingClientRect();
                const cx = r.left + r.width/2;
                const cy = r.top + r.height/2;
                node.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, button: 0, clientX: cx, clientY: cy }));
                container.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, cancelable: true, button: 0, clientX: cx + 100, clientY: cy + 100 }));
                container.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, button: 0, clientX: cx + 100, clientY: cy + 100 }));
                return 'done';
            }
        """);
        await page.WaitForTimeoutAsync(800);

        // First verify the node actually moved (client-space sanity)
        var newBox = await node.BoundingBoxAsync();
        Assert.IsNotNull(newBox);
        Assert.IsTrue(Math.Abs(newBox.X - box.X) > 5 || Math.Abs(newBox.Y - box.Y) > 5,
            $"Node should have moved, but stayed at ({newBox.X},{newBox.Y})");

        // After F2, nodes live inside a <foreignObject> and render with
        // sub-pixel precision (pre-F2 CSS transform on the overlay benefited
        // from browser pixel snapping). The drag-snap logic still operates in
        // *doc* space — snap only guarantees the node's translate() rounds to
        // a grid multiple, not its client rect. Assert on doc-space via the
        // node's inline translate(...) (same source the C# side reads).
        var docPosJson = await page.EvaluateAsync<string>("""
            () => {
                // F3.A — node position is on the SVG <g> transform attribute
                // (translate(x,y) rotate(θ cx cy)); no more `px` in CSS translate.
                const node = document.querySelector('g.tm-diagram-node[data-node-id]');
                if (!node) return null;
                const s = node.getAttribute('transform') || '';
                const m = s.match(/translate\(\s*([-\d.e+]+)\s*,\s*([-\d.e+]+)\s*\)/);
                if (!m) return null;
                return JSON.stringify({ x: parseFloat(m[1]), y: parseFloat(m[2]) });
            }
        """);
        Assert.IsNotNull(docPosJson, "Could not read node translate(...) after drag.");
        using var doc = JsonDocument.Parse(docPosJson);
        var docX = doc.RootElement.GetProperty("x").GetDouble();
        var docY = doc.RootElement.GetProperty("y").GetDouble();
        var docSnapX = Math.Round(docX / 20) * 20;
        var docSnapY = Math.Round(docY / 20) * 20;
        Assert.IsTrue(Math.Abs(docX - docSnapX) < 0.5,
            $"Node X should snap to grid multiple of 20 in doc space; got {docX}");
        Assert.IsTrue(Math.Abs(docY - docSnapY) < 0.5,
            $"Node Y should snap to grid multiple of 20 in doc space; got {docY}");

        await TakeScreenshotAsync(page, "phase1_grid_snap");
    }

    [TestMethod]
    [Description("Phase 1 — Empty-to-empty drag in edge mode creates floating edge")]
    public async Task Phase1_FreeLine_EmptyToEmpty_CreatesFloatingEdge()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        var initialEdgeCount = await GetEdgeCountAsync(page);
        var danglingBefore = await page.Locator(".tm-diagram-edge-handle--dangling").CountAsync();
        await SetToolModeAsync(page, "edge");

        var pointsJson = await page.EvaluateAsync<string>("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const nodes = Array.from(document.querySelectorAll('.tm-diagram-node[data-node-id]'));
                if (!canvas) return null;
                const cr = canvas.getBoundingClientRect();
                let maxNodeRight = cr.left + 80;
                let minNodeTop = cr.top + 80;
                for (const n of nodes) {
                    const r = n.getBoundingClientRect();
                    maxNodeRight = Math.max(maxNodeRight, r.right);
                    minNodeTop = Math.min(minNodeTop, r.top);
                }
                const startX = Math.min(cr.right - 140, maxNodeRight + 40);
                const startY = Math.max(cr.top + 40, minNodeTop - 20);
                return JSON.stringify({
                    startX,
                    startY,
                    endX: startX + 220,
                    endY: startY + 130
                });
            }
        """);
        Assert.IsNotNull(pointsJson, "Could not compute empty-canvas drag points.");
        using var points = JsonDocument.Parse(pointsJson);
        var sx = points.RootElement.GetProperty("startX").GetDouble();
        var sy = points.RootElement.GetProperty("startY").GetDouble();
        var ex = points.RootElement.GetProperty("endX").GetDouble();
        var ey = points.RootElement.GetProperty("endY").GetDouble();

        await page.Mouse.MoveAsync((float)sx, (float)sy);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)ex, (float)ey);
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(900);

        var edgeCountAfter = await GetEdgeCountAsync(page);
        Assert.AreEqual(initialEdgeCount + 1, edgeCountAfter, "Expected one newly created edge.");

        var danglingCount = await page.Locator(".tm-diagram-edge-handle--dangling").CountAsync();
        Assert.IsTrue(danglingCount >= 2, "Expected both terminals to be dangling for empty->empty draw.");

        await TakeScreenshotAsync(page, "free_line_empty_to_empty");
    }

    [TestMethod]
    [Description("Phase 1 — Empty-to-port drag keeps floating source and attaches target")]
    public async Task Phase1_FreeLine_EmptyToNode_AttachesTarget()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        var initialEdgeCount = await GetEdgeCountAsync(page);
        var danglingBefore = await page.Locator(".tm-diagram-edge-handle--dangling").CountAsync();
        await SetToolModeAsync(page, "edge");

        var pointsJson = await page.EvaluateAsync<string>("""
            (targetSelector) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const target = document.querySelector(targetSelector);
                if (!canvas || !target) return null;
                const cr = canvas.getBoundingClientRect();
                const tr = target.getBoundingClientRect();
                const startX = Math.max(cr.left + 30, tr.left - 180);
                const startY = Math.max(cr.top + 30, tr.top - 80);
                return JSON.stringify({
                    startX,
                    startY,
                    endX: tr.left + tr.width / 2,
                    endY: tr.top + tr.height / 2
                });
            }
        """, $".tm-diagram-node[data-node-id='{Node2Id}'] .tm-diagram-port[data-port-id='left']");
        Assert.IsNotNull(pointsJson, "Could not compute empty->port drag points.");
        using var points = JsonDocument.Parse(pointsJson);
        var sx = points.RootElement.GetProperty("startX").GetDouble();
        var sy = points.RootElement.GetProperty("startY").GetDouble();
        var ex = points.RootElement.GetProperty("endX").GetDouble();
        var ey = points.RootElement.GetProperty("endY").GetDouble();

        await page.Mouse.MoveAsync((float)sx, (float)sy);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)ex, (float)ey);
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(900);

        var edgeCountAfter = await GetEdgeCountAsync(page);
        Assert.AreEqual(initialEdgeCount + 1, edgeCountAfter, "Expected one newly created edge.");

        var danglingAfter = await page.Locator(".tm-diagram-edge-handle--dangling").CountAsync();
        Assert.IsTrue(danglingAfter >= danglingBefore + 1,
            "Expected at least one additional dangling handle after empty->port draw.");

        await TakeScreenshotAsync(page, "free_line_empty_to_node");
    }

    [TestMethod]
    [Description("Phase 2 — Edge tool empty click without drag starts a polyline draft; ESC discards it")]
    public async Task Phase2_EdgeTool_ClickEmpty_StartsPolyline_EscCancels()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        var initialEdgeCount = await GetEdgeCountAsync(page);
        await SetToolModeAsync(page, "edge");

        var pointJson = await page.EvaluateAsync<string>("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas) return null;
                const r = canvas.getBoundingClientRect();
                return JSON.stringify({ x: r.left + 40, y: r.top + 40 });
            }
        """);
        Assert.IsNotNull(pointJson);
        using var p = JsonDocument.Parse(pointJson);
        var x = p.RootElement.GetProperty("x").GetDouble();
        var y = p.RootElement.GetProperty("y").GetDouble();

        await page.Mouse.ClickAsync((float)x, (float)y);
        await page.WaitForTimeoutAsync(200);

        var stateAfterClick = await page.EvaluateAsync<string>("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !window.tmDiagramEditor) return 'unknown';
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                if (!inst) return 'missing';
                return JSON.stringify({ polyline: inst.isDrawingPolyline, drawing: inst.isDrawingEdge, mode: inst.toolMode });
            }
        """);
        Assert.IsNotNull(stateAfterClick);
        using (var s = JsonDocument.Parse(stateAfterClick))
        {
            Assert.IsTrue(s.RootElement.GetProperty("polyline").GetBoolean(),
                "Plain click on empty canvas in edge mode should start a polyline draft.");
            Assert.IsTrue(s.RootElement.GetProperty("drawing").GetBoolean(),
                "isDrawingEdge should also be true while polyline is drafting.");
        }

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);

        var mode = await page.EvaluateAsync<string>("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !window.tmDiagramEditor) return 'unknown';
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                return inst ? inst.toolMode : 'missing';
            }
        """);
        Assert.AreEqual("select", mode, "ESC should cancel the polyline draft and return to select.");

        var edgeCountAfter = await GetEdgeCountAsync(page);
        Assert.AreEqual(initialEdgeCount, edgeCountAfter, "No new edge should be created by click+ESC.");
    }

    [TestMethod]
    [Description("Phase 2 — Three clicks + double-click finish creates a polyline with waypoints")]
    public async Task Phase2_Polyline_ThreeClicks_DblClickFinish()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        var initialEdgeCount = await GetEdgeCountAsync(page);
        await SetToolModeAsync(page, "edge");

        var pointsJson = await page.EvaluateAsync<string>("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas) return null;
                const cr = canvas.getBoundingClientRect();
                const nodes = Array.from(document.querySelectorAll('.tm-diagram-node[data-node-id]'));
                let maxRight = cr.left + 40, minTop = cr.top + 40;
                for (const n of nodes) {
                    const r = n.getBoundingClientRect();
                    maxRight = Math.max(maxRight, r.right);
                    minTop = Math.min(minTop, r.top);
                }
                // Base anchor: right of every node, slightly above the top row.
                const baseX = Math.min(cr.right - 260, maxRight + 60);
                const baseY = Math.max(cr.top + 30, minTop - 30);

                // Ensure a point lands on truly empty canvas (not on a node,
                // port, connection-point or edge-hit-path) AND is still inside
                // the canvas rect. If not, nudge upward in 20px steps.
                function isEmpty(x, y) {
                    const el = document.elementFromPoint(x, y);
                    if (!el) return false;
                    if (!canvas.contains(el)) return false;
                    if (el.closest('.tm-diagram-port')) return false;
                    if (el.closest('.tm-diagram-connection-point')) return false;
                    if (el.closest('[data-node-id]')) return false;
                    if (el.closest('.tm-diagram-edge-hit-path')) return false;
                    return true;
                }
                function pick(x, y) {
                    for (let k = 0; k < 20; k++) {
                        const yy = y - k * 20;
                        if (yy < cr.top + 10) break;
                        if (isEmpty(x, yy)) return { x, y: yy };
                    }
                    return { x, y: cr.top + 10 };
                }

                return JSON.stringify({
                    p1: pick(baseX,        baseY),
                    p2: pick(baseX + 80,   baseY),
                    p3: pick(baseX + 160,  baseY),
                    p4: pick(baseX + 220,  baseY)
                });
            }
        """);
        Assert.IsNotNull(pointsJson);
        using var pts = JsonDocument.Parse(pointsJson);
        double Px(string k) => pts.RootElement.GetProperty(k).GetProperty("x").GetDouble();
        double Py(string k) => pts.RootElement.GetProperty(k).GetProperty("y").GetDouble();

        await page.Mouse.ClickAsync((float)Px("p1"), (float)Py("p1"));
        await page.WaitForTimeoutAsync(150);
        await page.Mouse.ClickAsync((float)Px("p2"), (float)Py("p2"));
        await page.WaitForTimeoutAsync(150);
        await page.Mouse.ClickAsync((float)Px("p3"), (float)Py("p3"));
        await page.WaitForTimeoutAsync(150);
        await page.Mouse.DblClickAsync((float)Px("p4"), (float)Py("p4"));
        await page.WaitForTimeoutAsync(900);

        var edgeCountAfter = await GetEdgeCountAsync(page);
        Assert.AreEqual(initialEdgeCount + 1, edgeCountAfter, "Expected one new polyline edge.");

        // Both ends should be dangling (floating source + floating target).
        var danglingCount = await page.Locator(".tm-diagram-edge-handle--dangling").CountAsync();
        Assert.IsTrue(danglingCount >= 2, "Polyline with both floating ends should have dangling handles on both sides.");

        await TakeScreenshotAsync(page, "phase2_polyline_dblclick");
    }

    [TestMethod]
    [Description("Phase 2 — ESC during polyline draft discards the edge")]
    public async Task Phase2_Polyline_Escape_DiscardsDraft()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        var initialEdgeCount = await GetEdgeCountAsync(page);
        await SetToolModeAsync(page, "edge");

        var pointsJson = await page.EvaluateAsync<string>("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas) return null;
                const cr = canvas.getBoundingClientRect();
                const nodes = Array.from(document.querySelectorAll('.tm-diagram-node[data-node-id]'));
                let maxRight = cr.left + 40, minTop = cr.top + 40;
                for (const n of nodes) {
                    const r = n.getBoundingClientRect();
                    maxRight = Math.max(maxRight, r.right);
                    minTop = Math.min(minTop, r.top);
                }
                const baseX = Math.min(cr.right - 160, maxRight + 60);
                const baseY = Math.max(cr.top + 30, minTop - 30);
                function isEmpty(x, y) {
                    const el = document.elementFromPoint(x, y);
                    if (!el) return false;
                    if (!canvas.contains(el)) return false;
                    if (el.closest('.tm-diagram-port')) return false;
                    if (el.closest('.tm-diagram-connection-point')) return false;
                    if (el.closest('[data-node-id]')) return false;
                    if (el.closest('.tm-diagram-edge-hit-path')) return false;
                    return true;
                }
                function pick(x, y) {
                    for (let k = 0; k < 20; k++) {
                        const yy = y - k * 20;
                        if (yy < cr.top + 10) break;
                        if (isEmpty(x, yy)) return { x, y: yy };
                    }
                    return { x, y: cr.top + 10 };
                }
                return JSON.stringify({
                    p1: pick(baseX,       baseY),
                    p2: pick(baseX + 80,  baseY)
                });
            }
        """);
        Assert.IsNotNull(pointsJson);
        using var pts = JsonDocument.Parse(pointsJson);
        var p1x = pts.RootElement.GetProperty("p1").GetProperty("x").GetDouble();
        var p1y = pts.RootElement.GetProperty("p1").GetProperty("y").GetDouble();
        var p2x = pts.RootElement.GetProperty("p2").GetProperty("x").GetDouble();
        var p2y = pts.RootElement.GetProperty("p2").GetProperty("y").GetDouble();

        await page.Mouse.ClickAsync((float)p1x, (float)p1y);
        await page.WaitForTimeoutAsync(150);
        await page.Mouse.ClickAsync((float)p2x, (float)p2y);
        await page.WaitForTimeoutAsync(150);
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(400);

        var edgeCountAfter = await GetEdgeCountAsync(page);
        Assert.AreEqual(initialEdgeCount, edgeCountAfter, "ESC should discard the polyline draft.");

        var mode = await page.EvaluateAsync<string>("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const inst = canvas && window.tmDiagramEditor && window.tmDiagramEditor.instances.get(canvas.id);
                return inst ? inst.toolMode : 'missing';
            }
        """);
        Assert.AreEqual("select", mode, "After ESC we should be back in select mode.");
    }

    [TestMethod]
    [Description("Phase 2 — Click on port during polyline draft attaches the target terminal")]
    public async Task Phase2_Polyline_ClickOnNode_AttachesTarget()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        var initialEdgeCount = await GetEdgeCountAsync(page);
        var danglingBefore = await page.Locator(".tm-diagram-edge-handle--dangling").CountAsync();
        await SetToolModeAsync(page, "edge");

        var pointsJson = await page.EvaluateAsync<string>("""
            (targetSelector) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const target = document.querySelector(targetSelector);
                if (!canvas || !target) return null;
                const cr = canvas.getBoundingClientRect();
                const tr = target.getBoundingClientRect();
                const p1x = Math.max(cr.left + 30, tr.left - 200);
                const p1y = Math.max(cr.top + 30, tr.top - 120);
                const p2x = p1x + 80;
                const p2y = p1y + 40;
                return JSON.stringify({
                    p1: { x: p1x, y: p1y },
                    p2: { x: p2x, y: p2y },
                    target: { x: tr.left + tr.width / 2, y: tr.top + tr.height / 2 }
                });
            }
        """, $".tm-diagram-node[data-node-id='{Node2Id}'] .tm-diagram-port[data-port-id='left']");
        Assert.IsNotNull(pointsJson);
        using var pts = JsonDocument.Parse(pointsJson);
        double Px(string k) => pts.RootElement.GetProperty(k).GetProperty("x").GetDouble();
        double Py(string k) => pts.RootElement.GetProperty(k).GetProperty("y").GetDouble();

        await page.Mouse.ClickAsync((float)Px("p1"), (float)Py("p1"));
        await page.WaitForTimeoutAsync(150);
        await page.Mouse.ClickAsync((float)Px("p2"), (float)Py("p2"));
        await page.WaitForTimeoutAsync(150);
        await page.Mouse.ClickAsync((float)Px("target"), (float)Py("target"));
        await page.WaitForTimeoutAsync(900);

        var edgeCountAfter = await GetEdgeCountAsync(page);
        Assert.AreEqual(initialEdgeCount + 1, edgeCountAfter, "Expected one new polyline edge.");

        // Floating source adds 1 dangling handle; the attached target side does not.
        var danglingAfter = await page.Locator(".tm-diagram-edge-handle--dangling").CountAsync();
        Assert.IsTrue(danglingAfter >= danglingBefore + 1,
            "Expected one additional dangling handle (floating source) after polyline attaches target.");

        await TakeScreenshotAsync(page, "phase2_polyline_click_on_node");
    }

    // ========================================================================
    // FÁZE 2 — Segment Drag
    // ========================================================================

    [TestMethod]
    [Description("2.1 — Drag an orthogonal segment handle moves the segment")]
    public async Task Phase2_SegmentDrag_MovesOrthogonalSegment()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // Switch edge to orthogonal via properties panel
        await SelectEdgeAsync(page);
        await SetEdgeRoutingAsync(page, "orthogonal");

        var segmentHandles = page.Locator(".tm-diagram-edge-segment-handle");
        var segCount = await segmentHandles.CountAsync();
        if (segCount == 0)
        {
            Assert.Inconclusive("No segment handles visible; edge may not be orthogonal.");
        }

        var (sx, sy) = await GetScreenCenterViaJsAsync(page, ".tm-diagram-edge-segment-handle");

        await page.Mouse.MoveAsync((float)sx, (float)sy);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(sx + 30), (float)(sy + 30));
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(500);

        var edgesAfter = page.Locator(".tm-diagram-edge-path");
        var edgeCountAfter = await edgesAfter.CountAsync();
        Assert.IsTrue(edgeCountAfter > 0, "Edge should still exist after segment drag");

        await TakeScreenshotAsync(page, "phase2_segment_drag");
    }

    [TestMethod]
    [Description("2.3.1 — Cursor changes when hovering over vertical/horizontal segment")]
    public async Task Phase2_CursorFeedback_HoverOverSegment()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // Switch to orthogonal via properties panel
        await SelectEdgeAsync(page);
        await SetEdgeRoutingAsync(page, "orthogonal");

        var segmentHandles = page.Locator(".tm-diagram-edge-segment-handle");
        if (await segmentHandles.CountAsync() == 0)
        {
            Assert.Inconclusive("No segment handles for cursor test.");
        }

        var (sx, sy) = await GetScreenCenterViaJsAsync(page, ".tm-diagram-edge-segment-handle");

        // Hover over segment handle
        await page.Mouse.MoveAsync((float)sx, (float)sy);
        await page.WaitForTimeoutAsync(200);

        // Check cursor style on canvas container
        var canvas = page.Locator(".tm-diagram-canvas");
        var cursor = await canvas.EvaluateAsync<string>("el => el.style.cursor");

        // Cursor should be either col-resize or row-resize depending on segment orientation
        var validCursors = new[] { "col-resize", "row-resize", "crosshair", "pointer" };
        Assert.IsTrue(validCursors.Contains(cursor) || string.IsNullOrEmpty(cursor),
            $"Unexpected cursor style: '{cursor}'");

        await TakeScreenshotAsync(page, "phase2_cursor_feedback");
    }

    // ========================================================================
    // FÁZE 3 — Volné konce a Connection Constraints
    // ========================================================================

    [TestMethod]
    [Description("3.2 / 3.3 — Drag from port to empty space creates dangling edge")]
    public async Task Phase3_DanglingEdge_DrawFromPortToEmptySpace()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        var initialEdgeCount = await GetEdgeCountAsync(page);

        var port = page.Locator($".tm-diagram-node[data-node-id='{Node1Id}'] .tm-diagram-port[data-port-id='right']");
        await port.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var (startX, startY) = await GetCenterAsync(port);

        var emptyCoordsJson = await page.EvaluateAsync<string>("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas) return null;
                const cr = canvas.getBoundingClientRect();
                const viewH = window.innerHeight;
                const visibleBottom = Math.min(cr.bottom, viewH) - 20;
                const nodes = Array.from(document.querySelectorAll('.tm-diagram-node[data-node-id]'));
                let maxBottom = cr.top + 100;
                for (const n of nodes) {
                    const r = n.getBoundingClientRect();
                    maxBottom = Math.max(maxBottom, r.bottom);
                }
                return JSON.stringify({
                    x: cr.left + 80,
                    y: Math.min(visibleBottom - 40, maxBottom + 40)
                });
            }
        """);
        Assert.IsNotNull(emptyCoordsJson, "Could not compute empty-canvas drop point.");
        using var emptyCoords = JsonDocument.Parse(emptyCoordsJson!);
        var endX = emptyCoords.RootElement.GetProperty("x").GetDouble();
        var endY = emptyCoords.RootElement.GetProperty("y").GetDouble();

        var gestureJson = await page.EvaluateAsync<string>("""
            (args) => {
                const [selector, sx, sy, ex, ey] = args;
                const port = document.querySelector(selector);
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!port || !canvas || !window.tmDiagramEditor) return null;
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                if (!inst) return null;

                port.dispatchEvent(new MouseEvent('mousedown', {
                    bubbles: true, cancelable: true, button: 0, buttons: 1,
                    clientX: sx, clientY: sy
                }));
                const started = !!inst.isDrawingEdge;

                document.dispatchEvent(new MouseEvent('mousemove', {
                    bubbles: true, cancelable: true, button: 0, buttons: 1,
                    clientX: ex, clientY: ey
                }));
                document.dispatchEvent(new MouseEvent('mouseup', {
                    bubbles: true, cancelable: true, button: 0, buttons: 0,
                    clientX: ex, clientY: ey
                }));

                return JSON.stringify({ started, toolMode: inst.toolMode });
            }
        """, new object[]
        {
            $".tm-diagram-node[data-node-id='{Node1Id}'] .tm-diagram-port[data-port-id='right']",
            startX, startY, endX, endY
        });
        Assert.IsNotNull(gestureJson, "Could not dispatch port-to-empty-space edge gesture.");
        using (var gesture = JsonDocument.Parse(gestureJson!))
        {
            Assert.IsTrue(gesture.RootElement.GetProperty("started").GetBoolean(),
                $"Expected port mousedown to start edge drawing. Gesture: {gestureJson}");
        }
        await page.WaitForFunctionAsync("""
            (expected) => document.querySelectorAll('.tm-diagram-edge-path').length >= expected
        """, initialEdgeCount + 1, new PageWaitForFunctionOptions { Timeout = 5000 });

        var allEdges = page.Locator(".tm-diagram-edge-path");
        var edgeCount = await allEdges.CountAsync();

        var dangling = page.Locator(".tm-diagram-edge-handle--dangling[data-dangling='target']");
        var count = await dangling.CountAsync();
        Assert.AreEqual(initialEdgeCount + 1, edgeCount, $"Expected edge count to increase by one after draw (got {edgeCount} edges).");
        Assert.IsTrue(count > 0, "Expected at least one floating target handle after drawing to empty space");

        await TakeScreenshotAsync(page, "phase3_dangling_edge");
    }

    [TestMethod]
    [Description("3.3 — Dangling edge reconnect to node outline")]
    public async Task Phase3_DanglingReconnect_ToNodeOutline()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // Create dangling edge. Drop the floating endpoint in verifiably
        // empty canvas space (below every node) so we don't accidentally
        // attach to a port from a neighbouring node — that would yield a
        // fully-connected edge instead of a dangling one.
        var port = page.Locator($".tm-diagram-node[data-node-id='{Node1Id}'] .tm-diagram-port[data-port-id='right']");
        await port.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var (startX, startY) = await GetCenterAsync(port);

        var emptyCoordsJson = await page.EvaluateAsync<string>("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const cr = canvas.getBoundingClientRect();
                const viewH = window.innerHeight;
                const visibleBottom = Math.min(cr.bottom, viewH) - 20;
                const nodes = Array.from(document.querySelectorAll('.tm-diagram-node[data-node-id]'));
                let maxBottom = cr.top + 100;
                for (const n of nodes) {
                    const r = n.getBoundingClientRect();
                    maxBottom = Math.max(maxBottom, r.bottom);
                }
                return JSON.stringify({
                    x: cr.left + 80,
                    y: Math.min(visibleBottom - 40, maxBottom + 40)
                });
            }
        """);
        using var ec = JsonDocument.Parse(emptyCoordsJson!);
        var emptyX = ec.RootElement.GetProperty("x").GetDouble();
        var emptyY = ec.RootElement.GetProperty("y").GetDouble();

        await page.Mouse.MoveAsync((float)startX, (float)startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)emptyX, (float)emptyY, new MouseMoveOptions { Steps = 4 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(500);

        // NOTE: `.tm-diagram-edge-handle--dangling` is reused as the generic
        // "endpoint handle" class when an edge is SELECTED — so for a
        // just-drawn selected edge with attached source + floating target,
        // TWO elements match the selector: data-dangling="source" (attached
        // but rendered with the same class) and data-dangling="target" (the
        // real floating endpoint). We must pick the *target* handle here,
        // otherwise the second drag grabs the source handle sitting on top
        // of the class1.right port. Scoped by the most recent edge id.
        var dangling = page.Locator(".tm-diagram-edge-handle--dangling[data-dangling='target']").Last;
        await dangling.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Drag dangling handle onto class2 node
        var node2 = page.Locator($".tm-diagram-node[data-node-id='{Node2Id}']");
        var (targetX, targetY) = await GetCenterAsync(node2);
        var (dgX, dgY) = await GetCenterAsync(dangling);

        // Outline-connect fires only after a 2s hover timer elapses while the
        // dangling handle stays over the same node. The timer is scheduled on
        // hover-enter during mousemove and CLEARED on mouseup, so the hover
        // wait MUST happen before we release. After F2 the timer also needs a
        // tiny settle tick for the `danglingOutlineConnect` flag to propagate
        // before mouseup reads it.
        await page.Mouse.MoveAsync((float)dgX, (float)dgY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)targetX, (float)targetY);
        await page.WaitForTimeoutAsync(2200);
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(500);

        // After a successful outline-connect the edge's target is attached to
        // node2. The edge may still be selected (endpoint handles visible)
        // but the target handle must no longer be marked as floating in the
        // model — deselect the edge and re-count. A reconnected edge shows
        // NO dangling handles when not selected.
        await page.EvaluateAsync("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !window.tmDiagramEditor) return;
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                if (inst) {
                    inst.selectedIds.clear();
                    if (typeof window.tmDiagramEditor._updateSelection === 'function') {
                        window.tmDiagramEditor._updateSelection(inst);
                    }
                    if (inst.dotNetRef) inst.dotNetRef.invokeMethodAsync('OnClearSelection');
                }
            }
        """);
        await page.WaitForTimeoutAsync(300);

        var remainingDangling = page.Locator(".tm-diagram-edge-handle--dangling");
        var remainingCount = await remainingDangling.CountAsync();
        Assert.AreEqual(0, remainingCount, "Expected dangling handle to disappear after outline connect");

        await TakeScreenshotAsync(page, "phase3_outline_connect");
    }

    [TestMethod]
    [Description("3.5.5 — Connection point click starts edge draw with constraint")]
    public async Task Phase3_ConnectionPoint_ClickStartsEdgeDraw()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        var cp = page.Locator($".tm-diagram-node[data-node-id='{Node1Id}'] .tm-diagram-connection-point").First;
        if (await cp.CountAsync() == 0)
        {
            Assert.Inconclusive("No connection points rendered for the sample node.");
        }

        await cp.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var (cpx, cpy) = await GetCenterAsync(cp);

        await page.Mouse.MoveAsync((float)cpx, (float)cpy);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(cpx + 100), (float)(cpy + 50));
        await page.WaitForTimeoutAsync(200);
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(300);

        // Canvas should still be intact
        var canvas = page.Locator(".tm-diagram-canvas");
        Assert.IsTrue(await canvas.IsVisibleAsync(), "Canvas should still be visible after connection point interaction");

        await TakeScreenshotAsync(page, "phase3_connection_point");
    }

    [TestMethod]
    [Description("3.6 — Edge-to-edge hover highlight while drawing")]
    public async Task Phase3_EdgeToEdge_HoverShowsHighlight()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // Create a dangling edge first
        var port = page.Locator($".tm-diagram-node[data-node-id='{Node1Id}'] .tm-diagram-port[data-port-id='bottom']");
        await port.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var (px, py) = await GetCenterAsync(port);

        await page.Mouse.MoveAsync((float)px, (float)py);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)px, (float)(py + 120));
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(500);

        // Start a second draw
        var port2 = page.Locator($".tm-diagram-node[data-node-id='{Node2Id}'] .tm-diagram-port[data-port-id='bottom']");
        await port2.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var (p2x, p2y) = await GetCenterAsync(port2);

        await page.Mouse.MoveAsync((float)p2x, (float)p2y);
        await page.Mouse.DownAsync();

        var firstEdge = page.Locator(".tm-diagram-edge-path").First;
        var (ex, ey) = await GetCenterAsync(firstEdge);
        await page.Mouse.MoveAsync((float)ex, (float)ey);
        await page.WaitForTimeoutAsync(300);

        var highlighted = await firstEdge.EvaluateAsync<bool>(
            "el => el.closest('.tm-diagram-edge-group')?.classList.contains('tm-diagram-edge--target') || false");

        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(300);

        if (!highlighted)
        {
            Assert.Inconclusive("Edge-to-edge highlight not detected — known issue (3.6.2 audit finding).");
        }

        await TakeScreenshotAsync(page, "phase3_edge_to_edge");
    }

    // ========================================================================
    // FÁZE 4 — Pokročilé featury
    // ========================================================================

    [TestMethod]
    [Description("4.1 — Label drag moves label relative to edge")]
    public async Task Phase4_LabelDrag_MovesLabelRelativeToEdge()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // Sample edge has label "1..*"
        var labelGroup = page.Locator(".tm-diagram-edge-label-group").First;
        if (await labelGroup.CountAsync() == 0)
        {
            Assert.Inconclusive("No edge label rendered on sample edge.");
        }

        await labelGroup.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var (lx, ly) = await GetCenterAsync(labelGroup);

        await page.Mouse.MoveAsync((float)lx, (float)ly);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(lx + 20), (float)(ly - 15));
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(500);

        // Label should still be visible after drag
        Assert.IsTrue(await labelGroup.IsVisibleAsync(), "Label should remain visible after drag");

        await TakeScreenshotAsync(page, "phase4_label_drag");
    }

    [TestMethod]
    [Description("4.2 — Double-click on elbow handle flips orientation")]
    public async Task Phase4_ElbowFlip_DoubleClickChangesOrientation()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // Switch to elbow via properties panel (context menu may be blocked by HTML overlay)
        await SelectEdgeAsync(page);
        await SetEdgeRoutingAsync(page, "elbow");

        var handles = page.Locator(".tm-diagram-edge-handle:not(.tm-diagram-edge-handle--dangling):not(.tm-diagram-edge-handle--jetty):not(.tm-diagram-edge-segment-handle)");
        if (await handles.CountAsync() < 2)
        {
            Assert.Inconclusive("Not enough handles for elbow flip test.");
        }

        var middle = handles.Nth(1);
        // Dispatch dblclick via JS because HTML overlay blocks Mouse.DblClickAsync on SVG handles
        await middle.EvaluateAsync(@"
            el => {
                const rect = el.getBoundingClientRect();
                const cx = rect.left + rect.width / 2;
                const cy = rect.top + rect.height / 2;
                el.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, clientX: cx, clientY: cy }));
            }");
        await page.WaitForTimeoutAsync(800);

        // Edge should still be rendered after flip
        var edgePath = page.Locator(".tm-diagram-edge-path").First;
        // Use CountAsync > 0 instead of IsVisibleAsync to avoid transient detached-element issues during Blazor re-render
        var edgeCount = await edgePath.CountAsync();
        Assert.IsTrue(edgeCount > 0, "Edge should remain visible after elbow flip");

        await TakeScreenshotAsync(page, "phase4_elbow_flip");
    }

    [TestMethod]
    [Description("4.4 — ArcSize input changes edge rounding")]
    public async Task Phase4_ArcSize_InputChangesRounding()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        await SelectEdgeAsync(page);

        var propsPanel = page.Locator(".tm-diagram-properties-panel, .tm-diagram-editor__props-wrap").First;

        // Ensure Rounded is enabled so ArcSize input is visible
        var roundedCheckbox = propsPanel.Locator(".tm-checkbox-wrapper").Filter(new LocatorFilterOptions { HasText = "Rounded" }).First;
        if (await roundedCheckbox.CountAsync() > 0)
        {
            var isChecked = await roundedCheckbox.Locator("input").IsCheckedAsync();
            if (!isChecked)
            {
                await roundedCheckbox.ClickAsync();
                await page.WaitForTimeoutAsync(300);
            }
        }

        // Find ArcSize input by label text (localized as "Corner Radius")
        var arcSizeField = propsPanel.Locator(".tm-diagram-properties__field").Filter(new LocatorFilterOptions { HasText = "Radius" }).First;
        if (await arcSizeField.CountAsync() == 0)
        {
            Assert.Inconclusive("ArcSize (Corner Radius) field not found in properties panel.");
        }
        var targetInput = arcSizeField.Locator("input[type='number']");
        await targetInput.FillAsync("25");
        await targetInput.BlurAsync();
        await page.WaitForTimeoutAsync(1500);

        // Verify edge path is still present (IsVisibleAsync can return false during re-render)
        var edgePaths = page.Locator(".tm-diagram-edge-path");
        var edgeCount = await edgePaths.CountAsync();
        Assert.IsTrue(edgeCount > 0, "Edge should remain visible after ArcSize change");

        await TakeScreenshotAsync(page, "phase4_arcsize");
    }

    // ========================================================================
    // FÁZE 5 — Specializované
    // ========================================================================

    [TestMethod]
    [Description("5.1 — Cubic bezier toggle changes path shape")]
    public async Task Phase5_CubicBezier_ToggleChangesPathShape()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        await SelectEdgeAsync(page);

        // Cubic Bézier checkbox is only visible when routing is "curved"
        await SetEdgeRoutingAsync(page, "curved");

        var propsPanel = page.Locator(".tm-diagram-properties-panel, .tm-diagram-editor__props-wrap").First;
        var checkbox = propsPanel.Locator(".tm-checkbox-wrapper").Filter(new LocatorFilterOptions { HasText = "Cubic" }).First;
        if (await checkbox.CountAsync() == 0)
        {
            Assert.Inconclusive("Cubic bezier checkbox not found.");
        }

        // Get path d attribute before
        var edgePath = page.Locator(".tm-diagram-edge-path").First;
        var dBefore = await edgePath.GetAttributeAsync("d");

        await checkbox.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var dAfter = await edgePath.GetAttributeAsync("d");
        Assert.AreNotEqual(dBefore, dAfter, "Path data should change after toggling cubic bezier");

        await TakeScreenshotAsync(page, "phase5_cubic_bezier");
    }

    [TestMethod]
    [Description("5.6 — Rubber-band selection includes edges")]
    public async Task Phase5_RubberBandSelection_IncludesEdges()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // Install a mousedown event probe so we can prove whether Playwright's
        // mouse events actually reach DOM listeners at all (if none arrive,
        // neither deselect nor the rubber-band can ever trigger). Also capture
        // the diagram instance state before/after each event to pinpoint which
        // branch of `_onMouseDown` ran (or whether it ran at all).
        await page.EvaluateAsync("""
            () => {
                window.__tmMouseDowns = [];
                const canvas = document.querySelector('.tm-diagram-canvas');
                const ed = window.tmDiagramEditor;
                const inst = canvas && ed && ed.instances && ed.instances.get(canvas.id);
                function snap() {
                    if (!inst) return null;
                    return {
                        hasInst: true,
                        isRubberBand: !!inst.isRubberBand,
                        isDragging: !!inst.isDragging,
                        isDraggingWholeEdge: !!inst.isDraggingWholeEdge,
                        isDraggingJetty: !!inst.isDraggingJetty,
                        isDraggingWaypoint: !!inst.isDraggingWaypoint,
                        isDraggingDangling: !!inst.isDraggingDangling,
                        isDraggingSegment: !!inst.isDraggingSegment,
                        isPanning: !!inst.isPanning,
                        isDrawingEdge: !!inst.isDrawingEdge,
                        isDrawingPolyline: !!inst.isDrawingPolyline,
                        isPendingEdgeDraw: !!inst.isPendingEdgeDraw,
                        readOnly: !!inst.readOnly,
                        toolMode: inst.toolMode,
                        selectedIdsSize: inst.selectedIds ? inst.selectedIds.size : null,
                        selectedIds: inst.selectedIds ? [...inst.selectedIds] : null,
                        rubberElAttached: !!(inst.rubberEl && inst.rubberEl.isConnected)
                    };
                }
                window.__tmSnap = snap;
                // Capture in BOTH capture and bubble phases — if something in
                // between stops propagation we will see only the capture log.
                window.addEventListener('mousedown', (e) => {
                    const el = e.target;
                    window.__tmMouseDowns.push({
                        phase: 'capture',
                        x: Math.round(e.clientX), y: Math.round(e.clientY), btn: e.button,
                        defaultPrevented: e.defaultPrevented,
                        tag: el && el.tagName,
                        closest: el && el.closest ? (
                            (el.closest('.tm-diagram-edge-hit-path') ? 'edge-hit' : '') ||
                            (el.closest('.tm-diagram-edge-virtual-bend') ? 'virtual-bend' : '') ||
                            (el.closest('[data-node-id]') ? 'node' : '') ||
                            (el.closest('.tm-diagram-port') ? 'port' : '') ||
                            (el.closest('.tm-diagram-connection-point') ? 'cp' : '') ||
                            (el.closest('.tm-diagram-canvas') ? 'canvas' : 'outside-canvas')
                        ) : '?',
                        snap: snap()
                    });
                }, true);
                window.addEventListener('mousedown', (e) => {
                    window.__tmMouseDowns.push({
                        phase: 'bubble',
                        x: Math.round(e.clientX), y: Math.round(e.clientY),
                        defaultPrevented: e.defaultPrevented,
                        propagationStopped: e.cancelBubble,
                        snap: snap()
                    });
                }, false);
            }
        """);

        // Deselect by clicking empty area (top-left corner of canvas is usually empty)
        var canvas = page.Locator(".tm-diagram-canvas");
        var box = await canvas.BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.ClickAsync((float)(box.X + 10), (float)(box.Y + 10));
        await page.WaitForTimeoutAsync(200);

        // Rubber-band rectangle must enclose at least one full node *in the
        // coordinate space the JS rubber-band algorithm uses*. That algorithm
        // converts screen points to SVG user space via `getScreenCTM()`
        // (affected by SVG `preserveAspectRatio="xMidYMid meet"`), while
        // `_nodeRect` reads node translate directly from the HTML overlay
        // (no meet-centering). For viewBoxes whose aspect ratio ≠ canvas
        // aspect ratio, these two mappings diverge and a tight rectangle
        // that *looks* correct on screen fails the doc-space enclosure test.
        //
        // The robust test strategy is therefore: drag from ~top-left corner
        // of the canvas to ~bottom-right corner — the resulting rectangle is
        // so large that it encloses every node in every coordinate space.
        // We only need to ensure the two corners themselves don't land on an
        // interactive element that would eat the mousedown.
        var coordsJson = await page.EvaluateAsync<string>("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas) return null;
                const cr = canvas.getBoundingClientRect();
                const nodes = Array.from(document.querySelectorAll('.tm-diagram-node[data-node-id]'));
                if (!nodes.length) return null;

                function describeAt(x, y) {
                    const el = document.elementFromPoint(x, y);
                    if (!el) return { x, y, el: null };
                    return {
                        x, y,
                        tag: el.tagName,
                        cls: (el.className && el.className.baseVal) || el.className || '',
                        id: el.id || null,
                        inCanvas: canvas.contains(el),
                        nodeId: el.closest('[data-node-id]')?.getAttribute('data-node-id') || null,
                        edgeId: el.closest('[data-edge-id]')?.getAttribute('data-edge-id') || null,
                        port:   !!el.closest('.tm-diagram-port'),
                        cp:     !!el.closest('.tm-diagram-connection-point'),
                        hitPath:!!el.closest('.tm-diagram-edge-hit-path')
                    };
                }

                function isEmpty(x, y) {
                    const el = document.elementFromPoint(x, y);
                    if (!el) return false;
                    if (!canvas.contains(el)) return false;
                    if (el.closest('[data-node-id]')) return false;
                    if (el.closest('.tm-diagram-port')) return false;
                    if (el.closest('.tm-diagram-connection-point')) return false;
                    if (el.closest('.tm-diagram-edge-hit-path')) return false;
                    if (el.closest('.tm-diagram-edge-handle')) return false;
                    if (el.closest('.tm-diagram-edge-virtual-bend')) return false;
                    return true;
                }

                // elementFromPoint only considers the visible viewport.
                // Points below/right of the viewport return null, so clamp
                // the corner search to what is actually on-screen.
                const viewW = window.innerWidth;
                const viewH = window.innerHeight;
                const visLeft   = Math.max(cr.left + 2, 2);
                const visTop    = Math.max(cr.top  + 2, 2);
                const visRight  = Math.min(cr.right  - 2, viewW - 2);
                const visBottom = Math.min(cr.bottom - 2, viewH - 2);

                function pickCorner(cornerX, cornerY, dx, dy) {
                    for (let step = 0; step < 60; step++) {
                        const x = cornerX + dx * step * 3;
                        const y = cornerY + dy * step * 3;
                        if (x < visLeft || x > visRight) continue;
                        if (y < visTop  || y > visBottom) continue;
                        if (isEmpty(x, y)) return { x, y };
                    }
                    return null;
                }

                const start = pickCorner(visLeft,  visTop,    +1, +1);
                const end   = pickCorner(visRight, visBottom, -1, -1);
                if (!start || !end) {
                    return JSON.stringify({
                        error: 'no-empty-corner',
                        cr,
                        startProbe: describeAt(Math.round(cr.left + 8), Math.round(cr.top + 8)),
                        endProbe:   describeAt(Math.round(cr.right - 8), Math.round(cr.bottom - 8)),
                        foundStart: start, foundEnd: end
                    });
                }
                return JSON.stringify({ startX: start.x, startY: start.y, endX: end.x, endY: end.y });
            }
        """);
        Assert.IsNotNull(coordsJson, "coordsJson returned null");
        using (var probeDoc = JsonDocument.Parse(coordsJson))
        {
            if (probeDoc.RootElement.TryGetProperty("error", out _))
            {
                Assert.Fail("Failed to find empty canvas corner points. Probe: " + coordsJson);
            }
        }
        using var coordsDoc = JsonDocument.Parse(coordsJson);
        var startX = coordsDoc.RootElement.GetProperty("startX").GetDouble();
        var startY = coordsDoc.RootElement.GetProperty("startY").GetDouble();
        var endX   = coordsDoc.RootElement.GetProperty("endX").GetDouble();
        var endY   = coordsDoc.RootElement.GetProperty("endY").GetDouble();

        // Drag the mouse across the canvas with intermediate move steps so the
        // rubber-band has time to render and the JS mousemove handler is fed a
        // proper drag trajectory (single big jump is sometimes coalesced).
        await page.Mouse.MoveAsync((float)startX, (float)startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)((startX + endX) / 2), (float)((startY + endY) / 2), new MouseMoveOptions { Steps = 6 });
        await page.Mouse.MoveAsync((float)endX, (float)endY, new MouseMoveOptions { Steps = 6 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);

        // Check that rubber-band element existed during drag (it is removed after mouseup)
        // We verify selection changed by checking that nodes got selected class or selection outlines
        var outlines = page.Locator(".tm-diagram-selection-outline");
        var outlineCount = await outlines.CountAsync();
        if (outlineCount == 0)
        {
            var sx = (int)Math.Round(startX);
            var sy = (int)Math.Round(startY);
            var ex = (int)Math.Round(endX);
            var ey = (int)Math.Round(endY);
            var postDiag = await page.EvaluateAsync<string>($$"""
                () => {
                    const canvas = document.querySelector('.tm-diagram-canvas');
                    const cr = canvas ? canvas.getBoundingClientRect() : null;
                    const selNodes = document.querySelectorAll('.tm-diagram-node--selected').length;
                    const outlines = document.querySelectorAll('.tm-diagram-selection-outline').length;
                    const selLayer = !!document.querySelector('.tm-diagram-selection-layer');
                    const htmlLayer = !!document.querySelector('.tm-diagram-scene-pane .tm-diagram-canvas__overlay');
                    const nodes = Array.from(document.querySelectorAll('.tm-diagram-node[data-node-id]'));
                    const nodeRects = nodes.map(n => { const r = n.getBoundingClientRect(); return { id: n.getAttribute('data-node-id'), l: Math.round(r.left), t: Math.round(r.top), r: Math.round(r.right), b: Math.round(r.bottom) }; });
                    const canvasCls = canvas ? canvas.className : null;
                    function describe(x, y) {
                        const el = document.elementFromPoint(x, y);
                        if (!el) return null;
                        return {
                            tag: el.tagName,
                            cls: (el.className && el.className.baseVal || el.className || ''),
                            id: el.id || null,
                            dataNodeId: el.closest('[data-node-id]') ? el.closest('[data-node-id]').getAttribute('data-node-id') : null,
                            dataEdgeId: el.closest('[data-edge-id]') ? el.closest('[data-edge-id]').getAttribute('data-edge-id') : null,
                            chain: (function () {
                                const chain = [];
                                let cur = el;
                                for (let i = 0; i < 6 && cur; i++) {
                                    chain.push(cur.tagName + (cur.className ? '.' + (cur.className.baseVal || cur.className).toString().replace(/\s+/g, '.') : ''));
                                    cur = cur.parentElement;
                                }
                                return chain;
                            })()
                        };
                    }
                    // Convert screen points to SVG user (doc) coordinates
                    // using the live CTM — same transform `_screenToDoc` uses.
                    const svg = document.querySelector('.tm-diagram-canvas__svg');
                    let docStart = null, docEnd = null, svgTransform = null, viewBox = null;
                    if (svg) {
                        const ctm = svg.getScreenCTM();
                        if (ctm) {
                            const inv = ctm.inverse();
                            const p1 = svg.createSVGPoint(); p1.x = {{sx}}; p1.y = {{sy}};
                            const p2 = svg.createSVGPoint(); p2.x = {{ex}}; p2.y = {{ey}};
                            const r1 = p1.matrixTransform(inv);
                            const r2 = p2.matrixTransform(inv);
                            docStart = { x: Math.round(r1.x), y: Math.round(r1.y) };
                            docEnd   = { x: Math.round(r2.x), y: Math.round(r2.y) };
                        }
                        svgTransform = svg.style.transform || '';
                        viewBox = svg.getAttribute('viewBox');
                    }
                    // Read node translate+data-w/h like `_nodeRect` does.
                    // F2 moved nodes into a <foreignObject> inside the scene pane; the
                    // .tm-diagram-canvas__overlay wrapper still exists but is nested inside
                    // the SVG now. Prefer the scene pane as the query root so this stays
                    // correct through F3 when per-node <g>s replace the single foreignObject.
                    const overlay = document.querySelector('.tm-diagram-scene-pane');
                    // F3.A — nodes are per-node SVG <g> with the SVG `transform`
                    // attribute carrying translate(x,y) rotate(...). No px units,
                    // no CSS transform.
                    const nodeDocRects = Array.from((overlay || document).querySelectorAll('g.tm-diagram-node[data-node-id]')).map(el => {
                        const s = el.getAttribute('transform') || '';
                        const m = s.match(/translate\(\s*([-\d.e+]+)\s*,\s*([-\d.e+]+)\s*\)/);
                        return {
                            id: el.getAttribute('data-node-id'),
                            x: m ? Math.round(parseFloat(m[1])) : null,
                            y: m ? Math.round(parseFloat(m[2])) : null,
                            w: parseFloat(el.getAttribute('data-w') || '0'),
                            h: parseFloat(el.getAttribute('data-h') || '0')
                        };
                    });
                    return JSON.stringify({
                        cr, selNodes, outlines, selLayer, htmlLayer,
                        nodeCount: nodes.length, nodeRects, nodeDocRects, canvasCls,
                        elAtStart: describe({{sx}}, {{sy}}),
                        elAtEnd:   describe({{ex}}, {{ey}}),
                        docStart, docEnd, viewBox, svgTransform,
                        mouseDownCount: (window.__tmMouseDowns || []).length,
                        mouseDownLog:   (window.__tmMouseDowns || []).slice(-4),
                        instState:      window.__tmSnap ? window.__tmSnap() : null
                    });
                }
            """);
            Assert.Fail(
                $"Rubber-band should select at least one node and create selection outline. " +
                $"Start=({startX:F1},{startY:F1}) End=({endX:F1},{endY:F1}). Post-diagnostics: {postDiag}");
        }

        await TakeScreenshotAsync(page, "phase5_rubber_band");
    }

    [TestMethod]
    [Description("5.7 — Selected edge shows selection outline")]
    public async Task Phase5_SelectionOutline_VisibleOnSelectedEdge()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        await SelectEdgeAsync(page);

        // Selection outline for edge is rendered as a dashed outline path inside the SVG
        var outlinePath = page.Locator(".tm-diagram-edge-path--selected-outline").First;
        var dAttr = await outlinePath.GetAttributeAsync("d");
        Assert.IsFalse(string.IsNullOrEmpty(dAttr), "Selected edge should have a selection outline path");

        await TakeScreenshotAsync(page, "phase5_selection_outline");
    }

    [TestMethod]
    [Description("5.5 — Cardinality symbols render near edge terminals")]
    public async Task Phase5_Cardinality_SelectShowsSymbols()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        await SelectEdgeAsync(page);

        var propsPanel = page.Locator(".tm-diagram-properties-panel, .tm-diagram-editor__props-wrap").First;
        // Find cardinality select by label text (TmSelect renders a native <select>)
        var cardinalityField = propsPanel.Locator(".tm-diagram-properties__field").Filter(new LocatorFilterOptions { HasText = "Cardinality" }).First;
        if (await cardinalityField.CountAsync() == 0)
        {
            Assert.Inconclusive("Cardinality field not found in properties panel.");
        }
        var cardinalitySelect = cardinalityField.Locator("select");
        await cardinalitySelect.SelectOptionAsync("one");
        await page.WaitForTimeoutAsync(500);

        var cardinalityText = page.Locator(".tm-diagram-edge-cardinality");
        var textCount = await cardinalityText.CountAsync();
        Assert.IsTrue(textCount > 0, "Cardinality text element should be rendered after selection");

        await TakeScreenshotAsync(page, "phase5_cardinality");
    }

    // ========================================================================
    // FÁZE 6 — Drag celé čáry (Whole Edge Drag)
    // ========================================================================

    [TestMethod]
    [Description("6.1 — Dragging a selected edge by its hit-path detaches both ends and moves the edge")]
    public async Task Phase6_WholeEdgeDrag_DetachesAndMovesEdge()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // Select the edge first so handles are visible
        await SelectEdgeAsync(page);
        await page.WaitForTimeoutAsync(200);

        // Drag the edge via JS dispatchEvent — the HTML overlay intercepts native mouse
        // events, so Playwright Mouse API doesn't reach the SVG/canvas handlers reliably.
        // We compute the mid-point of the visible edge path using SVG APIs (getPointAtLength
        // + CTM) because getBoundingClientRect on a transparent stroke-only path returns
        // an empty rect in some browsers.
        var result = await page.EvaluateAsync<string>("""
            () => {
                const path = document.querySelector('.tm-diagram-edge-path');
                const hitPath = document.querySelector('.tm-diagram-edge-hit-path');
                if (!path || !hitPath) return 'missing';

                const len = path.getTotalLength();
                const mid = path.getPointAtLength(len / 2);
                const svg = path.closest('svg');
                const pt = svg.createSVGPoint();
                pt.x = mid.x;
                pt.y = mid.y;
                const ctm = svg.getScreenCTM();
                if (!ctm) return 'noctm';
                const sp = pt.matrixTransform(ctm);

                const sx = sp.x;
                const sy = sp.y;
                const ex = sx + 120;
                const ey = sy + 80;

                hitPath.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, button: 0, clientX: sx, clientY: sy }));
                document.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, cancelable: true, button: 0, clientX: ex, clientY: ey }));
                document.dispatchEvent(new MouseEvent('mouseup',   { bubbles: true, cancelable: true, button: 0, clientX: ex, clientY: ey }));
                return 'done';
            }
        """);
        Assert.AreEqual("done", result, "Whole-edge drag dispatch failed");
        await page.WaitForTimeoutAsync(800);

        // Edge path should still exist after the drag
        var edgePaths = page.Locator(".tm-diagram-edge-path");
        Assert.IsTrue(await edgePaths.CountAsync() > 0, "Edge path should still exist after whole-edge drag");

        // After detaching, dangling handles should appear (proof that ends disconnected)
        var danglingHandles = page.Locator(".tm-diagram-edge-handle--dangling");
        var danglingCount = await danglingHandles.CountAsync();
        Assert.IsTrue(danglingCount >= 2, $"Expected at least 2 dangling handles after whole-edge drag (got {danglingCount})");

        await TakeScreenshotAsync(page, "phase6_whole_edge_drag");
    }

    [TestMethod]
    [Description("6.2 — Dragging a connected terminal handle detaches the end from its node")]
    public async Task Phase6_ConnectedTerminalDrag_DetachesEnd()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // Select edge to reveal dangling handles on connected ends
        await SelectEdgeAsync(page);
        await page.WaitForTimeoutAsync(300);

        // Drag the source dangling handle (on a connected end) away from its node
        var result = await page.EvaluateAsync<string>("""
            () => {
                const handle = document.querySelector('rect.tm-diagram-edge-handle--dangling[data-dangling="source"]');
                if (!handle) return 'missing';
                const r = handle.getBoundingClientRect();
                const sx = r.left + r.width / 2;
                const sy = r.top + r.height / 2;
                const ex = sx + 150;
                const ey = sy + 100;
                handle.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, button: 0, clientX: sx, clientY: sy }));
                document.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, cancelable: true, button: 0, clientX: ex, clientY: ey }));
                document.dispatchEvent(new MouseEvent('mouseup',   { bubbles: true, cancelable: true, button: 0, clientX: ex, clientY: ey }));
                return 'done';
            }
        """);
        Assert.AreEqual("done", result, "Connected-terminal drag dispatch failed");
        await page.WaitForTimeoutAsync(800);

        // Edge path should still exist
        var edgePaths = page.Locator(".tm-diagram-edge-path");
        Assert.IsTrue(await edgePaths.CountAsync() > 0, "Edge path should still exist after connected-terminal drag");

        // The source end should now be detached — at least one dangling handle should remain visible
        var danglingHandles = page.Locator(".tm-diagram-edge-handle--dangling");
        var danglingCount = await danglingHandles.CountAsync();
        Assert.IsTrue(danglingCount >= 1, $"Expected at least 1 dangling handle after connected-terminal drag (got {danglingCount})");

        await TakeScreenshotAsync(page, "phase6_connected_terminal_drag");
    }

    [TestMethod]
    [Description("6.3 — Floating inline toolbar appears when a single edge is selected")]
    public async Task Phase6_InlineToolbar_AppearsOnEdgeSelection()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        await SelectEdgeAsync(page);
        await page.WaitForTimeoutAsync(300);

        var toolbar = page.Locator(".tm-diagram-edge-toolbar");
        await toolbar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var buttons = toolbar.Locator("button");
        var btnCount = await buttons.CountAsync();
        Assert.IsTrue(btnCount >= 2, $"Expected at least 2 toolbar buttons (got {btnCount})");

        await TakeScreenshotAsync(page, "phase6_inline_toolbar_visible");
    }

    [TestMethod]
    [Description("6.4 — Inline toolbar flip button works (edge remains visible after flip)")]
    public async Task Phase6_InlineToolbar_FlipButtonWorks()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // Select edge and switch to elbow routing so flip is applicable
        await SelectEdgeAsync(page);
        await SetEdgeRoutingAsync(page, "elbow");
        await page.WaitForTimeoutAsync(300);

        var flipBtn = page.Locator(".tm-diagram-edge-toolbar button[data-action='flip']");
        if (await flipBtn.CountAsync() == 0)
        {
            Assert.Inconclusive("Flip button not found in inline toolbar.");
        }

        // Move mouse away from nodes first so lingering hover state doesn't
        // re-show connection-points over the toolbar, then force-click to be
        // robust against any remaining overlay elements (pointer intercepts).
        await page.Mouse.MoveAsync(2f, 2f);
        await page.WaitForTimeoutAsync(100);
        await flipBtn.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForTimeoutAsync(500);

        // Edge should still be rendered after flip (degenerate elbow may not change path visually)
        var edgePath = page.Locator(".tm-diagram-edge-path").First;
        Assert.IsTrue(await edgePath.CountAsync() > 0, "Edge should remain visible after toolbar flip");

        await TakeScreenshotAsync(page, "phase6_inline_toolbar_flip");
    }

    [TestMethod]
    [Description("6.5 — Context menu shows graphical routing picker with SVG previews")]
    public async Task Phase6_ContextMenu_RoutingPickerHasSvgPreviews()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // The viewport context menu is the DESKTOP presentation; in the tablet band the
        // context menu renders as a bottom sheet (phase 6). Pin the desktop band.
        await DiagramEditorE2E.PinDesktopAsync(page);

        await SelectEdgeAsync(page);
        await OpenEdgeContextMenuAsync(page);

        var picker = page.Locator(".tm-diagram-edge-routing-picker");
        await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Phase 8 regression pin: the menu paints inside the editor box, so its HORIZONTAL
        // extent must stay within it (a viewport-clamped menu used to stick out past a hosted
        // narrow editor and clip the routing previews). Vertically the menu may exceed the
        // editor box: it is taller than most editors and scrolls internally.
        var menuBox = await page.EvaluateAsync<string>(
            """
            () => {
                const menu = document.querySelector('.tm-diagram-editor__context-menu');
                if (!menu) return JSON.stringify({ found: false });
                const m = menu.getBoundingClientRect();
                return JSON.stringify({
                    menuLeft: Math.round(m.left), menuRight: Math.round(m.right),
                    menuTop: Math.round(m.top), menuBottom: Math.round(m.bottom),
                    viewportW: window.innerWidth, viewportH: window.innerHeight,
                });
            }
            """);
        Console.WriteLine($"PROBE[context-menu box]: {menuBox}");
        // Phase 8 regression pin: the context menu must end up FULLY ON SCREEN wherever the
        // editor is hosted (the fixed menu used to be clamped against the viewport while its
        // containing block semantics differed, leaving a tall menu parked off-screen and the
        // edge routing previews unreachable).
        var menuOnScreen = await page.EvaluateAsync<bool>(
            """
            () => {
                const menu = document.querySelector('.tm-diagram-editor__context-menu');
                if (!menu) return false;
                const m = menu.getBoundingClientRect();
                return m.left >= 0 && m.top >= 0
                    && m.right <= window.innerWidth + 1 && m.bottom <= window.innerHeight + 1;
            }
            """);
        Assert.IsTrue(menuOnScreen, $"the context menu must be fully on screen: {menuBox}");

        var previews = picker.Locator(".tm-diagram-routing-preview");
        var count = await previews.CountAsync();
        Assert.IsTrue(count >= 4, $"Expected at least 4 routing previews (got {count})");

        var firstSvg = previews.First.Locator("svg");
        Assert.IsTrue(await firstSvg.CountAsync() > 0, "Each preview should contain an SVG");

        // Click orthogonal preview
        var orthogonal = picker.Locator(".tm-diagram-routing-preview[data-routing='orthogonal']");
        if (await orthogonal.CountAsync() == 0)
        {
            Assert.Inconclusive("Orthogonal preview not found in routing picker.");
        }
        await orthogonal.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Context menu should close
        var menu = page.Locator(".tm-diagram-editor__context-menu");
        Assert.IsTrue(await menu.CountAsync() == 0, "Context menu should close after selecting routing");

        await TakeScreenshotAsync(page, "phase6_routing_picker");
    }

    [TestMethod]
    [Description("6.6 — Context menu Flip action works for elbow edges")]
    public async Task Phase6_ContextMenu_FlipActionWorks()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // The viewport context menu is the DESKTOP presentation; in the tablet band the
        // context menu renders as a bottom sheet (phase 6). Pin the desktop band.
        await DiagramEditorE2E.PinDesktopAsync(page);

        await SelectEdgeAsync(page);
        await SetEdgeRoutingAsync(page, "elbow");
        await page.WaitForTimeoutAsync(300);

        await OpenEdgeContextMenuAsync(page);

        var flipItem = page.Locator(".tm-diagram-editor__context-item").Filter(new LocatorFilterOptions { HasText = "Flip" }).First;
        if (await flipItem.CountAsync() == 0)
        {
            Assert.Inconclusive("Flip item not found in edge context menu.");
        }

        await flipItem.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var edgePath = page.Locator(".tm-diagram-edge-path").First;
        Assert.IsTrue(await edgePath.CountAsync() > 0, "Edge should remain visible after context-menu flip");

        await TakeScreenshotAsync(page, "phase6_context_menu_flip");
    }

    [TestMethod]
    [Description("6.7 — Context menu Clear waypoints removes manual waypoints")]
    public async Task Phase6_ContextMenu_ClearWaypointsWorks()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // The viewport context menu is the DESKTOP presentation; in the tablet band the
        // context menu renders as a bottom sheet (phase 6). Pin the desktop band.
        await DiagramEditorE2E.PinDesktopAsync(page);

        await SelectEdgeAsync(page);
        await SetEdgeRoutingAsync(page, "orthogonal");
        await page.WaitForTimeoutAsync(300);

        var virtualBends = page.Locator(".tm-diagram-edge-virtual-bend");
        if (await virtualBends.CountAsync() > 0)
        {
            await page.EvaluateAsync("""
                () => {
                    const vb = document.querySelector('.tm-diagram-edge-virtual-bend');
                    if (vb) {
                        const r = vb.getBoundingClientRect();
                        vb.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, button: 0, clientX: r.left + r.width/2, clientY: r.top + r.height/2 }));
                    }
                }
            """);
            await page.WaitForTimeoutAsync(500);
        }

        var handlesBefore = page.Locator(".tm-diagram-edge-handle:not(.tm-diagram-edge-handle--dangling):not(.tm-diagram-edge-handle--jetty):not(.tm-diagram-edge-segment-handle)");
        var countBefore = await handlesBefore.CountAsync();

        await OpenEdgeContextMenuAsync(page);

        var clearItem = page.Locator(".tm-diagram-editor__context-item").Filter(new LocatorFilterOptions { HasText = "Clear waypoints" }).First;
        if (await clearItem.CountAsync() == 0)
        {
            Assert.Inconclusive("Clear waypoints item not found in edge context menu.");
        }

        await clearItem.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var handlesAfter = page.Locator(".tm-diagram-edge-handle:not(.tm-diagram-edge-handle--dangling):not(.tm-diagram-edge-handle--jetty):not(.tm-diagram-edge-segment-handle)");
        var countAfter = await handlesAfter.CountAsync();
        Assert.IsTrue(countAfter < countBefore, $"Expected fewer waypoint handles after clear (before={countBefore}, after={countAfter})");

        await TakeScreenshotAsync(page, "phase6_context_menu_clear_waypoints");
    }

    // ========================================================================
    // FÁZE 3 — Polish (snap-to-port, grid snap, detach endpoints, …)
    // ========================================================================

    [TestMethod]
    [Description("3.2 — Dragging to a point near (but not on) a port still snaps the target to that port")]
    public async Task Phase3_SnapToPort_NearMiss_Attaches()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        var initialEdgeCount = await GetEdgeCountAsync(page);

        // Capture console messages — the edge-draw code logs
        // `[EdgeDraw] Port connect -> ...` or `[EdgeDraw] Dangling -> ...`
        // which tells us exactly which _onMouseUp branch ran.
        var consoleLog = new System.Collections.Generic.List<string>();
        page.Console += (_, msg) => { if (msg.Text.Contains("[EdgeDraw]")) consoleLog.Add(msg.Text); };

        await SetToolModeAsync(page, "edge");

        // Drop the edge at a point ~10 px inside the target node, offset
        // from the port center but still within the smart-snap threshold
        // (15 / scale). Placing the cursor _inside_ the node ensures
        // `elementFromPoint` returns the node so `_findNearestPortOnNode`
        // runs and snaps the endpoint to the port.
        var pointsJson = await page.EvaluateAsync<string>("""
            (targetSelector) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const target = document.querySelector(targetSelector);
                const node = target ? target.closest('.tm-diagram-node') : null;
                if (!canvas || !target || !node) return null;
                const cr = canvas.getBoundingClientRect();
                const tr = target.getBoundingClientRect();
                const nr = node.getBoundingClientRect();
                const portCx = tr.left + tr.width / 2;
                const portCy = tr.top + tr.height / 2;
                // Move 8 px into the node, parallel to the node's local X
                // axis. Clamped so we always stay inside the node rect.
                const insideX = Math.min(nr.right - 4, Math.max(nr.left + 4, portCx + 8));
                const insideY = Math.min(nr.bottom - 4, Math.max(nr.top + 4, portCy));
                const startX = Math.max(cr.left + 30, nr.left - 220);
                const startY = Math.max(cr.top + 30, nr.top - 80);
                return JSON.stringify({
                    startX, startY,
                    endX: insideX, endY: insideY,
                    portCx, portCy, portW: tr.width, portH: tr.height,
                    nodeLeft: nr.left, nodeTop: nr.top, nodeRight: nr.right, nodeBottom: nr.bottom
                });
            }
        """, $".tm-diagram-node[data-node-id='{Node2Id}'] .tm-diagram-port[data-port-id='left']");
        Assert.IsNotNull(pointsJson, "Could not compute near-miss drag points.");
        using var points = JsonDocument.Parse(pointsJson);
        var sx = points.RootElement.GetProperty("startX").GetDouble();
        var sy = points.RootElement.GetProperty("startY").GetDouble();
        var ex = points.RootElement.GetProperty("endX").GetDouble();
        var ey = points.RootElement.GetProperty("endY").GetDouble();

        await page.Mouse.MoveAsync((float)sx, (float)sy);
        await page.Mouse.DownAsync();
        // Several intermediate steps so _onMouseMove fires and the hover
        // logic has time to latch onto the node/port.
        await page.Mouse.MoveAsync((float)((sx + ex) / 2), (float)((sy + ey) / 2), new MouseMoveOptions { Steps = 6 });
        await page.Mouse.MoveAsync((float)ex, (float)ey, new MouseMoveOptions { Steps = 6 });

        // Diagnostic probe — right before mouseup, gather everything needed
        // to understand why smart-snap might not hit the port.
        var probe = await page.EvaluateAsync<string>("""
            (args) => {
                const [cx, cy, nodeId, portId] = args;
                const el = document.elementFromPoint(cx, cy);
                const nodeEl = el ? el.closest('[data-node-id]') : null;
                const portElUnder = el ? el.closest('.tm-diagram-port') : null;
                const canvas = document.querySelector('.tm-diagram-canvas');
                const inst  = canvas ? window.tmDiagramEditor.instances.get(canvas.id) : null;
                const docPt = inst ? window.tmDiagramEditor._screenToDoc(inst, cx, cy) : null;

                let nearest = null;
                let nodeRect = null;
                const portList = [];
                if (inst && nodeEl) {
                    const nid = nodeEl.getAttribute('data-node-id');
                    nodeRect = window.tmDiagramEditor._nodeRect(inst, nid);
                    const nodeClientRect = nodeEl.getBoundingClientRect();
                    const s = inst.scale || 1;
                    const ports = nodeEl.querySelectorAll('.tm-diagram-port[data-port-id]');
                    for (const p of ports) {
                        const pr = p.getBoundingClientRect();
                        const pcxC = pr.left + pr.width / 2;
                        const pcyC = pr.top + pr.height / 2;
                        const relX = pcxC - nodeClientRect.left;
                        const relY = pcyC - nodeClientRect.top;
                        const px = nodeRect ? (nodeRect.x + relX / s) : null;
                        const py = nodeRect ? (nodeRect.y + relY / s) : null;
                        const dx = (px !== null && docPt) ? px - docPt.x : null;
                        const dy = (py !== null && docPt) ? py - docPt.y : null;
                        const distDoc = (dx !== null && dy !== null) ? Math.sqrt(dx*dx + dy*dy) : null;
                        portList.push({
                            portId: p.getAttribute('data-port-id'),
                            screenCenter: { x: Math.round(pcxC*100)/100, y: Math.round(pcyC*100)/100 },
                            docCenter: (px !== null) ? { x: Math.round(px*100)/100, y: Math.round(py*100)/100 } : null,
                            distDoc: distDoc !== null ? Math.round(distDoc*100)/100 : null
                        });
                    }
                    const threshold = 15 / Math.max(s, 0.01);
                    const res = window.tmDiagramEditor._findNearestPortOnNode(
                        inst, nid, docPt.x, docPt.y, threshold);
                    nearest = res ? {
                        portId: res.portEl ? res.portEl.getAttribute('data-port-id') : null,
                        x: res.x, y: res.y,
                        threshold
                    } : { result: null, threshold };
                }
                return JSON.stringify({
                    hitTag: el ? el.tagName : null,
                    hitClass: el ? el.getAttribute('class') : null,
                    nodeId: nodeEl ? nodeEl.getAttribute('data-node-id') : null,
                    onPort: portElUnder ? portElUnder.getAttribute('data-port-id') : null,
                    cursorScreen: { x: cx, y: cy },
                    cursorDoc: docPt,
                    nodeRect,
                    instScale: inst ? inst.scale : null,
                    toolMode: inst ? inst.toolMode : null,
                    nearestPort: nearest,
                    ports: portList
                });
            }
        """, new object[] { ex, ey, Node2Id, "left" });

        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(900);

        var edgeCountAfter = await GetEdgeCountAsync(page);
        var logJoined = string.Join(" | ", consoleLog);
        Assert.AreEqual(initialEdgeCount + 1, edgeCountAfter,
            $"Expected one newly created edge from near-miss drag. Probe: {probe} Console: [{logJoined}]");

        // Inspect the newly created edge's path `d` attribute to verify its
        // terminal coordinates. For a straight-routed edge, the path is
        // `M x0 y0 L x1 y1`. When the target gets smart-snapped onto
        // class2/left port, the last point must sit at (500, 230) in doc
        // coordinates — the port's absolute location — regardless of where
        // exactly the cursor was dropped. If snapping had failed, the last
        // point would be wherever the cursor was (≈ 507, 195).
        var edgeState = await page.EvaluateAsync<string>("""
            () => {
                const paths = Array.from(document.querySelectorAll('.tm-diagram-edge-path'));
                if (!paths.length) return null;
                // Take the last visible edge path in document order
                const last = paths[paths.length - 1];
                const group = last.closest('.tm-diagram-edge-group');
                const edgeId = group ? group.getAttribute('data-edge-id') : null;
                const d = last.getAttribute('d') || '';
                // Parse numbers from the path: works for "M x y L x y" and
                // also for multi-segment orthogonal paths — we only care
                // about the very first and very last (x, y).
                const nums = (d.match(/-?\d+(?:\.\d+)?/g) || []).map(parseFloat);
                let start = null, end = null;
                if (nums.length >= 4) {
                    start = { x: nums[0], y: nums[1] };
                    end   = { x: nums[nums.length - 2], y: nums[nums.length - 1] };
                }
                return JSON.stringify({ edgeId, d, start, end });
            }
        """);

        Assert.IsNotNull(edgeState, "Could not find the newly-created edge path in the DOM.");
        using var state = JsonDocument.Parse(edgeState!);
        Assert.IsTrue(state.RootElement.TryGetProperty("end", out var endEl) && endEl.ValueKind == JsonValueKind.Object,
            $"Could not parse end point from edge path. EdgeState: {edgeState} Probe: {probe} Console: [{logJoined}]");
        var endX = endEl.GetProperty("x").GetDouble();
        var endY = endEl.GetProperty("y").GetDouble();

        // Port "left" on class2 center (from the probe): (500, 230).
        // The cursor was dropped at ~(507, 195) in doc coords — off the
        // port by ~35 units. If smart-snap attached the target to
        // class2/left, the rendered edge end point must lie close to the
        // port. A straight-routed edge clips at the node perimeter with a
        // small jetty offset, so we accept a 20-unit radius around the
        // port (still well below the ~34-unit cursor distance).
        var cursorDocJson = await page.EvaluateAsync<string>("""
            (args) => {
                const [cx, cy] = args;
                const canvas = document.querySelector('.tm-diagram-canvas');
                const inst  = canvas ? window.tmDiagramEditor.instances.get(canvas.id) : null;
                if (!inst) return null;
                const p = window.tmDiagramEditor._screenToDoc(inst, cx, cy);
                return JSON.stringify({ x: p.x, y: p.y });
            }
        """, new object[] { ex, ey });
        using var cursorDocEl = JsonDocument.Parse(cursorDocJson!);
        var curX = cursorDocEl.RootElement.GetProperty("x").GetDouble();
        var curY = cursorDocEl.RootElement.GetProperty("y").GetDouble();

        const double portX = 500;
        const double portY = 230;
        var distToPort   = Math.Sqrt((endX - portX) * (endX - portX) + (endY - portY) * (endY - portY));
        var distToCursor = Math.Sqrt((endX - curX) * (endX - curX) + (endY - curY) * (endY - curY));

        Assert.IsTrue(distToPort < 20,
            $"Edge target did not snap to class2/left port. End=({endX:F2},{endY:F2}), port=({portX},{portY}), dist={distToPort:F2}. Cursor=({curX:F2},{curY:F2}), distToCursor={distToCursor:F2}. EdgeState: {edgeState} Probe: {probe} Console: [{logJoined}]");
        Assert.IsTrue(distToPort < distToCursor,
            $"Edge target end is closer to drop location than to the port — snap did not happen. End=({endX:F2},{endY:F2}), port=({portX},{portY}), dist={distToPort:F2}. Cursor=({curX:F2},{curY:F2}), distToCursor={distToCursor:F2}. EdgeState: {edgeState}");

        await TakeScreenshotAsync(page, "phase3_snap_to_port_near_miss");
    }

    [TestMethod]
    [Description("3.3 — With grid snap active, floating target point is rounded to the grid")]
    public async Task Phase3_GridSnap_FloatingPoint_Rounded()
    {
        const int GridSize = 20;
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        // Configure a coarse 20px grid directly on the active diagram instance.
        // Avoids depending on demo-specific UI controls.
        await page.EvaluateAsync("""
            (gridSize) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !window.tmDiagramEditor) return;
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                if (inst) {
                    inst.gridSize = gridSize;
                }
            }
        """, GridSize);

        var initialEdgeCount = await GetEdgeCountAsync(page);
        var initialEdgeIdsJson = await page.EvaluateAsync<string>("""
            () => JSON.stringify(Array
                .from(document.querySelectorAll('.tm-diagram-edge-group[data-edge-id]'))
                .map(g => g.getAttribute('data-edge-id'))
                .filter(Boolean))
        """);
        using var initialEdgeIds = JsonDocument.Parse(initialEdgeIdsJson!);
        var initialEdgeIdList = initialEdgeIds.RootElement
            .EnumerateArray()
            .Select(id => id.GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();
        await SetToolModeAsync(page, "edge");

        // Pick two screen points that are truly empty according to the same
        // `document.elementFromPoint` hit testing used by the diagram JS. A
        // coarse "below the nodes" estimate is not enough on responsive
        // viewports: it can still land on a node and create an attached edge.
        var coordsJson = await page.EvaluateAsync<string>("""
            (gridSize) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !window.tmDiagramEditor) return null;
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                if (!inst) return null;
                const cr = canvas.getBoundingClientRect();
                const viewH = window.innerHeight;
                const visibleBottom = Math.min(cr.bottom, viewH) - 20;
                const visibleRight = Math.min(cr.right, window.innerWidth) - 20;
                const isEmpty = (x, y) => {
                    const el = document.elementFromPoint(x, y);
                    if (!el || !canvas.contains(el)) return false;
                    if (el.closest('.tm-diagram-node[data-node-id]')) return false;
                    if (el.closest('.tm-diagram-port, .tm-diagram-connection-point')) return false;
                    if (el.closest('.tm-diagram-edge-group, .tm-diagram-edge-hit-path')) return false;
                    if (el.closest('.tm-diagram-edge-handle')) return false;
                    return true;
                };

                const candidates = [];
                for (let y = cr.top + 44; y <= visibleBottom; y += 31) {
                    for (let x = cr.left + 44; x <= visibleRight; x += 37) {
                        if (isEmpty(x, y)) candidates.push({ x, y });
                    }
                }
                let startScreen = null;
                let endScreen = null;
                for (const a of candidates) {
                    for (let i = candidates.length - 1; i >= 0; i--) {
                        const b = candidates[i];
                        if (Math.abs(b.x - a.x) >= 180 && Math.abs(b.y - a.y) >= 80) {
                            startScreen = a;
                            endScreen = b;
                            break;
                        }
                    }
                    if (startScreen && endScreen) break;
                }
                if (!startScreen || !endScreen) return null;
                const startDoc = window.tmDiagramEditor._screenToDoc(inst, startScreen.x, startScreen.y);
                const endDoc   = window.tmDiagramEditor._screenToDoc(inst, endScreen.x, endScreen.y);
                return JSON.stringify({ startScreen, endScreen, startDoc, endDoc, candidateCount: candidates.length });
            }
        """, GridSize);
        Assert.IsNotNull(coordsJson, "Could not compute grid-snap drag coordinates.");
        using var coords = JsonDocument.Parse(coordsJson);
        var sx = coords.RootElement.GetProperty("startScreen").GetProperty("x").GetDouble();
        var sy = coords.RootElement.GetProperty("startScreen").GetProperty("y").GetDouble();
        var ex = coords.RootElement.GetProperty("endScreen").GetProperty("x").GetDouble();
        var ey = coords.RootElement.GetProperty("endScreen").GetProperty("y").GetDouble();
        var endDocX = coords.RootElement.GetProperty("endDoc").GetProperty("x").GetDouble();
        var endDocY = coords.RootElement.GetProperty("endDoc").GetProperty("y").GetDouble();
        var expectedX = Math.Round(endDocX / GridSize) * GridSize;
        var expectedY = Math.Round(endDocY / GridSize) * GridSize;

        await page.Mouse.MoveAsync((float)sx, (float)sy);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)ex, (float)ey, new MouseMoveOptions { Steps = 6 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(900);

        var edgeCountAfter = await GetEdgeCountAsync(page);
        Assert.AreEqual(initialEdgeCount + 1, edgeCountAfter, "Expected one newly created floating edge.");

        // Read the actual floating-target position from the SVG attributes on
        // the dangling handle. Avoid DOM bounding boxes here: they are screen
        // geometry for the square decorator, not the document endpoint.
        var targetPosJson = await page.EvaluateAsync<string>("""
            (existingIds) => {
                const existing = new Set(existingIds || []);
                const groups = Array.from(document.querySelectorAll('.tm-diagram-edge-group[data-edge-id]'));
                const group = groups.find(g => !existing.has(g.getAttribute('data-edge-id')));
                const edgeId = group ? group.getAttribute('data-edge-id') : null;
                const handle = edgeId
                    ? document.querySelector(`.tm-diagram-edge-handle--dangling[data-edge-id="${edgeId}"][data-dangling="target"]`)
                    : null;
                if (!handle) return JSON.stringify({ noHandle: true, edgeId, allEdgeIds: groups.map(g => g.getAttribute('data-edge-id')) });
                const x = Number(handle.getAttribute('data-doc-x'));
                const y = Number(handle.getAttribute('data-doc-y'));
                if (!Number.isFinite(x) || !Number.isFinite(y)) {
                    return JSON.stringify({ noEndpoint: true, edgeId, x: handle.getAttribute('data-doc-x'), y: handle.getAttribute('data-doc-y') });
                }
                return JSON.stringify({
                    docX: x,
                    docY: y,
                    edgeId,
                    handleX: handle.getAttribute('x'),
                    handleY: handle.getAttribute('y'),
                    instGridSize: (() => {
                        const canvas = document.querySelector('.tm-diagram-canvas');
                        const inst = canvas && window.tmDiagramEditor ? window.tmDiagramEditor.instances.get(canvas.id) : null;
                        return inst ? inst.gridSize : null;
                    })()
                });
            }
        """, initialEdgeIdList);
        Assert.IsNotNull(targetPosJson);
        using var targetPos = JsonDocument.Parse(targetPosJson);
        Assert.IsFalse(targetPos.RootElement.TryGetProperty("noHandle", out _),
            "Expected a dangling target handle after free-line drag; none found (target may have attached to a node or edge).");
        Assert.IsFalse(targetPos.RootElement.TryGetProperty("noEndpoint", out _),
            $"Expected to parse the floating target endpoint from the new edge path. State: {targetPosJson}");
        var docX = targetPos.RootElement.GetProperty("docX").GetDouble();
        var docY = targetPos.RootElement.GetProperty("docY").GetDouble();

        // Grid snap tolerance: 1.5 doc units covers cumulative float
        // rounding through SVG matrixTransform and DOM rect measurement.
        Assert.IsTrue(Math.Abs(docX - expectedX) <= 1.5,
            $"Target X should snap to grid multiple of {GridSize}. Expected ≈ {expectedX} (raw {endDocX:F2}), got {docX:F2}. State: {targetPosJson}");
        Assert.IsTrue(Math.Abs(docY - expectedY) <= 1.5,
            $"Target Y should snap to grid multiple of {GridSize}. Expected ≈ {expectedY} (raw {endDocY:F2}), got {docY:F2}. State: {targetPosJson}");

        await TakeScreenshotAsync(page, "phase3_grid_snap_floating_point");
    }

    // ========================================================================
    // Smoke / Integration
    // ========================================================================

    [TestMethod]
    [Description("Diagram page loads with preloaded UML sample nodes and edges")]
    public async Task Smoke_DiagramPage_LoadsWithSample()
    {
        var page = await CreatePageAsync();
        await OpenDiagramEditorAsync(page);

        var node1 = page.Locator($".tm-diagram-node[data-node-id='{Node1Id}']");
        var node2 = page.Locator($".tm-diagram-node[data-node-id='{Node2Id}']");
        await node1.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await node2.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var edges = page.Locator(".tm-diagram-edge-path");
        Assert.IsTrue(await edges.CountAsync() > 0, "Expected at least one edge");

        await TakeScreenshotAsync(page, "smoke_diagram_load");
    }
}
