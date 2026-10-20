using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace Tempo.Blazor.E2E;

/// <summary>
/// End-to-end coverage for Phase 5 of the unified SVG canvas refactor —
/// <em>resize / rotate / connect-arrow</em> handles live in the
/// <c>.tm-diagram-decorator-pane</c> as native SVG elements (&lt;rect&gt;,
/// &lt;circle&gt;, &lt;foreignObject&gt;), <strong>not</strong> as HTML divs
/// absolutely-positioned inside the node's foreignObject.
///
/// <para>
/// Mirrors draw.io's <c>mxVertexHandler.redrawHandles</c>: each selected node
/// gets exactly one <c>&lt;g class="tm-diagram-node-handles" data-node-id="X"&gt;</c>
/// whose <c>transform</c> attribute is identical to the node's
/// <c>translate()+rotate()</c> — so rotating the node automatically rotates
/// the handles, no bespoke geometry required.
/// </para>
/// </summary>
[TestClass]
public class DiagramF5E2ETests : WasmTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    private async Task<IPage> PrepareDiagramPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
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
    /// F5.1 / F5.2 / F5.4 / F5.5 canary — after selecting a node we expect
    /// exactly one <c>g.tm-diagram-node-handles</c> inside the decorator-pane,
    /// containing 8 resize rects, 1 rotate circle and 4 connect-arrow
    /// foreignObjects. No stray HTML <c>div.tm-diagram-resize-handle</c>
    /// should remain inside the node's <c>&lt;g&gt;</c>.
    /// </summary>
    [TestMethod]
    public async Task SelectedNode_RendersAllHandlesInDecoratorPane_AsNativeSvgPrimitives()
    {
        var page = await PrepareDiagramPageAsync();

        var firstNode = page.Locator("g.tm-diagram-node[data-node-id]").First;
        var nodeId = await firstNode.GetAttributeAsync("data-node-id");
        Assert.IsNotNull(nodeId);

        var body = page.Locator($"g.tm-diagram-node[data-node-id=\"{nodeId}\"] .tm-diagram-node__body").First;
        await body.ClickAsync(new() { Position = new() { X = 20, Y = 10 } });
        await page.WaitForTimeoutAsync(200);

        var json = await page.EvaluateAsync<string>($$"""
            () => {
                const deco = document.querySelector('.tm-diagram-decorator-pane');
                const scene = document.querySelector('.tm-diagram-scene-pane');
                const handlesGroups = deco ? deco.querySelectorAll('g.tm-diagram-node-handles[data-node-id="{{nodeId}}"]') : [];
                // Paint-order contract (review fix, phase 4): the quick-connect arrows live in
                // their OWN sibling g.tm-diagram-connect-arrows (painted after the port hit
                // discs), not inside the handles group anymore.
                const arrowsGroups = deco ? deco.querySelectorAll('g.tm-diagram-connect-arrows[data-node-id="{{nodeId}}"]') : [];
                let tagSummary = {};
                let resizeRectCount = 0;
                let rotateCircleCount = 0;
                let connectFoCount = 0;
                if (handlesGroups.length) {
                    const g = handlesGroups[0];
                    g.querySelectorAll('rect.tm-diagram-resize-handle').forEach(() => resizeRectCount++);
                    g.querySelectorAll('circle.tm-diagram-rotate-handle').forEach(() => rotateCircleCount++);
                    // Snapshot all resize handle variants present for a better error.
                    ['nw','n','ne','e','se','s','sw','w'].forEach(v => {
                        tagSummary[v] = g.querySelectorAll('rect.tm-diagram-resize-handle--' + v).length;
                    });
                }
                arrowsGroups.forEach(g => {
                    g.querySelectorAll('foreignObject.tm-diagram-connect-arrow-wrap').forEach(() => connectFoCount++);
                });
                // Nothing handle-like should be left inside the node's own <g> in the scene-pane.
                const sceneStray = scene
                    ? scene.querySelectorAll('g.tm-diagram-node[data-node-id="{{nodeId}}"] .tm-diagram-resize-handle, ' +
                                             'g.tm-diagram-node[data-node-id="{{nodeId}}"] .tm-diagram-rotate-handle, ' +
                                             'g.tm-diagram-node[data-node-id="{{nodeId}}"] .tm-diagram-connect-arrow').length
                    : 0;
                return JSON.stringify({
                    handlesGroupCount: handlesGroups.length,
                    arrowsGroupCount: arrowsGroups.length,
                    arrowsGroupParentClass: arrowsGroups.length ? (arrowsGroups[0].parentElement && arrowsGroups[0].parentElement.getAttribute('class')) : null,
                    parentClass: handlesGroups.length ? (handlesGroups[0].parentElement && handlesGroups[0].parentElement.getAttribute('class')) : null,
                    hasTransform: handlesGroups.length ? !!(handlesGroups[0].getAttribute('transform') || '').match(/translate\(/) : false,
                    resizeRectCount, rotateCircleCount, connectFoCount, tagSummary, sceneStray,
                });
            }
        """);

        Assert.IsNotNull(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("handlesGroupCount").GetInt32().Should().Be(1,
            "F5.1 — exactly one tm-diagram-node-handles <g> per selected node");
        root.GetProperty("parentClass").GetString().Should().Contain("tm-diagram-decorator-pane",
            "F5.1 — handles live in the decorator-pane (stacking order above scene + overlay)");
        root.GetProperty("hasTransform").GetBoolean().Should().BeTrue(
            "F5.11 — handles group mirrors the node's translate()+rotate()");
        root.GetProperty("resizeRectCount").GetInt32().Should().Be(8,
            "F5.2 — 8 resize squares (nw/n/ne/e/se/s/sw/w)");
        root.GetProperty("rotateCircleCount").GetInt32().Should().Be(1,
            "F5.4 — rotate handle is a single <circle>");
        root.GetProperty("connectFoCount").GetInt32().Should().Be(4,
            "F5.5 — 4 quick-connect arrows (N/E/S/W) as <foreignObject> wrappers in the arrows group");
        root.GetProperty("arrowsGroupCount").GetInt32().Should().Be(1,
            "the arrows render in exactly one g.tm-diagram-connect-arrows per selected node");
        root.GetProperty("arrowsGroupParentClass").GetString().Should().Contain("tm-diagram-decorator-pane",
            "the arrows group lives in the decorator-pane too");
        root.GetProperty("sceneStray").GetInt32().Should().Be(0,
            "F5.8 — no legacy HTML handles linger inside the node's <g> in the scene-pane");
    }

    /// <summary>
    /// F5.13 — rotating a node 45° rotates the handles group's transform by
    /// the same angle, so resize squares stay anchored to the rotated corners.
    /// We verify (a) the handles <c>&lt;g&gt;</c> transform contains
    /// <c>rotate(45</c> and (b) the bounding box of a corner handle is
    /// visibly offset from the node's unrotated local corner — i.e. rotation
    /// is actually applied, not just written.
    /// </summary>
    [TestMethod]
    public async Task RotatedNode_HandlesGroupFollowsRotation_KeepingHandlesAtRotatedCorners()
    {
        var page = await PrepareDiagramPageAsync();

        var firstNode = page.Locator("g.tm-diagram-node[data-node-id]").First;
        var nodeId = await firstNode.GetAttributeAsync("data-node-id");
        Assert.IsNotNull(nodeId);

        var body = page.Locator($"g.tm-diagram-node[data-node-id=\"{nodeId}\"] .tm-diagram-node__body").First;
        await body.ClickAsync(new() { Position = new() { X = 20, Y = 10 } });
        await page.WaitForTimeoutAsync(200);

        // Capture the NW handle's on-screen center BEFORE rotation.
        var beforeJson = await page.EvaluateAsync<string>($$"""
            () => {
                const nw = document.querySelector('.tm-diagram-decorator-pane g.tm-diagram-node-handles[data-node-id="{{nodeId}}"] rect.tm-diagram-resize-handle--nw');
                if (!nw) return null;
                const r = nw.getBoundingClientRect();
                return JSON.stringify({ cx: r.left + r.width / 2, cy: r.top + r.height / 2 });
            }
        """);
        Assert.IsNotNull(beforeJson, "NW handle must be rendered before rotation");

        // Drive the rotation directly via the JS API — Playwright dragging the
        // rotate knob is flaky because the pointer capture dance mixes mouse
        // events with the SVG coordinate space. `setNodeRotation` is the same
        // commit path used at rotate-drag end.
        await page.EvaluateAsync($$"""
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                window.tmDiagramEditor.setNodeRotation(canvas, '{{nodeId}}', 45);
            }
        """);
        await page.WaitForTimeoutAsync(300);

        var afterJson = await page.EvaluateAsync<string>($$"""
            () => {
                const g = document.querySelector('.tm-diagram-decorator-pane g.tm-diagram-node-handles[data-node-id="{{nodeId}}"]');
                const nw = g ? g.querySelector('rect.tm-diagram-resize-handle--nw') : null;
                const r = nw ? nw.getBoundingClientRect() : null;
                return JSON.stringify({
                    handlesTransform: g ? g.getAttribute('transform') : null,
                    cx: r ? r.left + r.width / 2 : null,
                    cy: r ? r.top + r.height / 2 : null,
                });
            }
        """);
        Assert.IsNotNull(afterJson);

        using var before = JsonDocument.Parse(beforeJson);
        using var after = JsonDocument.Parse(afterJson);

        var ht = after.RootElement.GetProperty("handlesTransform").GetString();
        ht.Should().NotBeNull();
        ht!.Should().MatchRegex(@"rotate\(\s*45",
            "F5.11 — handles group transform must include rotate(45 …) after setNodeRotation(45)");

        // The NW corner of a 45°-rotated rect moves; a tiny threshold lets us
        // assert "not the same pixel" without over-constraining fonts/scaling.
        var bx = before.RootElement.GetProperty("cx").GetDouble();
        var by = before.RootElement.GetProperty("cy").GetDouble();
        var ax = after.RootElement.GetProperty("cx").GetDouble();
        var ay = after.RootElement.GetProperty("cy").GetDouble();
        var dist = Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        dist.Should().BeGreaterThan(5,
            "F5.13 — NW handle's on-screen position must move when the node rotates 45°");
    }
}
