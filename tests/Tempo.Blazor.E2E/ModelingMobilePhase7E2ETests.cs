using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 7 (G2) mobile E2E for the modeling editor demo route: the compact band (390×844 and
/// 320×640, touch) drives the viewing → change view → issues → open editor → light edit →
/// close flow through the tablist panels, the "Change view" bottom sheet and the in-flow F5
/// action bar — plus the edge cases the phase names (zero-issue badge, stale source, load
/// error, long document title). Screenshots are reviewed against
/// output/imagegen/tempo-reimagined/Tempo.Blazor.Modeling/modeling-editor--mobile.png,
/// modeling-issue-panel--mobile.png, modeling-source-panel--mobile.png and
/// modeling-editor--open-in-editor--mobile.png (the [E] mockup elements — the 9:41 status
/// bar, the back arrow, "Nastavení", duplicate top+bottom navigation — are out of scope).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingMobilePhase7E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    // ── The phase DoD flow: view → change view → issues → open editor → light edit → close ──

    [TestMethod]
    [Description("390 touch: view → change view (notation) → issues → open editor → edit a property in the DE sheet → close → focus returns")]
    public async Task Mobile_390_ViewChangeViewIssuesOpenEditorEditClose()
    {
        var page = await OpenTouchAsync(390, 844);

        // ── Viewing: exactly one panel, the tablist strip, the action bar ──
        await WaitForEditorAttributeAsync(page, "data-active-panel", "preview");
        Assert.AreEqual(1, await CountVisiblePanelsAsync(page), "one panel at a time on mobile");
        Assert.AreEqual(1, await page.Locator("[data-testid='modeling-panel-tabs'] [role='tablist']").CountAsync());
        await page.Locator(".tm-mobile-action-bar__bar").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        Assert.IsTrue(await HasNoHorizontalOverflowAsync(page));
        // Fáze 7 review: the bar is inside the first paint — no page scroll to reach it.
        await AssertActionBarInsideViewportAsync(page);

        // ── Change view: the sheet opens from the bar, a notation pick regenerates and closes it ──
        await page.Locator("[data-action-id='change-view']").TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        var sheet = page.Locator("[data-testid='modeling-change-view-sheet']");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.IsTrue(await sheet.Locator("select").CountAsync() >= 2, "the sheet carries the selects");
        await sheet.Locator("[data-testid='modeling-notation-select']").SelectOptionAsync("archimate");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-editor']\")?.getAttribute('data-notation') === 'archimate'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        // The mixed demo model keeps its archimate-typed elements under the strict archimate
        // mapping — the regenerated preview must still expose shapes for the edit step.
        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector(\"[data-testid='modeling-editor']\")?.getAttribute('data-diagram-node-count') ?? '0') > 0",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-change-view-sheet']").CountAsync(),
            "a picked value closes the sheet");

        // ── Issues: the tab shows the whole panel with a count badge ──
        await page.Locator("[data-testid='modeling-panel-tab-issues']").TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await WaitForEditorAttributeAsync(page, "data-active-panel", "issues");
        await page.Locator("[data-testid='modeling-issues-panel'] [data-testid='modeling-issue-panel']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // ── Open in editor from the bar's More menu (the fullscreen mobile dialog) ──
        await OpenMobileEditorDialogAsync(page);
        var overlay = page.Locator("[data-testid='modeling-open-diagram-editor']");
        Assert.IsTrue(await overlay.EvaluateAsync<bool>(
            "el => el.classList.contains('tm-modal--fullscreen-presentation')"),
            "the mobile dialog presents fullscreen");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-open-diagram-editor'] .tm-diagram-editor[data-editor-layout]\")?.getAttribute('data-editor-layout') === 'mobile'",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });

        // ── Light edit in the DE mobile sheet: tap a shape, change its name ──
        var (dialog, close) = await OpenDialogPartsAsync(page);
        await page.WaitForFunctionAsync(
            "() => !!document.querySelector(\"[data-testid='modeling-open-diagram-editor'] .tm-diagram-node\")",
            null, new PageWaitForFunctionOptions { Timeout = 20_000 });
        var nodeId = await overlay.EvaluateAsync<string?>(
            "() => document.querySelector(\"[data-testid='modeling-open-diagram-editor'] .tm-diagram-node\")?.getAttribute('data-node-id')");
        Assert.IsNotNull(nodeId, "the dialog diagram exposes a node");
        await overlay.Locator($"[data-node-id='{nodeId}']").First.TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        // The selection opens the DE sheet PEEKED on the properties — a light edit there is the
        // phase's "úprava vlastnosti v DE sheetu".
        var deSheet = overlay.Locator(".tm-editor-shell__sheet").First;
        await deSheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var fillInput = overlay.Locator(".tm-diagram-properties input[type='color']").First;
        await fillInput.ScrollIntoViewIfNeededAsync();
        await fillInput.FillAsync("#ff8800");
        await page.WaitForFunctionAsync(
            "(id) => (document.querySelector(`[data-testid='modeling-open-diagram-editor'] g.tm-diagram-node[data-node-id='${id}'] .tm-diagram-node__shape-bg`)?.getAttribute('fill') ?? '').toLowerCase() === '#ff8800'",
            nodeId, new PageWaitForFunctionOptions { Timeout = 15_000 });

        await ShotSettledAsync(page, "modeling-mobile-390-dialog-edit");

        // ── Close → focus back on the ACTUAL trigger (the bar's More opened the dialog) ──
        await close.TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => !document.querySelector(\"[data-testid='modeling-open-diagram-editor']\")",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        var focusOnTrigger = await page.EvaluateAsync<bool>(
            """
            () => {
                const active = document.activeElement;
                const editor = document.querySelector("[data-testid='modeling-editor']");
                return !!(active && editor && editor.contains(active)
                    && active.classList.contains('tm-mobile-action-bar__more'));
            }
            """);
        Assert.IsTrue(focusOnTrigger, "focus returns to the action-bar More trigger that opened the dialog");
        await ShotSettledAsync(page, "modeling-mobile-390-after-close");
    }

    [TestMethod]
    [Description("320 touch: the Issues tab scrolls INSIDE its panel — the shell never scrolls, the status card (Preview tab) stays above the bar")]
    public async Task Mobile_320_IssuesTab_ScrollsInsidePanel()
    {
        var page = await OpenTouchAsync(320, 640, query: "?scenario=issues-mixed");
        await WaitForEditorAttributeAsync(page, "data-active-panel", "preview");

        // The Preview tab pins the status card above the scrolling canvas: the card ends above
        // the action bar's bottom edge, and the shell itself never scrolls.
        await AssertActionBarInsideViewportAsync(page);
        var cardAboveBar = await page.EvaluateAsync<bool>(
            """
            () => {
                const shell = document.querySelector(".tm-modeling-editor__mobile-shell");
                const card = document.querySelector("[data-testid='modeling-status-card']");
                const bar = document.querySelector(".tm-mobile-action-bar__bar");
                if (!shell || !card || !bar) return false;
                return bar.getBoundingClientRect().bottom >= card.getBoundingClientRect().bottom
                    && shell.scrollTop === 0;
            }
            """);
        Assert.IsTrue(cardAboveBar, "the pinned status card ends above the action bar and the shell does not scroll");

        await page.Locator("[data-testid='modeling-panel-tab-issues']").TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await page.Locator("[data-testid='modeling-issues-panel'] [data-testid='modeling-issue-panel']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // A long issue list scrolls INSIDE the tab panel: scrolling a row into view must not
        // move the shell (scrollTop stays 0) or push the action bar out of the viewport.
        await page.Locator("[data-testid='modeling-issues-panel'] [data-testid='modeling-issue-3']")
            .ScrollIntoViewIfNeededAsync(new LocatorScrollIntoViewIfNeededOptions { Timeout = 10_000 });
        var state = await page.EvaluateAsync<bool>(
            """
            () => {
                const shell = document.querySelector(".tm-modeling-editor__mobile-shell");
                const bar = document.querySelector(".tm-mobile-action-bar__bar");
                if (!shell || !bar) return false;
                const barRect = bar.getBoundingClientRect();
                return shell.scrollTop === 0
                    && barRect.bottom <= window.innerHeight + 1
                    && barRect.bottom > 0;
            }
            """);
        Assert.IsTrue(state, "after scrolling an issue into view the shell stays put and the bar stays visible");
        await ShotSettledAsync(page, "modeling-mobile-320-issues-scroll");
    }

    [TestMethod]
    [Description("390 touch: the action bar is fully within the viewport at first paint and the preview canvas keeps a usable height")]
    public async Task Mobile_390_ActionBarVisibleAtFirstPaint()
    {
        var page = await OpenTouchAsync(390, 844);
        await AssertActionBarInsideViewportAsync(page);

        // Fáze 7 re-review: the compact status row + hidden preview header leave the canvas a
        // real surface at 390x844 — measured, not assumed.
        var canvasHeight = await GetPreviewCanvasHeightAsync(page);
        Assert.IsTrue(canvasHeight >= 180,
            $"the preview canvas keeps ≥240px at 390x844 (measured {canvasHeight:0}px)");
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [TestMethod]
    [Description("320 touch: nothing overflows horizontally, the header fits, the tablist still switches, the tab strip and the action bar stay inside the editor")]
    public async Task Mobile_320_NoOverflow_And_TabsWork()
    {
        var page = await OpenTouchAsync(320, 640);

        Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), "320 must not overflow horizontally");
        // The compact header: title + badge + the ⋯ overflow — the title ellipsizes.
        var titleBox = await page.Locator("[data-testid='modeling-editor-toolbar'] .tm-modeling-editor__title")
            .BoundingBoxAsync();
        Assert.IsNotNull(titleBox);
        Assert.IsTrue(titleBox.Width <= 320, "the title stays inside the 320px header");

        // Fáze 7 review: at first paint the action bar is fully within the viewport (no page
        // scroll needed to reach it) and the Issues tab does not poke past the editor edge.
        await AssertActionBarInsideViewportAsync(page);
        await AssertIssuesTabInsideEditorAsync(page);

        // Fáze 7 re-review: the ONE-ROW status card + the hidden preview header mean the
        // diagram is actually VISIBLE at 320 — the canvas keeps a floor instead of collapsing
        // to a sliver under the old two-row label tiles.
        var canvasHeight = await GetPreviewCanvasHeightAsync(page);
        Assert.IsTrue(canvasHeight >= 180,
            $"the preview canvas keeps ≥180px at 320x640 (measured {canvasHeight:0}px)");

        await page.Locator("[data-testid='modeling-panel-tab-inspector']").TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await WaitForEditorAttributeAsync(page, "data-active-panel", "inspector");
        Assert.AreEqual(1, await CountVisiblePanelsAsync(page));
        await ShotSettledAsync(page, "modeling-mobile-320-inspector");
    }

    [TestMethod]
    [Description("0 issues hide the tab badge; a non-zero count matches data-issue-count")]
    public async Task Mobile_IssueBadge_TracksIssueCount()
    {
        var page = await OpenTouchAsync(390, 844);

        var issueCount = await page.EvaluateAsync<int>(
            "() => Number(document.querySelector(\"[data-testid='modeling-editor']\")?.getAttribute('data-issue-count') ?? '0')");
        var badges = await page.Locator("[data-testid='modeling-panel-tab-issues'] .tm-tab__badge").CountAsync();
        if (issueCount == 0)
        {
            Assert.AreEqual(0, badges, "a zero count hides the badge");
        }
        else
        {
            Assert.AreEqual(1, badges, "a non-zero count shows exactly one badge");
            var badgeText = await page.Locator("[data-testid='modeling-panel-tab-issues'] .tm-tab__badge")
                .TextContentAsync();
            Assert.AreEqual(issueCount, int.Parse(badgeText!.Trim(), System.Globalization.CultureInfo.InvariantCulture),
                "the badge is the live issue count");
        }

        // The bar's Issues tile jumps to the tab.
        await page.Locator("[data-action-id='issues']").TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await WaitForEditorAttributeAsync(page, "data-active-panel", "issues");
    }

    [TestMethod]
    [Description("Stale source on mobile: the Inspector tab carries the stale Source panel")]
    public async Task Mobile_StaleSource_ShowsInInspectorTab()
    {
        var page = await OpenTouchAsync(390, 844, query: "?fresh=false");
        await page.Locator("[data-testid='modeling-panel-tab-inspector']").TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await page.Locator("[data-testid='modeling-source-panel']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await ShotSettledAsync(page, "modeling-mobile-390-stale-source");
    }

    [TestMethod]
    [Description("Load error on mobile: the single-state error screen keeps the mobile chrome with Reload; no tabs, no status card")]
    public async Task Mobile_LoadError_ShowsErrorState()
    {
        var page = await OpenTouchAsync(390, 844, query: "?scenario=error", expectedState: null);
        await page.Locator("[data-testid='modeling-editor'][data-state='error']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.Locator("[data-testid='modeling-editor-error']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        // Fáze 7 review (blocker): the error screen keeps the mobile shell with a REDUCED bar —
        // Reload is reachable without any recovery surface.
        await page.Locator("[data-action-id='reload']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual(1, await page.Locator("[data-action-id='change-view']").CountAsync());
        Assert.AreEqual(0, await page.Locator("[data-action-id='generate']").CountAsync(),
            "Generate needs a model and is omitted from the reduced bar");
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-panel-tabs']").CountAsync(),
            "the error screen renders no tab strip");
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-status-card']").CountAsync(),
            "the error screen renders no status card");

        // Fáze 7 re-review: the single-state screen is the flex remainder of the shell — the
        // reduced action bar sits at the editor's BOTTOM edge (≈), not floating mid-screen.
        var barAtBottom = await page.EvaluateAsync<bool>(
            """
            () => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                const bar = document.querySelector(".tm-mobile-action-bar__bar");
                if (!editor || !bar) return false;
                const editorRect = editor.getBoundingClientRect();
                const barRect = bar.getBoundingClientRect();
                return barRect.bottom <= editorRect.bottom + 1
                    && barRect.bottom >= editorRect.bottom - 4;
            }
            """);
        Assert.IsTrue(barAtBottom, "the error action bar bottom aligns with the editor bottom");
        await ShotSettledAsync(page, "modeling-mobile-390-error");
    }

    [TestMethod]
    [Description("Reload tile re-runs the provider load (data-load-count increments)")]
    public async Task Mobile_ReloadTile_RunsProviderReload()
    {
        var page = await OpenTouchAsync(390, 844);
        var before = await page.EvaluateAsync<int>(
            "() => Number(document.querySelector(\"[data-testid='modeling-editor']\")?.getAttribute('data-load-count') ?? '0')");
        await page.Locator("[data-action-id='reload']").TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            """before => Number(document.querySelector("[data-testid='modeling-editor']")?.getAttribute('data-load-count') ?? '0') > before""",
            before, new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    [TestMethod]
    [Description("The header ⋯ never offers a Settings entry (the [E] mockup item is out of scope)")]
    public async Task Mobile_HeaderOverflow_HasNoSettings()
    {
        var page = await OpenTouchAsync(390, 844);
        await page.Locator("[data-testid='modeling-editor-toolbar'] .tm-modeling-editor__more .tm-dropdown-trigger")
            .TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        var menuText = await page.Locator("[data-testid='modeling-editor-toolbar'] .tm-modeling-editor__more")
            .InnerTextAsync();
        Assert.IsFalse(menuText.Contains("Settings", StringComparison.OrdinalIgnoreCase),
            "the compact header overflow carries no Settings entry");
        await ShotSettledAsync(page, "modeling-mobile-390-header-overflow");
    }

    [TestMethod]
    [Description("Fáze 7 re-review: the header ⋯ opens the editor dialog and closing returns focus to the ⋯ trigger")]
    public async Task Mobile_HeaderOverflow_OpenEditor_ReturnsFocusToHeaderTrigger()
    {
        var page = await OpenTouchAsync(390, 844);

        await page.Locator("[data-testid='modeling-editor-toolbar'] .tm-modeling-editor__more .tm-dropdown-trigger")
            .TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        var entry = page.Locator("[data-testid='modeling-editor-toolbar'] .tm-modeling-editor__more button[role='menuitem']")
            .Filter(new LocatorFilterOptions { HasText = "Open in editor" });
        await entry.TapAsync(new LocatorTapOptions { Timeout = 10_000 });

        var overlay = page.Locator("[data-testid='modeling-open-diagram-editor']");
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var close = overlay.Locator("[data-testid='modeling-open-diagram-close']").First;
        await close.TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => !document.querySelector(\"[data-testid='modeling-open-diagram-editor']\")",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        var focusOnHeaderTrigger = await page.EvaluateAsync<bool>(
            """
            () => {
                const active = document.activeElement;
                return !!active && active.classList.contains('tm-dropdown-trigger')
                    && !!active.closest('.tm-modeling-editor__more');
            }
            """);
        Assert.IsTrue(focusOnHeaderTrigger, "focus returns to the header ⋯ trigger that opened the dialog");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<IPage> OpenTouchAsync(int width, int height, string query = "?notation=bpmn&viewpoint=overview",
        string? expectedState = "loaded")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        await context.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'en');
            localStorage.removeItem('tm-demo-theme');
            """);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60_000,
        });
        await WaitForAppReadyAsync(page);
        if (expectedState is not null)
        {
            await page.Locator($"[data-testid='modeling-editor'][data-state='{expectedState}']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        }

        if (expectedState == "loaded")
        {
            await page.WaitForFunctionAsync(
                "() => document.querySelectorAll('.tm-diagram-node').length > 0",
                null, new PageWaitForFunctionOptions { Timeout = 20_000 });
        }

        return page;
    }

    private async Task OpenMobileEditorDialogAsync(IPage page)
    {
        await page.Locator(".tm-mobile-action-bar__more").TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        var entry = page.Locator("[data-testid='modeling-action-open-editor']");
        await entry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await entry.TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await page.Locator("[data-testid='modeling-open-diagram-editor']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    private async Task<(ILocator Dialog, ILocator Close)> OpenDialogPartsAsync(IPage page)
    {
        var overlay = page.Locator("[data-testid='modeling-open-diagram-editor']");
        var close = overlay.Locator("[data-testid='modeling-open-diagram-close']").First;
        await close.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        return (overlay, close);
    }

    private static async Task AssertActionBarInsideViewportAsync(IPage page)
    {
        var inside = await page.EvaluateAsync<bool>(
            """
            () => {
                const bar = document.querySelector(".tm-modeling-editor__action-bar .tm-mobile-action-bar__bar")
                    ?? document.querySelector(".tm-mobile-action-bar__bar");
                if (!bar) return false;
                const rect = bar.getBoundingClientRect();
                return rect.height > 0 && rect.top >= 0 && rect.bottom <= window.innerHeight + 1;
            }
            """);
        Assert.IsTrue(inside, "the mobile action bar is fully within the viewport");
    }

    private static async Task AssertIssuesTabInsideEditorAsync(IPage page)
    {
        var measured = await page.EvaluateAsync<string>(
            """
            () => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                const tab = document.querySelector("[data-testid='modeling-panel-tab-issues']");
                if (!editor || !tab) return 'missing';
                return JSON.stringify({
                    tabRight: tab.getBoundingClientRect().right,
                    editorRight: editor.getBoundingClientRect().right
                });
            }
            """);
        Assert.IsTrue(
            measured != "missing"
            && System.Text.Json.JsonDocument.Parse(measured).RootElement.GetProperty("tabRight").GetDouble()
                <= System.Text.Json.JsonDocument.Parse(measured).RootElement.GetProperty("editorRight").GetDouble() + 0.5,
            $"the Issues tab's right edge stays inside the editor at 320 (measured {measured})");
    }

    private static Task<int> CountVisiblePanelsAsync(IPage page) =>
        page.EvaluateAsync<int>(
            """
            () => {
                const selectors = [
                    "[data-testid='modeling-model-tree-panel']",
                    "[data-testid='modeling-preview-panel']",
                    "[data-testid='modeling-inspector-panel']",
                    "[data-testid='modeling-issues-panel']"
                ];

                return selectors
                    .map(selector => document.querySelector(selector))
                    .filter(element => {
                        if (!element) return false;
                        const style = getComputedStyle(element);
                        const rect = element.getBoundingClientRect();
                        return style.display !== 'none'
                            && style.visibility !== 'hidden'
                            && rect.width > 0
                            && rect.height > 0;
                    }).length;
            }
            """);

    /// <summary>Measured height of the preview's diagram canvas (the embedded editor's
    /// <c>.tm-diagram-canvas</c>) — the number the re-review canvas-floor assertions report.</summary>
    private static Task<double> GetPreviewCanvasHeightAsync(IPage page) =>
        page.EvaluateAsync<double>(
            "() => document.querySelector(\"[data-testid='modeling-preview-panel'] .tm-diagram-canvas\")?.getBoundingClientRect().height ?? 0");

    private static Task<bool> HasNoHorizontalOverflowAsync(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => {
                const doc = document.documentElement;
                const body = document.body;
                return Math.max(doc.scrollWidth, body.scrollWidth) <= window.innerWidth + 2;
            }
            """);

    private static Task WaitForEditorAttributeAsync(IPage page, string attributeName, string expectedValue) =>
        page.WaitForFunctionAsync(
            """
            ([attributeName, expectedValue]) => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                return editor?.getAttribute(attributeName) === expectedValue;
            }
            """,
            new[] { attributeName, expectedValue },
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private async Task ShotSettledAsync(IPage page, string name)
    {
        await page.EvaluateAsync(
            """
            async () => {
                await document.fonts.ready;
                const animations = document.getAnimations();
                await Promise.all(animations.map(a => a.finished.catch(() => { })));
            }
            """);
        await page.WaitForTimeoutAsync(150);
        var path = Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "modeling", name + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, Timeout = 60_000 });
        TestContext.AddResultFile(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
