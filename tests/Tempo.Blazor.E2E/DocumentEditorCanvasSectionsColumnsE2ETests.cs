using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>E3 E2E coverage for canvas sections, multi-column flow, line numbering, and page geometry.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasSectionsColumnsE2ETests : WasmTestBase
{
    private const string PhaseE3DocumentId = "phase-e3-canvas-sections-columns";

    [TestMethod]
    public async Task PhaseE3_CanvasSectionsColumnsLineNumbersAndLandscapePersist()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await OpenPhaseE3DocumentAsync(page);

        var output = CreateOutputDirectory("desktop-1440x1000");
        var beforePath = Path.Combine(output, "00-phasee3-sections-columns-before.png");
        var afterPath = Path.Combine(output, "01-phasee3-sections-columns-after-reload.png");

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = beforePath,
            Type = ScreenshotType.Png
        });

        var initialProbe = await ReadPhaseE3ProbeAsync(page);
        Assert.AreEqual(PhaseE3DocumentId, initialProbe.ModelDocumentId);
        Assert.IsTrue(initialProbe.PageCount >= 2, $"Expected at least two pages for section geometry. Actual: {initialProbe.PageCount}.");
        Assert.AreEqual(2, initialProbe.FirstPageColumnCount);
        Assert.AreEqual(2, initialProbe.FirstPageBalancedColumnCounts.Length, "The balanced paragraph should use both columns on the first page.");
        Assert.IsTrue(initialProbe.FirstPageBalancedColumnSpread <= 1, $"Expected balanced newspaper columns with at most one-line spread. Actual counts: {string.Join(", ", initialProbe.FirstPageBalancedColumnCounts)}.");
        Assert.IsTrue(initialProbe.FirstPageLineNumberCount > 0, "The two-column section should render line numbering in the margin.");
        Assert.IsTrue(initialProbe.HasLandscapePage, "The next-page section break should create a landscape page.");
        Assert.IsTrue(initialProbe.HasColumnSeparator, "The two-column section should expose a separator command on the page canvas.");

        await page.GetByTestId("document-ribbon-tab-layout").ClickAsync();
        await page.GetByTestId("document-page-layout").ClickAsync();
        await page.GetByTestId("document-column-count").SelectOptionAsync("3");
        await WaitForFirstPageColumnsAsync(page, 3);
        await page.GetByTestId("document-line-numbering-enabled").SetCheckedAsync(false);
        await WaitForFirstPageLineNumbersAsync(page, 0);
        await Assertions.Expect(page.GetByTestId("document-undo")).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 5_000 });
        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForFirstPageLineNumbersGreaterThanAsync(page, 0);
        await page.GetByTestId("document-undo").ClickAsync();
        await WaitForFirstPageColumnsAsync(page, 2);

        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var contentMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);
        var backgroundMetrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='page-background']").First);

        await page.GetByTestId("document-save").ClickAsync();
        await WaitForSaveBoundaryAsync(page);

        await NavigateWithinBlazorAsync(page, "/canvas-engine-host?documentId=phase-5-canvas-render");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-page\"]')?.getAttribute('data-canvas-model-document-id') === 'phase-5-canvas-render'", 20_000);
        await NavigateWithinBlazorAsync(page, $"/canvas-engine-host?documentId={PhaseE3DocumentId}&showToolbar=true&preferLocalDraft=false");
        await WaitForPhaseE3ReadyAsync(page);

        var reloadedProbe = await ReadPhaseE3ProbeAsync(page);
        Assert.AreEqual(initialProbe.FirstPageColumnCount, reloadedProbe.FirstPageColumnCount);
        CollectionAssert.AreEqual(initialProbe.FirstPageBalancedColumnCounts, reloadedProbe.FirstPageBalancedColumnCounts);
        Assert.AreEqual(initialProbe.FirstPageBalancedColumnSpread, reloadedProbe.FirstPageBalancedColumnSpread);
        Assert.AreEqual(initialProbe.HasLandscapePage, reloadedProbe.HasLandscapePage);
        Assert.IsTrue(reloadedProbe.FirstPageLineNumberCount > 0);

        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = afterPath,
            Type = ScreenshotType.Png
        });

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(PhaseE3_CanvasSectionsColumnsLineNumbersAndLandscapePersist),
            seedDocumentId = PhaseE3DocumentId,
            userActions = new[]
            {
                "Open the phase E3 canvas sections and columns seed document.",
                "Verify two-column page geometry, margin line numbers, and landscape section page metadata.",
                "Use the production Page layout dialog to change columns and line numbering, then undo both setup changes through the toolbar.",
                "Save through the production Save command, navigate away, navigate back, and verify section geometry survives reload."
            },
            expectedVisibleChanges = "The first page uses two balanced text columns with a separator and line numbers in the margin, while the following section switches to a professional landscape page frame after a next-page section break.",
            screenshotPaths = new[] { beforePath, afterPath },
            initialProbe,
            reloadedProbe,
            contentMetrics,
            backgroundMetrics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(beforePath);
        TestContext.AddResultFile(afterPath);
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenPhaseE3DocumentAsync(IPage page)
    {
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={PhaseE3DocumentId}&showToolbar=true&preferLocalDraft=false");
        await WaitForPhaseE3ReadyAsync(page);
    }

    private static Task WaitForPhaseE3ReadyAsync(IPage page)
        => WaitForPredicateAsync(page, """
            () => {
                const hostReady = document.querySelector('[data-testid="document-canvas-engine-host"]')?.getAttribute('data-canvas-engine-ready') === 'true';
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const pages = Array.from(document.querySelectorAll('[data-testid="document-canvas-page"]'));
                const first = pages[0];
                return hostReady
                    && first?.getAttribute('data-canvas-model-document-id') === 'phase-e3-canvas-sections-columns'
                    && Number(root?.getAttribute('data-canvas-page-count') || '0') >= 2
                    && Number(first.getAttribute('data-canvas-column-count') || '0') === 2
                    && first.getAttribute('data-canvas-column-balanced') === 'true'
                    && Number(first.getAttribute('data-canvas-balanced-column-line-spread') || '99') <= 1
                    && (first.getAttribute('data-canvas-balanced-column-line-counts') || '').split(',').filter(value => Number(value || '0') > 0).length === 2
                    && Number(root?.getAttribute('data-canvas-column-separator-count') || first.getAttribute('data-canvas-column-separator-count') || '0') > 0
                    && Number(root?.getAttribute('data-canvas-line-number-count') || first.getAttribute('data-canvas-line-number-count') || '0') > 0
                    && root?.getAttribute('data-canvas-layout-has-landscape-page') === 'true';
            }
            """, 30_000);

    private static Task WaitForFirstPageColumnsAsync(IPage page, int expected)
        => page.WaitForFunctionAsync(
            """
            expected => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-column-count') || '0') === expected
            """,
            expected,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForFirstPageLineNumbersAsync(IPage page, int expected)
        => page.WaitForFunctionAsync(
            """
            expected => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-line-number-count') || '0') === expected
            """,
            expected,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task WaitForFirstPageLineNumbersGreaterThanAsync(IPage page, int minimum)
        => page.WaitForFunctionAsync(
            """
            minimum => Number(document.querySelector('[data-testid="document-canvas-page"]')?.getAttribute('data-canvas-line-number-count') || '0') > minimum
            """,
            minimum,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

    private static Task<PhaseE3Probe> ReadPhaseE3ProbeAsync(IPage page)
        => page.EvaluateAsync<PhaseE3Probe>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const pages = Array.from(document.querySelectorAll('[data-testid="document-canvas-page"]'));
                const first = pages[0];
                const firstPageBalancedColumnCounts = (first?.getAttribute('data-canvas-balanced-column-line-counts') || '')
                    .split(',')
                    .map(value => Number(value || '0'))
                    .filter(value => value > 0);
                const pageSummaries = pages.map((page, index) => {
                    const rect = page.getBoundingClientRect();
                    const logicalWidth = Number(page.getAttribute('data-canvas-page-logical-width') || '0');
                    const logicalHeight = Number(page.getAttribute('data-canvas-page-logical-height') || '0');
                    return {
                        index,
                        width: rect.width,
                        height: rect.height,
                        logicalWidth,
                        logicalHeight,
                        columnCount: Number(page.getAttribute('data-canvas-column-count') || '0'),
                        balancedColumnLineCounts: (page.getAttribute('data-canvas-balanced-column-line-counts') || '')
                            .split(',')
                            .map(value => Number(value || '0'))
                            .filter(value => value > 0),
                        balancedColumnLineSpread: Number(page.getAttribute('data-canvas-balanced-column-line-spread') || '0'),
                        columnSeparatorCount: Number(page.getAttribute('data-canvas-column-separator-count') || '0'),
                        lineNumberCount: Number(page.getAttribute('data-canvas-line-number-count') || '0')
                    };
                });
                return {
                    modelDocumentId: first?.getAttribute('data-canvas-model-document-id') || '',
                    pageCount: Number(root?.getAttribute('data-canvas-page-count') || pages.length),
                    firstPageColumnCount: Number(first?.getAttribute('data-canvas-column-count') || '0'),
                    firstPageBalancedColumnCounts,
                    firstPageBalancedColumnSpread: Number(first?.getAttribute('data-canvas-balanced-column-line-spread') || '0'),
                    firstPageLineNumberCount: Number(root?.getAttribute('data-canvas-line-number-count') || first?.getAttribute('data-canvas-line-number-count') || '0'),
                    hasLandscapePage: root?.getAttribute('data-canvas-layout-has-landscape-page') === 'true' || pageSummaries.some(summary => summary.logicalWidth > summary.logicalHeight),
                    hasColumnSeparator: Number(root?.getAttribute('data-canvas-column-separator-count') || '0') > 0 || pageSummaries.some(summary => summary.columnSeparatorCount > 0),
                    pages: pageSummaries
                };
            }
            """);

    private static async Task WaitForSaveBoundaryAsync(IPage page)
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
            "phasee3-sections-columns",
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

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from test output directory.");
    }

    /// <summary>Browser-side phase E3 render state.</summary>
    public sealed class PhaseE3Probe
    {
        /// <summary>Document id reported by the first canvas page.</summary>
        public string ModelDocumentId { get; set; } = string.Empty;

        /// <summary>Rendered canvas page count.</summary>
        public int PageCount { get; set; }

        /// <summary>Column count reported by the first page.</summary>
        public int FirstPageColumnCount { get; set; }

        /// <summary>Balanced paragraph line counts by first-page column.</summary>
        public int[] FirstPageBalancedColumnCounts { get; set; } = [];

        /// <summary>Largest line-count difference in the balanced first-page paragraph.</summary>
        public int FirstPageBalancedColumnSpread { get; set; }

        /// <summary>Line number command count reported by the first page.</summary>
        public int FirstPageLineNumberCount { get; set; }

        /// <summary>Whether any rendered page is landscape.</summary>
        public bool HasLandscapePage { get; set; }

        /// <summary>Whether the first page exposes two-column separator rendering.</summary>
        public bool HasColumnSeparator { get; set; }

        /// <summary>Rendered page summaries.</summary>
        public PhaseE3PageProbe[] Pages { get; set; } = [];
    }

    /// <summary>Browser-side phase E3 page state.</summary>
    public sealed class PhaseE3PageProbe
    {
        /// <summary>Rendered page index.</summary>
        public int Index { get; set; }

        /// <summary>Rendered page width.</summary>
        public double Width { get; set; }

        /// <summary>Rendered page height.</summary>
        public double Height { get; set; }

        /// <summary>Logical canvas page width.</summary>
        public double LogicalWidth { get; set; }

        /// <summary>Logical canvas page height.</summary>
        public double LogicalHeight { get; set; }

        /// <summary>Column count.</summary>
        public int ColumnCount { get; set; }

        /// <summary>Balanced paragraph line counts by column.</summary>
        public int[] BalancedColumnLineCounts { get; set; } = [];

        /// <summary>Largest line-count difference for the balanced paragraph.</summary>
        public int BalancedColumnLineSpread { get; set; }

        /// <summary>Column separator command count.</summary>
        public int ColumnSeparatorCount { get; set; }

        /// <summary>Line number count.</summary>
        public int LineNumberCount { get; set; }
    }
}
