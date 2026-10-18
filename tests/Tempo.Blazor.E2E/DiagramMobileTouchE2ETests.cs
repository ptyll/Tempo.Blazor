using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// DiagramEditor redesign phase 7 — mobile viewing and light edits (G2). At container widths
/// below 768 the editor renders the shell's mobile presentation: a compact header (title,
/// Undo/Redo, "⋯"), scrollable page tabs, the full-area canvas with a floating zoom pill and
/// the floating minimap, a non-modal bottom sheet (peek/half/full) with Shapes | Properties |
/// Layers, the F5 action bar (Select | Pan | Properties | More) and the long-press context menu
/// as a sheet. Light edits only: tap-insert, selection, fill change, layers, undo, export via
/// "More" (no layout, no import, no resize/rotate/connect handles). 1440/1024 regression-check
/// the desktop/tablet chrome. Screenshots land in tests/Tempo.Blazor.E2E/__screenshots__/
/// diagram-editor/ and are reviewed against output/imagegen/tempo-reimagined/
/// Tempo.Blazor.DiagramEditor/diagram-editor--mobile.png and diagram-layers-panel--mobile.png
/// (the [E] mockup elements — back arrow, document subtitle, "Spustit", layer descriptions,
/// Share/Save — are out of scope).
/// </summary>
[TestClass]
public sealed class DiagramMobileTouchE2ETests : WasmTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";
    private const string ModelingEditorUrl = "/modeling-editor";

    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "diagram-editor");

    [TestMethod]
    [TestCategory("WASM")]
    [Description("390×844 touch: mobile chrome, tap-insert, selection peek, fill change, layers, pages, pinch, long-press sheet, export via More")]
    public async Task Mobile_390Touch_LightEditFlow()
    {
        var page = await OpenEditorAsync(390, 844);

        // ── Mobile presentation ─────────────────────────────────────────────
        await ForceEditorWidthAsync(page, 390, "mobile");
        Assert.AreEqual(0, await page.Locator(".tm-editor-shell__rail--left").CountAsync(),
            "no docked-panel rails on mobile");
        Assert.AreEqual(0, await page.Locator(".tm-diagram-editor__statusbar").CountAsync(),
            "the status bar row is replaced by the floating zoom on mobile");
        await page.Locator(".tm-mobile-action-bar__bar").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.Locator(".tm-diagram-editor__zoom-overlay").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.Locator(".tm-diagram-editor__minimap-overlay").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        // The capture must read like the mockup: the diagram visible ABOVE the sheet, the sheet
        // only at the bottom. The sheet opens on the lowest (peek) snap and the editor fills the
        // viewport (below the demo's fixed header), so the canvas keeps its full strip.
        await SetMobileSheetPeekAsync(page);
        await FrameEditorInViewportAsync(page);
        await FrameDiagramAboveSheetAsync(page);
        await ShotSettledAsync(page, "mobile-390-touch--editor");

        // ── Action bar: Select | Pan | Properties tiles + More ──────────────
        var tiles = page.Locator(".tm-mobile-action-bar__action");
        Assert.AreEqual(3, await tiles.CountAsync(), "Select | Pan | Properties are the visible tiles");
        Assert.IsTrue(await page.Locator(".tm-mobile-action-bar__more").CountAsync() > 0,
            "the More trigger closes the bar");

        // ── Tap Properties: the sheet opens on the inspector ────────────────
        await tiles.Filter(new() { HasText = "Properties" }).First.TapAsync();
        var sheet = page.Locator(".tm-editor-shell__sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.Locator("[role='tabpanel'][data-region='right']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        // ── Sheet tabs: Shapes | Inspector (inspector keeps Properties | Layers) ──
        await sheet.Locator(".tm-editor-shell__tabs [role='tab']").First.TapAsync();
        await page.Locator(".tm-diagram-toolbox").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await ShotAsync(page, "mobile-390-touch--sheet-shapes");

        // ── Tap-insert at the viewport centre ───────────────────────────────
        var before = await NodeCountAsync(page);
        var beforeIds = await page.EvaluateAsync<string>(
            "() => JSON.stringify([...document.querySelectorAll('.tm-diagram-node')].map(n => n.getAttribute('data-node-id')))");
        var paletteTile = page.Locator("[data-stencil-id='general.rectangle']").First;
        await paletteTile.ScrollIntoViewIfNeededAsync();
        await paletteTile.TapAsync();
        await page.WaitForFunctionAsync(
            "(n) => document.querySelectorAll('.tm-diagram-node').length > n",
            before, new PageWaitForFunctionOptions { Timeout = 15_000 });
        var insertedId = await page.EvaluateAsync<string>(
            "(prev) => { const prevSet = new Set(JSON.parse(prev)); const nodes = [...document.querySelectorAll('.tm-diagram-node')]; return (nodes.find(n => !prevSet.has(n.getAttribute('data-node-id'))))?.getAttribute('data-node-id'); }",
            beforeIds);
        Assert.IsNotNull(insertedId, "the tap-insert must create a new node");

        // ── Selection opens the sheet PEEKED on the properties ──────────────
        // The peeked sheet covers the lower canvas; a user dismisses it (✕) to work on the
        // canvas, and the next selection re-opens it peeked — exactly the flow under test.
        // The tap-insert left the new node selected: deselect on the empty canvas first
        // (the canvas raises no selection event for re-tapping the same node).
        await ClickFirstAsync(page, page.Locator(".tm-editor-shell__sheet-close"));
        await page.Locator(".tm-editor-shell__sheet").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
        await page.EvaluateAsync(
            """
            async () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const r = canvas.getBoundingClientRect();
                const opts = (x, y) => ({
                    bubbles: true, cancelable: true, composed: true,
                    pointerId: 11, pointerType: 'touch', isPrimary: true,
                    button: 0, buttons: 1, clientX: x, clientY: y,
                });
                const x = r.left + 24, y = r.top + 24;
                canvas.dispatchEvent(new PointerEvent('pointerdown', opts(x, y)));
                canvas.dispatchEvent(new PointerEvent('pointerup', { ...opts(x, y), buttons: 0 }));
            }
            """);
        await page.WaitForTimeoutAsync(400);
        await TapNodeAsync(page, insertedId!);
        await page.Locator(".tm-editor-shell__sheet").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.Locator("[role='tabpanel'][data-region='right']:not([hidden])").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var snapIndex = await sheet.GetAttributeAsync("data-snap-index");
        Assert.AreEqual("0", snapIndex, "a selection opens the sheet at the PEEK snap");
        await ShotSettledAsync(page, "mobile-390-touch--selection-peek");

        // ── Light edit: change the fill in the properties ───────────────────
        var fillInput = page.Locator(".tm-diagram-properties input[type='color']").First;
        await fillInput.ScrollIntoViewIfNeededAsync();
        await fillInput.FillAsync("#ff8800");
        await page.WaitForFunctionAsync(
            "(id) => (document.querySelector(`g.tm-diagram-node[data-node-id='${id}'] .tm-diagram-node__shape-bg`)?.getAttribute('fill') ?? '').toLowerCase() === '#ff8800'",
            insertedId, new PageWaitForFunctionOptions { Timeout = 15_000 });

        // ── Layers: switch to the Layers surface, add a layer, toggle visibility ──
        var layersTab = page.Locator(".tm-diagram-editor__inspector [role='tab']").Filter(new() { HasText = "Layers" }).First;
        await ClickFirstAsync(page, layersTab);
        var layersPanel = page.Locator(".tm-diagram-layers").First;
        await layersPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await layersPanel.ScrollIntoViewIfNeededAsync();
        await ShotSettledAsync(page, "mobile-390-touch--layers-panel");

        await ClickFirstAsync(page, page.Locator(".tm-diagram-layers__add-btn"));
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-layers__item').length >= 2",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        // Toggle the NEW (empty) layer's visibility — toggling the active layer would hide
        // the inserted node and break the following steps.
        await ClickFirstAsync(page, page.Locator(".tm-diagram-layers__item").Nth(1).Locator(".tm-diagram-layers__visibility"));
        await page.WaitForTimeoutAsync(300);

        // ── Pinch zoom (floating zoom label mirrors it) ──────────────────────
        var zoomBefore = await ZoomLabelAsync(page);
        await TwoFingerPinchAsync(page, 120);
        var zoomAfter = await ZoomLabelAsync(page);
        Assert.AreNotEqual(zoomBefore, zoomAfter, "a two-finger pinch must change the zoom");

        // ── Long-press: the context menu is a SHEET ─────────────────────────
        await LongPressNodeAsync(page, insertedId!);
        var contextSheet = page.Locator(".tm-overlay-panel-sheet[aria-label='Actions']");
        await contextSheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        Assert.AreEqual(0, await page.Locator(".tm-diagram-editor__context-menu").CountAsync(),
            "the fixed menu must not render on mobile");
        await ScrollEditorActionBarIntoViewAsync(page);
        await ShotSettledAsync(page, "mobile-390-touch--long-press-sheet");
        await ClickFirstAsync(page, contextSheet.Locator(".tm-overlay-panel-sheet__done"));
        await contextSheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        // ── Export via "More" (a real JSON download) ────────────────────────
        await page.Locator(".tm-mobile-action-bar__more").First.TapAsync();
        var moreSheet = page.Locator(".tm-overlay-panel-sheet").Filter(new() { Has = page.Locator("[role='menuitem']") }).First;
        await moreSheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var moreLabels = await moreSheet.Locator("[role='menuitem']").AllTextContentsAsync();
        Assert.IsTrue(moreLabels.Any(l => l.Trim().StartsWith("Export", StringComparison.OrdinalIgnoreCase)),
            $"the More menu carries the export commands (found: {string.Join(", ", moreLabels)})");
        Assert.IsFalse(moreLabels.Any(l => l.Contains("Layout", StringComparison.OrdinalIgnoreCase) || l.Contains("Import", StringComparison.OrdinalIgnoreCase)),
            "layout and import are tablet+ commands and never in the mobile More");
        await ShotSettledAsync(page, "mobile-390-touch--more-sheet");

        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await moreSheet.Locator("[role='menuitem']").Filter(new() { HasText = "Export JSON" }).First.ClickAsync();
        }, new PageRunAndWaitForDownloadOptions { Timeout = 60_000 });
        Assert.IsTrue(download.SuggestedFilename.EndsWith(".json", StringComparison.OrdinalIgnoreCase),
            $"the JSON export downloads a .json file (got {download.SuggestedFilename})");

        // ── Full-screen flow shot ───────────────────────────────────────────
        await ScrollEditorActionBarIntoViewAsync(page);
        await ShotSettledAsync(page, "mobile-390-touch--after-flow");

        // ── Pages: add and switch (last — the flow stays on the edited page) ─
        await ClickFirstAsync(page, page.Locator(".tm-diagram-editor__page-tab-add"));
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-editor__page-tab').length >= 2",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await ClickFirstAsync(page, page.Locator(".tm-diagram-editor__page-tab").Nth(1));
        await page.WaitForTimeoutAsync(300);
        var activeTab = await page.Locator(".tm-diagram-editor__page-tab--active").First.TextContentAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(activeTab), "a page tab must be active after switching");
        // Back to the page holding the inserted node (the switch-back is part of the flow).
        await ClickFirstAsync(page, page.Locator(".tm-diagram-editor__page-tab").Nth(0));
        await page.WaitForFunctionAsync(
            "(id) => !!document.querySelector(`g.tm-diagram-node[data-node-id='${id}']`)",
            insertedId, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("320×640 touch: nothing overflows horizontally, the chrome stays usable")]
    public async Task Mobile_320Touch_NoHorizontalOverflow()
    {
        var page = await OpenEditorAsync(320, 640);
        await ForceEditorWidthAsync(page, 320, "mobile");

        await page.Locator(".tm-mobile-action-bar__bar").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        // The page never scrolls horizontally; the action bar fits its container.
        var metrics = await page.EvaluateAsync<string>(
            "() => JSON.stringify({ doc: document.documentElement.scrollWidth, win: window.innerWidth, bar: (() => { const b = document.querySelector('.tm-mobile-action-bar__bar'); return b ? b.scrollWidth : -1; })(), barClient: (() => { const b = document.querySelector('.tm-mobile-action-bar__bar'); return b ? b.clientWidth : -1; })() })");
        using var doc = System.Text.Json.JsonDocument.Parse(metrics);
        var root = doc.RootElement;
        Assert.IsTrue(root.GetProperty("doc").GetInt32() <= root.GetProperty("win").GetInt32(),
            $"no horizontal page overflow at 320: {metrics}");
        Assert.IsTrue(root.GetProperty("barClient").GetInt32() > 0,
            $"the action bar rendered: {metrics}");
        Assert.IsTrue(root.GetProperty("bar").GetInt32() <= root.GetProperty("barClient").GetInt32() + 1,
            $"the action bar does not overflow its container at 320: {metrics}");

        // The sheet still opens and its tiles stay inside the sheet body.
        await page.Locator(".tm-mobile-action-bar__action").Filter(new() { HasText = "Properties" }).First.TapAsync();
        await page.Locator(".tm-editor-shell__sheet").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        // Same capture contract as 390: the sheet backs down to the peek snap so the diagram
        // stays visible above it, and the canvas is framed clear of the floating overlays.
        await SetMobileSheetPeekAsync(page);
        await FrameEditorInViewportAsync(page);
        await ScrollEditorActionBarIntoViewAsync(page);
        await FrameDiagramAboveSheetAsync(page);
        await ShotSettledAsync(page, "mobile-320-touch--editor");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1440 desktop regression: no action bar, status bar present, minimap card, handles render")]
    public async Task Desktop_1440_Regression()
    {
        var page = await OpenEditorAsync(1440, 900, touch: false);
        await ForceEditorWidthAsync(page, 1400, "desktop");

        Assert.AreEqual(0, await page.Locator(".tm-mobile-action-bar__bar").CountAsync(),
            "desktop renders no mobile action bar");
        Assert.AreEqual(0, await page.Locator(".tm-diagram-editor__zoom-overlay").CountAsync(),
            "desktop keeps the status-bar zoom");
        Assert.IsTrue(await page.Locator(".tm-diagram-editor__statusbar").CountAsync() > 0,
            "desktop keeps the status bar");
        Assert.IsTrue(await page.Locator(".tm-diagram-editor__minimap-card").CountAsync() > 0,
            "desktop keeps the minimap card in the right column");

        var nodeId = await page.EvaluateAsync<string?>(
            "() => document.querySelector('.tm-diagram-node')?.getAttribute('data-node-id')");
        Assert.IsNotNull(nodeId);
        await SelectNodeViaJsAsync(page, nodeId!);
        Assert.IsTrue(await page.Locator(".tm-diagram-resize-handle-hit").CountAsync() > 0,
            "desktop keeps the resize handles");
        Assert.IsTrue(await page.Locator(".tm-diagram-connect-arrow").CountAsync() > 0,
            "desktop keeps the quick-connect arrows");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1024 tablet regression: railed palette, no mobile action bar, minimap overlay")]
    public async Task Tablet_1024_Regression()
    {
        var page = await OpenEditorAsync(1024, 768, touch: true);
        await ForceEditorWidthAsync(page, 1024, "tablet");

        Assert.AreEqual(0, await page.Locator(".tm-mobile-action-bar__bar").CountAsync(),
            "the mobile action bar is a mobile-only presentation");
        Assert.IsTrue(await page.Locator(".tm-editor-shell__rail--left").CountAsync() > 0,
            "the tablet palette rails");
        Assert.IsTrue(await page.Locator(".tm-diagram-editor__minimap-overlay").CountAsync() > 0,
            "the tablet minimap floats over the canvas");
        Assert.IsTrue(await page.Locator(".tm-diagram-editor__statusbar").CountAsync() > 0,
            "the tablet keeps the status bar zoom");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("390 Modeling preview: the Viewer-preset hosted editor has no edit bar and no sheet collision with the overlay header")]
    public async Task Modeling_390_ViewerPreview_Mobile()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
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

        var hosted = page.Locator(".tm-modeling-editor .tm-diagram-editor").First;
        if (await hosted.CountAsync() == 0)
        {
            Assert.Inconclusive("the modeling demo page did not expose the hosted diagram editor on load");
            return;
        }
        await hosted.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.tm-modeling-editor .tm-diagram-editor')?.getAttribute('data-editor-layout') === 'mobile'",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });

        // Viewer preview contract: no edit action bar, no panels sheet — only the floating zoom.
        Assert.AreEqual(0, await page.Locator(".tm-modeling-editor .tm-mobile-action-bar__bar").CountAsync(),
            "the Viewer preview has no edit action bar on mobile");
        Assert.AreEqual(0, await page.Locator(".tm-modeling-editor .tm-editor-shell__sheet").CountAsync(),
            "the Viewer preview opens no panels sheet (no collision with the overlay header)");
        Assert.IsTrue(await page.Locator(".tm-modeling-editor .tm-diagram-editor__zoom-overlay").CountAsync() > 0,
            "the Viewer preview keeps the floating zoom");

        await page.EvaluateAsync(
            "() => { const b = document.querySelector('.tm-modeling-editor .tm-diagram-editor__zoom-overlay'); b?.scrollIntoView({ block: 'center', behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(150);
        await ShotSettledAsync(page, "mobile-390-modeling-preview");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<IPage> OpenEditorAsync(int width, int height, bool touch = true)
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
    /// Pins the editor container to the phone width inside the demo column and waits for the
    /// resolution (the demo gutters shrink the available width; 24px covers them at 320).
    /// </summary>
    private static async Task ForceEditorWidthAsync(IPage page, double width, string expectedLayout)
    {
        var target = Math.Max(300, width - 24);
        await page.EvaluateAsync(
            """
            (w) => {
                const editor = document.querySelector('.tm-diagram-editor');
                const host = editor?.parentElement;
                if (!host) throw new Error('editor host not found');
                host.style.width = w + 'px';
                host.style.maxWidth = w + 'px';
            }
            """,
            target);
        await page.WaitForFunctionAsync(
            """
            (expected) => document.querySelector('.tm-diagram-editor')?.getAttribute('data-editor-layout') === expected
            """,
            expectedLayout,
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        await page.Locator(".tm-diagram-editor").First.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(250);
    }

    private static async Task TapNodeAsync(IPage page, string nodeId)
    {
        var node = page.Locator($"g.tm-diagram-node[data-node-id='{nodeId}']").First;
        await node.ScrollIntoViewIfNeededAsync();
        await node.TapAsync();
        await page.WaitForTimeoutAsync(250);
    }

    private static async Task SelectNodeViaJsAsync(IPage page, string nodeId)
    {
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
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>Holds a touch pointer on the node for 700ms (the long-press menu).</summary>
    private static async Task LongPressNodeAsync(IPage page, string nodeId)
    {
        await page.EvaluateAsync(
            """
            async (id) => {
                const node = document.querySelector(`g.tm-diagram-node[data-node-id='${id}']`);
                node.scrollIntoView({ block: 'center', behavior: 'instant' });
                await new Promise(res => setTimeout(res, 150));
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
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>Two-finger pinch around the canvas centre by the given spread delta.</summary>
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

    private static async Task<string> ZoomLabelAsync(IPage page) =>
        (await page.Locator(".tm-diagram-editor__zoom-overlay .tm-diagram-editor__zoom-label").First.TextContentAsync())?.Trim() ?? string.Empty;

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
            await locator.First.EvaluateAsync("el => el.click()");
        }
    }

    /// <summary>
    /// Scrolls the editor's bottom chrome (the F5 action bar) into the viewport before a mobile
    /// capture — the phase 4/6 pitfall: the shot must show the status/action bar. Instant
    /// behavior keeps the frame deterministic.
    /// </summary>
    private static async Task ScrollEditorActionBarIntoViewAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                const bar = document.querySelector('.tm-mobile-action-bar__bar')
                    ?? document.querySelector('.tm-diagram-editor__zoom-overlay');
                bar?.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
            }
            """);
        await page.WaitForTimeoutAsync(100);
    }

    /// <summary>
    /// Drives the mobile panels sheet to the PEEK snap (the lowest one) through the sheet
    /// handle's keyboard contract (Home = the lowest snap, and Home never dismisses), so an
    /// editor capture keeps the diagram visible above the sheet — the mockup's bottom-only
    /// sheet, not a half-open one covering the canvas.
    /// </summary>
    private static async Task SetMobileSheetPeekAsync(IPage page)
    {
        var sheet = page.Locator(".tm-editor-shell__sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await sheet.Locator(".tm-sheet__handle[role='slider']").FocusAsync();
        await page.Keyboard.PressAsync("Home");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.tm-editor-shell__sheet')?.getAttribute('data-snap-index') === '0'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    /// <summary>
    /// Parks the editor right under the demo's fixed 4rem mobile header for the capture: hides
    /// the demo top bar, the page intro above the editor section and the section's own
    /// heading/buttons row (capture framing only — the DOM change lives and dies with the test
    /// context) and scrolls so the whole editor fills the viewport — bottom-aligned when the
    /// phone is too short for it, which keeps the action bar inside the frame.
    /// </summary>
    private static async Task FrameEditorInViewportAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                const editor = document.querySelector('.tm-diagram-editor');
                if (!editor) return;
                const topbar = document.querySelector('main > div.sticky');
                if (topbar) topbar.style.display = 'none';
                const host = editor.parentElement;
                const section = host ? host.parentElement : null;
                let intro = host ? host.previousElementSibling : null;
                while (intro) {
                    intro.style.display = 'none';
                    intro = intro.previousElementSibling;
                }
                let outer = section ? section.previousElementSibling : null;
                while (outer) {
                    outer.style.display = 'none';
                    outer = outer.previousElementSibling;
                }
                window.scrollTo({ top: 0, behavior: 'instant' });
                const box = editor.getBoundingClientRect();
                const overflow = box.top + editor.offsetHeight - window.innerHeight;
                if (overflow > 0) {
                    window.scrollTo({ top: overflow, behavior: 'instant' });
                }
            }
            """);
        await page.WaitForTimeoutAsync(150);
    }

    /// <summary>
    /// Reframes the canvas for the mobile editor capture: zooms the loaded content out and
    /// parks it in the free band between the floating chrome and the sheet — below the
    /// top-right minimap, right of the zoom pill, above the peeked sheet — the mockup's
    /// readable-diagram framing. The band is bounded by the live SHEET PANEL edge (the sheet
    /// root spans the whole stage) and clamped to the width left of the pill. The zoom label
    /// and the minimap viewport are synced the same way the editor's own fit command syncs them.
    /// </summary>
    private static async Task FrameDiagramAboveSheetAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const ed = window.tmDiagramEditor;
                const inst = ed.instances.get(canvas.id);
                const box = inst.svg.getBoundingClientRect();
                let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
                inst.scenePane.querySelectorAll('g.tm-diagram-node[data-node-id]').forEach(el => {
                    const r = ed._nodeRect(inst, el.getAttribute('data-node-id'));
                    if (!r) return;
                    minX = Math.min(minX, r.x); minY = Math.min(minY, r.y);
                    maxX = Math.max(maxX, r.x + r.w); maxY = Math.max(maxY, r.y + r.h);
                });
                if (!isFinite(minX)) return;
                const card = document.querySelector('.tm-diagram-editor__minimap-overlay');
                const cardBottom = card ? card.getBoundingClientRect().bottom - box.top + 8 : box.height * 0.4;
                const pill = document.querySelector('.tm-diagram-editor__zoom-overlay');
                const pillRight = pill ? pill.getBoundingClientRect().right - box.left + 8 : 0;
                const sheet = document.querySelector('.tm-editor-shell__sheet .tm-drawer__panel')
                    ?? document.querySelector('.tm-editor-shell__sheet');
                const sheetTop = sheet ? sheet.getBoundingClientRect().top - box.top : box.height;
                // Zoom out until the fitted band fits the strip between the minimap, the pill
                // and the sheet (at most 50%, so the pair stays readable).
                const bandW = maxX - minX, bandH = maxY - minY;
                const availH = Math.max(40, sheetTop - cardBottom - 12);
                const availW = Math.max(40, box.width - pillRight - 8);
                const scale = Math.min(0.5, (availH * 0.9) / bandH, availW / bandW);
                const left = Math.max((box.width - bandW * scale) / 2, pillRight);
                const top = Math.min(Math.max(cardBottom, sheetTop * 0.4), sheetTop - bandH * scale - 8);
                const w = box.width / scale, h = box.height / scale;
                const x = minX - left / scale;
                const y = minY - top / scale;
                ed._setViewBox(inst, x, y, w, h);
                inst.dotNetRef.invokeMethodAsync('OnViewportChanged', x, y, w, h);
                inst.dotNetRef.invokeMethodAsync('OnZoomChanged', inst.scale);
            }
            """);
        await page.WaitForTimeoutAsync(250);
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
