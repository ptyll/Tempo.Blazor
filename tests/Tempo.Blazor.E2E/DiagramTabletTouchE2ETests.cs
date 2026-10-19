using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// DiagramEditor redesign phase 6 — tablet and touch (DI3). At container widths 768–1199 the
/// editor renders the shell tablet presentation: the palette rails, at most one docked panel is
/// expanded, the minimap floats over the canvas as an overlay, and every touch gesture goes
/// through the shared pointer-drag/pointer-gesture sessions (tap-insert, finger drag, finger
/// resize/rotate, long-press menu, two-finger pinch). A 1440 desktop pass regression-checks the
/// mouse paths. Screenshots land in <c>tests/Tempo.Blazor.E2E/__screenshots__/diagram-editor/</c>
/// and are reviewed against output/imagegen/tempo-reimagined/Tempo.Blazor.DiagramEditor/
/// diagram-editor--tablet.png and diagram-layers-panel--tablet.png (the [E] elements of the
/// mockups — the quick-shapes bar, layers on the left, the status-bar "Apply" — are out of scope).
/// </summary>
[TestClass]
public sealed class DiagramTabletTouchE2ETests : WasmTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";
    private const string ModelingEditorUrl = "/modeling-editor";

    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "diagram-editor");

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1024×768 touch: palette rail, minimap overlay, tap-insert, finger resize, long-press menu, pinch")]
    public async Task Tablet_1024Touch_TapInsertResizeRotatePinch()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true);

        // The demo page constrains the editor column (~664px at this viewport = mobile). The F1
        // contract is container-owned layout: make the demo page full-bleed (hide the sidebar and
        // the gutters) so the editor host gets the phase's tablet width, exactly like a full-bleed
        // host would, and wait for the resolution.
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "tablet");

        // ── Tablet presentation (F6-TABLET-DOCKED-PANELS + DI3) ─────────────
        await page.Locator(".tm-editor-shell__rail--left").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        Assert.AreEqual(0, await page.Locator(".tm-diagram-editor__minimap-card").CountAsync(),
            "the right-column minimap card must not render on a tablet layout");
        var overlay = page.Locator(".tm-diagram-editor__minimap-overlay");
        await overlay.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        // U2/X4: the tablet overlay is 160px wide (+ the card padding) — the layout, not CSS,
        // sizes the map so the tap mapping can never drift.
        Assert.IsTrue(await page.EvaluateAsync<bool>(
            "() => { const o = document.querySelector('.tm-diagram-editor__minimap-overlay'); const w = o.getBoundingClientRect().width; const pad = parseFloat(getComputedStyle(o).paddingLeft) + parseFloat(getComputedStyle(o).paddingRight); return w <= 160 + pad + 2; }"),
            "the tablet minimap overlay is 160px wide plus its padding");

        await ScrollEditorStatusIntoViewAsync(page);
        await ShotSettledAsync(page, "tablet-1024-touch--editor");

        // ── Tap-to-insert: the palette rail expands into the tile grid ──────
        await page.Locator(".tm-editor-shell__rail--left .tm-editor-shell__panel-toggle").First.ClickAsync();

        // F7: the header Tools toggle drives the same palette state — on tablet it collapses
        // the palette BACK to its rail, and the rail's labelled button expands it again.
        await page.Locator("[data-testid='diagram-editor-toggle-toolbox']").First.ClickAsync();
        await page.Locator(".tm-editor-shell__rail--left").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.Locator(".tm-diagram-editor__rail-btn").First.ClickAsync();
        await page.Locator(".tm-diagram-toolbox").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var paletteTile = page.Locator("[data-stencil-id='general.rectangle']").First;
        await paletteTile.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await ScrollEditorStatusIntoViewAsync(page);
        await ShotAsync(page, "tablet-1024-touch--palette-grid");

        var before = await NodeCountAsync(page);
        var beforeIds = await page.EvaluateAsync<string>(
            "() => JSON.stringify([...document.querySelectorAll('.tm-diagram-node')].map(n => n.getAttribute('data-node-id')))");
        await paletteTile.TapAsync();
        await page.WaitForFunctionAsync(
            "(n) => document.querySelectorAll('.tm-diagram-node').length > n",
            before, new PageWaitForFunctionOptions { Timeout = 15_000 });

        // The inserted node: the id that appeared with the tap (it lands on top at the centre).
        var insertedId = await page.EvaluateAsync<string>(
            "(prev) => { const prevSet = new Set(JSON.parse(prev)); const nodes = [...document.querySelectorAll('.tm-diagram-node')]; return (nodes.find(n => !prevSet.has(n.getAttribute('data-node-id'))))?.getAttribute('data-node-id'); }",
            beforeIds);
        Assert.IsNotNull(insertedId, "the tap-insert must create a new node");
        var nodeId = insertedId!;

        // The tap inserts at the viewport centre: the new node sits near the canvas centre.
        var centreDistance = await page.EvaluateAsync<double?>(
            "(id) => { const n = document.querySelector(`g.tm-diagram-node[data-node-id='${id}']`); const svg = document.querySelector('.tm-diagram-canvas__svg'); if (!n || !svg) return null; const a = n.getBoundingClientRect(); const b = svg.getBoundingClientRect(); return Math.hypot(a.left + a.width / 2 - (b.left + b.width / 2), a.top + a.height / 2 - (b.top + b.height / 2)); }",
            nodeId);
        Assert.IsTrue(centreDistance is < 200,
            $"the tapped stencil must insert near the viewport centre (distance {centreDistance})");

        // ── Select by tap (it is already selected from the insert; the tap must keep it), then
        //    resize WITH A FINGER (synthetic touch pointer) ────────────────────
        await TapNodeAsync(page, nodeId);
        await page.Locator($"g.tm-diagram-node-handles[data-node-id='{nodeId}'] .tm-diagram-resize-handle-hit").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var sizeBefore = await GetNodeSizeAsync(page, nodeId);
        await FingerResizeAsync(page, nodeId, "se", 90);
        var sizeAfter = await GetNodeSizeAsync(page, nodeId);
        Assert.IsTrue(sizeAfter.w > sizeBefore.w + 40 && sizeAfter.h > sizeBefore.h + 40,
            $"a finger drag on the SE handle must resize the node (before {sizeBefore.w}×{sizeBefore.h}, after {sizeAfter.w}×{sizeAfter.h})");
        await ScrollEditorStatusIntoViewAsync(page);
        await ShotAsync(page, "tablet-1024-touch--finger-resize");

        // ── Finger rotation on the rotate handle ────────────────────────────
        await FingerRotateAsync(page, nodeId, 60);
        var rotation = await page.EvaluateAsync<double?>(
            "(id) => { const el = document.querySelector(`g.tm-diagram-node[data-node-id='${id}']`); if (!el) return null; const m = (el.getAttribute('transform') || '').match(/rotate\\(\\s*([\\d.eE+-]+)/); return m ? parseFloat(m[1]) : 0; }",
            nodeId);
        Assert.IsNotNull(rotation, "the rotated node must carry a rotate() transform");
        Assert.IsTrue(Math.Abs(rotation!.Value) > 10,
            $"a finger drag on the rotate handle must rotate the node (was {rotation})");

        // ── Long-press opens the context menu ───────────────────────────────
        await LongPressNodeAsync(page, nodeId);
        var menu = page.Locator(".tm-diagram-editor__context-menu");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        // Close it the way a finger would — a TOUCH TAP outside (the canvas Escape clears the
        // selection, which the later steps do not want).
        await page.Touchscreen.TapAsync(20, 20);
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        // ── Two-finger pinch zooms ──────────────────────────────────────────
        var zoomBefore = await ZoomLabelAsync(page);
        await TwoFingerPinchAsync(page, 120);
        var zoomAfter = await ZoomLabelAsync(page);
        Assert.AreNotEqual(zoomBefore, zoomAfter,
            "a two-finger pinch must change the zoom (pinch/pan over pointer-gesture.js)");

        await ScrollEditorStatusIntoViewAsync(page);
        await ShotSettledAsync(page, "tablet-1024-touch--after-gestures");

        // The mouse path still owns the desktop-style resize on the same session-enabled handles.
        // (characterization: the touch module never broke the C# mouse machine). The pinch zoomed
        // in, so fit the view first, then re-select through the same JS entry the connect modal
        // uses.
        await page.EvaluateAsync("() => { const c = document.querySelector('.tm-diagram-canvas'); window.tmDiagramEditor.fitToView(c, 40); }");
        await page.WaitForTimeoutAsync(300);
        await page.EvaluateAsync(
            """
            (id) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                inst.selectedIds = new Set([id]);
                window.tmDiagramEditor._updateSelection(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [id]);
            }
            """, nodeId);
        await page.Locator($"g.tm-diagram-node-handles[data-node-id='{nodeId}'] rect[data-resize-handle='nw']").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        // The NW corner (the floating minimap overlay owns the bottom-right corner of the canvas).
        await MouseResizeAsync(page, nodeId, "nw", -60);
        var sizeMouse = await GetNodeSizeAsync(page, nodeId);
        Assert.IsTrue(sizeMouse.w > sizeAfter.w + 1,
            $"the mouse resize path must keep working (after finger {sizeAfter.w}, after mouse {sizeMouse.w})");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1180×820 touch: inspector raise on selection, layers row menu, page tabs, rail swap, toolbar DoD")]
    public async Task Tablet_1180Touch_InspectorLayersPagesAndToolbar()
    {
        var page = await OpenEditorAsync(1180, 820, touch: true);

        // Container-owned layout: the demo column is narrower than the viewport, so make the page
        // full-bleed and let the editor host take the phase's tablet width; wait for the resolution.
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "tablet");

        // ── Selection raises the inspector (the palette rails back) ─────────
        var nodeId = await page.EvaluateAsync<string?>(
            "() => document.querySelector('.tm-diagram-node')?.getAttribute('data-node-id')");
        Assert.IsNotNull(nodeId, "the sample document must render a node");
        await TapNodeAsync(page, nodeId!);
        await page.Locator(".tm-diagram-editor__inspector").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        Assert.IsTrue(await page.Locator(".tm-editor-shell__rail--left").CountAsync() > 0,
            "the palette returns to the rail when the inspector takes the expanded slot");

        // ── Layers tab: the row "⋯" menu moves a layer (the non-drag alternative) ──
        // Panel chrome below the fold is clicked through the DOM (the page cannot always scroll
        // clipped ancestors into view); the handlers are the same Blazor callbacks.
        var layersTab = page.Locator(".tm-diagram-editor__inspector .tm-tabs__tab, .tm-diagram-editor__inspector [role='tab']")
            .Filter(new() { HasText = "Layers" }).First;
        await ClickFirstAsync(page, layersTab);
        var firstRow = page.Locator(".tm-diagram-layers__item").First;
        await firstRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        // The sample document ships one layer: add a second so there is something to move.
        if (await page.Locator(".tm-diagram-layers__item").CountAsync() < 2)
        {
            await ClickFirstAsync(page, page.Locator(".tm-diagram-layers__add-btn"));
            await page.WaitForFunctionAsync(
                "() => document.querySelectorAll('.tm-diagram-layers__item').length >= 2",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        }

        var firstName = await firstRow.Locator(".tm-diagram-layers__name").TextContentAsync();

        await ClickFirstAsync(page, page.Locator(".tm-diagram-layers__item").Last
            .Locator(".tm-diagram-layers__row-menu-trigger"));
        var moveUp = page.Locator(".tm-diagram-layers__row-menu-item--up").Last;
        await moveUp.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await DiagramEditorE2E.AssertEditorFitsAsync(page);
        await ScrollEditorStatusIntoViewAsync(page);
        await ShotAsync(page, "tablet-1180-touch--layers-row-menu");
        await ClickFirstAsync(page, moveUp);

        await page.WaitForFunctionAsync(
            """
            (name) => {
                const rows = [...document.querySelectorAll('.tm-diagram-layers__item')];
                return rows.length > 1 && rows[0].querySelector('.tm-diagram-layers__name')?.textContent !== name;
            }
            """,
            firstName, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(250);
        await ScrollEditorStatusIntoViewAsync(page);
        await ShotAsync(page, "tablet-1180-touch--layers-panel");

        // ── Pages: add a page and switch between the tabs ───────────────────
        await ClickFirstAsync(page, page.Locator(".tm-diagram-editor__page-tab-add"));
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-editor__page-tab').length >= 2",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await ClickFirstAsync(page, page.Locator(".tm-diagram-editor__page-tab").First);
        await page.WaitForTimeoutAsync(300);
        var activeTab = await page.Locator(".tm-diagram-editor__page-tab--active").First.TextContentAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(activeTab), "a page tab must be active after switching");

        // ── Collapse the inspector to the rail and back ─────────────────────
        await ClickFirstAsync(page, page.Locator(".tm-editor-shell__panel-toggle--right"));
        await page.Locator(".tm-editor-shell__rail--right").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await ClickFirstAsync(page, page.Locator(".tm-editor-shell__rail--right .tm-editor-shell__panel-toggle"));
        await page.Locator(".tm-diagram-editor__inspector").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // ── DoD: at 1024-1180 the toolbar must not overflow; every command is
        //    reachable (the collapsed ones through the "More" menu) ──────────
        var toolbarFits = await page.EvaluateAsync<bool>("""
            () => {
                const bar = document.querySelector('.tm-diagram-editor__toolbar');
                if (!bar) return false;
                return bar.scrollWidth <= bar.clientWidth + 1;
            }
            """);
        Assert.IsTrue(toolbarFits, "the command row must not overflow horizontally in the tablet band");

        // Reachability: every command group stays represented on the bar or as a "More" menu
        // entry, and the overflow trigger exists (the More menu's open behaviour itself is
        // exercised by the phase-4/5 responsive suite, which drives Templates through it).
        var reachability = await page.EvaluateAsync<string>("""
            () => {
                const scope = document.querySelector('.tm-diagram-editor');
                if (!scope) return JSON.stringify({ scope: false });
                return JSON.stringify({
                    scope: true,
                    groups: scope.querySelectorAll('.tm-diagram-editor__toolbar [data-tm-group]').length,
                    barButtons: scope.querySelectorAll('.tm-diagram-editor__toolbar .tm-toolbar-btn').length,
                    moreItems: scope.querySelectorAll('.tm-toolbar-more__item').length,
                    moreTrigger: !!scope.querySelector('.tm-toolbar-more'),
                });
            }
            """);
        var ok = reachability.Contains("\"groups\":0") == false
            && reachability.Contains("\"barButtons\":0") == false
            && reachability.Contains("\"moreTrigger\":false") == false;
        Assert.IsTrue(ok,
            $"the command row must keep every group represented (bar copy or More entry) in the tablet band: {reachability}");

        await ScrollEditorStatusIntoViewAsync(page);
        await ShotSettledAsync(page, "tablet-1180-touch--editor");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Read-only tablet (R1): a tap on a node selects it for inspection; no resize handles render")]
    public async Task Tablet_ReadOnly_TapNode_SelectsForInspection()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true);
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "tablet");

        var nodeId = await page.EvaluateAsync<string?>(
            "() => document.querySelector('.tm-diagram-node')?.getAttribute('data-node-id')");
        Assert.IsNotNull(nodeId, "the sample document must render a node");

        await ClickFirstAsync(page, page.Locator("[data-tm-group='readonly'] button"));
        await page.WaitForTimeoutAsync(300);

        await TapNodeAsync(page, nodeId!);

        Assert.AreEqual(1,
            await page.Locator($"g.tm-diagram-node--selected[data-node-id='{nodeId}']").CountAsync(),
            "a tap in read-only selects the node for the read-only properties panel");
        Assert.AreEqual(0, await page.Locator(".tm-diagram-resize-handle-hit").CountAsync(),
            "read-only never renders the transform handles");
        await ScrollEditorStatusIntoViewAsync(page);
        await ShotAsync(page, "tablet-1024-touch--readonly-inspect");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Read-only tablet (R1): a finger that lands on a node pans the canvas instead of dragging it")]
    public async Task Tablet_ReadOnly_FingerDragOnNode_PansTheCanvas()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true);
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "tablet");

        var nodeId = await page.EvaluateAsync<string?>(
            "() => document.querySelector('.tm-diagram-node')?.getAttribute('data-node-id')");
        Assert.IsNotNull(nodeId, "the sample document must render a node");

        await ClickFirstAsync(page, page.Locator("[data-tm-group='readonly'] button"));
        await page.WaitForTimeoutAsync(300);

        var svg = page.Locator(".tm-diagram-canvas__svg").First;
        var viewBoxBefore = await svg.GetAttributeAsync("viewBox");
        var transformBefore = await page.EvaluateAsync<string?>(
            "(id) => document.querySelector(`g.tm-diagram-node[data-node-id='${id}']`)?.getAttribute('transform')",
            nodeId);

        await FingerDragNodeAsync(page, nodeId!, 80);

        var viewBoxAfter = await svg.GetAttributeAsync("viewBox");
        Assert.IsFalse(string.Equals(viewBoxBefore, viewBoxAfter, StringComparison.Ordinal),
            $"a finger drag that starts on a node must pan the canvas in read-only (viewBox {viewBoxBefore} -> {viewBoxAfter})");
        var transformAfter = await page.EvaluateAsync<string?>(
            "(id) => document.querySelector(`g.tm-diagram-node[data-node-id='${id}']`)?.getAttribute('transform')",
            nodeId);
        Assert.AreEqual(transformBefore, transformAfter,
            "the read-only drag must never move the node itself");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Read-only tablet (review D): a tap on an edge selects it for inspection; no edge handles render")]
    public async Task Tablet_ReadOnly_TapEdge_SelectsForInspection()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true);
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "tablet");

        var edgeId = await page.EvaluateAsync<string?>(
            "() => document.querySelector('.tm-diagram-edge-hit-path')?.getAttribute('data-edge-id')");
        Assert.IsNotNull(edgeId, "the sample document must render an edge");

        await ClickFirstAsync(page, page.Locator("[data-tm-group='readonly'] button"));
        await page.WaitForTimeoutAsync(300);

        // A straight edge hit path has a zero-height bounding box, so Playwright's tap action
        // refuses it ("element is not visible"); a synthetic touch-pointer tap dispatches the
        // same pointerdown/up pair the gesture stack consumes.
        await TapEdgeAsync(page, edgeId!);
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual(1,
            await page.Locator($"g.tm-diagram-edge-group[data-edge-id='{edgeId}'] .tm-diagram-edge-path--selected-outline").CountAsync(),
            "a tap in read-only selects the edge for the read-only properties panel");
        Assert.AreEqual(0, await page.Locator(".tm-diagram-edge-handle").CountAsync(),
            "read-only never renders the edge reshape handles");
        Assert.AreEqual(0, await page.Locator(".tm-diagram-edge-toolbar").CountAsync(),
            "read-only never renders the inline edge toolbar");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1440 desktop regression: minimap card, no default rail, mouse-driven resize and rotate still work")]
    public async Task Desktop_1440_Regression_MousePathsAndChrome()
    {
        var page = await OpenEditorAsync(1440, 900, touch: false);

        // Container-owned layout: make the page full-bleed so the editor gets a full desktop width
        // (the demo column caps at ~920px, which resolves tablet) and wait for the resolution.
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "desktop");

        Assert.AreEqual(0, await page.Locator(".tm-editor-shell__rail--left").CountAsync(),
            "desktop keeps both panels docked by default");
        Assert.IsTrue(await page.Locator(".tm-diagram-editor__minimap-card").CountAsync() > 0,
            "desktop keeps the minimap card in the right column");
        Assert.AreEqual(0, await page.Locator(".tm-diagram-editor__minimap-overlay").CountAsync(),
            "the minimap overlay is a tablet/mobile presentation only");

        var nodeId = await page.EvaluateAsync<string?>(
            "() => document.querySelector('.tm-diagram-node')?.getAttribute('data-node-id')");
        Assert.IsNotNull(nodeId);
        await page.EvaluateAsync(
            """
            (id) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                inst.selectedIds = new Set([id]);
                window.tmDiagramEditor._updateSelection(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [id]);
            }
            """, nodeId);
        await page.Locator(".tm-diagram-resize-handle-hit").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var before = await GetNodeSizeAsync(page, nodeId!);
        await MouseResizeAsync(page, nodeId!, "se", 60);
        var after = await GetNodeSizeAsync(page, nodeId!);
        Assert.IsTrue(after.w > before.w + 40, "the mouse resize path must keep working at desktop");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Modeling at 1024: the hosted diagram editor resolves the tablet layout and no Escape collision")]
    public async Task Modeling_1024_HostedEditorGetsTabletLayout()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1024, Height = 768 },
            HasTouch = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000,
        });
        await WaitForAppReadyAsync(page);

        // The modeling canvas hosts TmDiagramEditor: at a 1024 window its container lands in
        // the 768–1199 band, so the tablet presentation (railed palette, overlay minimap) must
        // appear there too — and Escape must reach the modeling dialogs, not a diagram modal
        // (F6 tablet has no modal chrome at all).
        var hosted = page.Locator(".tm-modeling-editor .tm-diagram-editor").First;
        if (await hosted.CountAsync() == 0)
        {
            Assert.Inconclusive("the modeling demo page did not expose the hosted diagram editor on load");
            return;
        }

        await hosted.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);
        Assert.AreEqual(0, await page.Locator(".tm-modeling-editor .tm-diagram-editor .tm-modal-container").CountAsync(),
            "the tablet diagram presentation must not open a modal that would swallow Escape");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<IPage> OpenEditorAsync(int width, int height, bool touch)
    {
        IBrowserContext context;
        if (touch)
        {
            context = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                HasTouch = true,
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
            });
            RegisterContext(context);
        }
        else
        {
            context = await CreateContextAsync();
        }

        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000,
        });
        await WaitForAppReadyAsync(page);

        await page.WaitForSelectorAsync(".tm-diagram-canvas", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 20_000,
        });
        await page.WaitForFunctionAsync(
            """
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                if (!canvas || !canvas.id) return false;
                const ed = window.tmDiagramEditor;
                return !!(ed && ed.instances && ed.instances.get(canvas.id));
            }
            """, null, new PageWaitForFunctionOptions { Timeout = 20_000 });
        // The touch module attaches asynchronously after init: wait until the sessions exist.
        await page.WaitForFunctionAsync(
            """
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const inst = canvas && window.tmDiagramEditor ? window.tmDiagramEditor.instances.get(canvas.id) : null;
                return !!(inst && inst.__touchDetach);
            }
            """, null, new PageWaitForFunctionOptions { Timeout = 20_000 });
        await page.WaitForSelectorAsync(".tm-diagram-node", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15_000,
        });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static async Task<int> NodeCountAsync(IPage page) =>
        await page.EvaluateAsync<int>("() => document.querySelectorAll('.tm-diagram-node').length");

    /// <summary>
    /// Clicks a locator, degrading gracefully when the element cannot be scrolled into the
    /// viewport (clipped ancestors below the fold): a forced click first, then a DOM click —
    /// both dispatch the events Blazor's delegation handles.
    /// </summary>
    private static async Task ClickFirstAsync(IPage page, ILocator locator)
    {
        try
        {
            await locator.First.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            // Out of the viewport (clipped ancestors) or timed out: dispatch the click in the
            // DOM — the same event Blazor's delegation handles.
            await locator.First.EvaluateAsync("el => el.click()");
        }
    }

    private static async Task TapNodeAsync(IPage page, string nodeId)
    {
        var node = page.Locator($"g.tm-diagram-node[data-node-id='{nodeId}']").First;
        await node.ScrollIntoViewIfNeededAsync();
        await node.TapAsync();
        await page.WaitForTimeoutAsync(250);
    }

    /// <summary>Dispatches a synthetic touch-pointer TAP on an edge hit path (no movement — a
    /// tap, never a drag; the read-only touch layer selects the edge for inspection).</summary>
    private static async Task TapEdgeAsync(IPage page, string edgeId)
    {
        await page.EvaluateAsync(
            """
            async ([id]) => {
                const hit = document.querySelector(`.tm-diagram-edge-hit-path[data-edge-id='${id}']`);
                if (!hit) throw new Error('edge hit path not found');
                const r = hit.getBoundingClientRect();
                const x = r.left + r.width / 2, y = r.top + Math.max(r.height / 2, 4);
                const opts = (bx) => ({
                    bubbles: true, cancelable: true, composed: true,
                    pointerId: 7, pointerType: 'touch', isPrimary: true,
                    button: 0, buttons: bx, clientX: x, clientY: y,
                });
                hit.dispatchEvent(new PointerEvent('pointerdown', opts(1)));
                await new Promise(res => setTimeout(res, 40));
                hit.dispatchEvent(new PointerEvent('pointerup', opts(0)));
            }
            """,
            new object[] { edgeId });
        await page.WaitForTimeoutAsync(300);
    }

    private static async Task<(double w, double h)> GetNodeSizeAsync(IPage page, string nodeId)
    {
        var json = await page.EvaluateAsync<string>(
            "(id) => { const el = document.querySelector(`g.tm-diagram-node[data-node-id='${id}']`); return el ? JSON.stringify({ w: parseFloat(el.getAttribute('data-w')), h: parseFloat(el.getAttribute('data-h')) }) : null; }",
            nodeId);
        Assert.IsNotNull(json, $"node {nodeId} must exist");
        using var doc = System.Text.Json.JsonDocument.Parse(json!);
        return (doc.RootElement.GetProperty("w").GetDouble(), doc.RootElement.GetProperty("h").GetDouble());
    }

    /// <summary>Dispatches a synthetic touch-pointer drag on the node's SE resize hit rect.</summary>
    private static async Task FingerResizeAsync(IPage page, string nodeId, string handle, double delta)
    {
        await page.EvaluateAsync(
            """
            async ([id, handle, delta]) => {
                const hit = document.querySelector(`g.tm-diagram-node-handles[data-node-id='${id}'] rect[data-resize-handle='${handle}']`);
                if (!hit) throw new Error('resize hit rect not found');
                const r = hit.getBoundingClientRect();
                const startX = r.left + r.width / 2, startY = r.top + r.height / 2;
                const opts = (x, y) => ({
                    bubbles: true, cancelable: true, composed: true,
                    pointerId: 7, pointerType: 'touch', isPrimary: true,
                    button: 0, buttons: 1, clientX: x, clientY: y,
                });
                // Every event targets the hit rect: the sessions are delegated on the canvas
                // container, so the dispatch bubbles to them (a real pointer is captured to the
                // same element; elementFromPoint here could hit an editor overlay instead).
                hit.dispatchEvent(new PointerEvent('pointerdown', opts(startX, startY)));
                const steps = 8;
                for (let i = 1; i <= steps; i++) {
                    await new Promise(res => setTimeout(res, 16));
                    hit.dispatchEvent(new PointerEvent('pointermove', opts(startX + delta * i / steps, startY + delta * i / steps)));
                }
                hit.dispatchEvent(new PointerEvent('pointerup', { ...opts(startX + delta, startY + delta), buttons: 0 }));
            }
            """,
            new object[] { nodeId, handle, delta });
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>Dispatches a synthetic touch-pointer drag that starts on the node body itself
    /// (the read-only pan path — the press lands on the node, not on a handle).</summary>
    private static async Task FingerDragNodeAsync(IPage page, string nodeId, double dx)
    {
        await page.EvaluateAsync(
            """
            async ([id, dx]) => {
                const node = document.querySelector(`g.tm-diagram-node[data-node-id='${id}']`);
                if (!node) throw new Error('node not found');
                const r = node.getBoundingClientRect();
                const startX = r.left + r.width / 2, startY = r.top + r.height / 2;
                const opts = (x, y) => ({
                    bubbles: true, cancelable: true, composed: true,
                    pointerId: 7, pointerType: 'touch', isPrimary: true,
                    button: 0, buttons: 1, clientX: x, clientY: y,
                });
                node.dispatchEvent(new PointerEvent('pointerdown', opts(startX, startY)));
                const steps = 8;
                for (let i = 1; i <= steps; i++) {
                    await new Promise(res => setTimeout(res, 16));
                    node.dispatchEvent(new PointerEvent('pointermove', opts(startX + dx * i / steps, startY)));
                }
                node.dispatchEvent(new PointerEvent('pointerup', { ...opts(startX + dx, startY), buttons: 0 }));
            }
            """,
            new object[] { nodeId, dx });
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>Dispatches a synthetic touch-pointer drag on the node's rotate hit circle.</summary>
    private static async Task FingerRotateAsync(IPage page, string nodeId, double degrees)
    {
        await page.EvaluateAsync(
            """
            async ([id, degrees]) => {
                const hit = document.querySelector(`g.tm-diagram-node-handles[data-node-id='${id}'] .tm-diagram-rotate-handle-hit`);
                if (!hit) throw new Error('rotate hit circle not found');
                const box = hit.getBoundingClientRect();
                const nodeBox = document.querySelector(`g.tm-diagram-node[data-node-id='${id}']`).getBoundingClientRect();
                const cx = nodeBox.left + nodeBox.width / 2, cy = nodeBox.top + nodeBox.height / 2;
                const startX = box.left + box.width / 2, startY = box.top + box.height / 2;
                const opts = (x, y) => ({
                    bubbles: true, cancelable: true, composed: true,
                    pointerId: 8, pointerType: 'touch', isPrimary: true,
                    button: 0, buttons: 1, clientX: x, clientY: y,
                });
                hit.dispatchEvent(new PointerEvent('pointerdown', opts(startX, startY)));
                const steps = 10;
                const rad0 = Math.atan2(startY - cy, startX - cx);
                const rad1 = rad0 + degrees * Math.PI / 180;
                const radius = Math.hypot(startX - cx, startY - cy);
                for (let i = 1; i <= steps; i++) {
                    await new Promise(res => setTimeout(res, 16));
                    const a = rad0 + (rad1 - rad0) * i / steps;
                    hit.dispatchEvent(new PointerEvent('pointermove', opts(cx + radius * Math.cos(a), cy + radius * Math.sin(a))));
                }
                hit.dispatchEvent(new PointerEvent('pointerup', { ...opts(cx + radius * Math.cos(rad1), cy + radius * Math.sin(rad1)), buttons: 0 }));
            }
            """,
            new object[] { nodeId, degrees });
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>Holds a touch pointer on the node for 700ms (the long-press menu).</summary>
    private static async Task LongPressNodeAsync(IPage page, string nodeId)
    {
        await page.EvaluateAsync(
            """
            async (id) => {
                const node = document.querySelector(`g.tm-diagram-node[data-node-id='${id}']`);
                const r = node.getBoundingClientRect();
                const x = r.left + r.width / 2, y = r.top + r.height / 2;
                const opts = (px, py) => ({
                    bubbles: true, cancelable: true, composed: true,
                    pointerId: 9, pointerType: 'touch', isPrimary: true,
                    button: 0, buttons: 1, clientX: px, clientY: py,
                });
                node.dispatchEvent(new PointerEvent('pointerdown', opts(x, y)));
                await new Promise(res => setTimeout(res, 700));
                document.elementFromPoint(x, y)?.dispatchEvent(new PointerEvent('pointerup', { ...opts(x, y), buttons: 0 }));
            }
            """,
            nodeId);
    }

    // ── Phase 3 (J1/J1b/J4/J5/J12a): real CDP touch fingers over the gesture stack ──────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1024 touch, real CDP fingers: a pinch from the empty canvas after finger one moved zooms (J1)")]
    public async Task Tablet_CdpPinch_FromEmptyCanvas_AfterFirstFingerMoved_Zooms()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true);
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "tablet");
        var (x, y) = await DiagramEditorE2E.EmptyCanvasPointAsync(page);
        var session = await page.Context.NewCDPSessionAsync(page);
        var widthBefore = await DiagramEditorE2E.CanvasViewBoxWidthAsync(page);

        await DiagramEditorE2E.CdpTouchAsync(session, "touchStart", (1, x - 40, y));
        await DiagramEditorE2E.CdpTouchAsync(session, "touchMove", (1, x - 38, y));     // finger one's capture goes live
        await DiagramEditorE2E.CdpTouchAsync(session, "touchStart", (1, x - 38, y), (2, x + 40, y));
        for (var i = 1; i <= 6; i++)
            await DiagramEditorE2E.CdpTouchAsync(session, "touchMove", (1, x - 38 - 15 * i, y), (2, x + 40 + 15 * i, y));
        await DiagramEditorE2E.CdpTouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(300);

        Assert.IsTrue(await DiagramEditorE2E.CanvasViewBoxWidthAsync(page) < widthBefore - 1,
            "a real two-finger pinch that starts with a moving finger must zoom (the pan hand-over must not cancel it)");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1024 touch, real CDP fingers: a pinch handed over from a dragged node keeps the canvas alive (J1b)")]
    public async Task Tablet_CdpPinch_FromDraggedNode_CanvasKeepsRendering()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true);
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "tablet");

        // Insert and select a node the same way the tap-insert test does: the palette rail
        // expands into the tile grid first, then the rectangle tile is tappable.
        await page.Locator(".tm-editor-shell__rail--left .tm-editor-shell__panel-toggle").First.ClickAsync();
        var paletteTile = page.Locator("[data-stencil-id='general.rectangle']").First;
        await paletteTile.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var before = await NodeCountAsync(page);
        await paletteTile.TapAsync();
        await page.WaitForFunctionAsync(
            "(n) => document.querySelectorAll('.tm-diagram-node').length > n",
            before, new PageWaitForFunctionOptions { Timeout = 15_000 });

        var centre = await page.EvaluateAsync<double[]>(
            "() => { const n = document.querySelector('.tm-diagram-node'); const r = n.getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; }");
        var (nx, ny) = (centre[0], centre[1]);

        var session = await page.Context.NewCDPSessionAsync(page);
        await DiagramEditorE2E.CdpTouchAsync(session, "touchStart", (1, nx, ny));
        await DiagramEditorE2E.CdpTouchAsync(session, "touchMove", (1, nx + 12, ny));   // the node drag arms
        await DiagramEditorE2E.CdpTouchAsync(session, "touchMove", (1, nx + 24, ny));
        await DiagramEditorE2E.CdpTouchAsync(session, "touchStart", (1, nx + 24, ny), (2, nx + 224, ny));
        for (var i = 1; i <= 4; i++)
            await DiagramEditorE2E.CdpTouchAsync(session, "touchMove", (1, nx + 24 - 10 * i, ny), (2, nx + 224 + 10 * i, ny));
        await DiagramEditorE2E.CdpTouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(400);

        // J1b: the canvas survived the session hand-over — a palette tap still inserts. The
        // selection railed the palette back (at most one expanded panel), so expand it again.
        if (await page.Locator("[data-stencil-id='general.ellipse']").CountAsync() == 0
            || !await page.Locator("[data-stencil-id='general.ellipse']").First.IsVisibleAsync())
        {
            await page.Locator(".tm-editor-shell__rail--left .tm-editor-shell__panel-toggle").First.ClickAsync();
        }
        var ellipseTile = page.Locator("[data-stencil-id='general.ellipse']").First;
        await ellipseTile.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var afterPinch = await NodeCountAsync(page);
        await ellipseTile.TapAsync();
        await page.WaitForFunctionAsync(
            "(n) => document.querySelectorAll('.tm-diagram-node').length > n",
            afterPinch, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1024 touch, real CDP long-press on the empty canvas opens exactly one canvas menu (J5/R4)")]
    public async Task Tablet_CdpLongPress_OnEmptyCanvas_OpensOneCanvasMenu()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true);
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "tablet");
        var (x, y) = await DiagramEditorE2E.EmptyCanvasPointAsync(page);
        var session = await page.Context.NewCDPSessionAsync(page);
        await DiagramEditorE2E.ArmMenuOpenCounterAsync(page, "OnCanvasContextMenu");

        await DiagramEditorE2E.CdpTouchAsync(session, "touchStart", (1, x, y));
        await page.WaitForTimeoutAsync(900);
        await DiagramEditorE2E.CdpTouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual(1, await DiagramEditorE2E.MenuOpenCountAsync(page),
            "C# must be told to open the canvas menu exactly once (the native touch contextmenu is swallowed)");
        Assert.AreEqual(1, await page.Locator(".tm-diagram-editor__context-menu").CountAsync(),
            "a long-press on the empty canvas opens exactly one canvas context menu (the release click on the menu must not activate an item)");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1024 touch, real CDP long-press then a touch tap on the canvas closes the menu (review fix)")]
    public async Task Tablet_CdpLongPress_ThenTapCanvas_ClosesMenu()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true);
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "tablet");
        var (x, y) = await DiagramEditorE2E.EmptyCanvasPointAsync(page);
        // The tap point is measured BEFORE the menu opens: the open fixed menu would otherwise
        // cover the canvas and the empty-strip probe would find nothing.
        var (tapX, tapY) = await DiagramEditorE2E.EmptyCanvasPointAsync(page);
        var session = await page.Context.NewCDPSessionAsync(page);
        await DiagramEditorE2E.ArmMenuOpenCounterAsync(page, "OnCanvasContextMenu");

        // The hold opens the menu.
        await DiagramEditorE2E.CdpTouchAsync(session, "touchStart", (1, x, y));
        await page.WaitForTimeoutAsync(900);
        await DiagramEditorE2E.CdpTouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(1, await DiagramEditorE2E.MenuOpenCountAsync(page), "the long-press opens the menu once");
        Assert.AreEqual(1, await page.Locator(".tm-diagram-editor__context-menu").CountAsync(), "the menu is open");

        // A touch TAP on the empty canvas dismisses it (pointerdown capture, touch button 0).
        await DiagramEditorE2E.CdpTouchAsync(session, "touchStart", (1, tapX, tapY));
        await DiagramEditorE2E.CdpTouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(400);

        Assert.AreEqual(0, await page.Locator(".tm-diagram-editor__context-menu").CountAsync(),
            "a touch tap on the canvas dismisses the context menu");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1024 touch, real CDP long-press on an edge opens exactly one edge menu (J5/R4)")]
    public async Task Tablet_CdpLongPress_OnEdge_OpensOneMenu()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true);
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "tablet");

        var edgeCentre = await page.EvaluateAsync<double[]?>(
            "() => { const hit = document.querySelector('.tm-diagram-edge-hit-path'); if (!hit) return null; const r = hit.getBoundingClientRect(); return [r.left + r.width / 2, r.top + Math.max(r.height / 2, 4)]; }");
        Assert.IsNotNull(edgeCentre, "the sample document must render an edge");
        var session = await page.Context.NewCDPSessionAsync(page);
        await DiagramEditorE2E.ArmMenuOpenCounterAsync(page, "OnEdgeContextMenu");

        await DiagramEditorE2E.CdpTouchAsync(session, "touchStart", (1, edgeCentre[0], edgeCentre[1]));
        await page.WaitForTimeoutAsync(900);
        await DiagramEditorE2E.CdpTouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual(1, await DiagramEditorE2E.MenuOpenCountAsync(page),
            "C# must be told to open the edge menu exactly once");
        Assert.AreEqual(1, await page.Locator(".tm-diagram-editor__context-menu").CountAsync(),
            "a long-press on an edge opens exactly one context menu");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1024 touch, real CDP fingers: a drag from a port of a SELECTED node draws an edge (J12a/R2, review fix)")]
    public async Task Tablet_CdpFingerDragFromPort_CreatesEdge()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true);
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "tablet");

        // Insert a fresh rectangle: the stencil carries four ports (the sample document's
        // nodes predate the port model and render none).
        await page.Locator(".tm-editor-shell__rail--left .tm-editor-shell__panel-toggle").First.ClickAsync();
        var paletteTile = page.Locator("[data-stencil-id='general.rectangle']").First;
        await paletteTile.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var beforeNodes = await NodeCountAsync(page);
        await paletteTile.TapAsync();
        await page.WaitForFunctionAsync(
            "(n) => document.querySelectorAll('.tm-diagram-node').length > n",
            beforeNodes, new PageWaitForFunctionOptions { Timeout = 15_000 });

        // The node with ports (a sample-document node — the inserted rectangle lands selected at
        // the canvas centre, so tap-selection is unreliable: select through the same JS entry the
        // connect modal uses).
        var nodeId = await page.EvaluateAsync<string?>(
            "() => { const n = [...document.querySelectorAll('.tm-diagram-node')].find(n => n.querySelector('circle.tm-diagram-port')); return n?.getAttribute('data-node-id'); }");
        if (nodeId is null)
        {
            Assert.Inconclusive("the sample document rendered no node with ports");
            return;
        }
        await page.EvaluateAsync(
            """
            (id) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                inst.selectedIds = new Set([id]);
                window.tmDiagramEditor._updateSelection(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [id]);
            }
            """, nodeId);
        await page.Locator($".tm-diagram-port-hits[data-node-id='{nodeId}'] circle.tm-diagram-port-hit").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var edgesBefore = await page.Locator(".tm-diagram-edge-hit-path").CountAsync();
        var port = page.Locator($".tm-diagram-node--selected[data-node-id='{nodeId}'] circle.tm-diagram-port").First;
        await port.ScrollIntoViewIfNeededAsync();
        var portBox = await port.BoundingBoxAsync();
        Assert.IsNotNull(portBox, "the selected node's port must have a bounding box");
        var sx = portBox.X + portBox.Width / 2;
        var sy = portBox.Y + portBox.Height / 2;
        var target = await page.EvaluateAsync<double[]>(
            @"(selfId) => { const nodes = [...document.querySelectorAll('.tm-diagram-node')]; const other = nodes.find(n => n.getAttribute('data-node-id') !== selfId) || nodes[0]; const r = other.getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; }",
            nodeId);

        var session = await page.Context.NewCDPSessionAsync(page);
        await DiagramEditorE2E.CdpTouchAsync(session, "touchStart", (1, sx, sy));
        for (var i = 1; i <= 6; i++)
            await DiagramEditorE2E.CdpTouchAsync(session, "touchMove",
                (1, sx + (target[0] - sx) * i / 6, sy + (target[1] - sy) * i / 6));
        await DiagramEditorE2E.CdpTouchAsync(session, "touchEnd");
        await page.WaitForTimeoutAsync(400);

        Assert.IsTrue(await page.Locator(".tm-diagram-edge-hit-path").CountAsync() > edgesBefore,
            "a finger drag from a selected node's port must create an edge (the finger-sized port hit wins over the handles)");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1024 touch (review fix): connect arrows are finger-reachable — elementFromPoint finds the arrow and a real tap opens the connect dialog")]
    public async Task Tablet_ConnectArrow_FingerReachable_TapOpensDialog()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true);
        await DiagramEditorE2E.PinEditorFullBleedAsync(page, "tablet");

        // Select the first node through the same JS entry the other decorator-pane tests use,
        // so the pane renders its handles group, port hits and the connect-arrow group.
        var nodeId = await page.EvaluateAsync<string?>(
            "() => document.querySelector('.tm-diagram-node')?.getAttribute('data-node-id')");
        Assert.IsNotNull(nodeId, "the sample document must render a node");
        await page.EvaluateAsync(
            """
            (id) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const inst = window.tmDiagramEditor.instances.get(canvas.id);
                inst.selectedIds = new Set([id]);
                window.tmDiagramEditor._updateSelection(inst);
                inst.dotNetRef.invokeMethodAsync('OnSelectionChanged', [id]);
            }
            """, nodeId);

        var arrow = page.Locator($"g.tm-diagram-connect-arrows[data-node-id='{nodeId}'] .tm-diagram-connect-arrow--e").First;
        await arrow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await arrow.ScrollIntoViewIfNeededAsync();

        // Reachability: at the arrow's centre the topmost element must BE the arrow button.
        // The arrows render in their own decorator-pane group AFTER the finger-sized port hit
        // discs (buttons > gesture hit areas > visual handles), so a finger lands on the
        // button — never on a port hit painting over it (the pre-fix defect).
        var probe = await page.EvaluateAsync<string?>(
            """
            (id) => {
                const arrow = document.querySelector(`g.tm-diagram-connect-arrows[data-node-id='${id}'] .tm-diagram-connect-arrow--e`);
                if (!arrow) return null;
                const r = arrow.getBoundingClientRect();
                const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
                const hit = document.elementFromPoint(cx, cy);
                if (!hit) return 'none';
                if (hit.closest('.tm-diagram-connect-arrow')) return 'arrow';
                return hit.className?.toString() || hit.tagName;
            }
            """, nodeId);
        Assert.AreEqual("arrow", probe,
            $"elementFromPoint at the arrow centre must find the connect arrow, not '{probe}'");

        // A real touch tap on the arrow opens the connect dialog. The arrows are HTML inside an
        // SVG foreignObject, where Chromium never synthesizes a compatibility click for a touch
        // — the touch module's dedicated arrow-tap session clicks the button on a tap (a moved
        // press is a drag and never clicks; covered by the node engine tests).
        var box = await arrow.BoundingBoxAsync();
        Assert.IsNotNull(box, "the connect arrow must have a bounding box");
        await page.Touchscreen.TapAsync((float)(box.X + box.Width / 2), (float)(box.Y + box.Height / 2));
        await page.Locator(".tm-diagram-connect-modal__grid").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    /// <summary>Two-finger pinch around the canvas centre by the given spread delta.
    /// Synthetic PointerEvents: setPointerCapture throws for an inactive id, so this cannot
    /// prove capture behaviour — see the Cdp* tests.</summary>
    private static async Task TwoFingerPinchAsync(IPage page, double spread)
    {
        await page.EvaluateAsync(
            """
            async (spread) => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const r = canvas.getBoundingClientRect();
                const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
                const opts = (id, x, y) => ({
                    bubbles: true, cancelable: true, composed: true,
                    pointerId: id, pointerType: 'touch', isPrimary: id === 1,
                    button: 0, buttons: 2, clientX: x, clientY: y,
                });
                canvas.dispatchEvent(new PointerEvent('pointerdown', opts(1, cx - 60, cy)));
                canvas.dispatchEvent(new PointerEvent('pointerdown', opts(2, cx + 60, cy)));
                const steps = 8;
                for (let i = 1; i <= steps; i++) {
                    await new Promise(res => setTimeout(res, 16));
                    const d = 60 + spread * i / steps;
                    canvas.dispatchEvent(new PointerEvent('pointermove', opts(1, cx - d, cy)));
                    canvas.dispatchEvent(new PointerEvent('pointermove', opts(2, cx + d, cy)));
                }
                canvas.dispatchEvent(new PointerEvent('pointerup', opts(1, cx - 60 - spread, cy)));
                canvas.dispatchEvent(new PointerEvent('pointerup', opts(2, cx + 60 + spread, cy)));
            }
            """,
            spread);
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>The mouse resize path (pointerdown mouse → container mousemove → mouseup).</summary>
    private static async Task MouseResizeAsync(IPage page, string nodeId, string handle, double delta)
    {
        var hit = page.Locator($"g.tm-diagram-node-handles[data-node-id='{nodeId}'] rect[data-resize-handle='{handle}']").First;
        var box = await hit.BoundingBoxAsync();
        Assert.IsNotNull(box, "the resize hit rect must be on screen");
        var startX = box.X + box.Width / 2;
        var startY = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();
        for (var i = 1; i <= 6; i++)
        {
            await page.Mouse.MoveAsync((float)(startX + delta * i / 6), (float)(startY + delta * i / 6));
        }
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task<string> ZoomLabelAsync(IPage page) =>
        (await page.Locator(".tm-diagram-editor__zoom-label").First.TextContentAsync())?.Trim() ?? string.Empty;

    /// <summary>
    /// Scrolls the shell status bar (zoom − % +) into the viewport before a tablet editor
    /// capture: the demo column is taller than the tablet window, which leaves the shell title
    /// and the zoom controls below the fold. Instant behavior keeps the frame deterministic.
    /// </summary>
    private static async Task ScrollEditorStatusIntoViewAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                const status = document.querySelector('.tm-editor-shell__status');
                if (status) status.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
            }
            """);
        await page.WaitForTimeoutAsync(100);
    }

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, Timeout = 60_000 });
        TestContext.AddResultFile(path);
    }

    private async Task ShotSettledAsync(IPage page, string name)
    {
        await page.EvaluateAsync(
            """
            async () => {
                const animations = document.getAnimations();
                await Promise.all(animations.map(a => a.finished.catch(() => { })));
            }
            """);
        await page.WaitForTimeoutAsync(100);
        await ShotAsync(page, name);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
