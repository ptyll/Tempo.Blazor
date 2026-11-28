using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 5 (DE-1.4 .. DE-1.7): the "after" screenshots of every floating surface - the ribbon panels (style
/// gallery, table grid, equation, symbol, page layout, link), the image panel, the token panel, the form-field popover, the command
/// palette and the dialogs (version, compare, JSON and clipboard debug) - at 1440, 1024 (touch) and 390. Gated by
/// <c>TM_WRITE_BASELINES</c>; the PNGs land in <c>tests/Tempo.Blazor.E2E/__screenshots__/document-editor-redesign/phase-5/</c> (override the
/// folder name with <c>TM_DOCUMENT_EDITOR_BASELINE_DIR</c>). The specs that assert behaviour are <see cref="DocumentEditorRibbonE2ETests"/>
/// and <see cref="DocumentEditorModalityE2ETests"/>; this class asserts only that each surface is there.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase5CaptureE2ETests : BaselineGeneratorTestBase
{
    private static readonly (string Name, int Width, int Height, bool Touch)[] Viewports =
    [
        ("1440", 1440, 900, false),
        ("1024-touch", 1024, 768, true),
        ("390", 390, 844, true),
    ];

    private static string OutputDirectory
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_DOCUMENT_EDITOR_BASELINE_DIR");
            var name = string.IsNullOrWhiteSpace(target) ? "phase-5" : target.Trim();
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
    [Description("The ribbon panels: style gallery, table grid, equation, symbol, page layout and link at 1440 / 1024 touch / 390")]
    public async Task CaptureRibbonPanels()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenEditorAsync(width, height, touch);

            await ClickRibbonAsync(page, "document-style-gallery-toggle");
            await ShotWhenVisibleAsync(page, "document-style-gallery", $"style-gallery-{name}.png");
            await CloseWithEscapeAsync(page);

            await ClickPopupItemAsync(page, "document-link", "Link");
            await ShotWhenVisibleAsync(page, "document-link-dialog", $"link-{name}.png");
            await CloseWithEscapeAsync(page);

            await ClickPopupItemAsync(page, "document-ribbon-tab-insert", "");
            await ClickPopupItemAsync(page, "document-toolbar-table", "Table");
            await ShotWhenVisibleAsync(page, "document-table-grid-picker", $"table-grid-{name}.png");
            await CloseWithEscapeAsync(page);

            await ClickPopupItemAsync(page, "document-toolbar-symbol", "Symbol");
            await ShotWhenVisibleAsync(page, "document-symbol-menu", $"symbol-menu-{name}.png");
            await CloseWithEscapeAsync(page);

            await ClickPopupItemAsync(page, "document-ribbon-tab-math", "");
            await ClickPopupItemAsync(page, "document-toolbar-equation", "Equation");
            await ShotWhenVisibleAsync(page, "document-equation-menu", $"equation-menu-{name}.png");
            await CloseWithEscapeAsync(page);

            await ClickPopupItemAsync(page, "document-ribbon-tab-layout", "");
            await ClickPopupItemAsync(page, "document-page-layout", "Page layout");
            await ShotWhenVisibleAsync(page, "document-page-layout-inspector", $"page-layout-{name}.png");

            if (width == 1440)
            {
                await ApplyDarkThemeAsync(page);
                await Shot(page, $"page-layout-dark-{name}.png");
                await ApplyLightThemeAsync(page);
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The header/footer fields menu and presets menu (header editing mode) at 1440 and 390")]
    public async Task CaptureHeaderFooterMenus()
    {
        foreach (var (name, width, height, touch) in new[] { Viewports[0], Viewports[2] })
        {
            var context = await CreateContextAsync(ContextOptions(width, height, touch));
            var page = await context.NewPageAsync();
            await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-16-canvas-headers-footers-notes&showToolbar=true", Microsoft.Playwright.WaitUntilState.DOMContentLoaded);
            await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-engine-host\"][data-canvas-engine-ready=\"true\"]') && document.querySelector('[data-canvas-header-footer-region]')", 60_000);
            await page.WaitForTimeoutAsync(800);
            var point = await page.EvaluateAsync<double[]>(
                """
                () => {
                    const node = document.querySelector('[data-canvas-header-footer-region][data-region="Header"]');
                    node.scrollIntoView({ block: 'center' });
                    const rect = node.getBoundingClientRect();
                    return [rect.x + rect.width / 2, rect.y + rect.height / 2];
                }
                """);
            await page.Mouse.DblClickAsync((float)point[0], (float)point[1]);
            await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-header-footer-editing') === 'true'", 10_000);

            // The tab appears with the header editing mode; the two menu buttons sit on the bar at 1440 and (collapsed into More) at 390.
            await Assertions.Expect(page.GetByTestId("document-ribbon-tab-header-footer")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByTestId("document-ribbon-tab-header-footer").ClickAsync();
            await page.WaitForTimeoutAsync(400);
            await ClickHeaderFooterMenuAsync(page, "document-header-footer-insert-field-menu", "Insert field");
            await ShotWhenVisibleAsync(page, "document-header-footer-field-menu", $"header-footer-fields-{name}.png");
            await CloseWithEscapeAsync(page);

            await ClickHeaderFooterMenuAsync(page, "document-header-footer-preset-menu", "Presets");
            await ShotWhenVisibleAsync(page, "document-header-footer-presets-menu", $"header-footer-presets-{name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The image panel (URL tab with a filled URL) and the token panel at 1440 / 1024 touch / 390")]
    public async Task CaptureImageAndTokenPanels()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenEditorAsync(width, height, touch);
            await ClickPopupItemAsync(page, "document-ribbon-tab-insert", "");

            await ClickPopupItemAsync(page, "document-toolbar-image", "From URL");
            await ShotWhenVisibleAsync(page, "document-canvas-image-dialog", $"image-panel-{name}.png", async () =>
            {
                var input = page.GetByTestId("document-canvas-image-url-input");
                if (await input.CountAsync() > 0)
                {
                    await input.FillAsync("https://example.com/obrazek.png");
                }
            });
            await CloseWithEscapeAsync(page);

            if (await page.GetByTestId("document-insert-menu").CountAsync() > 0)
            {
                await ClickPopupItemAsync(page, "document-insert-menu", "Insert");
                if (await page.GetByTestId("document-token-insert-panel").CountAsync() > 0)
                {
                    await ShotWhenVisibleAsync(page, "document-token-insert-panel", $"token-panel-{name}.png");
                }
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The form-field popover on the content-controls document at 1440 / 1024 touch / 390")]
    public async Task CaptureFieldPopover()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var context = await CreateContextAsync(ContextOptions(width, height, touch));
            var page = await context.NewPageAsync();
            await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-e9-canvas-content-controls&showToolbar=true", Microsoft.Playwright.WaitUntilState.DOMContentLoaded);
            await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-engine-host\"][data-canvas-engine-ready=\"true\"]')", 60_000);
            await page.WaitForTimeoutAsync(800);
            await page.EvaluateAsync(
                """
                async () => {
                    const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                    const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    module.execCommand(handle, 'focusContentControl', JSON.stringify({ controlId: 'canvas-form-renewal' }));
                }
                """);
            await ShotWhenVisibleAsync(page, "document-canvas-content-control-popover", $"field-popover-{name}.png");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The command palette at 1440 / 1024 touch / 390 (light, 1440 also dark)")]
    public async Task CaptureCommandPalette()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenEditorAsync(width, height, touch);
            await page.GetByTestId("document-canvas-engine-host").ClickAsync(new() { Position = new() { X = 40, Y = 60 } });
            await page.Keyboard.PressAsync("Control+Shift+P");
            await ShotWhenVisibleAsync(page, "document-command-palette", $"command-palette-{name}.png");
            if (width == 1440)
            {
                await ApplyDarkThemeAsync(page);
                await Shot(page, $"command-palette-dark-{name}.png");
                await ApplyLightThemeAsync(page);
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The compare, version and the two debug dialogs at 1440 / 1024 touch / 390")]
    public async Task CaptureDialogs()
    {
        foreach (var (name, width, height, touch) in Viewports)
        {
            var page = await OpenEditorAsync(width, height, touch);

            await ClickPopupItemAsync(page, "document-ribbon-tab-review", "");
            await ClickRibbonAsync(page, "document-compare-open");
            await ShotWhenVisibleAsync(page, "document-compare-dialog", $"compare-dialog-{name}.png");
            await page.GetByTestId("document-compare-close").ClickAsync();

            await ClickPopupItemAsync(page, "document-ribbon-tab-view", "");
            await ClickRibbonAsync(page, "document-view-json");
            await ShotWhenVisibleAsync(page, "document-json-debug-modal", $"json-debug-modal-{name}.png");
            if (width == 1440)
            {
                await ApplyDarkThemeAsync(page);
                await Shot(page, $"json-debug-modal-dark-{name}.png");
                // The clipboard modal and the version dialog that follow are LIGHT captures.
                await ApplyLightThemeAsync(page);
            }

            await page.GetByTestId("document-json-debug-close").ClickAsync();
            await ClickRibbonAsync(page, "document-view-clipboard-html");
            await ShotWhenVisibleAsync(page, "document-clipboard-html-debug-modal", $"clipboard-html-debug-modal-{name}.png");
            await page.GetByTestId("document-clipboard-html-debug-close").ClickAsync();

            await ClickRibbonAsync(page, "document-open-versions");
            if (await page.GetByTestId("document-version-create-open").CountAsync() > 0)
            {
                await page.GetByTestId("document-version-create-open").ClickAsync();
                await ShotWhenVisibleAsync(page, "document-version-dialog", $"version-dialog-{name}.png");
            }

            await page.Context.CloseAsync();
        }
    }

    private static async Task ClickHeaderFooterMenuAsync(IPage page, string testId, string menuLabel)
    {
        var button = page.GetByTestId(testId);
        if (await button.CountAsync() > 0 && await button.First.IsVisibleAsync())
        {
            await button.First.ScrollIntoViewIfNeededAsync();
            await button.First.ClickAsync();
            await page.WaitForTimeoutAsync(200);
            return;
        }

        await ClickPopupItemAsync(page, testId, menuLabel);
    }

    private async Task<IPage> OpenEditorAsync(int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?bounded=false&documentId=contract-demo", Microsoft.Playwright.WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-toolbar"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return page;
    }

    private static async Task ShotWhenVisibleAsync(IPage page, string testId, string fileName, Func<Task>? prepare = null)
    {
        await Assertions.Expect(page.GetByTestId(testId).First).ToBeVisibleAsync(new() { Timeout = 10_000 });
        if (prepare is not null)
        {
            await prepare();
        }

        await Shot(page, fileName);
    }

    private static async Task CloseWithEscapeAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(250);
    }

    private static async Task Shot(IPage page, string fileName)
    {
        await page.WaitForTimeoutAsync(400);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDirectory, fileName), Type = ScreenshotType.Png, FullPage = false });
    }
}
