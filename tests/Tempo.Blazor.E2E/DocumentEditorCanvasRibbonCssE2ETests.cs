using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 15 — ribbon stylovaly dva soubory: legacy blok v _document-editor.css a novější
/// _document-editor-toolbar.css. Legacy blok byl smazán (prosakoval width:100% na ribbon-groups
/// a min-width na tlačítka do nowrap+scroll modelu) a toolbar CSS je od Fáze 15 self-contained.
/// Kontrakty: selecty nerostou (flex-grow 0), skupiny nemají max-height (clipping
/// advanced-char-tools), selecty/tlačítka sdílí 3.25rem osu, ribbon-groups nemá width:100%.
/// Screenshoty: všech 7 tabů v light+dark na 1920, Home tab na 1366/1024 — UX review.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasRibbonCssE2ETests : WasmTestBase
{
    private const string ContractDocumentId = "contract-demo";

    private static readonly string[] RibbonTabs = ["home", "insert", "math", "layout", "references", "review", "view"];

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    /// <summary>Computed-style kontrakty layoutu ribbonu po odstranění legacy CSS.</summary>
    [TestMethod]
    public async Task Phase15_RibbonLayoutContracts_HoldAfterLegacyCssRemoval()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1000);
        await OpenDocumentAsync(page);

        var output = CreateOutputDirectory("phase15-contracts");
        var probeJson = await page.EvaluateAsync<string>(
            """
            () => {
                const cs = el => el ? getComputedStyle(el) : null;
                const select = document.querySelector('.tm-document-editor__ribbon-select');
                const groups = document.querySelector('.tm-document-editor__ribbon-groups');
                const group = document.querySelector('.tm-document-editor__ribbon-group');
                const button = document.querySelector('[data-testid="document-bold"]');
                const ribbon = document.querySelector('.tm-document-editor__ribbon');
                const wrapper = document.querySelector('.tm-document-editor__ribbon-commands-wrapper');
                return JSON.stringify({
                    selectFlexGrow: cs(select)?.flexGrow,
                    selectMinHeight: cs(select)?.minHeight,
                    selectMaxHeight: cs(select)?.maxHeight,
                    buttonMinHeight: cs(button)?.minHeight,
                    groupMaxHeight: cs(group)?.maxHeight,
                    groupsAlignItems: cs(groups)?.alignItems,
                    groupAlignItems: cs(group)?.alignItems,
                    ribbonDisplay: cs(ribbon)?.display,
                    ribbonFlexDirection: cs(ribbon)?.flexDirection,
                    groupsWidthVsWrapper: groups && wrapper ? Math.round(groups.getBoundingClientRect().width - wrapper.getBoundingClientRect().width) : null,
                    buttonDisabledColorRuleApplies: !!button,
                });
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(output, "contracts.json"), probeJson);

        using var probe = JsonDocument.Parse(probeJson);
        var root = probe.RootElement;
        Assert.AreEqual("0", root.GetProperty("selectFlexGrow").GetString(), "selecty nesmí růst (flex: 0 1 auto) — legacy flex:1 1 7rem je pryč.");
        Assert.AreEqual("52px", root.GetProperty("selectMinHeight").GetString(), "selecty sdílí 3.25rem (52px) osu s tlačítky.");
        Assert.AreEqual("52px", root.GetProperty("buttonMinHeight").GetString(), "tlačítka drží 3.25rem (52px).");
        Assert.AreEqual("none", root.GetProperty("groupMaxHeight").GetString(), "ribbon-group max-height odstraněna (ořezávala advanced-char-tools).");
        Assert.AreEqual("flex", root.GetProperty("ribbonDisplay").GetString(), "ribbon musí zůstat display:flex i po smazání legacy bloku (base props přeneseny).");
        Assert.AreEqual("column", root.GetProperty("ribbonFlexDirection").GetString());
        Assert.AreEqual("stretch", root.GetProperty("groupsAlignItems").GetString());
        Assert.AreEqual("center", root.GetProperty("groupAlignItems").GetString());
    }

    /// <summary>Všech 7 tabů v light i dark režimu na 1920 px — screenshoty pro UX review.</summary>
    [TestMethod]
    public async Task Phase15_AllTabs_LightAndDark_1920()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1000);
        await OpenDocumentAsync(page);

        var output = CreateOutputDirectory("phase15-tabs-light-dark");
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            foreach (var tab in RibbonTabs)
            {
                await SelectRibbonTabAsync(page, tab);
                await ViewportScreenshotAsync(page, Path.Combine(output, $"{theme}-tab-{tab}.png"));
            }
        }
    }

    /// <summary>Edge case: užší okna (1366/1024) — overflow chování ribbonu, Home + Review tab.</summary>
    [TestMethod]
    public async Task Phase15_NarrowViewports_RibbonOverflowsGracefully()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        var output = CreateOutputDirectory("phase15-narrow");

        foreach (var width in new[] { 1366, 1024 })
        {
            await page.SetViewportSizeAsync(width, 900);
            await OpenDocumentAsync(page);
            foreach (var tab in new[] { "home", "review" })
            {
                await SelectRibbonTabAsync(page, tab);
                await ViewportScreenshotAsync(page, Path.Combine(output, $"w{width}-tab-{tab}.png"));
            }

            // Skupiny se musí posouvat horizontálně (nowrap+scroll model), ne přetékat vertikálně.
            var overflow = await page.EvaluateAsync<string>(
                """
                () => {
                    const groups = document.querySelector('.tm-document-editor__ribbon-groups');
                    const cs = getComputedStyle(groups);
                    return JSON.stringify({ overflowX: cs.overflowX, flexWrap: cs.flexWrap });
                }
                """);
            using var doc = JsonDocument.Parse(overflow);
            Assert.AreEqual("auto", doc.RootElement.GetProperty("overflowX").GetString(), $"ribbon-groups na {width}px musí scrollovat horizontálně.");
            Assert.AreEqual("nowrap", doc.RootElement.GetProperty("flexWrap").GetString(), $"ribbon-groups na {width}px drží nowrap model.");
        }
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

    private static async Task SelectRibbonTabAsync(IPage page, string tab)
    {
        await page.GetByTestId($"document-ribbon-tab-{tab}").ClickAsync();
        await WaitForPredicateAsync(page, $"() => (document.querySelector('[data-testid=\"document-ribbon-tab-{tab}\"]')?.getAttribute('aria-selected') || '').toLowerCase() === 'true'", 10_000);
        await page.WaitForTimeoutAsync(150);
    }

    private static Task SetThemeAsync(IPage page, string theme)
        => page.EvaluateAsync(
            "theme => document.querySelector('[data-theme]')?.setAttribute('data-theme', theme)",
            theme);

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
            "phase15-ribbon-css", "2026-07-10", scenario);
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
