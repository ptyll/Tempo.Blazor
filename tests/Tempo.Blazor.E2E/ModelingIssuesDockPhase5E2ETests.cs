using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 5 (MO1) E2E for the issues dock under the preview and the inspector's Source tab:
/// the collapse/expand contract, the Source-tab reload path and the 0/500-issue edge cases
/// (the list scrolls INSIDE the dock; the preview is never overlapped).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingIssuesDockPhase5E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("The issues dock collapses to its header (body hidden, rows unfocusable) and expands again, keeping aria-expanded in sync")]
    public async Task IssuesDock_CollapseAndExpand_KeepsAriaExpanded()
    {
        // The uniform 500-row stress list keeps the geometry assertions free of the mixed
        // scenario's taller recommendation-box rows.
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-500");

        var toggle = page.Locator("[data-testid='modeling-issues-toggle']");
        Assert.AreEqual("true", await toggle.GetAttributeAsync("aria-expanded"), "The dock starts expanded.");
        await ExpectVisibleAsync(page, "[data-testid='modeling-issue-list']");
        var expandedPreviewHeight = await PreviewHeightAsync(page);

        await toggle.ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        // The body STAYS in the DOM under [hidden] (a11y review, phase 5) — aria-controls always
        // points at a real id; wait for the HIDDEN state instead of a detached list.
        await page.Locator("[data-testid='modeling-issue-panel'] .tm-modeling-issue-panel__content")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        var collapsedState = await page.EvaluateAsync<CollapseProbe>(
            """
            () => {
                const panel = document.querySelector("[data-testid='modeling-issue-panel']");
                const body = panel?.querySelector('.tm-modeling-issue-panel__content');
                const header = panel?.querySelector('.tm-modeling-issue-panel__header');
                const toggle = document.querySelector("[data-testid='modeling-issues-toggle']");
                const display = body ? getComputedStyle(body).display : '';
                const focusable = body
                    ? [...body.querySelectorAll('button, [href], input, select, textarea, [tabindex]')]
                        .some(el => el.getBoundingClientRect().height > 0)
                    : false;
                return {
                    hasHiddenAttribute: !!body && body.hasAttribute('hidden'),
                    bodyDisplay: display,
                    hasVisibleFocusable: focusable,
                    ariaControlsMatches: toggle?.getAttribute('aria-controls') === body?.id,
                    dockHeight: panel?.getBoundingClientRect().height ?? 0,
                    headerHeight: header?.getBoundingClientRect().height ?? 0
                };
            }
            """);
        Assert.IsTrue(collapsedState.HasHiddenAttribute, "The collapsed dock hides its body under [hidden].");
        Assert.AreEqual("none", collapsedState.BodyDisplay, "The collapsed body computes display:none ([hidden] must win over display:grid).");
        Assert.IsFalse(collapsedState.HasVisibleFocusable, "No row of the collapsed dock stays visible/focusable.");
        Assert.IsTrue(collapsedState.AriaControlsMatches, "The aria-controls target keeps its id.");
        Assert.AreEqual("false", await toggle.GetAttributeAsync("aria-expanded"), "The collapsed dock hides the list.");

        // Review fix (phase 5): the collapsed dock is its header — the parent's auto row gives the
        // panel exactly the header height (plus the panel's own border), so the canvas row GROWS.
        Assert.IsTrue(collapsedState.DockHeight <= collapsedState.HeaderHeight + 2,
            $"The collapsed dock is ≈ its header (dock {collapsedState.DockHeight:0.#}px, header {collapsedState.HeaderHeight:0.#}px).");
        var collapsedPreviewHeight = await PreviewHeightAsync(page);
        Assert.IsTrue(collapsedPreviewHeight > expandedPreviewHeight,
            $"The preview grows when the dock collapses (was {expandedPreviewHeight:0.#}px, now {collapsedPreviewHeight:0.#}px).");

        // The state is the session's: an unrelated re-render (a viewport nudge) must not re-open it.
        await page.Mouse.WheelAsync(0, -10);
        await page.WaitForTimeoutAsync(200);
        Assert.AreEqual("false", await page.Locator("[data-testid='modeling-issues-toggle']").GetAttributeAsync("aria-expanded"));

        await page.Locator("[data-testid='modeling-issues-toggle']").ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        await ExpectVisibleAsync(page, "[data-testid='modeling-issue-list']");
        Assert.AreEqual("true", await page.Locator("[data-testid='modeling-issues-toggle']").GetAttributeAsync("aria-expanded"));

        // The collapsed/expanded dock never overlaps the preview it sits under.
        Assert.IsFalse(await IssuesDockOverlapsPreviewAsync(page), "The dock must stay under the preview row.");
        await SaveStableScreenshotAsync(page, "issues-dock-expanded.png");
    }

    [TestMethod]
    [Description("Switching to the Source tab and reloading drives the editor's ReloadModelAsync")]
    public async Task SourceTab_Reload_IncrementsLoadCount()
    {
        var page = await OpenLoadedModelingPageAsync("?delay=200");
        var initialLoadCount = await GetLoadCountAsync(page);

        await page.Locator("[data-testid='modeling-tab-source']").ClickAsync(new LocatorClickOptions { Timeout = 10000 });
        await ExpectVisibleAsync(page, "[data-testid='modeling-source-panel']");
        await page.Locator("[data-testid='modeling-source-load-button']").ClickAsync(new LocatorClickOptions { Timeout = 10000 });

        await WaitForLoadCountAsync(page, initialLoadCount + 1);
        Assert.AreEqual(initialLoadCount + 1, await GetLoadCountAsync(page));
    }

    [TestMethod]
    [Description("Zero issues render the empty state inside the dock without overlapping the preview")]
    public async Task IssuesDock_ZeroIssues_ShowsEmptyStateWithoutOverlap()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-empty");

        await ExpectVisibleAsync(page, "[data-testid='modeling-issue-empty']");
        var count = await page.Locator("[data-testid='modeling-issue-panel']").GetAttributeAsync("data-issue-count");
        Assert.AreEqual("0", count);
        Assert.IsFalse(await IssuesDockOverlapsPreviewAsync(page));

        // The dock is scrollable as a region and the preview keeps its own row height.
        Assert.IsTrue(await DockAndPreviewStackVerticallyAsync(page), "The dock must sit BELOW the preview.");
        await SaveStableScreenshotAsync(page, "issues-dock-empty.png");
    }

    [TestMethod]
    [Description("500 issues scroll inside the dock (parent-owned height) without preview overlap or page overflow")]
    public async Task IssuesDock_500Issues_ScrollInsideTheDock()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-500");

        var panelCount = await page.Locator("[data-testid='modeling-issue-panel']").GetAttributeAsync("data-issue-count");
        Assert.AreEqual("500", panelCount, "The demo scenario must load the 500-issue stress model.");

        // The list scrolls inside the dock: the scroll happens on the list element and the last
        // row becomes visible without moving the PAGE (the dock height is parent-owned).
        var scrollState = await page.EvaluateAsync<bool>(
            """
            () => {
                const list = document.querySelector("[data-testid='modeling-issue-list']");
                if (!list) return false;
                list.scrollTop = list.scrollHeight;
                const scrolled = list.scrollTop > 0;
                const pageMoved = Math.abs((document.scrollingElement?.scrollTop ?? 0)) > 1;
                return scrolled && !pageMoved;
            }
            """);
        Assert.IsTrue(scrollState, "The 500-issue list must scroll inside the dock, not the page.");

        await ExpectVisibleAsync(page, "[data-testid='modeling-issue-499']");
        Assert.IsFalse(await IssuesDockOverlapsPreviewAsync(page), "Even at 500 issues the dock stays under the preview.");
        Assert.IsTrue(await DockAndPreviewStackVerticallyAsync(page), "The dock and the preview stack vertically.");
        Assert.IsTrue(await HasNoHorizontalOverflowAsync(page), "The 500-issue dock must not create horizontal overflow.");

        await SaveStableScreenshotAsync(page, "issues-dock-500.png");
    }

    [TestMethod]
    [Description("Fáze 11: collapsing the dock with an ACTIVE filter hides the bar with the body and the filter state survives the collapse (it lives in the editor)")]
    public async Task IssuesDock_CollapseWithActiveFilter_HidesBarAndKeepsState()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-mixed");

        var panel = page.Locator("[data-testid='modeling-issue-panel']");

        // Activate a severity chip filter first (the Error chip narrows the list to one row).
        await panel.Locator("[data-testid='modeling-issue-severity-chip-error']")
            .ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-issue-panel']\")?.getAttribute('data-visible-issue-count') === '1'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // Collapse: the bar (inside the body) hides with the list.
        await page.Locator("[data-testid='modeling-issues-toggle']").ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-issue-panel'] .tm-modeling-issue-panel__content\")?.hasAttribute('hidden') === true",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        var barHidden = await page.EvaluateAsync<bool>(
            """
            () => {
                const bar = document.querySelector("[data-testid='modeling-issue-panel'] [data-testid='modeling-issue-filterbar']");
                return !!bar && bar.closest('.tm-modeling-issue-panel__content').hasAttribute('hidden');
            }
            """);
        Assert.IsTrue(barHidden, "the collapsed dock hides the filter bar with the body");

        // Expand: the chip state comes back — the filter lives in the EDITOR.
        await page.Locator("[data-testid='modeling-issues-toggle']").ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-issue-panel']\")?.getAttribute('data-visible-issue-count') === '1'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual("true", await panel.Locator("[data-testid='modeling-issue-severity-chip-error']")
            .GetAttributeAsync("aria-pressed"), "the chip state survives the dock collapse");
    }

    private async Task<IPage> OpenLoadedModelingPageAsync(string query = "")
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.Locator("[data-testid='modeling-issue-panel']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 5000)
    {
        await page.Locator(selector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    private static async Task<int> GetLoadCountAsync(IPage page)
    {
        var value = await page.Locator("[data-testid='modeling-editor']").GetAttributeAsync("data-load-count");
        return int.TryParse(value, out var count) ? count : 0;
    }

    private static async Task WaitForLoadCountAsync(IPage page, int expectedCount)
    {
        await page.WaitForFunctionAsync(
            """
            expected => {
                const editor = document.querySelector("[data-testid='modeling-editor']");
                return Number(editor?.getAttribute('data-load-count') ?? '0') === expected;
            }
            """,
            expectedCount,
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    /// <summary>The preview row's height — the section above the dock grows when the dock collapses.</summary>
    private static Task<double> PreviewHeightAsync(IPage page) =>
        page.EvaluateAsync<double>(
            "() => document.querySelector(\"[data-testid='modeling-preview-panel']\")?.getBoundingClientRect().height ?? 0");

    private sealed class CollapseProbe
    {
        public bool HasHiddenAttribute { get; set; }
        public string BodyDisplay { get; set; } = string.Empty;
        public bool HasVisibleFocusable { get; set; }
        public bool AriaControlsMatches { get; set; }
        public double DockHeight { get; set; }
        public double HeaderHeight { get; set; }
    }

    private static Task<bool> IssuesDockOverlapsPreviewAsync(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => {
                const panel = document.querySelector("[data-testid='modeling-issue-panel']");
                const preview = document.querySelector("[data-testid='modeling-preview-panel']");
                if (!panel || !preview) {
                    return true;
                }

                const a = panel.getBoundingClientRect();
                const b = preview.getBoundingClientRect();
                return !(a.right <= b.left || a.left >= b.right || a.bottom <= b.top || a.top >= b.bottom);
            }
            """);

    private static Task<bool> DockAndPreviewStackVerticallyAsync(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => {
                const panel = document.querySelector("[data-testid='modeling-issue-panel']");
                const preview = document.querySelector("[data-testid='modeling-preview-panel']");
                if (!panel || !preview) return false;
                const a = panel.getBoundingClientRect();
                const b = preview.getBoundingClientRect();
                return a.top >= b.bottom - 1 && a.left >= b.left - 1 && a.right <= b.right + 1;
            }
            """);

    private static Task<bool> HasNoHorizontalOverflowAsync(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1
            """);

    /// <summary>Stable evidence screenshot under TestResults/modeling-phase5 (not a tracked baseline).</summary>
    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingIssuesDockPhase5E2ETests).Assembly.Location)!,
            "..", "..", "..", "TestResults", "modeling-phase5");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the rendered modeling editor.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
