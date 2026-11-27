using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 6 (DE-2.2 .. DE-2.5): the "after" screenshots of the inspector panels - the image inspector, the table
/// and cell inspector, find, the revision panel, the review summary and the version panel / preview strip / dialog - at 1440 and 1024
/// (touch), plus find at 390. Gated by <c>TM_WRITE_BASELINES</c>; the PNGs land in
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/document-editor-redesign/phase-6/</c> (override the folder name with
/// <c>TM_DOCUMENT_EDITOR_BASELINE_DIR</c>). The specs that assert behaviour are <see cref="DocumentEditorRedesignPhase6E2ETests"/>.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase6CaptureE2ETests : BaselineGeneratorTestBase
{
    private const string ContractDocumentId = "contract-demo";

    private static readonly (string Name, int Width, int Height, bool Touch)[] Viewports =
    [
        ("1440", 1440, 900, false),
        ("1024-touch", 1024, 768, true),
    ];

    private static string OutputDirectory
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_DOCUMENT_EDITOR_BASELINE_DIR");
            var name = string.IsNullOrWhiteSpace(target) ? "phase-6" : target.Trim();
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
    [Description("The image inspector (contextual on 1440, in the Properties tab on 1024 touch), light and dark")]
    public async Task CaptureImageInspector()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenAsync(ContractDocumentId, "/document-editor", width, height, touch);
            var image = await DocumentEditorRedesignPhase2CaptureE2ETests.ScrollToCanvasElementAsync(page, "[data-canvas-object][data-object-id='contract-inline-image']");
            await page.Mouse.ClickAsync((float)image.X, (float)image.Y);
            await Assertions.Expect(page.GetByTestId("document-image-inspector")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"image-inspector-{name}.png");
            // The panel body scrolls inside the sticky panel: one shot per lower section, scrolled to the top of the panel body.
            foreach (var section in new[] { "wrap", "position", "order", "accessibility" })
            {
                await ShotSection(page, $"document-image-inspector-section-{section}", $"image-inspector-{section}-{name}.png");
            }

            if (width == 1440)
            {
                await ApplyDarkThemeAsync(page);
                await Shot(page, $"image-inspector-dark-{name}.png");
                await ShotSection(page, "document-image-inspector-section-position", $"image-inspector-dark-position-{name}.png");
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The table inspector with its table and cell sections, the colour picker open, light and dark")]
    public async Task CaptureTableInspector()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenAsync(ContractDocumentId, "/document-editor", width, height, touch);
            var cell = await DocumentEditorRedesignPhase2CaptureE2ETests.ScrollToCanvasElementAsync(page, "[data-canvas-table-cell][data-cell-id='contract-pricing-table-r1-item']");
            await page.Mouse.ClickAsync((float)cell.X, (float)cell.Y);
            if (width < 1200)
            {
                await DocumentEditorRedesignPhase2CaptureE2ETests.RunPaletteCommandAsync(page, "tableProperties");
            }

            await Assertions.Expect(page.GetByTestId("document-table-properties-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(page.GetByTestId("document-cell-properties-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"table-inspector-{name}.png");
            await ShotSection(page, "document-cell-properties-panel", $"table-inspector-cell-{name}.png");
            if (width == 1440)
            {
                await ApplyDarkThemeAsync(page);
                await Shot(page, $"table-inspector-dark-{name}.png");
                await ShotSection(page, "document-cell-properties-panel", $"table-inspector-dark-cell-{name}.png");
                await ApplyLightThemeAsync(page);
                await page.GetByTestId("document-table-properties-background").Locator(".tm-color-picker-trigger").ClickAsync();
                await Assertions.Expect(page.Locator(".tm-color-picker-dropdown:visible").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
                await Shot(page, $"table-inspector-color-picker-{name}.png");
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Find with results (and the replace section open), light and dark; on a phone as a sheet")]
    public async Task CaptureFindPanel()
    {
        foreach (var (name, width, height, touch) in new[] { Viewports[0], Viewports[1], ("390", 390, 844, true) })
        {
            var page = await OpenAsync(ContractDocumentId, "/document-editor", width, height, touch);
            if (width >= 1024)
            {
                await page.GetByTestId("document-canvas-engine-host").ClickAsync();
                await page.Keyboard.PressAsync("Control+F");
            }
            else
            {
                await page.GetByTestId("document-header-search").ClickAsync();
            }

            await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByTestId("document-find-input").FillAsync("the");
            await Assertions.Expect(page.GetByTestId("document-find-result").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"find-panel-{name}.png");
            await page.GetByTestId("document-find-replace-toggle").ClickAsync();
            await page.GetByTestId("document-replace-input").FillAsync("The");
            await Shot(page, $"find-panel-replace-{name}.png");
            if (width == 1440)
            {
                await ApplyDarkThemeAsync(page);
                await Shot(page, $"find-panel-dark-{name}.png");
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The revision panel with its cards and the review summary strip, light and dark")]
    public async Task CaptureRevisionPanelAndReviewSummary()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenAsync(RevisionsDocumentId, "/canvas-engine-host", width, height, touch);
            await Assertions.Expect(page.GetByTestId("document-review-summary")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Shot(page, $"review-summary-{name}.png", "document-doc-banners");
            await page.GetByTestId("document-side-panel-tab-revisions").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-revision-item").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Shot(page, $"revision-panel-{name}.png");
            if (width == 1440)
            {
                await ApplyDarkThemeAsync(page);
                await Shot(page, $"revision-panel-dark-{name}.png");
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The version panel with a previewed version (the strip of the doc region, the badge), the confirmation, the dialog")]
    public async Task CaptureVersionPanelStripAndDialog()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            // A fresh in-memory history per viewport, and a label per viewport: the second pass must not show "1.3" twice.
            await DocumentEditorE2EReset.ResetAsync();
            var versionLabel = width == 1440 ? "1.3" : "1.4";
            var page = await OpenAsync(HistoryDocumentId, "/document-editor", width, height, touch);
            await ClickRibbonAsync(page, "document-ribbon-tab-view");
            await ClickRibbonAsync(page, "document-open-versions");
            await Assertions.Expect(page.GetByTestId("document-version-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByTestId("document-version-create-open").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-version-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"version-dialog-{name}.png", "document-version-dialog");
            await page.GetByTestId("document-version-label").FillAsync(versionLabel);
            await page.GetByTestId("document-version-description").FillAsync("Edits to the schedule and the budget.");
            await page.GetByTestId("document-version-label").PressAsync("Enter");
            await Assertions.Expect(page.GetByTestId("document-version-item").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"version-panel-{name}.png");
            await page.GetByTestId("document-version-item").First.ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-version-preview-state")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Shot(page, $"version-preview-{name}.png", "document-doc-banners");
            await page.GetByTestId("document-version-restore").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-version-restore-confirm")).ToBeVisibleAsync();
            await Shot(page, $"version-restore-confirm-{name}.png", "document-doc-banners");
            if (width == 1440)
            {
                await ApplyDarkThemeAsync(page);
                await Shot(page, $"version-preview-dark-{name}.png", "document-doc-banners");
            }

            await page.Context.CloseAsync();
        }
    }

    private async Task<IPage> OpenAsync(string documentId, string route, int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        var url = route == "/document-editor" && documentId == ContractDocumentId
            ? $"{BaseUrl}{route}?documentId={documentId}"
            : OpenUrl(BaseUrl, documentId, route);
        await GotoAppReadyAsync(page, url, WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-toolbar"], [data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return page;
    }

    /// <summary>
    /// The viewport, scrolled so the surface the shot is about is in it (the demo page is taller than the viewport, so the inspector is not
    /// where a plain capture looks), plus the side panel on its own as <c>*-panel.png</c> when it is present.
    /// </summary>
    private static async Task Shot(IPage page, string fileName, string focusTestId = "document-side-panel")
    {
        await page.EvaluateAsync(
            """
            id => {
                const target = document.querySelector(`[data-testid="${id}"]`);
                if (target) target.scrollIntoView({ block: 'center', inline: 'nearest' });
            }
            """,
            focusTestId);
        await page.WaitForTimeoutAsync(500);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, fileName),
            Type = ScreenshotType.Png,
            FullPage = false
        });
        var panel = page.GetByTestId("document-side-panel");
        if (focusTestId == "document-side-panel" && await panel.CountAsync() > 0 && await panel.IsVisibleAsync())
        {
            // The sticky ribbon would paint over the top of the panel in an element screenshot taken after the scroll: hide it for the shot.
            await SetRibbonVisibleAsync(page, false);
            try
            {
                await panel.ScreenshotAsync(new LocatorScreenshotOptions
                {
                    Path = Path.Combine(OutputDirectory, Path.GetFileNameWithoutExtension(fileName) + "-panel.png"),
                    Type = ScreenshotType.Png
                });
            }
            finally
            {
                await SetRibbonVisibleAsync(page, true);
            }
        }
    }

    private static Task SetRibbonVisibleAsync(IPage page, bool visible)
        => page.EvaluateAsync(
            "v => { const ribbon = document.querySelector('.tm-document-editor__ribbon'); if (ribbon) ribbon.style.visibility = v ? '' : 'hidden'; }",
            visible);

    /// <summary>
    /// Scrolls the inspector body so <paramref name="sectionTestId"/> is at its top (the body scrolls inside the sticky panel; a shot of
    /// the panel alone only ever shows the first screen of a long inspector), then takes the viewport and panel shots.
    /// </summary>
    private static async Task ShotSection(IPage page, string sectionTestId, string fileName)
    {
        var found = await page.EvaluateAsync<bool>(
            """
            id => {
                const section = document.querySelector(`[data-testid="${id}"]`);
                if (!section) return false;
                let scroller = section.parentElement;
                while (scroller && scroller.scrollHeight <= scroller.clientHeight + 1) scroller = scroller.parentElement;
                if (scroller && scroller !== document.documentElement && scroller !== document.body) {
                    scroller.scrollTop += section.getBoundingClientRect().top - scroller.getBoundingClientRect().top - 8;
                }
                return true;
            }
            """,
            sectionTestId);
        Assert.IsTrue(found, $"{sectionTestId} is rendered");
        await Shot(page, fileName);
    }
}
