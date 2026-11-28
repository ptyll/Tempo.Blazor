using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 11 (the engine fits every narrow document column): ELEMENT screenshots of the editor (never the page, so the demo header
/// cannot hide a clipped page) where the page used to be cut off - a 1440 viewport with the demo sidebar (container ~1048 px: the readable floor presents the
/// inspector as a sheet and the page holds 100 percent), a 1600 viewport (container ~1200 px: the docked inspector next to a fitted page), the inspector tabs
/// at each width, a tablet at 820 px and a phone at 390 px - in light and dark - plus the status-bar zoom label fitted / after a reader zoom / after Reset
/// zoom and a 1920 viewport without the demo's width cap. Gated by
/// <c>TM_WRITE_BASELINES</c>; the PNGs land in <c>tests/Tempo.Blazor.E2E/__screenshots__/document-editor-redesign/phase-11/</c>. The specs that assert
/// behaviour are the <c>AutoFit_*</c> specs of <see cref="DocumentEditorResponsiveShellE2ETests"/>.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase11CaptureE2ETests : BaselineGeneratorTestBase
{
    private static readonly (string Name, int Width, int Height, bool Touch)[] Viewports =
    [
        ("1048", 1440, 900, false),
        ("1200", 1600, 900, false),
        ("820", 820, 1180, true),
        ("390", 390, 844, true),
    ];

    private static string OutputDirectory
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_DOCUMENT_EDITOR_BASELINE_DIR");
            var name = string.IsNullOrWhiteSpace(target) ? "phase-11" : target.Trim();
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
    [Description("The fitted page at the widths where it used to be clipped, light and dark, plus the inspector's version tab and the docked tab row at 1048")]
    public async Task CaptureFittedPage()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenAsync(width, height, touch);
            foreach (var (dark, indigo, theme) in ThemeMatrix.Where(t => !t.Indigo))
            {
                await ApplyThemeAsync(page, dark, indigo);
                await Shot(page, $"fitted-{theme}-{name}.png");
            }

            await ApplyThemeAsync(page, false, false);
            // The inspector wherever it lives: docked (1200), a sheet over the page (1048, 820) or a phone's bottom sheet (390).
            await OpenInspectorTabAsync(page, "revisions");
            await Shot(page, $"revisions-light-{name}.png");
            if (await page.GetByTestId("document-side-panel-tab-versions").CountAsync() > 0)
            {
                await OpenInspectorTabAsync(page, "versions");
                await Shot(page, $"versions-light-{name}.png");
            }

            foreach (var (dark, indigo, theme) in ThemeMatrix.Where(t => !t.Indigo))
            {
                await ApplyThemeAsync(page, dark, indigo);
                var tabs = page.Locator(".tm-document-side-panel__tabs").First;
                if (await tabs.CountAsync() > 0)
                {
                    await tabs.ScreenshotAsync(new LocatorScreenshotOptions
                    {
                        Path = Path.Combine(OutputDirectory, $"inspector-tabs-{theme}-{name}.png"),
                        Type = ScreenshotType.Png,
                    });
                }
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The status-bar zoom: the automatic fit's label, after a reader zoom, and after Automatic (Reset zoom) from the menu")]
    public async Task CaptureZoomLabel()
    {
        var page = await OpenAsync(1600, 900, false);
        await Assertions.Expect(page.GetByTestId("document-status-zoom")).ToContainTextAsync("Fit ", new() { Timeout = 15_000 });
        await StatusShot(page, "status-zoom-fitted.png");

        await page.GetByTestId("document-status-zoom-in").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-status-zoom")).Not.ToContainTextAsync("Fit ", new() { Timeout = 10_000 });
        await StatusShot(page, "status-zoom-after-reader-zoom.png");

        await page.Locator("[data-testid='document-status-zoom'] .tm-dropdown-trigger").ClickAsync();
        await page.GetByTestId("document-status-zoom-auto").WaitForAsync();
        await StatusShot(page, "status-zoom-menu-open.png", includePopover: true);
        await page.GetByTestId("document-status-zoom-auto").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-status-zoom")).ToContainTextAsync("Fit ", new() { Timeout = 10_000 });
        await StatusShot(page, "status-zoom-after-reset.png");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A 1920 viewport without the demo's width cap: the column holds the page, 100 percent, no fit state")]
    public async Task Capture1920WithoutTheWidthCap()
    {
        var page = await OpenAsync(1920, 1080, false);
        await page.EvaluateAsync(
            """
            () => {
                for (let el = document.querySelector('.tm-document-editor-host'); el && el !== document.documentElement; el = el.parentElement) {
                    el.style.maxWidth = 'none';
                    el.style.maxInlineSize = 'none';
                }
            }
            """);
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-zoom-percent') === '100'", 15_000);
        await Assertions.Expect(page.GetByTestId("document-status-zoom")).ToContainTextAsync("100%", new() { Timeout = 10_000 });
        await Shot(page, "unfitted-light-1920.png");
        await page.Context.CloseAsync();
    }

    private static async Task StatusShot(IPage page, string fileName, bool includePopover = false)
    {
        var bar = page.GetByTestId("document-status-bar");
        await bar.ScrollIntoViewIfNeededAsync();
        var path = Path.Combine(OutputDirectory, fileName);
        if (!includePopover)
        {
            await bar.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
            return;
        }

        // The open menu is a popover next to the bar: clip the viewport to the union of the bar and the visible menu so the shot shows the menu itself.
        var clip = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            () => {
                const bar = document.querySelector('[data-testid="document-status-bar"]').getBoundingClientRect();
                const item = document.querySelector('[data-testid="document-status-zoom-auto"]');
                const menu = item.closest('[role="menu"], [popover], .tm-dropdown-menu, .tm-overlay-panel') || item.parentElement;
                const m = menu.getBoundingClientRect();
                const left = Math.max(0, Math.min(bar.left, m.left) - 8), top = Math.max(0, Math.min(bar.top, m.top) - 8);
                const right = Math.min(innerWidth, Math.max(bar.right, m.right) + 8), bottom = Math.min(innerHeight, Math.max(bar.bottom, m.bottom) + 8);
                return { x: left, y: top, width: right - left, height: bottom - top };
            }
            """);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            Clip = new Clip
            {
                X = (float)clip.GetProperty("x").GetDouble(),
                Y = (float)clip.GetProperty("y").GetDouble(),
                Width = (float)clip.GetProperty("width").GetDouble(),
                Height = (float)clip.GetProperty("height").GetDouble(),
            },
        });
    }

    private async Task<IPage> OpenAsync(int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, OpenUrl(BaseUrl, HistoryDocumentId, "/canvas-engine-host"), WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(900);
        return page;
    }

    // The editor element itself: a screenshot of the page would let the demo's sticky header hide exactly the clipping this phase removes.
    private static async Task Shot(IPage page, string fileName)
    {
        // The demo's sticky top bar would sit over the editor header in an element screenshot (the bar is not part of the editor).
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = ".sticky.top-0, .fixed.top-0 { visibility: hidden !important; }" });
        await page.Locator(".tm-document-editor").First.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);
        await page.Locator(".tm-document-editor").First.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, fileName),
            Type = ScreenshotType.Png,
        });
    }
}
