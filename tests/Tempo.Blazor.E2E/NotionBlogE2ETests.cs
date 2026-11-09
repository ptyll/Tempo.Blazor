using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public sealed class NotionBlogE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF30: blog panel lists chronological posts, creates and publishes a draft, reads post blocks, and captures UX baseline.")]
    public async Task CF30_Blog_HappyPathAndBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedBlogPageAsync();
        await OpenBlogAsync(page);

        var postItems = page.GetByTestId("notion-blog-post-item");
        await Assertions.Expect(postItems.First).ToContainTextAsync("Launch notes for Tempo editor");
        await Assertions.Expect(postItems.Nth(1)).ToContainTextAsync("Knowledge base cleanup");
        await Assertions.Expect(page.GetByTestId("notion-blog-post")).ToContainTextAsync("Launch highlights");
        await CaptureBaselineAsync("blog", "cf30-list-detail", page.Locator(".tm-notion-blog-panel").First);

        await page.GetByTestId("notion-blog-new").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notion-blog-post")).ToContainTextAsync("Untitled blog post", new LocatorAssertionsToContainTextOptions
        {
            Timeout = 10000
        });
        await Assertions.Expect(page.GetByTestId("notion-blog-post")).ToContainTextAsync("Draft");
        await CaptureBaselineAsync("blog", "cf30-draft-post", page.Locator(".tm-notion-blog-panel").First);

        await page.GetByTestId("notion-blog-publish").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notion-blog-post")).ToContainTextAsync("Published", new LocatorAssertionsToContainTextOptions
        {
            Timeout = 10000
        });
        await Assertions.Expect(page.GetByTestId("notion-blog-post")).ToContainTextAsync("Draft the post body here.");

        var capture = await CaptureBaselineAsync("blog", "cf30-list-post", page.Locator(".tm-notion-blog-panel").First);
        await CaptureBaselineAsync("blog", "cf30-published-post", page.Locator(".tm-notion-blog-panel").First);
        TestContext.WriteLine($"UX CF30 blog baseline captured: {capture.FullPagePath} / {capture.RegionPath}");
        TestContext.WriteLine("UX CF30 review: the list/detail split keeps chronological scanning fast while the post body remains calm and page-like through the shared read-only block renderer.");
    }

    [TestMethod]
    [Description("CF30: providerless, empty, draft visibility, and pagination states work.")]
    public async Task CF30_Blog_EdgeCases_Work()
    {
        var providerless = await OpenNotionEditorAsync("?disableBlogProvider=true");
        Assert.AreEqual(0, await providerless.GetByTestId("notion-blog-open").CountAsync(), "Blog entry point should be hidden when no provider is configured.");

        var empty = await OpenNotionEditorAsync();
        await SeedEmptyBlogPageAsync();
        await OpenBlogAsync(empty);
        await Assertions.Expect(empty.GetByTestId("notion-blog-empty")).ToContainTextAsync("No blog posts");
        await CaptureBaselineAsync("blog", "cf30-empty-posts", empty.Locator(".tm-notion-blog-panel").First);

        var page = await OpenNotionEditorAsync();
        await SeedBlogPageAsync();
        await OpenBlogAsync(page);
        var draftItem = page.GetByTestId("notion-blog-post-item").Filter(new LocatorFilterOptions { HasText = "Draft migration checklist" });
        await Assertions.Expect(draftItem).ToContainTextAsync("Draft");
        await draftItem.ClickAsync();
        Assert.AreEqual(1, await page.GetByTestId("notion-blog-publish").CountAsync(), "Opening the seeded draft should expose publish.");

        page = await OpenNotionEditorAsync();
        await SeedManyBlogPostsPageAsync();
        await OpenBlogAsync(page);
        await Assertions.Expect(page.GetByTestId("notion-blog-post-item").First).ToContainTextAsync("Blog pagination entry 01");
        await page.GetByTestId("notion-blog-next").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notion-blog-post-item").First).ToContainTextAsync("Blog pagination entry 06", new LocatorAssertionsToContainTextOptions
        {
            Timeout = 10000
        });
        await CaptureBaselineAsync("blog", "cf30-pagination-page2", page.Locator(".tm-notion-blog-panel").First);
    }

    private static async Task OpenBlogAsync(IPage page)
    {
        await ClickNotionCommandAsync(page, "notion-blog-open");
        await page.GetByTestId("notion-blog-panel").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
    }
}
