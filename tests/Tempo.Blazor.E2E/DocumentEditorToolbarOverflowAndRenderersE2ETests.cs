using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Uzavření dvou carry-forward nálezů z perf plánu:
/// (1) JS overflow measurement controller — SetOverflowingAsync byl mrtvý JSInvokable a More menu
///     se v aplikaci nikdy neukázalo; nově toolbar-overflow.mjs měří [data-command] prvky ribbonu
///     (ResizeObserver + scroll + MutationObserver) a na úzkém okně se More menu poprvé dá ověřit
///     E2E včetně empty-state hlášky filtru z Fáze 18.
/// (2) Demo stránka deklarativních toolbar rendererů (/document-toolbar-renderers) — extension API
///     dostává runtime povrch: registry-driven toggle/select/color/button pipeline v prohlížeči.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorToolbarOverflowAndRenderersE2ETests : WasmTestBase
{
    private const string ContractDocumentId = "contract-demo";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    /// <summary>Úzké okno → More tlačítko se odkryje, menu vyjmenuje přetečené příkazy + funguje
    /// filtr s empty-state (Fáze 18); přepnutí na nepřetékající tab More schová. POZN.: demo layout
    /// /document-editor má max-width cap obsahu (~943px i na 1920px viewportu), takže Home tab
    /// nepřestane přetékat žádnou šířkou okna — „vejde se → skryto" dokazuje Layout tab.</summary>
    [TestMethod]
    public async Task OverflowController_NarrowViewport_ShowsMoreMenu_WideViewport_HidesIt()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(700, 900);
        await OpenDocumentAsync(page);
        var output = CreateOutputDirectory("overflow-controller");

        // Úzké okno: Home tab přetéká → controller odkryje More.
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-toolbar-more\"]')?.hidden === false",
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        await ViewportScreenshotAsync(page, Path.Combine(output, "00-narrow-more-button-visible.png"));

        await page.GetByTestId("document-toolbar-more").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-toolbar-more-menu")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var menuItemCount = await page.Locator("[data-testid='document-toolbar-more-menu'] [role='menuitem']").CountAsync();
        Assert.IsTrue(menuItemCount > 0, "overflow menu musí vyjmenovat přetečené příkazy ribbonu.");
        await ViewportScreenshotAsync(page, Path.Combine(output, "01-narrow-more-menu-open.png"));

        // Fáze 18 empty-state — poprvé ověřitelný E2E: nesmyslný filtr nechá search box + hlášku.
        var searchVisible = await page.GetByTestId("document-toolbar-more-search").IsVisibleAsync();
        if (searchVisible)
        {
            await page.GetByTestId("document-toolbar-more-search").FillAsync("xyz-neexistuje");
            await Assertions.Expect(page.GetByTestId("document-toolbar-more-empty")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(page.GetByTestId("document-toolbar-more-search")).ToBeVisibleAsync();
            await ViewportScreenshotAsync(page, Path.Combine(output, "02-narrow-filter-empty-state.png"));
            await page.GetByTestId("document-toolbar-more-search").FillAsync("");
        }

        await page.Keyboard.PressAsync("Escape");

        // Nepřetékající tab (Layout má na 700px 3 příkazy): controller nahlásí false → More zmizí.
        await page.GetByTestId("document-ribbon-tab-layout").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-toolbar-more\"]')?.hidden === true",
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        Assert.AreEqual(0, await page.GetByTestId("document-toolbar-more-menu").CountAsync(),
            "se schovaným More nesmí zůstat otevřené overflow menu.");
        await ViewportScreenshotAsync(page, Path.Combine(output, "03-layout-tab-more-hidden.png"));
    }

    /// <summary>Přepnutí ribbon tabu na úzkém okně přeměří overflow (MutationObserver větev).</summary>
    [TestMethod]
    public async Task OverflowController_TabSwitch_RemeasuresOverflowedCommands()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(700, 900);
        await OpenDocumentAsync(page);
        var output = CreateOutputDirectory("overflow-tab-switch");

        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-toolbar-more\"]')?.hidden === false",
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        await page.GetByTestId("document-toolbar-more").ClickAsync();
        var homeCommands = await ReadOverflowMenuCommandsAsync(page);
        await page.Keyboard.PressAsync("Escape");

        // Layout tab má na 700px jen 3 příkazy a NEpřetéká → MutationObserver musí More schovat
        // (negativní větev: přepnutí tabu nesmí nechat zamrzlý overflow stav Home tabu).
        await page.GetByTestId("document-ribbon-tab-layout").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-ribbon-panel\"]')?.getAttribute('data-active-ribbon-tab') === 'layout'",
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-toolbar-more\"]')?.hidden === true",
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        await ViewportScreenshotAsync(page, Path.Combine(output, "00-layout-tab-no-overflow.png"));

        // Insert tab přetéká → More se zase odkryje a menu obsahuje příkazy NOVÉHO tabu.
        await page.GetByTestId("document-ribbon-tab-insert").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-ribbon-panel\"]')?.getAttribute('data-active-ribbon-tab') === 'insert'",
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-toolbar-more\"]')?.hidden === false",
            new PageWaitForFunctionOptions { Timeout = 15_000 });
        await page.GetByTestId("document-toolbar-more").ClickAsync();
        var insertCommands = await ReadOverflowMenuCommandsAsync(page);
        await ViewportScreenshotAsync(page, Path.Combine(output, "01-insert-tab-overflow-menu.png"));

        Assert.IsTrue(insertCommands.Length > 0, "Insert tab musí na úzkém okně hlásit přetečené příkazy.");
        CollectionAssert.AreNotEqual(homeCommands, insertCommands,
            "po přepnutí tabu musí overflow menu obsahovat příkazy nového tabu, ne zamrzlý Home seznam.");
    }

    /// <summary>Demo stránka deklarativních rendererů: registry-driven toggle/select/color/button pipeline.</summary>
    [TestMethod]
    public async Task ToolbarRenderersDemoPage_RegistryDrivenPipeline_Works()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 900);
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-toolbar-renderers", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync("[data-testid='toolbar-renderers-toolbar']",
            new PageWaitForSelectorOptions { Timeout = 60_000 });
        var output = CreateOutputDirectory("renderers-demo");
        await ViewportScreenshotAsync(page, Path.Combine(output, "00-initial.png"));

        // Toggle: klik přepne aria-pressed i stav v command registry.
        var bold = page.Locator("[data-toolbar-item='demoBold']");
        await Assertions.Expect(bold).ToHaveAttributeAsync("aria-pressed", "false");
        await bold.ClickAsync();
        await Assertions.Expect(bold).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("toolbar-renderers-bold-state")).ToHaveTextAsync("active");

        // Select: onchange payload doteče přes ExecuteAsync do stavu.
        await page.Locator("[data-toolbar-item='demoLineSpacing'] select, select[data-toolbar-item='demoLineSpacing']")
            .First.SelectOptionAsync("1.5");
        await Assertions.Expect(page.GetByTestId("toolbar-renderers-spacing-state")).ToHaveTextAsync("1.5", new() { Timeout = 10_000 });

        // Color picker: nativní input[type=color] nejde otevřít Playwrightem — nastavíme hodnotu a vystřelíme change.
        await page.EvaluateAsync(
            """
            () => {
                const input = document.querySelector("input[type='color'][data-command='demoHighlightColor']")
                    ?? document.querySelector("[data-toolbar-item='demoHighlightColor'] input[type='color']")
                    ?? document.querySelector("input[type='color']");
                input.value = '#00ff00';
                input.dispatchEvent(new Event('change', { bubbles: true }));
            }
            """);
        await Assertions.Expect(page.GetByTestId("toolbar-renderers-color-state")).ToHaveTextAsync("#00ff00", new() { Timeout = 10_000 });

        // Log zaznamenal exekuce.
        var logEntries = await page.Locator("[data-testid='toolbar-renderers-log'] li").CountAsync();
        Assert.IsTrue(logEntries >= 3, $"execution log má mít >= 3 záznamy (bold, spacing, color), má {logEntries}.");
        await ViewportScreenshotAsync(page, Path.Combine(output, "01-after-interactions.png"));

        // Read-only: registry vypne commandy s AffectsData; button bez AffectsData zůstává aktivní.
        await page.GetByTestId("toolbar-renderers-readonly").CheckAsync();
        await Assertions.Expect(bold).ToBeDisabledAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("[data-toolbar-item='demoClearLog']")).ToBeEnabledAsync();
        await ViewportScreenshotAsync(page, Path.Combine(output, "02-readonly.png"));

        await page.GetByTestId("toolbar-renderers-readonly").UncheckAsync();
        await Assertions.Expect(bold).ToBeEnabledAsync(new() { Timeout = 10_000 });

        // Button renderer: Clear log exekuuje přes registry a log se vyprázdní.
        await page.Locator("[data-toolbar-item='demoClearLog']").ClickAsync();
        await Assertions.Expect(page.GetByTestId("toolbar-renderers-log-empty")).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task OpenDocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId={ContractDocumentId}", WaitUntilState.DOMContentLoaded);
        await page.WaitForSelectorAsync(
            "[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 120_000 });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-bold"]')
            """,
            new PageWaitForFunctionOptions { Timeout = 60_000 });
    }

    private static async Task<string[]> ReadOverflowMenuCommandsAsync(IPage page)
    {
        await Assertions.Expect(page.GetByTestId("document-toolbar-more-menu")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        return await page.EvaluateAsync<string[]>(
            """
            () => Array.from(document.querySelectorAll('[data-testid="document-toolbar-more-menu"] [role="menuitem"]'))
                .map(item => item.getAttribute('data-command') || '')
            """);
    }

    /// <summary>Viewport screenshot (poznatek Fáze 13: locator screenshot vysokého prvku scrolluje a zavírá floating UI).</summary>
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
            "toolbar-overflow-renderers", "2026-07-11", scenario);
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
}
