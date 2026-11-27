using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan b75365fb Fáze 4i — the boolean ARIA states of the NotionEditor page chrome and sidebar
/// render as STRINGS in both positions: an open disclosure carries <c>"true"</c>, a closed one
/// <c>"false"</c> — never the invalid empty value a bool produced, never a missing attribute. The
/// touched surfaces are exercised in a real browser on <c>/notion-editor</c> (sidebar sections
/// Recent/Favorites/Pages, the active page in the tree and its context-menu button, the page
/// settings menu with its Export/Import/Move-to submenus and the Full width/Small text/Lock/
/// Favorite menuitemcheckboxes, the watch toggle, the page comments panel, My tasks and the page
/// history inline/side-by-side diff modes), a scoped axe scan (aria-valid-attr-value) runs in both
/// themes plus a phone pass, and screenshots land in
/// <c>__screenshots__/aria-boolean-states/</c> for the UX review (the visual state must be
/// unchanged — every surface styles the state through BEM classes or value-based selectors, never
/// through attribute presence). Two surfaces carry the state in the NAME instead of a state
/// attribute (APG): the watch toggle renders NO <c>aria-pressed</c> (the name flips "Watch page" ↔
/// "Unwatch") and the settings-menu favorite item keeps the invariant name "Favorite" with
/// <c>aria-checked</c> carrying the state.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class NotionAriaBooleanStateE2ETests : NotionE2ETestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";
    private const string Sidebar = ".tm-notion-sidebar";
    private const string SettingsMenu = ".tm-npsm";
    private const string Watch = ".tm-notion-watch";
    private const string CommentPanel = ".tm-npcp";
    private const string TasksPanel = ".tm-notion-tasks-panel";
    private const string HistoryPanel = ".tm-nph";

    private readonly List<string> _clientErrors = [];

    [TestCleanup]
    public void Cleanup() => AssertNoClientErrors();

    // ── Sidebar sections + page tree ───────────────────────────────────────────────────────

    [TestMethod]
    [Description("Sidebar Recent/Favorites/Pages headers, the active tree page and the tree-item menu announce the string state in both positions")]
    public async Task SidebarSections_AnnounceTheStringState_InBothStates()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);
        await page.Locator(".tm-npt-item").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        // Pages section starts expanded, Favorites starts expanded — both flip to "false" and back.
        await AssertDisclosureBothStatesAsync(page, ".tm-ns-section__header", startsExpanded: true, "pages section");
        await AssertDisclosureBothStatesAsync(page, ".tm-nsf-header", startsExpanded: true, "favorites section");
        // Recent starts collapsed on the seeded data (it has pages), expand and collapse it.
        await AssertDisclosureBothStatesAsync(page, ".tm-nsr-header", startsExpanded: false, "recent section");

        // The active page keeps aria-selected="true", every other tree item "false".
        var selected = page.Locator(".tm-npt-item[aria-selected='true']");
        Assert.AreEqual(1, await selected.CountAsync(), "exactly the active page carries aria-selected=\"true\"");
        Assert.IsTrue(await page.Locator(".tm-npt-item[aria-selected='false']").CountAsync() >= 1,
            "inactive pages keep the attribute — the string \"false\"");

        // The tree-item context-menu button announces its menu in both positions.
        var firstItem = page.Locator(".tm-npt-item").First;
        await firstItem.HoverAsync();
        var menuButton = firstItem.Locator(".tm-npt-btn[aria-haspopup='true']").First;
        Assert.AreEqual("false", await menuButton.GetAttributeAsync("aria-expanded"),
            "a closed item menu announces the string \"false\"");
        await menuButton.ClickAsync(new LocatorClickOptions { Force = true });
        await Assertions.Expect(firstItem.Locator(".tm-npt-btn[aria-haspopup='true']").First)
            .ToHaveAttributeAsync("aria-expanded", "true");
        await page.Locator(".tm-npt-backdrop").First.ClickAsync(new LocatorClickOptions { Force = true });
        await Assertions.Expect(firstItem.Locator(".tm-npt-btn[aria-haspopup='true']").First)
            .ToHaveAttributeAsync("aria-expanded", "false");

        await AssertNoAxeViolationsAsync(page, Sidebar, "notion sidebar");
        await CaptureScreenAsync(page, Sidebar, "fze-4i-notion-sidebar-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, Sidebar);
        await AssertNoAxeViolationsAsync(page, Sidebar, "notion sidebar (dark)");
        await CaptureScreenAsync(page, Sidebar, "fze-4i-notion-sidebar-dark-1600.png");
    }

    // ── Page settings menu ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("The page settings trigger, Export/Import/Move-to submenus and the menuitemcheckbox view toggles announce the string state in both positions")]
    public async Task PageSettingsMenu_AnnounceTheStringState_InBothStates()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);
        await page.Locator(".tm-notion-page").First.HoverAsync();

        var trigger = page.Locator(".tm-npsm-trigger").First;
        await trigger.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-expanded"),
            "a closed settings menu announces the string \"false\"");

        await trigger.ClickAsync(new LocatorClickOptions { Force = true });
        var menu = page.Locator(SettingsMenu).First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await Assertions.Expect(page.Locator(".tm-npsm-trigger").First)
            .ToHaveAttributeAsync("aria-expanded", "true");

        // The four menuitemcheckbox view toggles keep their role and announce "true"/"false".
        var checkboxes = menu.Locator("[role='menuitemcheckbox']");
        Assert.AreEqual(4, await checkboxes.CountAsync(), "Full width, Small text, Lock page and the favorite toggle");
        for (var i = 0; i < 4; i++)
        {
            var value = await checkboxes.Nth(i).GetAttributeAsync("aria-checked");
            Assert.IsTrue(value is "true" or "false",
                $"menuitemcheckbox #{i} must carry the string state, got '{value ?? "<missing>"}'");
        }

        // The favorite item keeps the invariant name "Favorite" in both states — aria-checked
        // carries the state (APG: a changing name plus a state attribute would announce it twice).
        var favorite = checkboxes.Nth(3);
        var favoriteLabel = (await favorite.Locator(".tm-npsm__item-label").InnerTextAsync()).Trim();
        Assert.AreEqual("Favorite", favoriteLabel);
        var favoriteBefore = await favorite.GetAttributeAsync("aria-checked");
        await favorite.ClickAsync(); // persists through the demo provider and closes the menu
        await Assertions.Expect(page.Locator(".tm-npsm-trigger").First)
            .ToHaveAttributeAsync("aria-expanded", "false", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });
        await trigger.ClickAsync(new LocatorClickOptions { Force = true });
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var favoriteFlipped = menu.Locator("[role='menuitemcheckbox']").Nth(3);
        Assert.AreEqual(favoriteLabel, (await favoriteFlipped.Locator(".tm-npsm__item-label").InnerTextAsync()).Trim(),
            "the favorite name is invariant in both states");
        await Assertions.Expect(favoriteFlipped)
            .ToHaveAttributeAsync("aria-checked", favoriteBefore == "true" ? "false" : "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });
        // Flip it back so the page keeps its seeded favorites for the following tests.
        await favoriteFlipped.ClickAsync();
        await Assertions.Expect(page.Locator(".tm-npsm-trigger").First)
            .ToHaveAttributeAsync("aria-expanded", "false", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });
        await trigger.ClickAsync(new LocatorClickOptions { Force = true });
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Flip Full width: the checked state moves with the click (the demo provider persists it).
        var fullWidth = checkboxes.Nth(0);
        var before = await fullWidth.GetAttributeAsync("aria-checked");
        await fullWidth.ClickAsync();
        await Assertions.Expect(menu.Locator("[role='menuitemcheckbox']").Nth(0))
            .ToHaveAttributeAsync("aria-checked", before == "true" ? "false" : "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });
        // Flip it back so the page keeps its seeded layout for the screenshot.
        await menu.Locator("[role='menuitemcheckbox']").Nth(0).ClickAsync();
        await Assertions.Expect(menu.Locator("[role='menuitemcheckbox']").Nth(0))
            .ToHaveAttributeAsync("aria-checked", before, new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });

        // Export / Import / Move-to disclosures announce the string state and close each other.
        var disclosures = menu.Locator(".tm-npsm__item[aria-haspopup='true']");
        Assert.AreEqual(3, await disclosures.CountAsync(), "Export, Import and Move to");
        for (var i = 0; i < 3; i++)
        {
            Assert.AreEqual("false", await disclosures.Nth(i).GetAttributeAsync("aria-expanded"),
                $"submenu #{i} starts closed — the string \"false\"");
        }

        await disclosures.Nth(0).ClickAsync(); // Export
        await Assertions.Expect(menu.Locator(".tm-npsm__item[aria-haspopup='true']").Nth(0))
            .ToHaveAttributeAsync("aria-expanded", "true");
        await menu.Locator(".tm-npsm__item[aria-haspopup='true']").Nth(1).ClickAsync(); // Import
        await Assertions.Expect(menu.Locator(".tm-npsm__item[aria-haspopup='true']").Nth(0))
            .ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(menu.Locator(".tm-npsm__item[aria-haspopup='true']").Nth(1))
            .ToHaveAttributeAsync("aria-expanded", "true");
        await menu.Locator(".tm-npsm__item[aria-haspopup='true']").Nth(2).ClickAsync(); // Move to
        await Assertions.Expect(menu.Locator(".tm-npsm__item[aria-haspopup='true']").Nth(1))
            .ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(menu.Locator(".tm-npsm__item[aria-haspopup='true']").Nth(2))
            .ToHaveAttributeAsync("aria-expanded", "true");

        // Switch Small text ON for the screenshots — the checked state must be visible
        // (aria-checked="true" + the filled toggle), not just announced.
        await menu.Locator("[role='menuitemcheckbox']").Nth(1).ClickAsync();
        await Assertions.Expect(menu.Locator("[role='menuitemcheckbox']").Nth(1))
            .ToHaveAttributeAsync("aria-checked", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });

        await AssertNoAxeViolationsAsync(page, SettingsMenu, "page settings menu");
        await CaptureScreenAsync(page, SettingsMenu, "fze-4i-notion-settings-menu-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, SettingsMenu);
        await AssertNoAxeViolationsAsync(page, SettingsMenu, "page settings menu (dark)");
        await CaptureScreenAsync(page, SettingsMenu, "fze-4i-notion-settings-menu-dark-1600.png");

        // Switch Small text back OFF so the page keeps its seeded layout.
        await menu.Locator("[role='menuitemcheckbox']").Nth(1).ClickAsync();
        await Assertions.Expect(menu.Locator("[role='menuitemcheckbox']").Nth(1))
            .ToHaveAttributeAsync("aria-checked", "false", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-npsm-trigger").First)
            .ToHaveAttributeAsync("aria-expanded", "false");
    }

    // ── Watch button ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("The watch toggle carries no aria-pressed — the changing name announces the state (APG)")]
    public async Task WatchButton_CarriesNoAriaPressed_TheChangingNameAnnouncesTheState()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);

        var toggle = page.GetByTestId("notion-watch-toggle");
        await toggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        Assert.IsNull(await toggle.GetAttributeAsync("aria-pressed"),
            "a toggle whose name changes with the state carries no aria-pressed (APG — the state would be announced twice)");
        Assert.AreEqual("Watch page", await toggle.GetAttributeAsync("aria-label"),
            "the unwatched page is named by the action it offers");

        // One click into the watched state: the name flips, aria-pressed stays absent.
        await toggle.ClickAsync();
        await Assertions.Expect(page.GetByTestId("notion-watch-toggle"))
            .ToHaveAttributeAsync("aria-label", "Unwatch", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });
        Assert.IsNull(await page.GetByTestId("notion-watch-toggle").GetAttributeAsync("aria-pressed"));

        // Screenshots in the PRESSED (watching) state — after one click, not two.
        await AssertNoAxeViolationsAsync(page, Watch, "watch button (watching)");
        await CaptureScreenAsync(page, Watch, "fze-4i-notion-watch-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, Watch);
        await AssertNoAxeViolationsAsync(page, Watch, "watch button (watching, dark)");
        await CaptureScreenAsync(page, Watch, "fze-4i-notion-watch-dark-1600.png");

        // Back to the unwatched state for the following tests.
        await page.GetByTestId("notion-watch-toggle").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notion-watch-toggle"))
            .ToHaveAttributeAsync("aria-label", "Watch page", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });
    }

    // ── Page comments panel ────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("The page comments panel toggle announces aria-expanded as a string in both positions")]
    public async Task CommentPanel_AnnouncesTheStringState_InBothStates()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);

        var toggle = page.Locator($"{CommentPanel}__toggle").First;
        await toggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await toggle.ScrollIntoViewIfNeededAsync();
        Assert.AreEqual("false", await toggle.GetAttributeAsync("aria-expanded"),
            "a collapsed comments panel announces the string \"false\"");

        await toggle.ClickAsync();
        await Assertions.Expect(page.Locator($"{CommentPanel}__toggle").First)
            .ToHaveAttributeAsync("aria-expanded", "true");
        await page.Locator($"{CommentPanel}__body").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        await AssertNoAxeViolationsAsync(page, CommentPanel, "page comments panel (expanded)");
        await CaptureScreenAsync(page, CommentPanel, "fze-4i-notion-comments-expanded-light-1600.png");

        await page.Locator($"{CommentPanel}__toggle").First.ClickAsync();
        await Assertions.Expect(page.Locator($"{CommentPanel}__toggle").First)
            .ToHaveAttributeAsync("aria-expanded", "false");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, CommentPanel);
        await AssertNoAxeViolationsAsync(page, CommentPanel, "page comments panel (dark)");
        await CaptureScreenAsync(page, CommentPanel, "fze-4i-notion-comments-dark-1600.png");
    }

    // ── My tasks ───────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("The My tasks completion check announces aria-pressed as a string in both positions")]
    public async Task MyTasks_AnnounceTheStringState_InBothStates()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);
        await SeedTasksPageAsync();

        await ClickNotionCommandAsync(page, "notion-tasks");
        var panel = page.Locator(TasksPanel).First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.Locator(".tm-my-tasks__item").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // The open task announces "false"; completing it (and showing all statuses) announces "true".
        var taskId = await page.Locator(".tm-my-tasks__item").First.GetAttributeAsync("data-task-id");
        Assert.IsFalse(string.IsNullOrWhiteSpace(taskId), "the seeded tasks page renders at least one task");
        var check = page.Locator($"[data-task-id='{taskId}'] .tm-my-tasks__check").First;
        Assert.AreEqual("false", await check.GetAttributeAsync("aria-pressed"),
            "an open task announces the string \"false\"");

        await check.ClickAsync();
        await page.Locator($"[data-task-id='{taskId}']").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });

        await page.Locator(".tm-my-tasks__filters--inline .tm-my-tasks__filter")
            .Filter(new LocatorFilterOptions { HasTextString = "All statuses" }).First.ClickAsync();
        await Assertions.Expect(page.Locator($"[data-task-id='{taskId}'] .tm-my-tasks__check").First)
            .ToHaveAttributeAsync("aria-pressed", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10000 });

        await AssertNoAxeViolationsAsync(page, TasksPanel, "my tasks panel");
        await CaptureScreenAsync(page, TasksPanel, "fze-4i-notion-my-tasks-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, TasksPanel);
        await AssertNoAxeViolationsAsync(page, TasksPanel, "my tasks panel (dark)");
        await CaptureScreenAsync(page, TasksPanel, "fze-4i-notion-my-tasks-dark-1600.png");
    }

    // ── Page history diff modes ────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("The page history inline/side-by-side diff mode buttons announce aria-pressed as a string in both positions")]
    public async Task PageHistory_DiffModes_AnnounceTheStringState_InBothStates()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);
        await SeedHistoryDiffPageAsync();

        // Open the history through the settings menu (the only entry point).
        await page.Locator(".tm-notion-page").First.HoverAsync();
        var trigger = page.Locator(".tm-npsm-trigger").First;
        await trigger.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await trigger.ClickAsync(new LocatorClickOptions { Force = true });
        var menu = page.Locator(SettingsMenu).First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await menu.Locator(".tm-npsm__item").Filter(new LocatorFilterOptions { HasText = "Page history" }).First.ClickAsync();

        var panel = page.Locator(HistoryPanel).First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await panel.Locator(".tm-nph__version-item").Nth(3)
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await panel.Locator("[data-testid='notion-history-pick']").Nth(2).CheckAsync();
        await panel.Locator("[data-testid='notion-history-pick']").Nth(0).CheckAsync();
        var compare = page.GetByTestId("notion-history-compare-selected");
        await compare.ScrollIntoViewIfNeededAsync();
        await compare.ClickAsync();
        await panel.Locator("[data-testid='notion-diff-viewer']").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Inline is the active mode; side-by-side keeps the attribute with "false".
        Assert.AreEqual("true", await panel.GetByTestId("notion-diff-mode-inline").GetAttributeAsync("aria-pressed"),
            "the active diff mode announces the string \"true\"");
        Assert.AreEqual("false", await panel.GetByTestId("notion-diff-mode-side-by-side").GetAttributeAsync("aria-pressed"),
            "the inactive diff mode keeps the attribute — the string \"false\"");

        await AssertNoAxeViolationsAsync(page, HistoryPanel, "page history diff (inline)");
        await CaptureScreenAsync(page, HistoryPanel, "fze-4i-notion-history-inline-light-1600.png");

        await panel.GetByTestId("notion-diff-mode-side-by-side").ClickAsync();
        await Assertions.Expect(panel.GetByTestId("notion-diff-mode-side-by-side"))
            .ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(panel.GetByTestId("notion-diff-mode-inline"))
            .ToHaveAttributeAsync("aria-pressed", "false");
        await panel.Locator(".tm-ndv--sidebyside .tm-ndv__pane--before").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await CaptureScreenAsync(page, HistoryPanel, "fze-4i-notion-history-side-by-side-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, HistoryPanel);
        await AssertNoAxeViolationsAsync(page, HistoryPanel, "page history diff (dark)");
        await CaptureScreenAsync(page, HistoryPanel, "fze-4i-notion-history-dark-1600.png");
    }

    // ── Phone pass ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Phone: the sidebar drawer sections announce the string state in both positions")]
    public async Task Phone_SidebarSections_AnnounceTheStringState()
    {
        var page = await OpenNotionEditorAsync(390, 844);
        HookClientErrors(page);

        // The phone shell keeps the sidebar in a closed drawer — open it through the topbar toggle.
        await page.Locator(".tm-notion-sidebar-toggle").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.Locator(".tm-notion-sidebar-toggle").First.ClickAsync();
        await page.Locator(".tm-notion-sidebar--visible").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await page.Locator(".tm-npt-item").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        // Expand the sheet to the FULL snap before touching the sections: at the default half
        // snap the mobile action bar covers the RECENT header (pre-existing overlap), so the
        // Recent/Pages sections would be hidden in the screenshot and Recent unreachable.
        var sheetHandle = page.Locator(".tm-sheet__handle").First;
        await sheetHandle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await sheetHandle.PressAsync("End");

        await AssertDisclosureBothStatesAsync(page, ".tm-ns-section__header", startsExpanded: true, "pages section (phone)");
        await AssertDisclosureBothStatesAsync(page, ".tm-nsf-header", startsExpanded: true, "favorites section (phone)");
        // Recent is reachable on the phone (with the sheet at full): the header clicks through.
        await AssertDisclosureBothStatesAsync(page, ".tm-nsr-header", startsExpanded: false, "recent section (phone)");

        var selected = page.Locator(".tm-npt-item[aria-selected='true']");
        Assert.AreEqual(1, await selected.CountAsync(), "exactly the active page carries aria-selected=\"true\"");

        await AssertNoAxeViolationsAsync(page, Sidebar, "notion sidebar (phone)");
        await CaptureScreenAsync(page, Sidebar, "fze-4i-notion-sidebar-light-390.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, Sidebar);
        await AssertNoAxeViolationsAsync(page, Sidebar, "notion sidebar (phone, dark)");
        await CaptureScreenAsync(page, Sidebar, "fze-4i-notion-sidebar-dark-390.png");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────

    private async Task AssertDisclosureBothStatesAsync(IPage page, string selector, bool startsExpanded, string label)
    {
        var header = page.Locator(selector).First;
        await header.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var initial = startsExpanded ? "true" : "false";
        var flipped = startsExpanded ? "false" : "true";
        Assert.AreEqual(initial, await header.GetAttributeAsync("aria-expanded"),
            $"the {label} starts {(startsExpanded ? "expanded" : "collapsed")} — the string \"{initial}\"");

        await header.ClickAsync();
        await Assertions.Expect(page.Locator(selector).First).ToHaveAttributeAsync("aria-expanded", flipped);
        await header.ClickAsync();
        await Assertions.Expect(page.Locator(selector).First).ToHaveAttributeAsync("aria-expanded", initial);
    }

    private void HookClientErrors(IPage page)
        => page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    /// <summary>The demo shell's sticky header would cover the top of the element in a screenshot;
    /// hide it for the capture and restore it right after (on /notion-editor the bar is already
    /// static, so this is a no-op safeguard for the other shells).</summary>
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
        await AssertNoBlazorErrorUiAsync(page);
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

    private static async Task AssertNoAxeViolationsAsync(IPage page, string selector, string label)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });
        var violations = await page.EvaluateAsync<string[]>(
            """
            async (selector) => {
                const host = document.querySelector(selector) || document.body;
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const result = await axe.run(host, {
                    runOnly: { type: 'rule', values: ['aria-valid-attr-value'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """,
            selector);
        Assert.AreEqual(0, violations.Length,
            $"axe critical/serious violations on the {label}:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
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
