using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 14 — TmIcon renderuje pro neznámý název prázdný <c>&lt;span class="tm-icon-unknown"&gt;</c>
/// (tlačítko má místo ikony díru). Audit našel 25 názvů používaných DocumentEditorem, které chyběly
/// v built-in sadě, + duplicity (numbered list = bullet list, doubleStrikethrough = strikethrough).
/// Kontrakt: v žádném tabu ribbonu, ve float toolbaru ani v header/footer kontextovém tabu nesmí
/// existovat <c>.tm-icon-unknown</c>; každý tab se screenshotuje pro UX review.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasToolbarIconsE2ETests : WasmTestBase
{
    private const string ContractDocumentId = "contract-demo";
    private const string AgreementBlockId = "contract-scope";

    private static readonly string[] RibbonTabs = ["home", "insert", "math", "layout", "references", "review", "view"];

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    /// <summary>Všech 7 tabů ribbonu: žádné tlačítko nesmí mít prázdné místo místo ikony.</summary>
    [TestMethod]
    public async Task Phase14_AllRibbonTabs_HaveNoUnknownIcons()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1600, 1000);
        await OpenDocumentAsync(page);

        var output = CreateOutputDirectory("phase14-ribbon-tabs");
        var report = new Dictionary<string, int>();

        foreach (var tab in RibbonTabs)
        {
            await page.GetByTestId($"document-ribbon-tab-{tab}").ClickAsync();
            await WaitForPredicateAsync(page, $"() => (document.querySelector('[data-testid=\"document-ribbon-tab-{tab}\"]')?.getAttribute('aria-selected') || '').toLowerCase() === 'true'", 10_000);
            await page.WaitForTimeoutAsync(150);

            var unknown = await CountUnknownIconsAsync(page);
            report[tab] = unknown;
            await ViewportScreenshotAsync(page, Path.Combine(output, $"tab-{tab}.png"));
        }

        await File.WriteAllTextAsync(
            Path.Combine(output, "manifest.json"),
            JsonSerializer.Serialize(new { problem = "Fáze 14: chybějící built-in ikony → tm-icon-unknown díry v ribbonu", unknownIconsPerTab = report }, JsonWebIndented));

        foreach (var (tab, count) in report)
        {
            Assert.AreEqual(0, count, $"tab '{tab}' obsahuje {count} prvků .tm-icon-unknown — tlačítka s dírou místo ikony.");
        }
    }

    /// <summary>Edge case 1: float (mini) toolbar po výběru textu — žádné neznámé ikony.</summary>
    [TestMethod]
    public async Task Phase14_FloatToolbar_HasNoUnknownIcons()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1600, 1000);
        await OpenDocumentAsync(page);

        var output = CreateOutputDirectory("phase14-float-toolbar");
        await SelectCanvasTextRangeAsync(page, AgreementBlockId, 0, 12);
        await Assertions.Expect(page.GetByTestId("document-mini-toolbar")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var unknown = await page.EvaluateAsync<int>(
            "() => document.querySelectorAll('[data-testid=\"document-mini-toolbar\"] .tm-icon-unknown').length");
        await ViewportScreenshotAsync(page, Path.Combine(output, "float-toolbar.png"));
        Assert.AreEqual(0, unknown, "float toolbar obsahuje .tm-icon-unknown.");
    }

    /// <summary>Edge case 2: kontextový Header/Footer tab (renderuje se jen v header/footer módu).</summary>
    [TestMethod]
    public async Task Phase14_HeaderFooterTab_HasNoUnknownIcons()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1600, 1000);
        await OpenDocumentAsync(page);

        var output = CreateOutputDirectory("phase14-header-footer-tab");
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
        await page.WaitForTimeoutAsync(200);

        var unknown = await CountUnknownIconsAsync(page);
        await ViewportScreenshotAsync(page, Path.Combine(output, "header-footer-tab.png"));
        Assert.AreEqual(0, unknown, "header/footer tab obsahuje .tm-icon-unknown.");
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

    private static Task<int> CountUnknownIconsAsync(IPage page)
        => page.EvaluateAsync<int>(
            "() => document.querySelectorAll('[data-testid=\"document-editor-demo\"] .tm-icon-unknown').length");

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

    /// <summary>Viewport screenshot — locator screenshot vysokého elementu scrolluje stránku a zavírá floating UI (poznatek Fáze 13).</summary>
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
            "phase14-icons", "2026-07-10", scenario);
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

    private static readonly JsonSerializerOptions JsonWebIndented =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed class CanvasPoint
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
    }
}
