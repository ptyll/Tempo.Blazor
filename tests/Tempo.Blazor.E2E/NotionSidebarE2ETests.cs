using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for the Notion editor sidebar and page-tree navigation.
/// Pre-seeded data: 4 root pages (2 favorites), "Engineering Wiki" has 2 sub-pages.
/// </summary>
[TestClass]
public class NotionSidebarE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        using var http = DemoApiTestHttp.CreateClient();
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* ignore if API unavailable or cert untrusted */ }

        var context = await CreateContextAsync();
        var page    = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(2000);
        return page;
    }

    /// <summary>Waits until the sidebar page tree is fully loaded and at least one item is visible.</summary>
    private async Task WaitForSidebarReadyAsync(IPage page)
    {
        var firstItem = page.Locator(".tm-npt-item").First;
        await firstItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Sidebar visibility & toggle
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Sidebar is visible on initial load when ShowSidebar=true")]
    public async Task Sidebar_IsVisible_WhenShowSidebarTrue()
    {
        var page    = await OpenNotionEditorAsync();
        var sidebar = page.Locator(".tm-notion-sidebar").First;
        await sidebar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await sidebar.IsVisibleAsync(), "Sidebar should be visible on load");

        await TakeScreenshotAsync(page, "sidebar_visible");
    }

    [TestMethod]
    [Description("Clicking the sidebar toggle button hides the sidebar")]
    public async Task Sidebar_Toggle_HidesSidebar()
    {
        var page   = await OpenNotionEditorAsync();
        var toggle = page.Locator(".tm-notion-sidebar-toggle").First;
        await toggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await toggle.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // After toggle, sidebar should have --hidden class (not --visible)
        var visibleCount = await page.Locator(".tm-notion-sidebar--visible").CountAsync();
        Assert.AreEqual(0, visibleCount, "Sidebar --visible class should be gone after toggling");

        await TakeScreenshotAsync(page, "sidebar_hidden");
    }

    [TestMethod]
    [Description("Clicking the sidebar toggle twice shows the sidebar again")]
    public async Task Sidebar_Toggle_ShowsSidebar()
    {
        var page   = await OpenNotionEditorAsync();
        var toggle = page.Locator(".tm-notion-sidebar-toggle").First;
        await toggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // First click → hide
        await toggle.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Second click → show again
        await toggle.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var visibleCount = await page.Locator(".tm-notion-sidebar--visible").CountAsync();
        Assert.IsTrue(visibleCount > 0, "Sidebar should be visible again after clicking toggle a second time");

        await TakeScreenshotAsync(page, "sidebar_reshown");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Page tree
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("The page tree lists at least two root pages")]
    public async Task Sidebar_PageTree_ListsPages()
    {
        var page = await OpenNotionEditorAsync();
        await WaitForSidebarReadyAsync(page);

        var itemCount = await page.Locator(".tm-npt-item").CountAsync();
        Assert.IsTrue(itemCount >= 2, $"Page tree should have at least 2 items, found {itemCount}");

        await TakeScreenshotAsync(page, "sidebar_page_tree");
    }

    [TestMethod]
    [Description("Clicking a page title in the tree navigates the editor to that page")]
    public async Task Sidebar_PageTree_ClickPage_NavigatesToPage()
    {
        var page = await OpenNotionEditorAsync();
        await WaitForSidebarReadyAsync(page);

        // Click "Product Roadmap" — different from the initial "Getting Started" page
        var roadmapTitle = page.Locator(".tm-npt-title")
                               .Filter(new LocatorFilterOptions { HasText = "Product Roadmap" })
                               .First;
        await roadmapTitle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await roadmapTitle.ClickAsync();

        // Topbar title should update
        var topbarTitle = page.Locator(".tm-notion-topbar__title").First;
        await topbarTitle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await page.WaitForTimeoutAsync(1000);
        var titleText = await topbarTitle.InnerTextAsync();
        Assert.IsTrue(
            titleText.Contains("Product Roadmap", StringComparison.OrdinalIgnoreCase),
            $"Topbar title should be 'Product Roadmap', got '{titleText}'");

        await TakeScreenshotAsync(page, "sidebar_navigate_to_page");
    }

    [TestMethod]
    [Description("Clicking the expand toggle on Engineering Wiki reveals its sub-pages")]
    public async Task Sidebar_PageTree_ExpandCollapse_SubPages()
    {
        var page = await OpenNotionEditorAsync();
        await WaitForSidebarReadyAsync(page);

        // Find the Engineering Wiki tree item
        var engWikiItem = page.Locator(".tm-npt-item")
                              .Filter(new LocatorFilterOptions { HasText = "Engineering Wiki" })
                              .First;
        await engWikiItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Click the expand/collapse toggle
        var expandToggle = engWikiItem.Locator(".tm-npt-toggle").First;
        await expandToggle.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        // Children list should appear
        var children = engWikiItem.Locator(".tm-npt-children").First;
        await children.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await children.IsVisibleAsync(), "Sub-pages should be visible after expanding Engineering Wiki");

        var childCount = await children.Locator(".tm-npt-item").CountAsync();
        Assert.IsTrue(childCount >= 1, $"Engineering Wiki should have at least 1 child page, found {childCount}");

        await TakeScreenshotAsync(page, "sidebar_subtree_expanded");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Favorites
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Favorites section shows the favorited pages (Getting Started & Product Roadmap)")]
    public async Task Sidebar_Favorites_ShowsFavoritePages()
    {
        var page = await OpenNotionEditorAsync();

        // Favorites section renders when _favorites.Count > 0
        var favSection = page.Locator(".tm-nsf").First;
        await favSection.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await favSection.IsVisibleAsync(), "Favorites section should be visible");

        // Items are expanded by default (_isExpanded = true)
        var favItems = favSection.Locator(".tm-nsf-item");
        await favItems.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var itemCount = await favItems.CountAsync();
        Assert.IsTrue(itemCount >= 1, $"Favorites should have at least 1 item, found {itemCount}");

        await TakeScreenshotAsync(page, "sidebar_favorites");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Sidebar search
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Typing in the sidebar search filters pages and shows matching results")]
    public async Task Sidebar_Search_FiltersPageTree()
    {
        var page = await OpenNotionEditorAsync();
        await WaitForSidebarReadyAsync(page);

        // Open the sidebar search panel
        var searchBtn = page.Locator(".tm-ns-btn-search").First;
        await searchBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await searchBtn.ClickAsync();

        // Type a query
        var searchInput = page.Locator(".tm-ns-search__input").First;
        await searchInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await searchInput.FillAsync("Engineering");
        await page.WaitForTimeoutAsync(1500);

        // Results should include "Engineering Wiki"
        var results = page.Locator(".tm-ns-search__result");
        await results.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var resultCount = await results.CountAsync();
        Assert.IsTrue(resultCount >= 1, $"Search for 'Engineering' should return at least 1 result, got {resultCount}");

        await TakeScreenshotAsync(page, "sidebar_search_results");
    }
}

