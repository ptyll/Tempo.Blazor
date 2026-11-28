using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 8 (DE-2.7 + the mobile parts of DE-1.1 / DE-1.2): the "after" screenshots of the editor at 1024 (touch), 390
/// and 320 - the idle editor, the panels as sheets, the contextual sheets and the action bar. Gated by <c>TM_WRITE_BASELINES</c>; the PNGs land in
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/document-editor-redesign/phase-8/</c> (override the folder name with
/// <c>TM_DOCUMENT_EDITOR_BASELINE_DIR</c>). The specs that assert behaviour are <see cref="DocumentEditorPanelsResponsiveE2ETests"/>.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase8CaptureE2ETests : BaselineGeneratorTestBase
{
    private static readonly (string Name, int Width, int Height, bool Touch)[] Viewports =
    [
        ("1440", 1440, 900, false),
        ("1180-touch", 1180, 820, true),
        ("1024-touch", 1024, 768, true),
        ("390", 390, 844, true),
        ("320", 320, 640, true),
    ];

    private static string OutputDirectory
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_DOCUMENT_EDITOR_BASELINE_DIR");
            var name = string.IsNullOrWhiteSpace(target) ? "phase-8" : target.Trim();
            var directory = Path.Combine(
                FindRepositoryRootDirectory(),
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-redesign", name);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("The idle editor at every width")]
    public async Task CaptureIdleEditor()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenHistoryAsync(width, height, touch, RevisionsDocumentId);
            await Shot(page, $"editor-{name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The inspector panels as the side sheet of a tablet and the sheets of a phone: comments, revisions, versions, outline, pages")]
    public async Task CapturePanels()
    {
        foreach (var (name, width, height, touch) in Viewports.Skip(1))
        {
            var page = await OpenHistoryAsync(width, height, touch, RevisionsDocumentId);
            foreach (var tab in new[] { "comments", "revisions", "versions", "outline", "pages", "properties" })
            {
                await OpenInspectorTabAsync(page, tab);
                await Shot(page, $"panel-{tab}-{name}.png");
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Find with results, the replace section open")]
    public async Task CaptureFind()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenHistoryAsync(width, height, touch, RevisionsDocumentId);
            await page.GetByTestId("document-header-search").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByTestId("document-find-input").FillAsync("zzzz");
            await page.WaitForTimeoutAsync(500);
            await Shot(page, $"find-empty-{name}.png");
            await page.GetByTestId("document-find-input").FillAsync("text");
            await Assertions.Expect(page.GetByTestId("document-find-result").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"find-{name}.png");
            await page.GetByTestId("document-find-replace-toggle").ClickAsync();
            await page.GetByTestId("document-replace-input").FillAsync("The");
            await Shot(page, $"find-replace-{name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The phone chrome: More sheet of the action bar, the ribbon More, the Insert tab, the print preview")]
    public async Task CapturePhoneChrome()
    {
        foreach (var (name, width, height, touch) in Viewports.Skip(3))
        {
            var page = await OpenHistoryAsync(width, height, touch, RevisionsDocumentId);
            await page.GetByTestId("document-mobile-action-bar").Locator(".tm-mobile-action-bar__more").ClickAsync();
            await Shot(page, $"action-bar-more-{name}.png");
            await page.Keyboard.PressAsync("Escape");
            await page.Locator("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").ClickAsync();
            await Shot(page, $"ribbon-more-{name}.png");
            await page.Keyboard.PressAsync("Escape");
            await page.GetByTestId("document-ribbon-tab-insert").ClickAsync();
            await Shot(page, $"ribbon-insert-{name}.png");
            await page.GetByTestId("document-mobile-print-preview").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-print-preview-actions")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Shot(page, $"print-preview-{name}.png");
            await page.Context.CloseAsync();
        }
    }

    private const string ImageSelector = "[data-canvas-object][data-object-id='contract-inline-image']";
    private const string TableCellSelector = "[data-canvas-table-cell][data-cell-id='contract-pricing-table-r1-item']";

    // The pages are virtualized and the document height settles while they mount, so the page is scrolled in small steps - down to the object, back up
    // when it was passed - and the object is clicked only when it stands in the middle band of the viewport.
    private static async Task<(double X, double Y)> ScrollToObjectAsync(IPage page, string selector, int width, int height)
    {
        var direction = 1;
        var trace = new List<string>();
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var point = await page.EvaluateAsync<double[]?>(
                "selector => { const e = document.querySelector(selector); if (!e) return null; const r = e.getBoundingClientRect(); return [r.left + r.width / 2, r.top + r.height / 2]; }",
                selector);
            if (point is { Length: 2 } && point[1] > 150 && point[1] < height - 220 && point[0] > 0 && point[0] < width)
            {
                return (point[0], point[1]);
            }

            if (point is { Length: 2 })
            {
                direction = point[1] <= 150 ? -1 : 1;
            }

            if (attempt % 10 == 0)
            {
                trace.Add($"{attempt}:{(point is null ? "null" : $"{point[0]:0},{point[1]:0}")}/y{await page.EvaluateAsync<double>("() => scrollY"):0}");
            }

            var step = point is null ? 220 : Math.Min(220, Math.Max(40, (int)Math.Abs(point[1] - height / 2.0)));
            await page.EvaluateAsync("amount => window.scrollBy(0, amount)", direction * step);
            await page.WaitForTimeoutAsync(180);
        }

        Assert.Fail($"'{selector}' did not scroll into the viewport. {string.Join(" ", trace)}");
        return default;
    }

    [TestMethod]
    [Description("The image and the table: the inspector of a tablet, in the Properties tab of the side sheet")]
    public async Task CaptureObjectSheetsTablet()
    {
        foreach (var (name, width, height, touch) in Viewports.Skip(1).Take(2))
        {
            foreach (var kind in new[] { "image", "table" })
            {
                var page = await OpenContractAsync(width, height, touch);
                var point = await ScrollToObjectAsync(page, kind == "image" ? ImageSelector : TableCellSelector, width, height);
                await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
                await page.WaitForTimeoutAsync(600);
                var panel = page.GetByTestId(kind == "image" ? "document-image-inspector" : "document-table-properties-panel");
                if (!await panel.IsVisibleAsync())
                {
                    // A table is offered through its command (the Properties tab of the sheet stays closed until it is asked for).
                    await DocumentEditorRedesignPhase2CaptureE2ETests.RunPaletteCommandAsync(page, kind == "image" ? "imageProperties" : "tableProperties");
                }

                await Assertions.Expect(panel).ToBeVisibleAsync(new() { Timeout = 10_000 });
                await Shot(page, $"{kind}-{name}.png");
                await page.Context.CloseAsync();
            }
        }
    }

    [TestMethod]
    [Description("The short bottom sheet of a phone for an image and a table: the object is selected with a tap, then Properties opens the sheet")]
    public async Task CaptureObjectSheetsPhone()
    {
        foreach (var (name, width, height, touch) in Viewports.Skip(3))
        {
            // The first image / table of a document that opens with one: scrolling a virtualized contract to its inline image is not deterministic on a phone.
            foreach (var (kind, documentId, selector) in new[] { ("image", "exhibits-demo", "[data-canvas-object]"), ("table", "table-demo", "[data-canvas-table-cell]") })
            {
                var page = await OpenContractAsync(width, height, touch, documentId);
                var point = await ScrollToObjectAsync(page, selector, width, height);
                await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
                await page.WaitForTimeoutAsync(600);
                await Shot(page, $"{kind}-selected-{name}.png");
                await page.GetByTestId("document-mobile-action-bar").Locator(".tm-mobile-action-bar__more").ClickAsync();
                await page.GetByTestId("document-mobile-properties").ClickAsync();
                await Assertions.Expect(page.GetByTestId("document-context-sheet")).ToBeVisibleAsync(new() { Timeout = 10_000 });
                await page.WaitForTimeoutAsync(600);
                await Shot(page, $"{kind}-sheet-{name}.png");
                await page.Context.CloseAsync();
            }
        }
    }

    [TestMethod]
    [Description("The header kebab of a phone with the host's actions, and the debug modal as a fullscreen sheet")]
    public async Task CaptureKebabAndDebugModal()
    {
        foreach (var (name, width, height, touch) in Viewports.Skip(3))
        {
            var page = await OpenContractAsync(width, height, touch);
            await page.GetByTestId("document-editor-header-slots").CheckAsync();
            await Assertions.Expect(page.GetByTestId("document-header-kebab")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByTestId("document-header-kebab").ClickAsync();
            await Shot(page, $"header-kebab-{name}.png");
            await page.Keyboard.PressAsync("Escape");
            await page.Context.CloseAsync();

            var debugPage = await OpenHistoryAsync(width, height, touch, RevisionsDocumentId);
            await ClickPopupItemAsync(debugPage, "document-ribbon-tab-view", "");
            await ClickRibbonAsync(debugPage, "document-view-json");
            await Assertions.Expect(debugPage.GetByTestId("document-json-debug-modal")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(debugPage, $"debug-modal-{name}.png");
            await debugPage.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The diff of a phone: Unified with the Older / Newer / Differences switch")]
    public async Task CaptureDiff()
    {
        foreach (var (name, width, height, touch) in Viewports.Skip(3))
        {
            await DocumentEditorE2EReset.ResetAsync();
            // The two versions are made on a desktop-sized page (typing into the canvas), then the viewport shrinks to the phone.
            var page = await OpenContractAsync(1440, 900, false);
            await DocumentEditorRedesignPhase7E2ETests.CreateTwoVersionsAsync(page);
            await page.SetViewportSizeAsync(width, height);
            await page.WaitForFunctionAsync("() => document.querySelector('.tm-document-editor')?.dataset.layout === 'mobile'");
            await page.WaitForTimeoutAsync(500);
            await OpenInspectorTabAsync(page, "versions");
            await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Draft").GetByTestId("document-version-diff-base").ClickAsync();
            await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Final").GetByTestId("document-version-diff-compare").ClickAsync();
            await page.GetByTestId("document-version-show-diff").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await page.WaitForTimeoutAsync(600);
            await Shot(page, $"diff-{name}.png");
            await page.GetByTestId("document-diff-segments").GetByRole(AriaRole.Radio).Nth(0).ClickAsync();
            await page.WaitForTimeoutAsync(300);
            await Shot(page, $"diff-older-{name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The version dialog as a sheet of a phone")]
    public async Task CaptureDialogs()
    {
        foreach (var (name, width, height, touch) in Viewports.Skip(3))
        {
            await DocumentEditorE2EReset.ResetAsync();
            var page = await OpenContractAsync(width, height, touch);
            await OpenInspectorTabAsync(page, "versions");
            await page.GetByTestId("document-version-create-open").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-version-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"version-dialog-{name}.png");
            await page.GetByTestId("document-version-label").FillAsync("1.9");
            await page.GetByTestId("document-version-label").PressAsync("Enter");
            await Assertions.Expect(page.GetByTestId("document-version-item").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"version-panel-{name}.png");
            await page.Context.CloseAsync();
        }
    }

    private async Task<IPage> OpenContractAsync(int width, int height, bool touch, string documentId = "contract-demo")
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId={documentId}", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return page;
    }

    private async Task<IPage> OpenHistoryAsync(int width, int height, bool touch, string documentId)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId={documentId}", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return page;
    }

    private static async Task Shot(IPage page, string fileName)
    {
        // The demo page has a long header of its own: the shot starts at the editor, the way a host that gives the editor the screen shows it.
        // (the demo's top bar is sticky and 66 px tall: the editor is scrolled to just below it, so its header is in the shot)
        await page.EvaluateAsync("() => { document.querySelector('.tm-document-editor')?.scrollIntoView({ block: 'start', behavior: 'instant' }); window.scrollBy({ top: -72, behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDirectory, fileName), Type = ScreenshotType.Png, FullPage = false });
    }
}
