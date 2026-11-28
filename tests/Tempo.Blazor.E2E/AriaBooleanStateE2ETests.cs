using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan b75365fb Fáze 4h — the boolean ARIA states of the core / Wireframe / DocumentEditor
/// surfaces and the demo pages render as STRINGS in both positions: the active tab/segment carries
/// <c>"true"</c>, every inactive one <c>"false"</c> — never the invalid empty value a bool
/// produced, never a missing attribute. The affected screens are exercised in a real browser
/// (switching the state both ways), a scoped axe scan (aria-valid-attr-value among the wcag2a/2aa
/// rules) runs in both themes, and screenshots land in
/// <c>__screenshots__/aria-boolean-states/</c> for the UX review (the visual state must be
/// unchanged — every component styles the active state through its BEM <c>--active</c> class, not
/// through the attribute). The spreadsheet dialogs live in <c>SpreadsheetAriaStateE2ETests</c>
/// (partial of <see cref="SpreadsheetE2ETests"/>) and the report designer in
/// <c>ReportDesignerAriaStateE2ETests</c> (self-hosted Report Server).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class AriaBooleanStateE2ETests : WasmTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";
    private const string ActivityLog = ".tm-activity-log";
    private const string PageTabs = ".tm-wd-editor__page-tabs";
    private const string TreePanel = ".tsm-tree-panel";
    private const string EmbeddingTabs = "[role='tablist'][aria-label='Report source mode']";
    private const string TableSelectionPanel = "[data-testid='document-table-selection-properties-panel']";

    private readonly List<string> _clientErrors = [];

    private static Task PinCultureAsync(IBrowserContext context) =>
        context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");

    // ── /activity — TmActivityLog tabs ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task ActivityLog_Tabs_AnnounceTheStringState_InBothStates()
    {
        var page = await OpenScreenAsync("/activity", ActivityLog);

        // The demo hosts a second TmActivityLog further down — scope to the first one.
        var log = page.Locator(ActivityLog).First;
        var tabs = log.Locator(".tm-activity-tab");
        await Assertions.Expect(tabs).ToHaveCountAsync(3);
        Assert.AreEqual("true", await tabs.Nth(0).GetAttributeAsync("aria-selected"),
            "the Timeline tab is active — the string \"true\", never the invalid empty value");
        Assert.AreEqual("false", await tabs.Nth(1).GetAttributeAsync("aria-selected"),
            "an inactive tab keeps the attribute — the string \"false\", never an omitted attribute");
        Assert.AreEqual("false", await tabs.Nth(2).GetAttributeAsync("aria-selected"));

        await tabs.Nth(1).ClickAsync();
        await Assertions.Expect(tabs.Nth(1)).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(tabs.Nth(0)).ToHaveAttributeAsync("aria-selected", "false");
        await Assertions.Expect(tabs.Nth(2)).ToHaveAttributeAsync("aria-selected", "false");

        await tabs.Nth(0).ClickAsync();
        await Assertions.Expect(tabs.Nth(0)).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(tabs.Nth(1)).ToHaveAttributeAsync("aria-selected", "false");

        await AssertNoAxeViolationsAsync(page, ActivityLog, "activity log tabs");
        await CaptureScreenAsync(page, ActivityLog, "fze-4h-activity-tabs-light-1440.png");

        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, ActivityLog);
        await AssertNoAxeViolationsAsync(page, ActivityLog, "activity log tabs (dark)");
        await CaptureScreenAsync(page, ActivityLog, "fze-4h-activity-tabs-dark-1440.png");
        AssertNoClientErrors();
    }

    // ── /wireframe-editor — page tabs ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task WireframeEditor_PageTabs_AnnounceTheStringState_InBothStates()
    {
        var page = await OpenScreenAsync("/wireframe-editor", PageTabs);

        // The main demo editor navigates pages through the Panel (no top tabs) and the read-only
        // preview renders tabs whose Add early-returns — the custom-provider editor below keeps
        // the default Tabs navigation and is editable. Scope to its section.
        const string customTabs = "[data-testid='wf-demo-custom'] .tm-wd-editor__page-tabs";
        var editor = page.Locator("[data-testid='wf-demo-custom'] .tm-wd-editor");
        var tabs = editor.Locator(".tm-wd-editor__page-tab");
        await Assertions.Expect(tabs).ToHaveCountAsync(1);
        Assert.AreEqual("true", await tabs.Nth(0).GetAttributeAsync("aria-selected"),
            "the single page is active — the string \"true\"");

        // The phase contract for these screens is aria-valid-attr-value: the tablist itself
        // carries pre-existing structural debt (the Add/Duplicate buttons inside role=tablist
        // trip axe aria-required-children, and a second page adds a close button inside role=tab
        // — nested-interactive), reported as remaining work, so the full-tag scan is narrowed to
        // the rule this phase owns here.
        await AssertNoAxeViolationsAsync(page, customTabs, "wireframe page tabs", rules: ["aria-valid-attr-value"]);
        await CaptureScreenAsync(page, customTabs, "fze-4h-wireframe-page-tabs-light-1440.png");

        // Add a second page (the demo document starts with one): the new page becomes active, so
        // BOTH states are on screen — the inactive tab keeps aria-selected="false".
        var addButton = editor.Locator(".tm-wd-editor__page-tab-add");
        await addButton.ScrollIntoViewIfNeededAsync();
        await addButton.ClickAsync();
        await Assertions.Expect(tabs).ToHaveCountAsync(2);
        await Assertions.Expect(tabs.Nth(1)).ToHaveAttributeAsync("aria-selected", "true");
        Assert.AreEqual("false", await tabs.Nth(0).GetAttributeAsync("aria-selected"));

        await tabs.Nth(0).ClickAsync();
        await Assertions.Expect(tabs.Nth(0)).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(tabs.Nth(1)).ToHaveAttributeAsync("aria-selected", "false");

        // Both states on screen at once: the active tab "true", the inactive "false".
        await CaptureScreenAsync(page, customTabs, "fze-4h-wireframe-page-tabs-two-pages-light-1440.png");

        // Back to one page for the dark pass (the close button is the pre-existing debt above).
        await tabs.Nth(1).Locator(".tm-wd-editor__page-tab-close").ClickAsync();
        await Assertions.Expect(tabs).ToHaveCountAsync(1);

        // The wireframe page hides the shell sidebar (and with it the theme toggle) at ≥1400px —
        // EnsureDemoThemeAsync dispatches the toggle in-page instead of clicking it.
        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, customTabs);
        await AssertNoAxeViolationsAsync(page, customTabs, "wireframe page tabs (dark)", rules: ["aria-valid-attr-value"]);
        await CaptureScreenAsync(page, customTabs, "fze-4h-wireframe-page-tabs-dark-1440.png");
        AssertNoClientErrors();
    }

    // ── /wireframe-editor — toolbox filter tabs ────────────────────────────────────────────

    [TestMethod]
    public async Task WireframeToolbox_FilterTabs_AnnounceTheStringState_InBothStates()
    {
        var page = await OpenScreenAsync("/wireframe-editor", "[data-testid='wf-demo-custom'] .tm-wd-editor");

        // The custom-provider editor is the only one whose toolbox renders the filter tabs (All /
        // Built-in / Custom — the Custom tab exists because the demo provider adds components).
        const string toolbox = "[data-testid='wf-demo-custom']";
        var filters = page.Locator($"{toolbox} .tm-wd-toolbox__filters");
        var tabs = page.Locator($"{toolbox} .tm-wd-toolbox__filter-btn");
        if (!await filters.IsVisibleAsync())
        {
            // The editor resolves its layout from its own container: in the tablet band the left
            // panel is an overlay drawer — open it through the toolbar toggle first.
            await page.Locator($"{toolbox} [data-testid='wd-toggle-left']").First.ClickAsync();
            await Assertions.Expect(filters).ToBeVisibleAsync(new() { Timeout = 10_000 });
        }

        await Assertions.Expect(tabs).ToHaveCountAsync(3);
        Assert.AreEqual("true", await tabs.Nth(0).GetAttributeAsync("aria-selected"),
            "the All filter is active — the string \"true\", never the invalid empty value");
        Assert.AreEqual("false", await tabs.Nth(1).GetAttributeAsync("aria-selected"),
            "an inactive filter keeps the attribute — the string \"false\", never an omitted attribute");
        Assert.AreEqual("false", await tabs.Nth(2).GetAttributeAsync("aria-selected"));

        await tabs.Nth(2).ClickAsync();
        await Assertions.Expect(tabs.Nth(2)).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(tabs.Nth(0)).ToHaveAttributeAsync("aria-selected", "false");
        await Assertions.Expect(tabs.Nth(1)).ToHaveAttributeAsync("aria-selected", "false");

        await tabs.Nth(0).ClickAsync();
        await Assertions.Expect(tabs.Nth(0)).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(tabs.Nth(2)).ToHaveAttributeAsync("aria-selected", "false");

        // The phase contract for these screens is aria-valid-attr-value (the tablist has
        // pre-existing structural debt outside this phase — see the page-tabs test above).
        await AssertNoAxeViolationsAsync(page, $"{toolbox} .tm-wd-toolbox__filters", "wireframe toolbox filters", rules: ["aria-valid-attr-value"]);
        await CaptureScreenAsync(page, $"{toolbox} .tm-wd-toolbox__filters", "fze-4h-wireframe-toolbox-filters-light-1440.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, $"{toolbox} .tm-wd-toolbox__filters");
        await AssertNoAxeViolationsAsync(page, $"{toolbox} .tm-wd-toolbox__filters", "wireframe toolbox filters (dark)", rules: ["aria-valid-attr-value"]);
        await CaptureScreenAsync(page, $"{toolbox} .tm-wd-toolbox__filters", "fze-4h-wireframe-toolbox-filters-dark-1440.png");
        AssertNoClientErrors();
    }

    // ── /test-suite-manager — phone tree accordion ─────────────────────────────────────────

    [TestMethod]
    public async Task TestSuiteManager_TreeToggle_Phone_AnnouncesTheStringState()
    {
        // Edge case: the accordion toggle renders only on a phone-width container (≤768 px).
        var page = await OpenScreenAsync("/test-suite-manager", TreePanel, width: 390, height: 844, touch: true);

        var toggle = page.Locator($"{TreePanel} .tsm-tree-toggle");
        await Assertions.Expect(toggle).ToBeVisibleAsync();
        Assert.AreEqual("true", await toggle.GetAttributeAsync("aria-expanded"),
            "the tree starts open — the string \"true\"");

        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.AreEqual("none", await page.Locator($"{TreePanel} .tsm-tree-body").EvaluateAsync<string>(
                "el => getComputedStyle(el).display"),
            "the closed accordion hides the tree body");
        await CaptureScreenAsync(page, TreePanel, "fze-4h-test-suite-manager-tree-closed-light-390.png");

        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");

        await AssertNoAxeViolationsAsync(page, TreePanel, "test suite manager tree (phone)");
        await CaptureScreenAsync(page, TreePanel, "fze-4h-test-suite-manager-tree-light-390.png");

        // The 390 px shell hides the header theme toggle — EnsureDemoThemeAsync dispatches the
        // toggle in-page instead of clicking it.
        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, TreePanel);
        await AssertNoAxeViolationsAsync(page, TreePanel, "test suite manager tree (phone, dark)");
        await CaptureScreenAsync(page, TreePanel, "fze-4h-test-suite-manager-tree-dark-390.png");
        AssertNoClientErrors();
    }

    // ── /report-embedding — source-mode tabs ───────────────────────────────────────────────

    [TestMethod]
    public async Task ReportEmbedding_ModeTabs_AnnounceTheStringState_InBothStates()
    {
        var page = await OpenScreenAsync("/report-embedding", EmbeddingTabs);

        var embedded = page.Locator("[data-testid='embedding-mode-embedded']");
        var remote = page.Locator("[data-testid='embedding-mode-remote']");
        Assert.AreEqual("true", await embedded.GetAttributeAsync("aria-selected"),
            "the embedded source is the default mode — the string \"true\"");
        Assert.AreEqual("false", await remote.GetAttributeAsync("aria-selected"),
            "the inactive mode keeps the attribute — the string \"false\"");

        await remote.ClickAsync();
        await Assertions.Expect(remote).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(embedded).ToHaveAttributeAsync("aria-selected", "false");

        await embedded.ClickAsync();
        await Assertions.Expect(embedded).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(remote).ToHaveAttributeAsync("aria-selected", "false");

        await AssertNoAxeViolationsAsync(page, EmbeddingTabs, "report embedding mode tabs");
        await CaptureScreenAsync(page, EmbeddingTabs, "fze-4h-report-embedding-tabs-light-1440.png");

        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, EmbeddingTabs);
        await AssertNoAxeViolationsAsync(page, EmbeddingTabs, "report embedding mode tabs (dark)");
        await CaptureScreenAsync(page, EmbeddingTabs, "fze-4h-report-embedding-tabs-dark-1440.png");
        AssertNoClientErrors();
    }

    // ── /canvas-engine-host (document editor) — table & cell properties segments ──────────

    [TestMethod]
    public async Task DocumentEditor_TableAndCellSegments_AnnounceTheStringState_InBothStates()
    {
        await DocumentEditorE2EReset.ResetAsync();
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-12-canvas-history-save&showToolbar=true", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-canvas-table-cell][data-cell-id="canvas-history-table-h-category"]')
            """, 30_000);

        // The properties panel resolves the active cell from the SELECTION — click into the cell
        // (verified), then open the context menu (the phase-7 recipe).
        for (var attempt = 0; ; attempt++)
        {
            var target = await ReadCellRectAsync(page, "canvas-history-table-h-category");
            await page.Mouse.ClickAsync((float)target.CenterX, (float)target.CenterY);
            var selected = await page.EvaluateAsync<string>(
                "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-selection-cell-id') || ''");
            if (selected == "canvas-history-table-h-category")
            {
                break;
            }

            if (attempt >= 9)
            {
                Assert.Fail($"Click kept resolving to cell '{selected}'.");
            }

            await page.WaitForTimeoutAsync(250);
        }

        var cell = await ReadCellRectAsync(page, "canvas-history-table-h-category");
        await page.Mouse.ClickAsync((float)cell.CenterX, (float)cell.CenterY, new MouseClickOptions { Button = MouseButton.Right });
        await Assertions.Expect(page.GetByTestId("document-table-context-menu")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-table-table-properties").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-table-properties-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The phase-12 seed table is center-aligned; the pressed segment carries "true", the rest
        // "false" — never the invalid empty value, never a missing attribute.
        Assert.AreEqual("false", await page.GetByTestId("document-table-properties-align-left").GetAttributeAsync("aria-pressed"));
        Assert.AreEqual("true", await page.GetByTestId("document-table-properties-align-center").GetAttributeAsync("aria-pressed"));
        Assert.AreEqual("false", await page.GetByTestId("document-table-properties-align-right").GetAttributeAsync("aria-pressed"));

        // The cell vertical alignment defaults to Top.
        Assert.AreEqual("true", await page.GetByTestId("document-cell-properties-valign-top").GetAttributeAsync("aria-pressed"));
        Assert.AreEqual("false", await page.GetByTestId("document-cell-properties-valign-middle").GetAttributeAsync("aria-pressed"));
        Assert.AreEqual("false", await page.GetByTestId("document-cell-properties-valign-bottom").GetAttributeAsync("aria-pressed"));

        // Flip both groups and assert the states move with the click.
        await page.GetByTestId("document-table-properties-align-left").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-table-properties-align-left")).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(page.GetByTestId("document-table-properties-align-center")).ToHaveAttributeAsync("aria-pressed", "false");

        await page.GetByTestId("document-cell-properties-valign-middle").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-cell-properties-valign-middle")).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(page.GetByTestId("document-cell-properties-valign-top")).ToHaveAttributeAsync("aria-pressed", "false");

        await AssertNoAxeViolationsAsync(page, TableSelectionPanel, "table and cell properties panels");
        await CaptureScreenAsync(page, TableSelectionPanel, "fze-4h-document-editor-properties-light-1440.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, TableSelectionPanel);
        await AssertNoAxeViolationsAsync(page, TableSelectionPanel, "table and cell properties panels (dark)");
        await CaptureScreenAsync(page, TableSelectionPanel, "fze-4h-document-editor-properties-dark-1440.png");
        AssertNoClientErrors();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────

    private async Task<IPage> OpenScreenAsync(string path, string readySelector, int width = 1440, int height = 1000, bool touch = false)
    {
        var page = await OpenAppPageAsync(
            $"{BaseUrl}{path}",
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
                HasTouch = touch,
                IsMobile = touch,
            },
            PinCultureAsync);
        page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };
        await page.Locator(readySelector).First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    private static Task<CellRect> ReadCellRectAsync(IPage page, string cellId)
        => page.EvaluateAsync<CellRect>(
            """
            cellId => {
                const node = document.querySelector(`[data-canvas-table-cell][data-cell-id="${cellId}"]`);
                if (!node) {
                    throw new Error(`Canvas table cell metadata not found: ${cellId}`);
                }

                const rect = node.getBoundingClientRect();
                return {
                    x: rect.x,
                    y: rect.y,
                    width: rect.width,
                    height: rect.height,
                    centerX: rect.x + rect.width / 2,
                    centerY: rect.y + rect.height / 2
                };
            }
            """,
            cellId);

    private sealed class CellRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
    }

    /// <summary>The demo shell's sticky header would cover the top of the element in a screenshot;
    /// hide it for the capture and restore it right after.</summary>
    private static Task SetStickyHeaderVisibleAsync(IPage page, bool visible)
        => page.EvaluateAsync(
            """
            (visible) => {
                const header = document.querySelector('[data-demo-shell="main"] > .sticky');
                if (header) {
                    header.style.visibility = visible ? '' : 'hidden';
                }
            }
            """, visible);

    private async Task CaptureScreenAsync(IPage page, string selector, string fileName)
    {
        var directory = Path.Combine(
            FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "aria-boolean-states");
        Directory.CreateDirectory(directory);
        await WaitForLayoutToSettleAsync(page, selector);
        await SetStickyHeaderVisibleAsync(page, false);
        try
        {
            var path = Path.Combine(directory, fileName);
            await page.Locator(selector).First.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
            TestContext.AddResultFile(path);
        }
        finally
        {
            await SetStickyHeaderVisibleAsync(page, true);
        }
    }

    private static async Task AssertNoAxeViolationsAsync(IPage page, string selector, string label, string[]? rules = null)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });
        var violations = await page.EvaluateAsync<string[]>(
            """
            async ({ selector, rules }) => {
                const host = document.querySelector(selector) || document.body;
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const runOnly = rules && rules.length
                    ? { type: 'rule', values: rules }
                    : { type: 'tag', values: ['wcag2a', 'wcag2aa'] };
                const result = await axe.run(host, { runOnly, resultTypes: ['violations'] });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """,
            new { selector, rules });
        Assert.AreEqual(0, violations.Length,
            $"axe critical/serious violations on the {label}:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("repo root not found");
    }
}
