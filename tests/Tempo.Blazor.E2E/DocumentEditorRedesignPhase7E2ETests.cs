using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 7 (DE-1.8 + DE-2.6): behavioural assertions of the two document-region modes - the print preview (no ribbon,
/// thumbnails, a page / zoom bar, the actions and the information) and the comparison (the diff as text aligned by block, opened from the
/// version panel and from the compare dialog, "Back to document") - and of the status bar controls. The screenshots are
/// <see cref="DocumentEditorRedesignPhase7CaptureE2ETests"/> (a gated generator). The compare dialog's own redline flow stays in
/// <see cref="DocumentEditorRedlineExportE2ETests"/>.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase7E2ETests : WasmTestBase
{
    internal const string HistoryText = "canvas-history-text";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("Version panel: Base + Compare -> Show differences opens the comparison MODE of the document region (one canvas, hidden), Back returns")]
    public async Task VersionPanel_ShowDifferences_OpensTheComparisonMode_AndBackReturns()
    {
        // 1600: a docked inspector - the 1440 viewport's 1046 px editor presents it as a closed sheet (the readable floor)
        var page = await OpenEditorAsync(1600, 900, touch: false);
        var marker = await CreateTwoVersionsAsync(page);

        await Assertions.Expect(page.GetByTestId("document-version-show-diff")).ToBeDisabledAsync();
        await VersionEntry(page, "Draft").GetByTestId("document-version-diff-base").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-version-show-diff")).ToBeDisabledAsync();
        await VersionEntry(page, "Final").GetByTestId("document-version-diff-compare").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-version-show-diff")).ToBeEnabledAsync();
        await page.GetByTestId("document-version-show-diff").ClickAsync();

        var mode = page.Locator("[data-region='doc'] [data-testid='document-diff-mode']");
        await Assertions.Expect(mode.GetByTestId("document-diff-viewer")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("document-diff-side-by-side")).ToContainTextAsync(marker, new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-diff-legend").Locator("li")).ToHaveCountAsync(3);
        await Assertions.Expect(page.GetByTestId("document-diff-old-head")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("document-diff-new-head")).ToBeVisibleAsync();

        // The inline diff of the panel is gone, the canvas is ONE and hidden (two copies of the document are never drawn).
        Assert.AreEqual(0, await page.Locator("[data-testid='document-version-panel'] [data-testid='document-diff-viewer']").CountAsync());
        Assert.AreEqual(1, await page.Locator("[data-testid='document-canvas-engine-root']").CountAsync(), "one canvas engine, never a copy");
        Assert.IsFalse(await page.Locator(".tm-document-editor__page-surface").IsVisibleAsync(), "the page surface is hidden while the comparison is shown");

        // The changed block is tinted on BOTH sides and the next-change navigation selects it.
        await page.GetByTestId("document-diff-next").ClickAsync();
        // scoped to the editor's own comparison: the demo page also shows a standalone viewer card, whose first change is preselected too
        var current = page.Locator("[data-testid='document-diff-mode'] .tm-document-diff__row[aria-current='true']");
        await Assertions.Expect(current).ToHaveCountAsync(1);
        await Assertions.Expect(current).ToHaveAttributeAsync("data-change-kind", "changed");
        Assert.IsTrue(await current.Locator("[data-side='old']").IsVisibleAsync() && await current.Locator("[data-side='new']").IsVisibleAsync());

        // Unified is one column.
        await page.GetByTestId("document-diff-view-mode").SelectOptionAsync("Unified");
        await Assertions.Expect(page.GetByTestId("document-diff-unified")).ToBeVisibleAsync();
        Assert.AreEqual(0, await page.GetByTestId("document-diff-old-head").CountAsync());

        // Escape and the back button both return to the document - with NO command run, the document text is exactly what it was.
        var textBefore = await page.GetByTestId("document-canvas-a11y-mirror").TextContentAsync();
        await page.GetByTestId("document-diff-back").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator(".tm-document-editor__page-surface")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-canvas-a11y-mirror")).ToContainTextAsync(marker, new() { Timeout = 10_000 });
        Assert.AreEqual(textBefore, await page.GetByTestId("document-canvas-a11y-mirror").TextContentAsync(), "going back runs no command");

        // An editing command pressed while the comparison is shown leaves the comparison first.
        await VersionEntry(page, "Final").GetByTestId("document-version-diff-compare").ClickAsync();
        await page.GetByTestId("document-version-show-diff").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await ClickRibbonAsync(page, "document-ribbon-tab-home");
        await page.GetByTestId("document-bold").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The print preview has no ribbon, a thumbnail rail, a page / zoom bar, the actions as cards and the information; Close returns the ribbon")]
    public async Task PrintPreview_HasNoRibbon_ThumbnailsActionsAndInformation()
    {
        var page = await OpenHistoryAsync(1440, 900, touch: false);
        await ClickRibbonAsync(page, "document-ribbon-tab-view");
        await ClickRibbonAsync(page, "document-open-print-preview");

        var actions = page.Locator("[data-region='inspector'] [data-testid='document-print-preview-actions']");
        await Assertions.Expect(actions).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("document-ribbon-toolbar")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-region='nav'] [data-testid='document-page-navigator']")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("document-print-preview-toolbar")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("document-print-preview-info-paper")).ToContainTextAsync("A4");
        await Assertions.Expect(page.GetByTestId("document-print-preview-info-orientation")).ToHaveTextAsync("Portrait");

        // The page is fitted into the document region once the preview layout has settled: no horizontal clipping, centred.
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-zoom-preset') === 'fitWidth'", 15_000);
        var geometry = await page.EvaluateAsync<double[]>(
            """
            async () => {
                await new Promise(r => setTimeout(r, 700));
                const doc = document.querySelector('[data-region="doc"]').getBoundingClientRect();
                const pg = document.querySelector('[data-testid="document-canvas-page"]').getBoundingClientRect();
                return [doc.left, doc.right, pg.left, pg.right];
            }
            """);
        Assert.IsTrue(geometry[2] >= geometry[0] - 1 && geometry[3] <= geometry[1] + 1, $"the page lies inside the document region: doc [{geometry[0]:F0},{geometry[1]:F0}] page [{geometry[2]:F0},{geometry[3]:F0}]");
        var leftGap = geometry[2] - geometry[0];
        var rightGap = geometry[1] - geometry[3];
        Assert.IsTrue(Math.Abs(leftGap - rightGap) <= 40, $"the page is centred: left gap {leftGap:F0}, right gap {rightGap:F0}");

        // The zoom of the bar drives the engine and shows in the information block.
        await page.GetByTestId("document-print-preview-zoom-in").ClickAsync();
        await WaitForPredicateAsync(page, "() => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-zoom-percent') || '0') > 0", 10_000);
        await Assertions.Expect(page.GetByTestId("document-print-preview-info-scale")).ToContainTextAsync("%", new() { Timeout = 10_000 });

        // F6 never lands in a ribbon that is not there.
        var focusedRegions = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            await page.Keyboard.PressAsync("F6");
            focusedRegions.Add(await page.EvaluateAsync<string>("() => document.activeElement?.closest('[data-focus-region]')?.getAttribute('data-focus-region') ?? ''"));
        }

        CollectionAssert.DoesNotContain(focusedRegions, "ribbon");

        await page.GetByTestId("document-print-preview-close").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-ribbon-toolbar")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(actions).ToHaveCountAsync(0);
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A phone has no left region in the print preview; the actions are a bar and Close is one tap")]
    public async Task PrintPreview_OnAPhone_HasTheBottomActionBar()
    {
        var page = await OpenHistoryAsync(390, 844, touch: true);
        await ClickRibbonAsync(page, "document-ribbon-tab-view");
        await ClickRibbonAsync(page, "document-open-print-preview");

        var actions = page.Locator("[data-region='doc'] [data-testid='document-print-preview-actions']");
        await Assertions.Expect(actions).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.AreEqual(0, await page.Locator("[data-region='nav']").CountAsync(), "no thumbnail rail on a phone");
        var box = await actions.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.Y + box.Height <= 844 + 1, "the action bar is on screen");
        await page.GetByTestId("document-print-preview-close").TapAsync();
        await Assertions.Expect(actions).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The status bar shows Page X of Y and its zoom buttons drive the engine within 50 - 200 percent")]
    public async Task StatusBar_PageAndZoomControls()
    {
        var page = await OpenHistoryAsync(1440, 900, touch: false);
        var bar = page.GetByTestId("document-status-bar");
        await Assertions.Expect(bar.GetByTestId("document-status-page-count")).ToContainTextAsync("Page 1 of");

        // Phase 11: the 1440 viewport leaves the doc column narrower than the page, so the engine opens the document fitted (below 100 percent); the
        // buttons step from whatever the zoom is, and the reader's step turns the automatic fit off.
        var start = await page.EvaluateAsync<int>("() => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-zoom-percent') || '100')");
        var expected = ((start / 10) + 1) * 10; // the buttons step on the grid of 10 (a fitted 62 percent goes to 70)
        await page.GetByTestId("document-status-zoom-in").ClickAsync();
        await Assertions.Expect(bar.GetByTestId("document-status-zoom")).ToContainTextAsync($"{expected}%", new() { Timeout = 10_000 });
        await WaitForPredicateAsync(page, $"() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-zoom-percent') === '{expected}'", 10_000);
        for (var i = 0; i < 12; i++)
        {
            if (await page.GetByTestId("document-status-zoom-out").IsDisabledAsync())
            {
                break;
            }

            await page.GetByTestId("document-status-zoom-out").ClickAsync();
        }

        await Assertions.Expect(page.GetByTestId("document-status-zoom-out")).ToBeDisabledAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(bar.GetByTestId("document-status-zoom")).ToContainTextAsync("50%");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Edge case: comparing a document with itself shows the empty state, no list and no Open comparison")]
    public async Task CompareDialog_IdenticalDocuments_ShowTheEmptyState()
    {
        var page = await OpenHistoryAsync(1440, 900, touch: false);
        await ClickRibbonAsync(page, "document-ribbon-tab-review");
        await ClickRibbonAsync(page, "document-compare-open");
        await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await Assertions.Expect(page.GetByTestId("document-compare-base-card")).ToBeVisibleAsync();
        await page.GetByTestId("document-compare-target-change").ClickAsync();
        await page.GetByTestId("document-compare-target-source").SelectOptionAsync("Current");
        await Assertions.Expect(page.GetByTestId("document-compare-target-label")).ToHaveTextAsync("Current document");
        await page.GetByTestId("document-compare-run").ClickAsync();

        await Assertions.Expect(page.GetByTestId("document-compare-no-differences")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.AreEqual(0, await page.GetByTestId("document-compare-change").CountAsync());
        Assert.AreEqual(0, await page.GetByTestId("document-compare-open-comparison").CountAsync());
        await page.Context.CloseAsync();
    }

    private async Task<IPage> OpenHistoryAsync(int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={HistoryDocumentId}&showToolbar=true&preferLocalDraft=false", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, $$"""
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect][data-block-id="{{HistoryText}}"]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private async Task<IPage> OpenEditorAsync(int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId=contract-demo", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return page;
    }

    /// <summary>Creates version "Draft", types a marker, saves and creates version "Final"; the versions panel is left open.</summary>
    internal static async Task<string> CreateTwoVersionsAsync(IPage page)
    {
        await ClickRibbonAsync(page, "document-ribbon-tab-view");
        await ClickRibbonAsync(page, "document-open-versions");
        await Assertions.Expect(page.GetByTestId("document-version-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await CreateVersionAsync(page, "Draft");

        var marker = $"phase7marker{DateTimeOffset.UtcNow:HHmmssfff}";
        var point = await page.EvaluateAsync<int[]>(
            """
            () => {
                const nodes = Array.from(document.querySelectorAll('[data-canvas-text-rect]'));
                const node = nodes.find(n => n.getBoundingClientRect().width > 40) ?? nodes[0];
                node.scrollIntoView({ block: 'center' });
                const rect = node.getBoundingClientRect();
                return [Math.round(rect.right - 4), Math.round(rect.top + rect.height / 2)];
            }
            """);
        await page.Mouse.ClickAsync(point[0], point[1]);
        await page.EvaluateAsync("() => document.querySelector('[data-testid=\"document-canvas-hidden-input\"]')?.focus()");
        await page.Keyboard.TypeAsync($" {marker}");
        await Assertions.Expect(page.GetByTestId("document-canvas-a11y-mirror")).ToContainTextAsync(marker, new() { Timeout = 10_000 });

        await page.GetByTestId("document-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-dirty-status")).ToHaveCountAsync(0, new() { Timeout = 15_000 });
        await CreateVersionAsync(page, "Final");
        await Assertions.Expect(VersionEntry(page, "Final")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(VersionEntry(page, "Draft")).ToBeVisibleAsync();
        return marker;
    }

    /// <summary>The list entry of the version with this label (the demo seeds one version of its own, so the position is not stable).</summary>
    internal static ILocator VersionEntry(IPage page, string label)
        => page.Locator("li.tm-document-version-panel__entry").Filter(new() { HasText = label });

    private static async Task CreateVersionAsync(IPage page, string label)
    {
        await page.GetByTestId("document-version-create-open").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-version-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-version-label").FillAsync(label);
        await page.GetByTestId("document-version-label").PressAsync("Enter");
        await Assertions.Expect(page.GetByTestId("document-version-dialog")).ToBeHiddenAsync(new() { Timeout = 10_000 });
    }
}
