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

        // ── Close → focus back inside the editor chrome (the mobile focus-return host) ──
        await close.TapAsync(new LocatorTapOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => !document.querySelector(\"[data-testid='modeling-open-diagram-editor']\")",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        var focusInside = await page.EvaluateAsync<bool>(
            """
            () => {
                const active = document.activeElement;
                const editor = document.querySelector("[data-testid='modeling-editor']");
                return !!(active && editor && editor.contains(active));
            }
            """);
        Assert.IsTrue(focusInside, "focus returns into the editor chrome after the dialog closes");
        await ShotSettledAsync(page, "modeling-mobile-390-after-close");
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [TestMethod]
    [Description("320 touch: nothing overflows horizontally, the header fits and the tablist still switches")]
    public async Task Mobile_320_NoOverflow_And_TabsWork()
    {
        var page = await OpenTouchAsync(320, 640);

        Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), "320 must not overflow horizontally");
        // The compact header: title + badge + the ⋯ overflow — the title ellipsizes.
        var titleBox = await page.Locator("[data-testid='modeling-editor-toolbar'] .tm-modeling-editor__title")
            .BoundingBoxAsync();
        Assert.IsNotNull(titleBox);
        Assert.IsTrue(titleBox.Width <= 320, "the title stays inside the 320px header");

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
    [Description("Load error on mobile: the single-state error screen renders without the chrome")]
    public async Task Mobile_LoadError_ShowsErrorState()
    {
        var page = await OpenTouchAsync(390, 844, query: "?scenario=error", expectedState: null);
        await page.Locator("[data-testid='modeling-editor'][data-state='error']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.Locator("[data-testid='modeling-editor-error']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual(0, await page.Locator(".tm-mobile-action-bar__bar").CountAsync(),
            "the error screen replaces the workspace (and the bar with it)");
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
