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
        CollectionAssert.AreEquivalent(new[] { "zoom", "view", "search", "export" }, groups,
            "the preview must render exactly the viewer toolbar groups");

        // A host-pinned read-only preview can no longer be unlocked from the UI (DI1).
        Assert.AreEqual(0, await preview.Locator("[data-tm-group='readonly']").CountAsync(),
            "the preview must not offer the read-only unlock toggle");
        Assert.AreEqual(0, await preview.Locator("button[aria-label='Switch to edit mode']").CountAsync());
        Assert.AreEqual(1, await preview.Locator(".tm-diagram-editor--readonly").CountAsync(),
            "the preview editor root carries the read-only state class");
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
