using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling diagram preview phase M12.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingDiagramPreviewM12E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Generate diagram renders the preview canvas with visible nodes")]
    public async Task DiagramPreview_GenerateShowsCanvasWithNodes()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.Locator("[data-testid='modeling-generate-diagram-button']").ClickAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);

        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview']");
        await ExpectVisibleAsync(page, "[data-testid='diagram-canvas']");
        Assert.IsTrue(await page.Locator("[data-testid='modeling-diagram-preview'] .tm-diagram-node").CountAsync() > 0);
    }

    [TestMethod]
    [Description("Viewer preview renders exactly the viewer toolbar groups and no unlock toggle")]
    public async Task DiagramPreview_UsesViewerToolbarWithoutReadOnlyToggle()
    {
        var page = await OpenLoadedModelingPageAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);

        // The toolbar contract lives on the TmToolbar, which the editor renders from the
        // desktop band up. At the default demo viewport the preview column sits in the mobile
        // band (phase 7: no command row, the viewer chrome is the zoom pill + the "⋯" menu),
        // so pin the preview editor to the desktop band like a full-bleed host. The editor
        // root IS the inline-size container, so the width goes on it directly.
        await PinPreviewEditorToDesktopAsync(page);

        var preview = page.Locator("[data-testid='modeling-diagram-preview']");

        var groups = await preview.Locator("[data-tm-group]")
            .EvaluateAllAsync<string[]>(
                """
                els => els
                    .map(el => el.getAttribute('data-tm-group'))
                    .filter(name => !!name)
                """);
        // Final UX review (M1): the preview keeps the ZOOM group only — the search + "⋯"
        // header-bar row is gone with its groups (the DE renders no empty header bar) and the
        // status bar keeps the zoom controls.
        CollectionAssert.AreEquivalent(new[] { "zoom" }, groups,
            "the preview must render exactly the zoom toolbar group");

        // A host-pinned read-only preview can no longer be unlocked from the UI (DI1).
        Assert.AreEqual(0, await preview.Locator("[data-tm-group='readonly']").CountAsync(),
            "the preview must not offer the read-only unlock toggle");
        Assert.AreEqual(0, await preview.Locator("button[aria-label='Switch to edit mode']").CountAsync());
        Assert.AreEqual(1, await preview.Locator(".tm-diagram-editor--readonly").CountAsync(),
            "the preview editor root carries the read-only state class");

        // F6: the preview hides the document title through the editor API (ShowTitle="false").
        Assert.AreEqual(0, await preview.Locator(".tm-diagram-editor__title").CountAsync(),
            "the preview renders no document-title input");
    }

    [TestMethod]
    [Description("Read-only preview (R1): clicking a node selects it for inspection; no resize handles render")]
    public async Task DiagramPreview_ClickNode_SelectsForInspection()
    {
        var page = await OpenLoadedModelingPageAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);
        await PinPreviewEditorToDesktopAsync(page);

        var nodeId = await page.EvaluateAsync<string?>(
            "() => document.querySelector(\"[data-testid='modeling-diagram-preview'] g.tm-diagram-node\")?.getAttribute('data-node-id')");
        Assert.IsNotNull(nodeId, "the preview must render a node");

        await page.Locator($"[data-testid='modeling-diagram-preview'] g.tm-diagram-node[data-node-id='{nodeId}']").First
            .ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual(1,
            await page.Locator($"[data-testid='modeling-diagram-preview'] g.tm-diagram-node--selected[data-node-id='{nodeId}']").CountAsync(),
            "a click in the read-only preview selects the node for the read-only properties panel");
        Assert.AreEqual(0,
            await page.Locator("[data-testid='modeling-diagram-preview'] .tm-diagram-resize-handle").CountAsync(),
            "the read-only preview never renders resize handles");
    }

    [TestMethod]
    [Description("Viewer preview offers no import, template or other editing toolbar commands")]
    public async Task DiagramPreview_HasNoImportOrTemplateCommands()
    {
        var page = await OpenLoadedModelingPageAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);
        await PinPreviewEditorToDesktopAsync(page);

        var preview = page.Locator("[data-testid='modeling-diagram-preview']");
        // Precondition: the command row really rendered — otherwise every absence check below is vacuous.
        Assert.AreEqual(1, await preview.Locator("[data-tm-group='zoom']").CountAsync(),
            "the desktop viewer toolbar must be rendered before asserting what it lacks");
        Assert.AreEqual(0, await preview.Locator("[data-tm-group='import']").CountAsync(),
            "nothing may be imported into a read-only preview");
        Assert.AreEqual(0, await preview.Locator("[data-tm-group='templates']").CountAsync());
        Assert.AreEqual(0, await preview.Locator("[data-tm-group='clipboard']").CountAsync());
        Assert.AreEqual(0, await preview.Locator("[data-tm-group='insert']").CountAsync());
        Assert.AreEqual(0, await preview.GetByText("Import", new() { Exact = true }).CountAsync(),
            "no Import command surface anywhere in the preview");
    }

    [TestMethod]
    [Description("Preview canvas supports zooming and panning interactions")]
    public async Task DiagramPreview_CanvasZoomsAndPans()
    {
        var page = await OpenLoadedModelingPageAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);

        var preview = page.Locator("[data-testid='modeling-diagram-preview']");
        var zoomLabel = preview.Locator(".tm-diagram-editor__zoom-label").First;
        var initialZoom = await zoomLabel.TextContentAsync();

        await preview.Locator("button[aria-label='Zoom in']").First.ClickAsync();
        await page.WaitForFunctionAsync(
            """
            ([selector, initialZoom]) => document.querySelector(selector)?.textContent?.trim() !== initialZoom
            """,
            new[] { "[data-testid='modeling-diagram-preview'] .tm-diagram-editor__zoom-label", initialZoom ?? string.Empty });

        var svg = preview.Locator("[data-testid='diagram-canvas'] svg").First;
        var initialViewBox = await svg.GetAttributeAsync("viewBox") ?? string.Empty;
        var box = await svg.BoundingBoxAsync();
        Assert.IsNotNull(box, "Diagram preview SVG should have a visible bounding box.");

        await page.Mouse.MoveAsync(box!.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync(new MouseDownOptions { Button = MouseButton.Middle });
        await page.Mouse.MoveAsync(box.X + box.Width / 2 + 90, box.Y + box.Height / 2 + 40);
        await page.Mouse.UpAsync(new MouseUpOptions { Button = MouseButton.Middle });

        await page.WaitForFunctionAsync(
            """
            ([selector, initialViewBox]) => document.querySelector(selector)?.getAttribute('viewBox') !== initialViewBox
            """,
            new[] { "[data-testid='modeling-diagram-preview'] [data-testid='diagram-canvas'] svg", initialViewBox },
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    [TestMethod]
    [Description("Open in editor displays the generated document in a full diagram editor surface")]
    public async Task DiagramPreview_OpenInEditorShowsFullEditor()
    {
        var page = await OpenLoadedModelingPageAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);

        var previewNodeCount = await GetPreviewNodeCountAsync(page);
        await page.Locator("[data-testid='modeling-open-in-editor-button']").ClickAsync();

        await ExpectVisibleAsync(page, "[data-testid='modeling-open-diagram-editor']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-open-diagram-editor'] [data-testid='diagram-editor']");
        Assert.IsTrue(await page.Locator("[data-testid='modeling-open-diagram-editor'] .tm-diagram-node").CountAsync() >= previewNodeCount);
    }

    [TestMethod]
    [Description("At 1024x768 the preview header actions wrap instead of clipping outside the preview panel")]
    public async Task DiagramPreview_ActionsStayInsidePreviewAt1024()
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
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);

        var previewBox = await page.Locator("[data-testid='modeling-diagram-preview']").BoundingBoxAsync();
        Assert.IsNotNull(previewBox, "the preview panel must be measurable");
        var previewRight = previewBox!.X + previewBox.Width;

        foreach (var testId in new[] { "modeling-open-in-editor-button", "modeling-generate-diagram-button" })
        {
            var box = await page.Locator($"[data-testid='{testId}']").BoundingBoxAsync();
            Assert.IsNotNull(box, $"{testId} must be rendered and measurable");
            Assert.IsTrue(
                box!.X + box.Width <= previewRight + 0.5,
                $"{testId} right edge ({box.X + box.Width:0.#}) must not clip past the preview right edge ({previewRight:0.#})");
        }
    }

    [TestMethod]
    [Description("Empty model generation renders an empty diagram canvas with a helpful hint")]
    public async Task DiagramPreview_EmptyModelShowsEmptyCanvasHint()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=empty-preview");

        await page.Locator("[data-testid='modeling-diagram-preview'][data-state='empty-diagram']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await ExpectVisibleAsync(page, "[data-testid='diagram-canvas']");
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview-empty-diagram-hint']");
        await ExpectHiddenAsync(page, "[data-testid='modeling-editor-loading']");
    }

    [TestMethod]
    [Description("Large generated model renders more than 100 nodes without freezing the canvas")]
    public async Task DiagramPreview_LargeModelLoadsAndCanvasRemainsResponsive()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=large-preview&notation=bpmn");

        await WaitForPreviewNodeCountAtLeastAsync(page, 100);
        Assert.IsTrue(await GetPreviewNodeCountAsync(page) >= 100);

        var canvas = page.Locator("[data-testid='modeling-diagram-preview'] [data-testid='diagram-canvas']").First;
        var box = await canvas.BoundingBoxAsync();
        Assert.IsNotNull(box, "Large preview canvas should remain visible and measurable.");
        await page.Mouse.MoveAsync(box!.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.WheelAsync(0, 600);
        await ExpectVisibleAsync(page, "[data-testid='modeling-diagram-preview'] [data-testid='diagram-canvas']");
    }

    [TestMethod]
    [Description("Repeated generation replaces the previous preview document without duplicating nodes")]
    public async Task DiagramPreview_RegenerationReplacesPreviousDiagram()
    {
        var page = await OpenLoadedModelingPageAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);
        var expectedNodeCount = await GetPreviewNodeCountAsync(page);

        await page.Locator("[data-testid='modeling-generate-diagram-button']").ClickAsync();
        await page.Locator("[data-testid='modeling-generate-diagram-button']").ClickAsync();

        await WaitForPreviewNodeCountAsync(page, expectedNodeCount);
        Assert.AreEqual(expectedNodeCount, await page.Locator("[data-testid='modeling-diagram-preview'] .tm-diagram-node").CountAsync());
    }

    [TestMethod]
    [Description("Dark mode keeps preview canvas, nodes and edges readable")]
    public async Task DiagramPreview_DarkModeRendersReadableCanvas()
    {
        var page = await OpenLoadedModelingPageAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);

        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.documentElement.classList.add('tm-dark', 'dark');
                document.body.classList.add('tm-dark', 'dark');
            }
            """);
        await page.WaitForTimeoutAsync(300);

        await page.Locator("[data-testid='modeling-diagram-preview'] .tm-diagram-node").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.Locator("[data-testid='modeling-diagram-preview'] .tm-diagram-edge-path").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
    }

    [TestMethod]
    [Description("Captures required M12 populated, dark and empty preview screenshots")]
    public async Task DiagramPreview_CapturesM12Screenshots()
    {
        var populated = await OpenLoadedModelingPageAsync();
        await WaitForPreviewNodeCountAtLeastAsync(populated, 1);
        await TakeScreenshotAsync(populated, "diagram-preview-populated-light");
        await SaveStableScreenshotAsync(populated, "diagram-preview-populated-light.png");

        await populated.EvaluateAsync(
            """
            () => {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.documentElement.classList.add('tm-dark', 'dark');
                document.body.classList.add('tm-dark', 'dark');
            }
            """);
        await populated.WaitForTimeoutAsync(300);
        await TakeScreenshotAsync(populated, "diagram-preview-populated-dark");
        await SaveStableScreenshotAsync(populated, "diagram-preview-populated-dark.png");

        var empty = await OpenLoadedModelingPageAsync("?scenario=empty-preview");
        await ExpectVisibleAsync(empty, "[data-testid='modeling-diagram-preview-empty-diagram-hint']");
        await TakeScreenshotAsync(empty, "diagram-preview-empty");
        await SaveStableScreenshotAsync(empty, "diagram-preview-empty.png");
    }

    // ── Fáze 15: the preview legend ──────────────────────────────────────────

    [TestMethod]
    [Description("The desktop preview renders the legend card listing the notation types the document uses, and it collapses")]
    public async Task PreviewLegend_ListsPresentTypesAndCollapses()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=bpmn");
        await page.Locator("[data-testid='modeling-generate-diagram-button']").ClickAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);

        var legend = page.Locator("[data-testid='modeling-preview-legend']");
        await Assertions.Expect(legend).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // The legend lists ONLY the notation types present in the generated document: the demo
        // BPMN model contributes three element types and the mapped sequence flow.
        var items = legend.Locator(".tm-modeling-diagram-legend__list > li");
        await Assertions.Expect(items).ToHaveCountAsync(4, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Assertions.Expect(legend).ToContainTextAsync("User task", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        await Assertions.Expect(legend).ToContainTextAsync("Start event", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        await Assertions.Expect(legend).ToContainTextAsync("Service task", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        await Assertions.Expect(legend).ToContainTextAsync("Sequence flow", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });
        // The types the document does not use (the demo's unmapped ArchiMate element) never appear.
        Assert.AreEqual(0, await legend.Locator(".tm-modeling-diagram-legend__list > li")
            .Filter(new LocatorFilterOptions { HasTextString = "Application component" }).CountAsync());

        // Decorative icons next to visible text.
        Assert.AreEqual("true", await items.First.Locator("svg").First.GetAttributeAsync("aria-hidden"));

        // Collapsible with aria-expanded.
        var toggle = legend.Locator(".tm-modeling-diagram-legend__toggle");
        Assert.AreEqual("true", await toggle.GetAttributeAsync("aria-expanded"));
        await toggle.ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });
        Assert.AreEqual(0, await legend.Locator(".tm-modeling-diagram-legend__list").CountAsync(),
            "a collapsed legend renders no list body");

        await TakeScreenshotAsync(page, "modeling-legend-1440");
    }

    [TestMethod]
    [Description("The legend card never steals preview canvas height (overlay in the canvas corner)")]
    public async Task PreviewLegend_KeepsCanvasHeight()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=bpmn");
        await page.Locator("[data-testid='modeling-generate-diagram-button']").ClickAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);
        await Assertions.Expect(page.Locator("[data-testid='modeling-preview-legend']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // The legend is an OVERLAY in the canvas corner — it never participates in the canvas
        // column's flow, so the canvas keeps the exact height the docked bands give it
        // (the issues dock present: the 240px floor) and the zoom pill/hint stay reachable.
        var overlay = await page.EvaluateAsync<bool>(
            """
            () => {
                const legend = document.querySelector('.tm-modeling-diagram-preview__legend');
                if (!legend) return false;
                return getComputedStyle(legend).position === 'absolute'
                    && legend.getBoundingClientRect().bottom <=
                       (document.querySelector('.tm-modeling-diagram-preview__canvas-shell')?.getBoundingClientRect().bottom ?? 0);
            }
            """);
        Assert.IsTrue(overlay, "the legend is absolutely positioned inside the canvas shell (never in flow)");

        var canvasHeight = await CanvasHeightAsync(page);
        Assert.IsTrue(canvasHeight >= 240,
            $"the preview canvas keeps its docked-band floor with the legend (measured {canvasHeight:0.#})");
    }

    [TestMethod]
    [Description("Review fix: the top-right legend rect never intersects the preview's zoom group")]
    public async Task PreviewLegend_DoesNotCoverZoomGroup()
    {
        var page = await OpenLoadedModelingPageAsync("?notation=bpmn");
        await page.Locator("[data-testid='modeling-generate-diagram-button']").ClickAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);
        // Pin the desktop band so the STATUS BAR's zoom group deterministically renders — the
        // compact mobile band replaces the row with the floating pill (a different surface).
        await PinPreviewEditorToDesktopAsync(page);
        await Assertions.Expect(page.Locator("[data-testid='modeling-preview-legend']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        Assert.AreEqual(1, await page.Locator("[data-testid='modeling-diagram-preview'] [data-tm-group='zoom']").CountAsync(),
            "the desktop status bar renders the zoom group before the intersection check");
        var intersects = await page.EvaluateAsync<bool>(
            """
            () => {
                const legend = document.querySelector('[data-testid="modeling-preview-legend"]');
                const zoom = document.querySelector("[data-testid='modeling-diagram-preview'] [data-tm-group='zoom']");
                if (!legend || !zoom) return false;
                const a = legend.getBoundingClientRect();
                const b = zoom.getBoundingClientRect();
                return !(a.right <= b.left || b.right <= a.left || a.bottom <= b.top || b.bottom <= a.top);
            }
            """);
        Assert.IsFalse(intersects,
            "the top-right legend card must not cover the status bar's zoom group");
    }

    private static Task<double> CanvasHeightAsync(IPage page) => page.EvaluateAsync<double>(
        """
        () => document.querySelector('.tm-modeling-diagram-preview__editor .tm-diagram-canvas')
            ?.getBoundingClientRect().height ?? 0
        """);

    private async Task<IPage> OpenLoadedModelingPageAsync(string query = "")
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }

    /// <summary>Pins the preview editor to the desktop band (the editor root is the inline-size container).</summary>
    private static async Task PinPreviewEditorToDesktopAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                const editor = document.querySelector("[data-testid='modeling-diagram-preview'] .tm-diagram-editor");
                if (!editor) throw new Error('preview editor not found');
                editor.style.width = '1280px';
                editor.style.maxWidth = '1280px';
            }
            """);
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector("[data-testid='modeling-diagram-preview'] .tm-diagram-editor")
                ?.getAttribute('data-editor-layout') === 'desktop'
            """,
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 5000)
    {
        await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    private static async Task ExpectHiddenAsync(IPage page, string selector)
    {
        await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
    }

    private static Task WaitForPreviewNodeCountAtLeastAsync(IPage page, int minimumCount) =>
        page.WaitForFunctionAsync(
            """
            minimumCount => {
                const preview = document.querySelector("[data-testid='modeling-diagram-preview']");
                return Number(preview?.getAttribute('data-node-count') ?? '0') >= minimumCount;
            }
            """,
            minimumCount,
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private static Task WaitForPreviewNodeCountAsync(IPage page, int expectedCount) =>
        page.WaitForFunctionAsync(
            """
            expectedCount => {
                const preview = document.querySelector("[data-testid='modeling-diagram-preview']");
                return Number(preview?.getAttribute('data-node-count') ?? '-1') === expectedCount;
            }
            """,
            expectedCount,
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private static async Task<int> GetPreviewNodeCountAsync(IPage page)
    {
        var value = await page.Locator("[data-testid='modeling-diagram-preview']").GetAttributeAsync("data-node-count");
        return int.TryParse(value, out var count) ? count : 0;
    }

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingDiagramPreviewM12E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m12");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the rendered modeling diagram preview.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