/// <summary>
/// EB11 screenshot recovery tests for sidebar navigation, trash, and responsive shell states.
/// </summary>
[TestClass]
public class NotionSidebarRecoveryE2ETests : NotionE2ETestBase
{
    private const string Area = "sidebar";

    [TestMethod]
    [Description("EB11 captures deep tree, desktop collapsed, mobile slide-over, and empty navigation baselines.")]
    public async Task EB11_SidebarNavigationStates_AreCaptured()
    {
        await SetViewportAsync(1366, 768);
        await OpenNotionEditorAsync();
        await SeedSidebarDeepPageAsync();
        await ExpandAllVisibleTreeNodesAsync();

        await Page.Locator(".tm-npt-title").Filter(new LocatorFilterOptions { HasText = "EB11 Incident Checklist" }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await AssertNoHorizontalOverflowAsync();
        await CaptureBaselineAsync(Area, "deep-tree-desktop", Sidebar);

        await Page.Locator(".tm-notion-sidebar-toggle").First.ClickAsync();
        await Page.Locator(".tm-notion-sidebar--hidden").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await AssertNoHorizontalOverflowAsync();
        await CaptureBaselineAsync(Area, "desktop-collapsed", Editor);

        await SetViewportAsync(390, 844);
        await OpenNotionEditorAsync();
        await SeedSidebarDeepPageAsync();
        await ShowMobileSidebarAsync();
        await Page.Locator(".tm-notion-sidebar-overlay").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await AssertNoHorizontalOverflowAsync();
        await CaptureBaselineAsync(Area, "mobile-slide-over", Editor);

        await SetViewportAsync(1366, 768);
        await OpenNotionEditorAsync();
        await SeedSidebarEmptyPageAsync();
        await Page.Locator(".tm-nsf-empty").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Page.Locator(".tm-nsr-empty").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Page.Locator(".tm-npt-empty").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await AssertNoHorizontalOverflowAsync();
        await CaptureBaselineAsync(Area, "empty-navigation-sections", Sidebar);
    }

    [TestMethod]
    [Description("EB11 captures empty trash, populated trash, and restore/permanent-delete states.")]
    public async Task EB11_TrashStatesAndActions_AreCaptured()
    {
        await SetViewportAsync(1366, 768);
        await OpenNotionEditorAsync();
        await SeedSidebarDeepPageAsync();
        await OpenTrashAsync();

        await Page.Locator(".tm-nst-empty").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await CaptureBaselineAsync(Area, "empty-trash", Sidebar);

        await OpenNotionEditorAsync();
        await SeedSidebarTrashPageAsync();
        await OpenTrashAsync();

        await Page.Locator(".tm-nst-item").Filter(new LocatorFilterOptions { HasText = "EB11 Legacy Draft" }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Page.Locator(".tm-nst-item").Filter(new LocatorFilterOptions { HasText = "EB11 Retired Specification" }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Page.Locator(".tm-nst-item").Filter(new LocatorFilterOptions { HasText = "EB11 Retired Specification" }).First.HoverAsync();
        await CaptureBaselineAsync(Area, "trash-with-items", Sidebar);

        var legacyDraft = Page.Locator(".tm-nst-item").Filter(new LocatorFilterOptions { HasText = "EB11 Legacy Draft" }).First;
        await legacyDraft.HoverAsync();
        await legacyDraft.Locator(".tm-nst-item__btn--restore").First.ClickAsync();
        await Page.Locator(".tm-nst-item").Filter(new LocatorFilterOptions { HasText = "EB11 Legacy Draft" }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });

        var retiredSpec = Page.Locator(".tm-nst-item").Filter(new LocatorFilterOptions { HasText = "EB11 Retired Specification" }).First;
        await retiredSpec.HoverAsync();
        await retiredSpec.Locator(".tm-nst-item__btn--delete").First.ClickAsync();
        await retiredSpec.Locator(".tm-nst-item__confirm").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await retiredSpec.Locator(".tm-nst-item__confirm-yes").First.ClickAsync();

        await Page.Locator(".tm-nst-empty").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await AssertNoHorizontalOverflowAsync();
        await CaptureBaselineAsync(Area, "trash-after-actions", Sidebar);
    }

    [TestMethod]
    [Description("EB11 captures the tree after a page is drag-reparented under another page.")]
    public async Task EB11_DragMoveReparentedState_IsCaptured()
    {
        await SetViewportAsync(1366, 768);
        await OpenNotionEditorAsync();
        await SeedSidebarDeepPageAsync();

        await DragTreeItemAsync("EB11 Release Checklist", "EB11 Engineering Handbook");
        await ExpandTreeItemAsync("EB11 Engineering Handbook");

        var parentItem = Page.Locator(".tm-npt-item").Filter(new LocatorFilterOptions { HasText = "EB11 Engineering Handbook" }).First;
        await parentItem.Locator(".tm-npt-children .tm-npt-title").Filter(new LocatorFilterOptions { HasText = "EB11 Release Checklist" }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await AssertNoHorizontalOverflowAsync();
        await CaptureBaselineAsync(Area, "drag-move-reparented", Sidebar);
    }

    private ILocator Editor => Page.Locator(".tm-notion-editor").First;
    private ILocator Sidebar => Page.Locator(".tm-notion-sidebar").First;

    private async Task OpenTrashAsync()
    {
        await Page.Locator(".tm-ns-trash__btn").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await Page.Locator(".tm-ns-trash__btn").First.ClickAsync();
        await Page.Locator(".tm-nst").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
    }

    private async Task ShowMobileSidebarAsync()
    {
        var isVisible = await Sidebar.EvaluateAsync<bool>("el => el.classList.contains('tm-notion-sidebar--visible')");
        if (!isVisible)
            await Page.Locator(".tm-notion-sidebar-toggle").First.ClickAsync();

        await Page.Locator(".tm-notion-sidebar--visible").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
    }

    private async Task ExpandAllVisibleTreeNodesAsync()
    {
        for (var pass = 0; pass < 6; pass++)
        {
            var toggles = Page.Locator(".tm-npt-toggle");
            var count = await toggles.CountAsync();

            for (var i = 0; i < count; i++)
            {
                var toggle = toggles.Nth(i);
                if (!await toggle.IsVisibleAsync())
                    continue;

                var treeItem = toggle.Locator("xpath=ancestor::li[contains(@class,'tm-npt-item')][1]");
                var expanded = await treeItem.GetAttributeAsync("aria-expanded");
                if (string.Equals(expanded, "false", StringComparison.OrdinalIgnoreCase))
                {
                    await toggle.ClickAsync(new LocatorClickOptions { Force = true });
                    await Page.WaitForTimeoutAsync(80);
                }
            }
        }
    }

    private async Task ExpandTreeItemAsync(string title)
    {
        var item = Page.Locator(".tm-npt-item").Filter(new LocatorFilterOptions { HasText = title }).First;
        await item.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var expanded = await item.GetAttributeAsync("aria-expanded");
        if (!string.Equals(expanded, "true", StringComparison.OrdinalIgnoreCase))
        {
            await item.Locator(".tm-npt-toggle").First.ClickAsync(new LocatorClickOptions { Force = true });
            await Page.WaitForTimeoutAsync(250);
        }
    }

    private async Task DragTreeItemAsync(string sourceTitle, string targetTitle)
    {
        await Page.Locator(".tm-npt-title").Filter(new LocatorFilterOptions { HasText = sourceTitle }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Page.Locator(".tm-npt-title").Filter(new LocatorFilterOptions { HasText = targetTitle }).First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await Page.EvaluateAsync(
            """
            ([sourceTitle, targetTitle]) => {
                const treeItems = Array.from(document.querySelectorAll('.tm-npt-item'));
                const findItem = title => treeItems.find(item => item.innerText.includes(title));
                const source = findItem(sourceTitle);
                const target = findItem(targetTitle);
                if (!source || !target) {
                    throw new Error(`Could not find source or target tree item: ${sourceTitle} -> ${targetTitle}`);
                }

                const dataTransfer = new DataTransfer();
                source.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer }));
                target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer }));
                target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer }));
                source.dispatchEvent(new DragEvent('dragend', { bubbles: true, cancelable: true, dataTransfer }));
            }
            """,
            new[] { sourceTitle, targetTitle });

        await Page.WaitForTimeoutAsync(750);
    }

    private async Task AssertNoHorizontalOverflowAsync()
    {
        var hasOverflow = await Page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.IsFalse(hasOverflow, "EB11 sidebar state must not introduce document-level horizontal overflow.");
    }
}
