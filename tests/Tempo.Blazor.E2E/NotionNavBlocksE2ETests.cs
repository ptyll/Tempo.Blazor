using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for navigation block types: ChildPage, LinkedPage, and Breadcrumb.
/// All tests start on Page1 ("Getting Started with Notion Editor") which contains
/// pre-seeded ChildPage (→ Product Roadmap) and LinkedPage (→ Meeting Notes) blocks.
/// Breadcrumb path tests navigate to Page5 ("Architecture Guide", child of Page4)
/// so the breadcrumb renders two crumbs.
/// </summary>
[TestClass]
public class NotionNavBlocksE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        using var http = DemoApiTestHttp.CreateClient();
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* API may not be running; ignore */ }

        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(2000);
        return page;
    }

    /// <summary>
    /// Scrolls to the first ChildPage block on the page and returns its locator.
    /// </summary>
    private async Task<ILocator> ScrollToChildPageBlockAsync(IPage page)
    {
        var block = page.Locator(".tm-child-page").First;
        await block.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await block.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);
        return block;
    }

    /// <summary>
    /// Scrolls to the first LinkedPage block on the page and returns its locator.
    /// </summary>
    private async Task<ILocator> ScrollToLinkedPageBlockAsync(IPage page)
    {
        var block = page.Locator(".tm-linked-page").First;
        await block.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await block.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);
        return block;
    }

    /// <summary>
    /// Navigates the editor to Page5 ("Architecture Guide") using the sidebar search (Ctrl+K).
    /// Page5 is a child of Page4 ("Engineering Wiki") so its breadcrumb shows two crumbs.
    /// </summary>
    private async Task NavigateToArchitectureGuideAsync(IPage page)
    {
        // Open sidebar search
        var searchBtn = page.Locator(".tm-ns-btn-search").First;
        await searchBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await searchBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        // Type into the search input
        var searchInput = page.Locator(".tm-ns-search__input").First;
        await searchInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await searchInput.FillAsync("Architecture Guide");
        await page.WaitForTimeoutAsync(600);

        // Click the matching result
        var result = page.Locator(".tm-ns-search__result").Filter(new LocatorFilterOptions { HasText = "Architecture Guide" }).First;
        await result.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await result.ClickAsync();
        await page.WaitForTimeoutAsync(1500);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ChildPage block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("ChildPage block renders with the child page title and icon")]
    public async Task ChildPageBlock_Renders_WithTitle()
    {
        var page = await OpenNotionEditorAsync();
        var block = await ScrollToChildPageBlockAsync(page);

        var title = block.Locator(".tm-child-page__title").First;
        await title.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var titleText = await title.InnerTextAsync();
        Assert.IsTrue(
            titleText.Contains("Product Roadmap"),
            $"ChildPage block title should contain 'Product Roadmap' but was '{titleText}'");

        await TakeScreenshotAsync(page, "navblock_childpage_render");
    }

    [TestMethod]
    [Description("Clicking a ChildPage block navigates the editor to the child page")]
    public async Task ChildPageBlock_Click_NavigatesToPage()
    {
        var page = await OpenNotionEditorAsync();
        var block = await ScrollToChildPageBlockAsync(page);

        await block.ClickAsync();
        await page.WaitForTimeoutAsync(2000);

        // After navigation the editor should show the Product Roadmap page content
        var heading = page.Locator(".tm-notion-page").Locator("text=Product Roadmap").First;
        await heading.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await heading.IsVisibleAsync(),
            "After clicking the ChildPage block the editor should display the 'Product Roadmap' page content");

        await TakeScreenshotAsync(page, "navblock_childpage_click");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  LinkedPage block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("LinkedPage block renders with the linked page title")]
    public async Task LinkedPageBlock_Renders_WithTitle()
    {
        var page = await OpenNotionEditorAsync();
        var block = await ScrollToLinkedPageBlockAsync(page);

        var title = block.Locator(".tm-linked-page__title").First;
        await title.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var titleText = await title.InnerTextAsync();
        Assert.IsTrue(
            titleText.Contains("Meeting Notes"),
            $"LinkedPage block title should contain 'Meeting Notes' but was '{titleText}'");

        await TakeScreenshotAsync(page, "navblock_linkedpage_render");
    }

    [TestMethod]
    [Description("Clicking a LinkedPage block navigates the editor to the linked page")]
    public async Task LinkedPageBlock_Click_NavigatesToPage()
    {
        var page = await OpenNotionEditorAsync();
        var block = await ScrollToLinkedPageBlockAsync(page);

        await block.ClickAsync();
        await page.WaitForTimeoutAsync(2000);

        // After navigation the editor should show Meeting Notes page content
        var heading = page.Locator(".tm-notion-page").Locator("text=Weekly Team Meeting").First;
        await heading.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await heading.IsVisibleAsync(),
            "After clicking the LinkedPage block the editor should display the 'Weekly Team Meeting' page content");

        await TakeScreenshotAsync(page, "navblock_linkedpage_click");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Breadcrumb block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Breadcrumb block on a child page renders the full ancestor path")]
    public async Task BreadcrumbBlock_Renders_ShowsPath()
    {
        var page = await OpenNotionEditorAsync();
        await NavigateToArchitectureGuideAsync(page);

        // Wait for the breadcrumb block on Page5 to load
        var breadcrumb = page.Locator(".tm-notion-breadcrumb").First;
        await breadcrumb.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 12000 });

        // The skeleton disappears once crumbs are loaded
        await page.WaitForSelectorAsync(".tm-notion-breadcrumb__skeleton-row",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        var crumbs = breadcrumb.Locator(".tm-notion-breadcrumb__item");
        await crumbs.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        var crumbCount = await crumbs.CountAsync();
        Assert.IsTrue(crumbCount >= 2,
            $"Breadcrumb on a child page should show at least 2 crumbs (parent + current) but found {crumbCount}");

        var allText = await breadcrumb.InnerTextAsync();
        Assert.IsTrue(allText.Contains("Engineering Wiki"),
            $"Breadcrumb should contain 'Engineering Wiki' (parent page) but got: '{allText}'");
        Assert.IsTrue(allText.Contains("Architecture Guide"),
            $"Breadcrumb should contain 'Architecture Guide' (current page) but got: '{allText}'");

        await TakeScreenshotAsync(page, "navblock_breadcrumb_render");
    }

    [TestMethod]
    [Description("Clicking a parent crumb in a breadcrumb block navigates to that parent page")]
    public async Task BreadcrumbBlock_Click_NavigatesToParent()
    {
        var page = await OpenNotionEditorAsync();
        await NavigateToArchitectureGuideAsync(page);

        var breadcrumb = page.Locator(".tm-notion-breadcrumb").First;
        await breadcrumb.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 12000 });

        await page.WaitForSelectorAsync(".tm-notion-breadcrumb__skeleton-row",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        // Click the parent (non-current) crumb link — "Engineering Wiki"
        var parentLink = breadcrumb.Locator(".tm-notion-breadcrumb__item--link").First;
        await parentLink.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await parentLink.ClickAsync();
        await page.WaitForTimeoutAsync(2000);

        // After navigating to the parent the editor should show Engineering Wiki content
        var heading = page.Locator(".tm-notion-page").Locator("text=Engineering Wiki").First;
        await heading.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await heading.IsVisibleAsync(),
            "After clicking the parent breadcrumb the editor should display the 'Engineering Wiki' page content");

        await TakeScreenshotAsync(page, "navblock_breadcrumb_click");
    }
}

