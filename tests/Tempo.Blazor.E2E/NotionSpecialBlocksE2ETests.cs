using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for special block types: TableOfContents and TemplateButton.
/// Page1 has both blocks pre-seeded (TOC at order 41, TemplateButton with 2 blocks at order 42).
/// </summary>
[TestClass]
public class NotionSpecialBlocksE2ETests : WasmTestBase
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

    private async Task<ILocator> ScrollToTocBlockAsync(IPage page)
    {
        var toc = page.Locator(".tm-toc").First;
        await toc.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await toc.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);
        return toc;
    }

    private async Task<ILocator> ScrollToTemplateButtonAsync(IPage page)
    {
        var btn = page.Locator(".tm-template-btn").First;
        await btn.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await btn.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);
        return btn;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  TableOfContents
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("TableOfContents block renders and lists headings from the current page")]
    public async Task TableOfContents_Renders_ListsHeadings()
    {
        var page = await OpenNotionEditorAsync();
        var toc = await ScrollToTocBlockAsync(page);

        // TOC nav should appear once headings are collected from the page
        var nav = toc.Locator(".tm-toc__nav");
        await nav.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var items = toc.Locator(".tm-toc__item");
        await items.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        var count = await items.CountAsync();
        Assert.IsTrue(count > 0, $"TOC should list at least one heading but found {count}");

        // Page1 contains "Welcome to Notion Editor" (H1) — it should appear in the TOC
        var allText = await nav.InnerTextAsync();
        Assert.IsTrue(
            allText.Contains("Welcome to Notion Editor"),
            $"TOC should contain the H1 heading 'Welcome to Notion Editor' but got: '{allText}'");

        await TakeScreenshotAsync(page, "special_toc_render");
    }

    [TestMethod]
    [Description("Clicking a TOC item scrolls the editor to the target heading")]
    public async Task TableOfContents_Click_ScrollsToHeading()
    {
        var page = await OpenNotionEditorAsync();
        var toc = await ScrollToTocBlockAsync(page);

        var nav = toc.Locator(".tm-toc__nav");
        await nav.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Get the text of the last TOC item (deep in the page — ensures scroll happens)
        var items = toc.Locator(".tm-toc__item");
        await items.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        var lastItem = items.Last;
        var targetText = await lastItem.InnerTextAsync();

        await lastItem.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        // After click the TOC should still be rendered (no crash)
        Assert.IsTrue(await toc.IsVisibleAsync(), "TOC block should still be visible after clicking an entry");

        // The target heading element should exist in the page content
        var heading = page.Locator(".tm-notion-page")
            .Locator($"text={targetText.Trim()}").First;
        await heading.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        Assert.IsTrue(await heading.IsVisibleAsync(),
            $"The heading '{targetText.Trim()}' should be visible in the page after TOC click");

        await TakeScreenshotAsync(page, "special_toc_click");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  TemplateButton
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the TemplateButton trigger inserts the template blocks into the page")]
    public async Task TemplateButton_Click_InsertsTemplateBlocks()
    {
        var page = await OpenNotionEditorAsync();

        // Count existing blocks before insert
        var allBlocks = page.Locator("[data-notion-block]");
        await allBlocks.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var countBefore = await allBlocks.CountAsync();

        var btn = await ScrollToTemplateButtonAsync(page);

        // The trigger button (not the config gear)
        var trigger = btn.Locator(".tm-template-btn__trigger").First;
        await trigger.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await trigger.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        var countAfter = await page.Locator("[data-notion-block]").CountAsync();
        Assert.IsTrue(countAfter > countBefore,
            $"Clicking the template button should insert blocks (before: {countBefore}, after: {countAfter})");

        await TakeScreenshotAsync(page, "special_template_insert");
    }

    [TestMethod]
    [Description("Clicking the configure gear on a TemplateButton opens the config panel with an editable label")]
    public async Task TemplateButton_Label_Editable()
    {
        var page = await OpenNotionEditorAsync();
        var btn = await ScrollToTemplateButtonAsync(page);

        // Click the gear / configure toggle
        var configToggle = btn.Locator(".tm-template-btn__config-toggle").First;
        await configToggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await configToggle.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        // Config panel should open
        var configPanel = btn.Locator(".tm-template-btn__config");
        await configPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await configPanel.IsVisibleAsync(), "Config panel should open after clicking the gear");

        // Label input should be present and editable
        var labelInput = configPanel.Locator(".tm-template-btn__label-input").First;
        await labelInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });

        const string newLabel = "My Custom Template";
        await labelInput.FillAsync(newLabel);
        await page.WaitForTimeoutAsync(300);

        var inputValue = await labelInput.InputValueAsync();
        Assert.AreEqual(newLabel, inputValue,
            $"Label input should contain the new value '{newLabel}' but was '{inputValue}'");

        await TakeScreenshotAsync(page, "special_template_label");
    }
}

[TestClass]
public class NotionSpecialBlocksRecoveryE2ETests : NotionE2ETestBase
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
    [Description("EB15 recovery baseline for equation, bookmark, embed and template button special blocks.")]
    public async Task EB15_SpecialBlocks_EquationBookmarkEmbedTemplate_CapturesBaselines()
    {
        var page = await OpenNotionEditorAsync();
        await SeedSpecialBlocksPageAsync();

        var validEquation = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000010", ".tm-notion-equation-block");
        await CaptureBaselineAsync("special-blocks", "equation-valid", validEquation);

        var invalidEquation = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000011", ".tm-notion-equation-block");
        await CaptureBaselineAsync("special-blocks", "equation-invalid-fallback", invalidEquation);

        var resolvedBookmark = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000020", ".tm-notion-bookmark-block");
        await resolvedBookmark.Locator(".tm-notion-bookmark-block__title").Filter(new LocatorFilterOptions { HasText = "Tempo Notion special blocks" }).First.WaitForAsync();
        await CaptureBaselineAsync("special-blocks", "bookmark-og-resolved", resolvedBookmark);

        var providerFallback = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000021", ".tm-notion-bookmark-block");
        await providerFallback.Locator(".tm-notion-bookmark-block__domain").Filter(new LocatorFilterOptions { HasText = "fallback.tempo.local" }).First.WaitForAsync();
        await CaptureBaselineAsync("special-blocks", "bookmark-provider-fallback", providerFallback);

        var staticFallback = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000022", ".tm-notion-bookmark-block");
        await staticFallback.Locator(".tm-notion-bookmark-block__title").Filter(new LocatorFilterOptions { HasText = "Static fallback release notes" }).First.WaitForAsync();
        await CaptureBaselineAsync("special-blocks", "bookmark-static-fallback", staticFallback);

        var codePenEmbed = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000030", ".tm-notion-embed-block");
        await codePenEmbed.Locator("iframe").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 30000 });
        await CaptureBaselineAsync("special-blocks", "embed-codepen-detected", codePenEmbed);

        var genericEmbed = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000031", ".tm-notion-embed-block");
        await genericEmbed.Locator("iframe").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 30000 });
        await CaptureBaselineAsync("special-blocks", "embed-generic-unknown", genericEmbed);

        var templateButton = await VisibleBlockAsync(page, "eb150000-0000-0000-0000-000000000120", ".tm-template-btn");
        await templateButton.Locator(".tm-template-btn__trigger").Filter(new LocatorFilterOptions { HasText = "Insert release checklist" }).First.WaitForAsync();
        await CaptureBaselineAsync("special-blocks", "template-button", templateButton);
    }
}
