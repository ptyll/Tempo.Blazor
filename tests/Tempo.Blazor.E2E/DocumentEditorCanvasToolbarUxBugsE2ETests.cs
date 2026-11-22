using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 18 — drobné UX bugy toolbaru v reálném prohlížeči: changeCase select se po výběru musí
/// resetovat na placeholder (value="" bylo konstantní a Blazor diff reset neemitoval → select
/// zamrzl na poslední volbě a stejná volba už nešla vybrat), overflow menu musí při prázdném
/// výsledku filtru nechat search box viditelný s hláškou.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasToolbarUxBugsE2ETests : WasmTestBase
{
    private const string ContractDocumentId = "contract-demo";
    private const string AgreementBlockId = "contract-scope";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    /// <summary>changeCase: aplikace + reset selectu na placeholder + opakovaný výběr téže volby funguje.</summary>
    [TestMethod]
    public async Task Phase18_ChangeCase_AppliesAndResetsToPlaceholder()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1000);
        await OpenDocumentAsync(page);

        var output = CreateOutputDirectory("phase18-change-case");
        await SelectCanvasTextRangeAsync(page, AgreementBlockId, 0, 12);

        var select = page.GetByTestId("document-change-case");
        await select.SelectOptionAsync("uppercase");
        await page.WaitForFunctionAsync(
            $"""
            () => (document.querySelector('[data-testid="document-canvas-a11y-mirror"] [data-block-id="{AgreementBlockId}"]')
                ?.textContent || '').startsWith('THE PROVIDER')
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await ViewportScreenshotAsync(page, Path.Combine(output, "00-uppercase-applied.png"));

        // Kontrakt fixe: select se po výběru resetuje na placeholder (dřív zůstal na "uppercase").
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-change-case\"]')?.value === ''",
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        // Edge case: TÁŽ volba podruhé — se zamrzlým selectem nešla vybrat (onchange nevystřelí).
        await SelectCanvasTextRangeAsync(page, AgreementBlockId, 0, 12);
        await select.SelectOptionAsync("lowercase");
        await page.WaitForFunctionAsync(
            $"""
            () => (document.querySelector('[data-testid="document-canvas-a11y-mirror"] [data-block-id="{AgreementBlockId}"]')
                ?.textContent || '').startsWith('the provider')
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-testid=\"document-change-case\"]')?.value === ''",
            new PageWaitForFunctionOptions { Timeout = 10_000 });
        await ViewportScreenshotAsync(page, Path.Combine(output, "01-lowercase-applied-select-reset.png"));
    }

    /// <summary>
    /// Edge case: Layout tab — orientace má jediné ovládání (segmentová tlačítka; duplicitní select
    /// odstraněn) a přepnutí na Landscape se projeví. POZN.: overflow menu je od zavedení JS
    /// overflow controlleru (toolbar-overflow.mjs) pokryto E2E v
    /// DocumentEditorToolbarOverflowAndRenderersE2ETests včetně empty-state filtru.
    /// </summary>
    [TestMethod]
    public async Task Phase18_LayoutTab_OrientationSegmentsOnly_AndToggleWorks()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1920, 1000);
        await OpenDocumentAsync(page);

        var output = CreateOutputDirectory("phase18-orientation");
        await page.GetByTestId("document-ribbon-tab-layout").ClickAsync();
        await page.GetByTestId("document-page-layout").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-page-layout-inspector")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        Assert.AreEqual(0, await page.GetByTestId("document-page-orientation").CountAsync(),
            "duplicitní <select> orientace byl odstraněn — zůstávají segmentová tlačítka.");
        await ViewportScreenshotAsync(page, Path.Combine(output, "00-inspector-segments-only.png"));

        await page.GetByTestId("document-page-orientation-landscape").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-page-orientation-landscape"))
            .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 10_000 });
        await ViewportScreenshotAsync(page, Path.Combine(output, "01-landscape-applied.png"));

        await page.GetByTestId("document-page-orientation-portrait").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-page-orientation-portrait"))
            .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 10_000 });
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

    private static async Task SelectCanvasTextRangeAsync(IPage page, string blockId, int startOffset, int endOffset)
    {
        var start = await ReadCanvasPointAsync(page, blockId, startOffset);
        var end = await ReadCanvasPointAsync(page, blockId, endOffset);
        await page.Mouse.MoveAsync((float)start.X, (float)start.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)end.X, (float)end.Y, new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-collapsed') === 'false'
            """,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
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

    /// <summary>Viewport screenshot (poznatek Fáze 13).</summary>
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
            "phase18-ux-bugs", "2026-07-10", scenario);
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
