using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace Tempo.Blazor.E2E;

/// <summary>
/// End-to-end coverage for Phase 4 of the unified SVG canvas refactor — the
/// selection outline lives in the <c>.tm-diagram-overlay-pane</c> and is an
/// SVG <c>&lt;rect&gt;</c>, not an HTML <c>&lt;div&gt;</c>.
///
/// <para>
/// F4 is largely a bookkeeping / cleanup phase — most of the JS-side rewrite
/// (<c>_updateSelection</c>, <c>_updateSelectionTransforms</c>,
/// <c>_clearSelectionOutlines</c>) was already performed as part of F3.A when
/// the node wrapper itself moved from an HTML <c>&lt;div&gt;</c> inside the
/// old <c>transform-layer</c> to an SVG <c>&lt;g&gt;</c> inside the scene-pane.
/// These tests lock in the contract so a later regression cannot silently
/// reintroduce the HTML-based selection overlay.
/// </para>
/// </summary>
[TestClass]
public class DiagramF4E2ETests : WasmTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    private async Task<IPage> PrepareDiagramPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await WaitForAppReadyAsync(page);

        await page.WaitForSelectorAsync(".tm-diagram-canvas", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        await page.WaitForFunctionAsync("""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !canvas.id) return false;
                const ed = window.tmDiagramEditor;
                return !!(ed && ed.instances && ed.instances.get(canvas.id));
            }
        """, null, new PageWaitForFunctionOptions { Timeout = 15000 });

        await page.GetByRole(AriaRole.Button, new() { Name = "Load UML sample" }).ClickAsync();
        await page.WaitForSelectorAsync(".tm-diagram-node", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await page.WaitForTimeoutAsync(500);

        return page;
    }

    /// <summary>
    /// F4.1 / F4.4 canary — after clicking a node, exactly one
    /// <c>rect.tm-diagram-selection-outline</c> exists and lives as a direct
    /// child of <c>.tm-diagram-overlay-pane</c> (NOT inside the scene-pane or
    /// any HTML container).
    /// </summary>
    [TestMethod]
    public async Task SelectedNode_EmitsSvgRectOutline_InsideOverlayPane()
    {
        var page = await PrepareDiagramPageAsync();

        // Pick any node and click its body (foreignObject content) to select it.
        var firstNode = page.Locator("g.tm-diagram-node[data-node-id]").First;
        await firstNode.ClickAsync(new() { Position = new() { X = 40, Y = 20 } });
        await page.WaitForTimeoutAsync(200);

        var json = await page.EvaluateAsync<string>("""
            () => {
                const outlines = document.querySelectorAll('.tm-diagram-selection-outline');
                const info = [];
                outlines.forEach(el => {
                    info.push({
                        tag: el.tagName.toLowerCase(),
                        parentClass: (el.parentElement && el.parentElement.getAttribute('class')) || '',
                        hasTransform: !!(el.getAttribute('transform') || '').match(/translate\(/),
                        hasDataSelFor: !!el.getAttribute('data-sel-for'),
                    });
                });
                return JSON.stringify({ count: outlines.length, info });
            }
        """);

        Assert.IsNotNull(json);
        using var doc = JsonDocument.Parse(json);
        var count = doc.RootElement.GetProperty("count").GetInt32();
        count.Should().Be(1, "clicking a single node creates exactly one selection outline");

        var first = doc.RootElement.GetProperty("info")[0];
        first.GetProperty("tag").GetString().Should().Be("rect",
            "F4.1 — selection outline is a native SVG <rect>, not an HTML <div>");
        first.GetProperty("parentClass").GetString().Should().Contain("tm-diagram-overlay-pane",
            "F4.1 — selection outlines live inside the overlay-pane");
        first.GetProperty("hasTransform").GetBoolean().Should().BeTrue(
            "outline mirrors the node's translate() transform so pan/zoom keep it aligned");
        first.GetProperty("hasDataSelFor").GetBoolean().Should().BeTrue(
            "outline carries data-sel-for=<nodeId> for incremental transform updates");
    }

    /// <summary>
    /// F4.3 canary — clicking onto empty canvas clears the selection, and
    /// <c>_clearSelectionOutlines</c> removes every
    /// <c>rect.tm-diagram-selection-outline</c> from the overlay-pane.
    /// </summary>
    [TestMethod]
    public async Task ClickingEmptyCanvas_ClearsAllSelectionOutlines_FromOverlayPane()
    {
        var page = await PrepareDiagramPageAsync();

        var firstNode = page.Locator("g.tm-diagram-node[data-node-id]").First;
        await firstNode.ClickAsync(new() { Position = new() { X = 40, Y = 20 } });
        await page.WaitForTimeoutAsync(200);

        var before = await page.Locator(".tm-diagram-overlay-pane .tm-diagram-selection-outline").CountAsync();
        before.Should().Be(1, "precondition — single node must be selected before we clear");

        // Click a location guaranteed to be empty (far top-left corner of canvas).
        var canvasBox = await page.Locator(".tm-diagram-canvas").BoundingBoxAsync();
        Assert.IsNotNull(canvasBox);
        await page.Mouse.ClickAsync((float)(canvasBox.X + 5), (float)(canvasBox.Y + 5));
        await page.WaitForTimeoutAsync(200);

        var after = await page.Locator(".tm-diagram-overlay-pane .tm-diagram-selection-outline").CountAsync();
        after.Should().Be(0, "F4.3 — clearing the selection removes outlines from the overlay-pane");

        // Double-check — nothing stranded elsewhere in the DOM (e.g. leftover <div> overlay).
        var anywhere = await page.Locator(".tm-diagram-selection-outline").CountAsync();
        anywhere.Should().Be(0);
    }

    /// <summary>
    /// F4.5 smoke — dragging the selected node keeps the outline anchored to
    /// the node (JS path: <c>_updateSelectionTransforms</c>). We verify that
    /// the outline's <c>transform="translate(x,y)"</c> follows the node's new
    /// SVG <c>transform</c> after a drag.
    /// </summary>
    [TestMethod]
    public async Task DraggingSelectedNode_UpdatesOutlineTransform_ToTrackTheNode()
    {
        var page = await PrepareDiagramPageAsync();

        var firstNode = page.Locator("g.tm-diagram-node[data-node-id]").First;
        var nodeId = await firstNode.GetAttributeAsync("data-node-id");
        Assert.IsNotNull(nodeId);

        // Click on the inner body (HTML content) to guarantee we hit the node
        // rather than a port / connection-point / empty space inside the node's <g>.
        var nodeBody = page.Locator($"g.tm-diagram-node[data-node-id=\"{nodeId}\"] .tm-diagram-node__body").First;
        await nodeBody.ClickAsync(new() { Position = new() { X = 20, Y = 10 } });
        await page.WaitForTimeoutAsync(200);

        string GetTransformScript(string nodeId) => $$"""
            () => {
                function parseTranslate(s) {
                    const m = (s || '').match(/translate\(\s*(-?\d+(?:\.\d+)?)[\s,]+(-?\d+(?:\.\d+)?)/);
                    return m ? { x: parseFloat(m[1]), y: parseFloat(m[2]) } : null;
                }
                const node = document.querySelector('g.tm-diagram-node[data-node-id="{{nodeId}}"]');
                const outline = document.querySelector('.tm-diagram-overlay-pane rect.tm-diagram-selection-outline[data-sel-for="{{nodeId}}"]');
                return JSON.stringify({
                    nodeRaw: node ? node.getAttribute('transform') : null,
                    outlineRaw: outline ? outline.getAttribute('transform') : null,
                    node: node ? parseTranslate(node.getAttribute('transform')) : null,
                    outline: outline ? parseTranslate(outline.getAttribute('transform')) : null,
                });
            }
        """;

        var beforeJson = await page.EvaluateAsync<string>(GetTransformScript(nodeId));
        using var before = JsonDocument.Parse(beforeJson);
        if (before.RootElement.GetProperty("node").ValueKind == JsonValueKind.Null
            || before.RootElement.GetProperty("outline").ValueKind == JsonValueKind.Null)
        {
            Assert.Fail($"Missing node or outline transform. Raw: {beforeJson}");
        }
        var beforeNodeX = before.RootElement.GetProperty("node").GetProperty("x").GetDouble();
        var beforeOutlineX = before.RootElement.GetProperty("outline").GetProperty("x").GetDouble();
        // Outline shares the node's translate so pan/zoom keep them aligned.
        beforeOutlineX.Should().BeApproximately(beforeNodeX, 0.5,
            "pre-drag: outline translate matches the node translate");

        // Dispatch mousedown/mousemove/mouseup via JS — Playwright's native
        // mouse path doesn't reliably reach the SVG's Blazor/JS handlers (see
        // DiagramEdgeE2ETests.Phase1_GridSnap_NodeDragSnapsToGrid for rationale).
        await page.EvaluateAsync($$"""
            () => {
                const node = document.querySelector('g.tm-diagram-node[data-node-id="{{nodeId}}"]');
                const container = document.querySelector('.tm-diagram-canvas');
                if (!node || !container) return null;
                const r = node.getBoundingClientRect();
                const cx = r.left + r.width / 2;
                const cy = r.top + r.height / 2;
                node.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, button: 0, clientX: cx, clientY: cy }));
                container.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, cancelable: true, button: 0, clientX: cx + 80, clientY: cy + 60 }));
                container.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, button: 0, clientX: cx + 80, clientY: cy + 60 }));
                return 'done';
            }
        """);
        await page.WaitForTimeoutAsync(500);

        var afterJson = await page.EvaluateAsync<string>(GetTransformScript(nodeId));
        using var after = JsonDocument.Parse(afterJson);
        var afterNodeX = after.RootElement.GetProperty("node").GetProperty("x").GetDouble();
        var afterOutlineX = after.RootElement.GetProperty("outline").GetProperty("x").GetDouble();

        afterNodeX.Should().NotBe(beforeNodeX, "node must have actually moved after the drag");
        afterOutlineX.Should().BeApproximately(afterNodeX, 0.5,
            "F4.5 / _updateSelectionTransforms — outline must track the node's new position");
    }
}