[TestClass]
public class NotionNavBlocksRecoveryE2ETests : NotionE2ETestBase
{
    private static ILocator Block(IPage page, string id) =>
        page.Locator($"[data-block-id='{id}']").First;

    private static async Task<ILocator> VisibleBlockAsync(IPage page, string id, string selector)
    {
        var block = Block(page, id);
        await block.Locator(selector).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });
        await block.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(350);
        return block;
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB15 recovery baseline for synced origin/ref and navigation special blocks.")]
    public async Task EB15_NavigationAndSyncedBlocks_CapturesBaselines()
    {
        var page = await OpenNotionEditorAsync();
        await SeedSpecialBlocksPageAsync();

        var syncedOrigin = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000080", ".tm-synced-origin");
        await syncedOrigin.Locator(".tm-synced-origin__body").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });
        await CaptureBaselineAsync("special-blocks", "synced-origin", syncedOrigin);

        var syncedRef = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000090", ".tm-synced-ref");
        await syncedRef.Locator(".tm-synced-ref__body").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });
        await CaptureBaselineAsync("special-blocks", "synced-ref", syncedRef);

        var childPage = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000100", ".tm-child-page");
        await childPage.Locator(".tm-child-page__title").Filter(new LocatorFilterOptions { HasText = "Product Roadmap" }).First.WaitForAsync();
        await CaptureBaselineAsync("special-blocks", "child-page", childPage);

        var linkedPage = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000101", ".tm-linked-page");
        await linkedPage.Locator(".tm-linked-page__title").Filter(new LocatorFilterOptions { HasText = "Meeting Notes" }).First.WaitForAsync();
        await CaptureBaselineAsync("special-blocks", "linked-page", linkedPage);

        var breadcrumb = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000102", ".tm-notion-breadcrumb");
        await page.WaitForSelectorAsync(".tm-notion-breadcrumb__skeleton-row", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 30000
        });
        await CaptureBaselineAsync("special-blocks", "breadcrumb", breadcrumb);

        var tableOfContents = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000110", ".tm-toc");
        await tableOfContents.Locator(".tm-toc__item").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });
        await CaptureBaselineAsync("special-blocks", "table-of-contents", tableOfContents);
    }
}
