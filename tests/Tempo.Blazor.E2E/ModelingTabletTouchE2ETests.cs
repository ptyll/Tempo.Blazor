using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Tablet + touch contract of the modeling editor (Modeling redesign plan, phase 6): in the
/// editor container's 768–1199px band (the demo page hides its sidebar from 1024px of viewport
/// up, so a 1024×768 and a 1180×820 touch window land there) the workspace shows the preview
/// plus AT MOST ONE side panel — the tree drawer / the inspector sheet of the shell's overlay
/// presentation — driven by the "Panels" tabs, with 44px coarse-pointer targets, the non-drag
/// "Insert into preview" action and the ~90% open-in-editor dialog whose body hands the diagram
/// editor the tablet layout. The 1440 desktop regression keeps the full dock.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingTabletTouchE2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("1024x768 touch: the tablet band shows the preview plus at most one side panel, without horizontal overflow")]
    public async Task Tablet_1024_AtMostOneSidePanel_NoHorizontalOverflow()
    {
        var page = await OpenLoadedTabletPageAsync(1024, 768);
        try
        {
            await AssertTabletBandAsync(page);
            Assert.IsTrue(await SidePanelsVisibleAsync(page) <= 1,
                "the tablet layout shows the preview plus at most ONE side panel (the sheets start closed)");
            Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), "the tablet layout must not overflow horizontally");

            // The tablet canvas floor (review fix): the touch canvas keeps a usable block size.
            var canvasHeight = await CanvasHeightAsync(page);
            Assert.IsTrue(canvasHeight >= 240, $".tm-diagram-canvas must reach 240px on a tablet (measured {canvasHeight})");

            await SaveStableScreenshotAsync(page, "modeling-tablet-1024x768.png");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("1024x768 touch: tapping a tree node opens the inspector sheet, which closes again")]
    public async Task Tablet_1024_TapNode_OpensInspectorSheet_ThenCloses()
    {
        var page = await OpenLoadedTabletPageAsync(1024, 768);
        try
        {
            // Open the tree drawer (the tree starts closed — the inspector wins the initial conflict).
            await page.Locator("[data-testid='modeling-panel-tab-tree']").TapAsync();
            await page.Locator("[data-testid='modeling-tree-drawer']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            // A finger tap on a tree node: the selection is an editing intent — the inspector
            // sheet opens and the drawer yields (one sheet at a time).
            await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").TapAsync();
            await page.WaitForFunctionAsync(
                "() => document.querySelector(\"[data-testid='modeling-editor']\")?.getAttribute('data-selected-element-id') === 'bpmn-validate-order'",
                null,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
            await page.Locator("[data-testid='modeling-inspector-sheet']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            Assert.AreEqual(0, await page.Locator("[data-testid='modeling-tree-drawer']").CountAsync(),
                "the drawer yields to the inspector sheet");

            // The sheet's × closes it: the preview is alone again.
            await page.Locator("[data-testid='modeling-sheet-close']").TapAsync();
            await page.WaitForFunctionAsync(
                "() => !document.querySelector(\"[data-testid='modeling-inspector-sheet']\")",
                null,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
            Assert.IsTrue(await SidePanelsVisibleAsync(page) == 0, "no side panel after the close");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("1024x768 touch: the 'Insert into preview' tap reuses a tree element through the existing drop path")]
    public async Task Tablet_1024_InsertIntoPreview_Tap()
    {
        var page = await OpenLoadedTabletPageAsync(1024, 768);
        try
        {
            var before = await ReadNumericAttributeAsync(page, "[data-testid='modeling-editor']", "data-diagram-node-count");

            await page.Locator("[data-testid='modeling-panel-tab-tree']").TapAsync();
            await page.Locator("[data-testid='modeling-tree-drawer']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await page.Locator("[data-testid='modeling-tree-insert-bpmn-validate-order']").TapAsync();

            await WaitForPreviewNodeCountAtLeastAsync(page, before + 1);

            // The reused element selects itself in the inspector (the drop contract).
            await page.WaitForFunctionAsync(
                "() => document.querySelector(\"[data-testid='modeling-editor']\")?.getAttribute('data-selected-element-id') === 'bpmn-validate-order'",
                null,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("1024x768 touch: the issues are the fourth panel side sheet (no dock row) under the one-open rule")]
    public async Task Tablet_1024_IssuesSheet_OpensUnderOneOpenRule()
    {
        var page = await OpenLoadedTabletPageAsync(1024, 768, scenario: "issues-mixed");
        try
        {
            // Final UX review (M2): the tablet band has NO dock under the canvas — the issues
            // are the FOURTH panel, a side sheet opened from the panel tabs. Opening it closes
            // the drawer/sheet (the one-open rule) and the canvas keeps its floor.
            Assert.AreEqual(0, await page.Locator("[data-testid='modeling-issues-toggle']").CountAsync(),
                "the tablet band has no issues dock toggle under the canvas");

            await page.Locator("[data-testid='modeling-panel-tab-issues']").TapAsync();
            await page.WaitForFunctionAsync(
                "() => !!document.querySelector(\"[data-testid='modeling-issues-sheet']\")",
                null,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
            Assert.AreEqual("true", await page.Locator("[data-testid='modeling-panel-tab-issues']")
                .GetAttributeAsync("aria-expanded"), "the fourth tab is an aria-expanded disclosure");

            await page.Locator("[data-testid='modeling-panel-tab-inspector']").TapAsync();
            await page.WaitForFunctionAsync(
                "() => !document.querySelector(\"[data-testid='modeling-issues-sheet']\")",
                null,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
            Assert.AreEqual(1, await page.Locator("[data-testid='modeling-inspector-sheet']").CountAsync(),
                "opening the inspector closed the issues sheet (one open at a time)");

            Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), "the issues sheet must not introduce overflow");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("1024x768 touch: the open-in-editor dialog stays a ~90% TmModal whose body resolves the diagram editor's tablet layout, and a finger edits a shape inside it")]
    public async Task Tablet_1024_OpenInEditorDialog_TouchShapeEdit()
    {
        var page = await OpenLoadedTabletPageAsync(1024, 768);
        try
        {
            // The sheets start closed: the trigger is reachable without a close-first workaround.
            await page.Locator("[data-testid='modeling-open-in-editor-button']").TapAsync();
            await page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-modal")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

            // The dialog body decides: the embedded diagram editor reports its tablet layout.
            var embeddedLayout = await page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor[data-editor-layout]")
                .First.GetAttributeAsync("data-editor-layout");
            Assert.AreEqual("tablet", embeddedLayout, "the dialog body on a tablet hands the editor the tablet layout");

            // Escape consumers first: the diagram canvas' surfaces close before the dialog does.
            Assert.AreEqual(0, await page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor .tm-modal-container").CountAsync(),
                "the tablet diagram presentation must not stack a modal inside the dialog");

            // A finger edits a shape: tap a node to select it, then drag its SE resize handle.
            var nodeId = await FirstNodeIdAsync(page);
            Assert.IsNotNull(nodeId, "the dialog body must render diagram nodes");
            await FingerSelectNodeAsync(page, nodeId!);
            var before = await GetNodeWidthAsync(page, nodeId!);
            await FingerResizeNodeAsync(page, nodeId!, "se", 60);
            var after = await GetNodeWidthAsync(page, nodeId!);
            Assert.IsTrue(after > before, $"the finger resize must grow the shape (before {before}, after {after})");

            // The edit propagates live (G8): the dialog stays open and the preview behind counts the same node set.
            Assert.IsNotNull(await page.QuerySelectorAsync("[data-testid='modeling-open-diagram-editor']"),
                "the edit did not close the dialog");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("1024x768 touch (Fáze 9): the open-editor dialog's floating minimap starts COLLAPSED and its reopen button expands it")]
    public async Task Tablet_1024_OpenInEditorDialog_MinimapStartsCollapsedAndReopens()
    {
        var page = await OpenLoadedTabletPageAsync(1024, 768);
        try
        {
            await page.Locator("[data-testid='modeling-open-in-editor-button']").TapAsync();
            var dialog = page.Locator("[data-testid='modeling-open-diagram-editor']");
            await dialog.Locator(".tm-modal")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            await page.WaitForFunctionAsync(
                "() => document.querySelector(\"[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor[data-editor-layout]\")?.getAttribute('data-editor-layout') === 'tablet'",
                null, new PageWaitForFunctionOptions { Timeout = 20_000 });

            // Fáze 9: the dialog seeds the DE's MinimapInitiallyCollapsed with the editor's
            // tablet mode — the overlay (which covered the touch canvas) starts collapsed and
            // the floating reopen button expands it in place.
            Assert.AreEqual(0, await dialog.Locator(".tm-diagram-editor__minimap-overlay").CountAsync(),
                "the tablet dialog's minimap overlay starts collapsed");
            var reopen = dialog.Locator("[data-testid='diagram-editor-minimap-reopen']").First;
            await reopen.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await reopen.TapAsync(new LocatorTapOptions { Timeout = 10_000 });
            await dialog.Locator(".tm-diagram-editor__minimap-overlay")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("1024x768 touch: the coarse-pointer targets measure at least 44px tall (tabs, tree rows, selects, insert action)")]
    public async Task Tablet_1024_TouchTargets_Measure44px()
    {
        var page = await OpenLoadedTabletPageAsync(1024, 768);
        try
        {
            var selectors = new[]
            {
                "[data-testid='modeling-panel-tab-tree']",
                "[data-testid='modeling-panel-tab-preview']",
                "[data-testid='modeling-panel-tab-inspector']",
                "[data-testid='modeling-provider-select']",
                "[data-testid='modeling-notation-select']",
            };

            foreach (var selector in selectors)
            {
                var height = await page.Locator(selector).First.EvaluateAsync<double>("el => el.getBoundingClientRect().height");
                Assert.IsTrue(height >= 44, $"{selector} must reach the 44px coarse-pointer target (measured {height})");
            }

            // The tree rows and the insert action live in the drawer.
            await page.Locator("[data-testid='modeling-panel-tab-tree']").TapAsync();
            await page.Locator("[data-testid='modeling-tree-drawer']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            var nodeHeight = await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']")
                .EvaluateAsync<double>("el => el.getBoundingClientRect().height");
            Assert.IsTrue(nodeHeight >= 44, $"tree rows must reach the 44px target (measured {nodeHeight})");

            var insertHeight = await page.Locator("[data-testid='modeling-tree-insert-bpmn-validate-order']")
                .EvaluateAsync<double>("el => el.getBoundingClientRect().height");
            Assert.IsTrue(insertHeight >= 44, $"the non-drag action must reach the 44px target (measured {insertHeight})");

            // Fáze 6 review: the insert action is a full 44x44 target on a coarse pointer.
            var insertWidth = await page.Locator("[data-testid='modeling-tree-insert-bpmn-validate-order']")
                .EvaluateAsync<double>("el => el.getBoundingClientRect().width");
            Assert.IsTrue(insertWidth >= 44, $"the non-drag action must be 44px wide too (measured {insertWidth})");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("1180x820 touch: the same one-side-panel contract holds on the wider tablet band")]
    public async Task Tablet_1180_SameOneSheetContract()
    {
        var page = await OpenLoadedTabletPageAsync(1180, 820);
        try
        {
            await AssertTabletBandAsync(page);
            Assert.IsTrue(await SidePanelsVisibleAsync(page) <= 1, "at most one side panel at 1180 too");

            await page.Locator("[data-testid='modeling-panel-tab-tree']").TapAsync();
            await page.WaitForFunctionAsync(
                "() => !!document.querySelector(\"[data-testid='modeling-tree-drawer']\")",
                null,
                new PageWaitForFunctionOptions { Timeout = 10_000 });
            Assert.AreEqual(0, await page.Locator("[data-testid='modeling-inspector-sheet']").CountAsync(),
                "opening the drawer closed the sheet at 1180");

            // The tablet canvas floor (review fix) holds on the wider band too.
            var canvasHeight = await CanvasHeightAsync(page);
            Assert.IsTrue(canvasHeight >= 240, $".tm-diagram-canvas must reach 240px on a tablet (measured {canvasHeight})");

            await SaveStableScreenshotAsync(page, "modeling-tablet-1180x820.png");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("1440 regression: the desktop keeps all three docked regions and no panel tabs")]
    public async Task Desktop_1440_KeepsTheFullDock()
    {
        var page = await OpenLoadedTabletPageAsync(1440, 900);
        try
        {
            await page.WaitForFunctionAsync(
                "() => document.querySelector(\".tm-modeling-editor .tm-layout\")?.getAttribute('data-layout') === 'desktop'",
                null,
                new PageWaitForFunctionOptions { Timeout = 15_000 });

            Assert.AreEqual(0, await page.Locator("[data-testid='modeling-panel-tabs']").CountAsync(),
                "the desktop renders no visibility tabs (all three regions are docked)");
            var docked = await DockedSidePanelsVisibleAsync(page);
            Assert.IsTrue(docked == 2, $"the desktop dock shows both side columns (measured {docked} docked side panels)");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<IPage> OpenLoadedTabletPageAsync(int width, int height, string? scenario = null)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            IsMobile = false,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en'); localStorage.removeItem('tm-demo-theme');");

        var page = await context.NewPageAsync();
        var query = scenario is null ? $"?notation=bpmn&viewpoint=overview" : $"?notation=bpmn&viewpoint=overview&scenario={scenario}";
        await GotoAppReadyAsync(page, $"{BaseUrl}{ModelingEditorUrl}{query}");
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node').length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 20_000 });
        return page;
    }

    private static Task AssertTabletBandAsync(IPage page) => page.WaitForFunctionAsync(
        "() => document.querySelector(\".tm-modeling-editor .tm-layout\")?.getAttribute('data-layout') === 'tablet'",
        null,
        new PageWaitForFunctionOptions { Timeout = 15_000 });

    private static Task<int> SidePanelsVisibleAsync(IPage page) => page.EvaluateAsync<int>(
        """
        () => {
            const selectors = [
                "[data-testid='modeling-tree-drawer']",
                "[data-testid='modeling-inspector-sheet']"
            ];
            return selectors
                .map(selector => document.querySelector(selector))
                .filter(element => element && element.getBoundingClientRect().width > 0).length;
        }
        """);

    private static Task<int> DockedSidePanelsVisibleAsync(IPage page) => page.EvaluateAsync<int>(
        """
        () => {
            const selectors = [
                "[data-testid='modeling-model-tree-panel']",
                "[data-testid='modeling-inspector-panel']"
            ];
            return selectors
                .map(selector => document.querySelector(selector))
                .filter(element => element && element.getBoundingClientRect().width > 0).length;
        }
        """);

    private static Task<int> ReadNumericAttributeAsync(IPage page, string selector, string attributeName) => page.EvaluateAsync<int>(
        "([selector, attributeName]) => Number(document.querySelector(selector)?.getAttribute(attributeName) ?? '0')",
        new[] { selector, attributeName });

    /// <summary>The preview canvas' rendered block size (the review fix's tablet floor contract).</summary>
    private static Task<double> CanvasHeightAsync(IPage page) => page.EvaluateAsync<double>(
        "() => document.querySelector('.tm-modeling-diagram-preview .tm-diagram-canvas')?.getBoundingClientRect().height ?? 0");

    private static Task WaitForPreviewNodeCountAtLeastAsync(IPage page, int minimumCount) => page.WaitForFunctionAsync(
        """
        minimumCount => {
            const preview = document.querySelector("[data-testid='modeling-diagram-preview']");
            return Number(preview?.getAttribute('data-node-count') ?? '0') >= minimumCount;
        }
        """,
        minimumCount,
        new PageWaitForFunctionOptions { Timeout = 15_000 });

    private static Task<bool> HasNoHorizontalOverflowAsync(IPage page) => page.EvaluateAsync<bool>(
        """
        () => {
            const doc = document.documentElement;
            const body = document.body;
            return Math.max(doc.scrollWidth, body.scrollWidth) <= window.innerWidth + 2;
        }
        """);

    private static Task<string?> FirstNodeIdAsync(IPage page) => page.EvaluateAsync<string?>(
        "() => document.querySelector(\"[data-testid='modeling-open-diagram-editor'] g.tm-diagram-node[data-node-id]\")?.getAttribute('data-node-id') ?? null");

    private static async Task<double> GetNodeWidthAsync(IPage page, string nodeId)
    {
        var width = await page.EvaluateAsync<double?>(
            "(id) => { const el = document.querySelector(`[data-testid='modeling-open-diagram-editor'] g.tm-diagram-node[data-node-id='${id}']`); return el && el.hasAttribute('data-w') ? parseFloat(el.getAttribute('data-w')) : null; }",
            nodeId);
        Assert.IsTrue(width.HasValue, $"node {nodeId} must exist inside the dialog body");
        return width!.Value;
    }

    /// <summary>A finger tap selects the node through the canvas' own pointer sessions.</summary>
    private static async Task FingerSelectNodeAsync(IPage page, string nodeId)
    {
        await page.EvaluateAsync(
            """
            async (id) => {
                const node = document.querySelector(`[data-testid='modeling-open-diagram-editor'] g.tm-diagram-node[data-node-id='${id}']`);
                if (!node) throw new Error('node not found');
                const r = node.getBoundingClientRect();
                const opts = (x, y) => ({
                    bubbles: true, cancelable: true, composed: true,
                    pointerId: 7, pointerType: 'touch', isPrimary: true,
                    button: 0, buttons: 1, clientX: x, clientY: y,
                });
                node.dispatchEvent(new PointerEvent('pointerdown', opts(r.left + r.width / 2, r.top + r.height / 2)));
                node.dispatchEvent(new PointerEvent('pointerup', { ...opts(r.left + r.width / 2, r.top + r.height / 2), buttons: 0 }));
            }
            """,
            nodeId);
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>A synthetic touch-pointer drag that starts on the node's SE resize hit rect
    /// (the decorator-pane hit area the DiagramEditor renders for a selected node).</summary>
    private static async Task FingerResizeNodeAsync(IPage page, string nodeId, string handle, double delta)
    {
        await page.EvaluateAsync(
            """
            async ([id, handle, delta]) => {
                const hit = document.querySelector(`[data-testid='modeling-open-diagram-editor'] g.tm-diagram-node-handles[data-node-id='${id}'] rect[data-resize-handle='${handle}']`);
                if (!hit) throw new Error('resize hit rect not found — the node must be selected first');
                const r = hit.getBoundingClientRect();
                const startX = r.left + r.width / 2, startY = r.top + r.height / 2;
                const opts = (x, y) => ({
                    bubbles: true, cancelable: true, composed: true,
                    pointerId: 7, pointerType: 'touch', isPrimary: true,
                    button: 0, buttons: 1, clientX: x, clientY: y,
                });
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

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingTabletTouchE2ETests).Assembly.Location)!,
            "..", "..", "..", "TestResults", "modeling-tablet");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions { Type = ScreenshotType.Png, FullPage = false });
        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the rendered modeling editor.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
