using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase E12 E2E coverage for canvas hyphenation, page background, and advanced tables.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasHyphenationAdvancedTablesE2ETests : WasmTestBase
{
    private const string PhaseE12DocumentId = "phase-e12-canvas-hyphenation-advanced-tables";

    [TestMethod]
    public async Task PhaseE12_HyphenationBackgroundAndAdvancedTablesRenderPersistAndUndo()
    {
        await DocumentEditorE2EReset.ResetAsync();
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE12DocumentAsync(page);

        var output = CreateOutputDirectory("phasee12-hyphenation-advanced-tables");
        var beforePath = Path.Combine(output, "00-phasee12-before.png");
        var afterPath = Path.Combine(output, "01-phasee12-after-reload.png");

        var before = await ReadProbeAsync(page);
        Assert.AreEqual(PhaseE12DocumentId, before.ModelDocumentId, before.Debug);
        Assert.IsTrue(before.PageCount >= 2, before.Debug);
        Assert.IsTrue(before.HyphenatedTextRunCount > 0, before.Debug);
        Assert.AreEqual("#f8fafc", before.PageFill, before.Debug);
        Assert.IsTrue(before.PageBorderCount > 0, before.Debug);
        Assert.IsTrue(before.RepeatedHeaderCount > 0, before.Debug);
        Assert.IsTrue(before.BandedCellCount > 0, before.Debug);
        Assert.IsTrue(before.TotalCellCount > 0, before.Debug);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        var sort = await ExecuteCanvasCommandAsync(page, "sortTable", new { cellId = "canvas-e12-table-total-score", columnIndex = 2, direction = "descending" });
        Assert.IsTrue(sort.Handled && sort.Changed, sort.Debug);
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-command-last') === 'sorttable'", 10_000);

        var formula = await ExecuteCanvasCommandAsync(page, "setTableFormula", new { cellId = "canvas-e12-table-total-score", columnIndex = 2, formula = "SUM" });
        Assert.IsTrue(formula.Handled && formula.Changed, formula.Debug);
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-a11y-mirror\"]')?.textContent?.includes('462') === true", 10_000);

        var undo = await ExecuteCanvasCommandAsync(page, "undo", new { });
        Assert.IsTrue(undo.Handled && undo.Changed, undo.Debug);
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-command-last') === 'undo'", 10_000);

        formula = await ExecuteCanvasCommandAsync(page, "setTableFormula", new { cellId = "canvas-e12-table-total-score", columnIndex = 2, formula = "SUM" });
        Assert.IsTrue(formula.Handled && formula.Changed, formula.Debug);

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);
        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'", 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE12DocumentId}&showToolbar=true");
        await WaitForPhaseE12ReadyAsync(page);

        var after = await ReadProbeAsync(page);
        Assert.AreEqual(PhaseE12DocumentId, after.ModelDocumentId, after.Debug);
        Assert.IsTrue(after.PageCount >= 2, after.Debug);
        Assert.IsTrue(after.RepeatedHeaderCount > 0, after.Debug);
        Assert.IsTrue(after.MirrorContainsFormulaResult, after.Debug);
        Assert.IsTrue(after.MirrorContainsHyphenationSource, after.Debug);

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE12_HyphenationBackgroundAndAdvancedTablesRenderPersistAndUndo),
            seedDocumentId = PhaseE12DocumentId,
            userActions = new[]
            {
                "Open the phase E12 canvas seed document.",
                "Verify hyphenated text, watermark/page border commands, and multi-page advanced table metadata.",
                "Execute table sort and SUM formula commands through the canvas command bridge.",
                "Undo the formula command, apply it again, save, navigate away, and reload the same document."
            },
            expectedVisibleChanges = "The canvas renders a watermark/page background, hyphenated text, repeated table headers on later pages, banded rows, and a total row. Table command changes remain undoable and the formula result survives save/reload.",
            screenshotPaths = new[] { beforePath, afterPath },
            before,
            after
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhaseE12DocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={PhaseE12DocumentId}&showToolbar=true");
        await WaitForPhaseE12ReadyAsync(page);
    }

    private static async Task WaitForPhaseE12ReadyAsync(IPage page)
    {
        try
        {
            await WaitForPredicateAsync(page, """
                () => {
                    const hostReady = document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true';
                    const pages = document.querySelectorAll('[data-testid="document-canvas-page"]');
                    return hostReady
                        && pages.length >= 2
                        && pages[0]?.getAttribute('data-canvas-model-document-id') === 'phase-e12-canvas-hyphenation-advanced-tables'
                        && document.querySelector('[data-canvas-table-cell][data-repeated-header="true"]')
                        && document.querySelector('[data-canvas-table-cell][data-banded-row="true"]');
                }
                """, 30_000);
        }
        catch (TimeoutException ex)
        {
            var state = await page.EvaluateAsync<object>(
                """
                () => {
                    const pages = Array.from(document.querySelectorAll('[data-testid="document-canvas-page"]'));
                    return {
                        location: location.href,
                        hostReady: document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') || '',
                        pageCount: pages.length,
                        firstDocumentId: pages[0]?.getAttribute('data-canvas-model-document-id') || '',
                        tableCellCount: pages.reduce((sum, page) => sum + Number(page.getAttribute('data-canvas-table-cell-count') || '0'), 0),
                        repeatedHeaderCount: document.querySelectorAll('[data-canvas-table-cell][data-repeated-header="true"]').length,
                        bandedRowCount: document.querySelectorAll('[data-canvas-table-cell][data-banded-row="true"]').length,
                        bodyText: document.body.textContent?.slice(0, 500) || ''
                    };
                }
                """);
            Assert.Fail($"Timed out waiting for phase E12 canvas readiness. State: {JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web))}. {ex.Message}");
        }
    }

    private static async Task<PhaseE12CommandProbe> ExecuteCanvasCommandAsync(IPage page, string commandId, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return await page.EvaluateAsync<PhaseE12CommandProbe>(
            """
            async ({ commandId, json }) => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                const raw = module.execCommand(handle, commandId, json);
                const parsed = JSON.parse(raw || '{}');
                return {
                    handled: parsed?.handled === true,
                    changed: parsed?.result?.changed === true,
                    debug: JSON.stringify(parsed)
                };
            }
            """,
            new { commandId, json });
    }

    private static Task<PhaseE12Probe> ReadProbeAsync(IPage page)
        => page.EvaluateAsync<PhaseE12Probe>(
            """
            async () => {
                const pages = Array.from(document.querySelectorAll('[data-testid="document-canvas-page"]'));
                const mirror = document.querySelector('[data-testid="document-canvas-a11y-mirror"]')?.textContent || '';
                const hyphenatedTextRunCount = pages.reduce((count, page) => count + Number(page.getAttribute('data-canvas-hyphenated-text-run-count') || '0'), 0);
                const watermarkCount = pages.reduce((count, page) => count + Number(page.getAttribute('data-canvas-watermark-count') || '0'), 0);
                const pageBorderCount = pages.reduce((count, page) => count + Number(page.getAttribute('data-canvas-page-border-count') || '0'), 0);
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                let runtimeModel = {};
                if (handle) {
                    const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                    runtimeModel = JSON.parse(module.getModelJson(handle) || '{}');
                }
                const pageBackground = runtimeModel.pageBackground || runtimeModel.PageBackground || {};
                const hyphenation = runtimeModel.hyphenation || runtimeModel.Hyphenation || {};
                return {
                    modelDocumentId: pages[0]?.getAttribute('data-canvas-model-document-id') || '',
                    pageCount: pages.length,
                    hyphenatedTextRunCount,
                    watermarkCount,
                    pageFill: pages[0]?.getAttribute('data-canvas-page-fill') || '',
                    pageBorderCount,
                    repeatedHeaderCount: document.querySelectorAll('[data-canvas-table-cell][data-repeated-header="true"]').length,
                    bandedCellCount: document.querySelectorAll('[data-canvas-table-cell][data-banded-row="true"]').length,
                    totalCellCount: document.querySelectorAll('[data-canvas-table-cell][data-total-row="true"]').length,
                    mirrorContainsFormulaResult: mirror.includes('462'),
                    mirrorContainsHyphenationSource: mirror.includes('internationalization') || mirror.includes('international\u00ADization'),
                    debug: JSON.stringify({
                        pageCount: pages.length,
                        pageIds: pages.map(item => item.getAttribute('data-canvas-model-document-id')),
                        modelKeys: Object.keys(runtimeModel).sort(),
                        pageBackground,
                        hyphenation,
                        sourceHyphenationEnabled: host?.getAttribute('data-canvas-source-hyphenation-enabled') || '',
                        sourceHyphenationMode: host?.getAttribute('data-canvas-source-hyphenation-mode') || '',
                        sourcePageBackgroundColor: host?.getAttribute('data-canvas-source-page-background-color') || '',
                        sourceWatermarkText: host?.getAttribute('data-canvas-source-watermark-text') || '',
                        modelPageBackgroundColor: pages[0]?.getAttribute('data-canvas-model-page-background-color') || '',
                        modelHyphenationEnabled: pages[0]?.getAttribute('data-canvas-model-hyphenation-enabled') || '',
                        watermarkCount,
                        pageFill: pages[0]?.getAttribute('data-canvas-page-fill') || '',
                        mirror: mirror.slice(0, 400)
                    })
                };
            }
            """);

    private static async Task WaitForSaveBoundaryAsync(IPage page)
    {
        try
        {
            await WaitForPredicateAsync(page, """
                () => {
                    const saveMessage = document.querySelector('[data-testid="document-save-message"]')?.textContent || '';
                    const lastSaved = document.querySelector('[data-testid="document-last-saved"]')?.textContent || '';
                    const pending = document.querySelector('[data-testid="document-pending-status"]')?.textContent || '';
                    const saveButtonDisabled = document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true;
                    return saveButtonDisabled === false
                        && pending.trim().length === 0
                        && (/Saved|Autosaved/i.test(saveMessage) || /saved/i.test(lastSaved));
                }
                """, 10_000);
        }
        catch (TimeoutException ex)
        {
            var state = await page.EvaluateAsync<PhaseE12SaveDebugState>(
                """
                () => ({
                    saveMessage: document.querySelector('[data-testid="document-save-message"]')?.textContent || '',
                    lastSaved: document.querySelector('[data-testid="document-last-saved"]')?.textContent || '',
                    pending: document.querySelector('[data-testid="document-pending-status"]')?.textContent || '',
                    dirty: document.querySelector('[data-testid="document-dirty-status"]')?.textContent || '',
                    saveDisabled: document.querySelector('[data-testid="document-save"]')?.hasAttribute('disabled') === true,
                    statusBar: document.querySelector('[data-testid="document-status-bar"]')?.textContent || '',
                    bodyHasSaved: /Saved|Autosaved/i.test(document.body.textContent || '')
                })
                """);

            Assert.Fail($"Timed out waiting for the phase E12 save boundary. State: {JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web))}. {ex.Message}");
        }
    }

    private static Task NavigateWithinBlazorAsync(IPage page, string url)
        => page.EvaluateAsync(
            """
            url => window.Blazor?.navigateTo
                ? window.Blazor.navigateTo(url)
                : window.history.pushState({}, '', url)
            """,
            url);

    private static string CreateOutputDirectory(string viewport)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            "phasee12-hyphenation-advanced-tables",
            "2026-06-04",
            viewport);
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

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from the E2E test output directory.");
    }

    private sealed class PhaseE12CommandProbe
    {
        public bool Handled { get; set; }

        public bool Changed { get; set; }

        public string Debug { get; set; } = string.Empty;
    }

    private sealed class PhaseE12Probe
    {
        public string ModelDocumentId { get; set; } = string.Empty;

        public int PageCount { get; set; }

        public int HyphenatedTextRunCount { get; set; }

        public int WatermarkCount { get; set; }

        public string PageFill { get; set; } = string.Empty;

        public int PageBorderCount { get; set; }

        public int RepeatedHeaderCount { get; set; }

        public int BandedCellCount { get; set; }

        public int TotalCellCount { get; set; }

        public bool MirrorContainsFormulaResult { get; set; }

        public bool MirrorContainsHyphenationSource { get; set; }

        public string Debug { get; set; } = string.Empty;
    }

    private sealed class PhaseE12SaveDebugState
    {
        public string SaveMessage { get; set; } = string.Empty;

        public string LastSaved { get; set; } = string.Empty;

        public string Pending { get; set; } = string.Empty;

        public string Dirty { get; set; } = string.Empty;

        public bool SaveDisabled { get; set; }

        public string StatusBar { get; set; } = string.Empty;

        public bool BodyHasSaved { get; set; }
    }
}
