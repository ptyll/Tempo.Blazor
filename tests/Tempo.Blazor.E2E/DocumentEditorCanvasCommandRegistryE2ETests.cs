using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 16 — příkazy dřív dostupné jen přes callbacky (superscript, changeCase, showRuler,
/// header/footer toggly, ...) jsou nově registrované v command registry a fallback je sjednocený
/// (registry = zdroj pravdy; nezaregistrovaný příkaz ⇒ disabled). Kontrakty: registry-driven
/// tlačítka jsou po načtení dokumentu ENABLED a fungují end-to-end.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasCommandRegistryE2ETests : WasmTestBase
{
    private const string ContractDocumentId = "contract-demo";
    private const string AgreementBlockId = "contract-scope";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    /// <summary>Superscript (nově registry-driven) je enabled a aplikuje se na výběr.</summary>
    [TestMethod]
    public async Task Phase16_Superscript_IsEnabledAndApplies()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1000);
        await OpenDocumentAsync(page);

        var output = CreateOutputDirectory("phase16-superscript");
        await SelectCanvasTextRangeAsync(page, AgreementBlockId, 0, 12);

        // Superscript is OverflowOnly (DE-1.2): it lives in the core More menu as a check-marked entry.
        await DocumentEditorRedesignE2EHelpers.OpenMoreAsync(page);
        var superscript = page.Locator("[role='menu'] [data-testid='document-superscript']");
        Assert.IsFalse(
            await superscript.IsDisabledAsync(),
            "superscript musí být enabled — nově registrovaný v command registry (dřív jen callback fallback).");
        await superscript.ClickAsync();
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-command-superscript-state') === 'active'
            """, 10_000);
        await ViewportScreenshotAsync(page, Path.Combine(output, "00-superscript-applied.png"));

        await DocumentEditorRedesignE2EHelpers.OpenMoreAsync(page);
        await Assertions.Expect(page.Locator("[role='menu'] [data-testid='document-superscript']"))
            .ToHaveAttributeAsync("aria-checked", "true", new() { Timeout = 5_000 });
    }

    /// <summary>Edge case: header/footer kontextové toggly (differentFirstPage, closeHeaderFooter) — enabled jen v HF módu.</summary>
    [TestMethod]
    public async Task Phase16_HeaderFooterCommands_EnabledOnlyInHeaderFooterMode()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1000);
        await OpenDocumentAsync(page);

        var output = CreateOutputDirectory("phase16-header-footer");
        await page.EvaluateAsync(
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                return import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs')
                    .then(module => module.editHeaderFooter(handle, 'Header'));
            }
            """);
        await page.GetByTestId("document-ribbon-tab-header-footer").WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await page.GetByTestId("document-ribbon-tab-header-footer").ClickAsync();

        var firstPageToggle = page.GetByTestId("document-header-footer-different-first-page");
        await Assertions.Expect(firstPageToggle).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(firstPageToggle).ToBeEnabledAsync(new() { Timeout = 10_000 });
        await ViewportScreenshotAsync(page, Path.Combine(output, "00-header-footer-tab.png"));

        // closeHeaderFooter vrátí caret do body a kontextový tab zmizí.
        await page.GetByTestId("document-close-header-footer").ClickAsync();
        await WaitForPredicateAsync(page, "() => !document.querySelector('[data-testid=\"document-ribbon-tab-header-footer\"]')", 10_000);
        await ViewportScreenshotAsync(page, Path.Combine(output, "01-after-close.png"));
    }

    /// <summary>Edge case: View tab — showRuler toggle (nově registrovaný) přepíná pravítko tam i zpět.</summary>
    [TestMethod]
    public async Task Phase16_ShowRulerToggle_TogglesRuler()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1000);
        await OpenDocumentAsync(page);

        var output = CreateOutputDirectory("phase16-show-ruler");
        await page.GetByTestId("document-ribbon-tab-view").ClickAsync();
        var toggle = page.Locator("[data-command='showRuler']").First;
        await Assertions.Expect(toggle).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(toggle).ToBeEnabledAsync(new() { Timeout = 10_000 });

        var pressedBefore = await toggle.GetAttributeAsync("aria-pressed");
        await toggle.ClickAsync();
        await WaitForPredicateAsync(page, $"() => document.querySelector(\"[data-command='showRuler']\")?.getAttribute('aria-pressed') !== '{pressedBefore}'", 10_000);
        await ViewportScreenshotAsync(page, Path.Combine(output, "00-ruler-toggled.png"));

        await toggle.ClickAsync();
        await WaitForPredicateAsync(page, $"() => document.querySelector(\"[data-command='showRuler']\")?.getAttribute('aria-pressed') === '{pressedBefore}'", 10_000);
        await ViewportScreenshotAsync(page, Path.Combine(output, "01-ruler-restored.png"));
    }

    /// <summary>
    /// Fáze 10 (command-layer plán) — properties příkazy (tableProperties/cellProperties/replaceImage/
    /// setImageLink) NEJSOU engine příkazy: engine registruje jen mutace (setTableProperties/
    /// setCellProperties/setImageUrl), které vydává panel. Ribbon/palette verze proto otevírají
    /// Properties side panel — zrcadlí rozhodnutí kontextového menu tabulky.
    /// </summary>
    [TestMethod]
    public async Task Phase10_PropertiesCommands_OpenPropertiesSidePanel()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1000);
        await OpenDocumentAsync(page);

        var output = CreateOutputDirectory("phase10-properties-commands");

        // Caret into a pricing-table cell so tableProperties is enabled. Hit rects exist only for
        // VISIBLE pages (canvas-per-visible-page), so scroll until the table's page renders.
        var cellPoint = await ScrollToCanvasElementAsync(page, "[data-canvas-table-cell][data-cell-id='contract-pricing-table-r1-item']");
        await page.Mouse.ClickAsync((float)cellPoint.X, (float)cellPoint.Y);
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-cell-id') === 'contract-pricing-table-r1-item'
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        // Command palette → "Table properties" opens the Properties side panel (no engine no-op).
        await OpenCommandPaletteAndSearchAsync(page, "tableProperties");
        var tablePropertiesItem = page.Locator("[data-testid='document-command-palette-item'][data-command='tableProperties']");
        await Assertions.Expect(tablePropertiesItem).ToBeEnabledAsync(new() { Timeout = 10_000 });
        await tablePropertiesItem.ClickAsync();

        await Assertions.Expect(page.GetByTestId("document-side-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-editor-workspace"))
            .ToHaveAttributeAsync("data-active-side-panel-tab", "properties", new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-table-properties-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await ViewportScreenshotAsync(page, Path.Combine(output, "00-table-properties-panel.png"));

        // Edge case: close the panel, select the inline image and run "Replace image" — the SAME
        // panel-opening contract must hold for the image properties commands.
        await CloseInspectorAsync(page);

        var imagePoint = await ScrollToCanvasElementAsync(page, "[data-canvas-object][data-object-id='contract-inline-image']");
        await page.Mouse.ClickAsync((float)imagePoint.X, (float)imagePoint.Y);
        await page.WaitForFunctionAsync(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-object-selected') === 'true'
                    && root?.getAttribute('data-canvas-object-id') === 'contract-inline-image';
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        // Selecting an object opens the inspector as its image CONTEXT on a desktop (the engine's selection report reaches Blazor
        // after the canvas attribute above flips). Wait for that STATE, then close it, so the palette command's own panel-opening
        // behaviour is what the assertion proves - a one-shot IsVisible read right after the click raced the report.
        await Assertions.Expect(page.Locator("[data-testid='document-side-panel'][data-context='image']"))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await CloseInspectorAsync(page);

        await OpenCommandPaletteAndSearchAsync(page, "replaceImage");
        var replaceImageItem = page.Locator("[data-testid='document-command-palette-item'][data-command='replaceImage']");
        await Assertions.Expect(replaceImageItem).ToBeEnabledAsync(new() { Timeout = 10_000 });
        await replaceImageItem.ClickAsync();

        await Assertions.Expect(page.GetByTestId("document-side-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-editor-workspace"))
            .ToHaveAttributeAsync("data-active-side-panel-tab", "properties", new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-image-properties-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await ViewportScreenshotAsync(page, Path.Combine(output, "01-image-properties-panel.png"));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the command palette from the canvas and types the query, waiting for the STATE each step produces: the palette is up, its
    /// search input holds the focus (the core palette focuses it on open), and after the fill the input still holds the query - a fill into
    /// a palette that was re-created by a late editor render would leave an empty field, which the value assertion catches.
    /// </summary>
    private static async Task OpenCommandPaletteAndSearchAsync(IPage page, string query)
    {
        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
        await page.Keyboard.PressAsync("Control+Shift+P");
        var search = page.GetByTestId("document-command-palette-search");
        await Assertions.Expect(page.GetByTestId("document-command-palette")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        try
        {
            await Assertions.Expect(search).ToBeFocusedAsync(new() { Timeout = 10_000 });
        }
        catch (PlaywrightException ex)
        {
            var active = await page.EvaluateAsync<string>(
                "() => { const a = document.activeElement; return a ? a.tagName + '|' + (a.getAttribute('data-testid') || '') + '|' + (a.className || '') : 'none'; }");
            throw new PlaywrightException($"The command palette opened but its search input never took the focus; the focus is on [{active}]. {ex.Message}", ex);
        }

        await search.FillAsync(query);
        await Assertions.Expect(search).ToHaveValueAsync(query, new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Closes the inspector: a selected object shows it as the CONTEXT (desktop dock, tablet side sheet), whose X only dismisses the context and
    /// returns to the tabs; the tabs' own X then closes the panel. Every step waits for the STATE the previous click produces (the context
    /// header gone, the tabs' X rendered) - a one-shot <c>CountAsync</c> straight after the click races the Blazor re-render and, on a loaded
    /// machine, skipped the second X and timed out on "panel hidden".
    /// </summary>
    private static async Task CloseInspectorAsync(IPage page)
    {
        var panel = page.GetByTestId("document-side-panel");
        var contextPanel = page.Locator("[data-testid='document-side-panel'][data-context]");
        if (await contextPanel.CountAsync() > 0)
        {
            await page.GetByTestId("document-side-panel-context-close").ClickAsync();
            await Assertions.Expect(contextPanel).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        }

        if (await panel.CountAsync() > 0)
        {
            await page.GetByTestId("document-side-panel-close").ClickAsync(new() { Timeout = 10_000 });
        }

        await Assertions.Expect(panel).ToBeHiddenAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// Scrolls until the selector's element exists (hit rects render only for VISIBLE pages),
    /// centers it and returns its viewport-relative center point.
    /// </summary>
    private static async Task<CanvasPoint> ScrollToCanvasElementAsync(IPage page, string selector)
    {
        // scrollIntoView does not drive the canvas scroll container — wheel-scroll (like a user)
        // until the element exists AND its center sits inside the viewport.
        for (var attempt = 0; ; attempt++)
        {
            var point = await page.EvaluateAsync<CanvasPoint?>(
                """
                selector => {
                    const element = document.querySelector(selector);
                    if (!element) return null;
                    const rect = element.getBoundingClientRect();
                    return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
                }
                """,
                selector);
            if (point is { Y: > 120 and < 900 })
            {
                return point;
            }

            if (attempt >= 60)
            {
                Assert.Fail($"Canvas element '{selector}' did not scroll into the viewport (last point: {point?.X},{point?.Y}).");
            }

            var delta = point is null ? 900 : Math.Clamp(point.Y - 500, -800, 800);
            await page.Mouse.MoveAsync(700, 500);
            await page.Mouse.WheelAsync(0, (float)delta);
            await page.WaitForTimeoutAsync(200);
        }
    }

    private async Task OpenDocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId={ContractDocumentId}", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 120_000 });
        await WaitForPredicateAsync(page, """
            () => document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-bold"]')
            """, 60_000);
    }

    private static async Task SelectCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
    {
        var start = await ReadCanvasPointAsync(page, blockId, startOffset);
        var end = await ReadCanvasPointAsync(page, blockId, endOffset);
        await page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)end.X, (float)end.Y, new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-collapsed') === 'false'
            """, 10_000);
    }

    private static Task<CanvasPoint> ReadCanvasPointAsync(IPage page, string blockId, int offset)
        => page.EvaluateAsync<CanvasPoint>(
            """
            ([blockId, offset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => {
                        const rect = node.getBoundingClientRect();
                        const start = Number(node.getAttribute('data-canvas-start-offset') || '0');
                        const end = Number(node.getAttribute('data-canvas-end-offset') || '0');
                        return { rect, start, end };
                    })
                    .filter(item => item.end > item.start);
                if (!rects.length) throw new Error(`No canvas text rects found for ${blockId}.`);
                const target = rects.find(item => offset >= item.start && offset < item.end) || rects[0];
                const ratio = Math.max(0, Math.min(1, (offset - target.start) / Math.max(1, target.end - target.start)));
                return {
                    x: target.rect.left + Math.max(1, target.rect.width * ratio),
                    y: target.rect.top + target.rect.height / 2
                };
            }
            """,
            new object[] { blockId, offset });

    /// <summary>Viewport screenshot (poznatek Fáze 13: locator screenshot vysokého elementu scrolluje stránku).</summary>
    private static Task ViewportScreenshotAsync(IPage page, string path)
        => page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png
        });

    private static string CreateOutputDirectory(string scenario)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "TestResults", "document-editor-canvas",
            "phase16-command-registry", "2026-07-10", scenario);
        Directory.CreateDirectory(output);
        return output;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from test output directory.");
    }

    private sealed class CanvasPoint
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
    }
}
