using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Demo stránka deklarativních toolbar rendererů (/document-toolbar-renderers) — extension API
/// dostává runtime povrch: registry-driven toggle/select/color/button pipeline v prohlížeči.
/// Overflow ribbonu (core TmToolbar, priorita, More menu) pokrývá od DE-1.2 (plán 146c7e1b, fáze 4)
/// <see cref="DocumentEditorRibbonE2ETests"/>; původní testy DE-lokálního overflow controlleru
/// (toolbar-overflow.mjs, document-toolbar-more) zanikly spolu s ním.
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
        await WaitForPredicateAsync(page, """
            () => document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-bold"]')
            """, 60_000);
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
