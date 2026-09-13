using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for the Notion editor mention menu.
/// <list type="bullet">
///   <item>@@ trigger (People / Pages / Date tabs)</item>
///   <item>[[ trigger (PagesOnly mode — no tabs)</item>
/// </list>
/// Pre-seeded users: Alice Johnson, Bob Smith, Charlie Brown, Diana Prince, Demo User.
/// Pre-seeded pages: Getting Started, Project Roadmap, Team Wiki, Engineering Wiki (+children).
/// </summary>
[TestClass]
public class NotionMentionMenuE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        using var http = new HttpClient();
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

    /// <summary>
    /// Focuses the first editable paragraph, uses JS to place the cursor at the
    /// absolute end of the block content (avoiding visual-line ambiguity with End key),
    /// presses Enter to create a fresh empty block, waits for initKeyboardHandler
    /// to attach, then types the trigger via keyboard.
    /// Returns the new empty block locator so callers can inspect its children.
    /// </summary>
    private async Task<ILocator> TypeTriggerAsync(IPage page, string trigger)
    {
        // Wait for the first paragraph to be ready
        var firstPara = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await firstPara.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Use JS to focus the first paragraph and place the cursor at its very end
        // (keyboard End key only moves to end of the current visual line)
        await page.EvaluateAsync(@"() => {
            const el = document.querySelector('.tm-notion-paragraph[contenteditable=""true""]');
            if (!el) return;
            el.focus();
            const range = document.createRange();
            range.selectNodeContents(el);
            range.collapse(false); // false = collapse to end
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
        }");

        // Press Enter to create a new empty block after the first paragraph
        await page.Keyboard.PressAsync("Enter");

        // Wait for the new block to render and for initKeyboardHandler to attach
        await page.WaitForTimeoutAsync(1200);

        // Type the trigger — keyboard events fire input events on the focused block
        await page.Keyboard.TypeAsync(trigger);

        // Return a locator for the currently focused paragraph (the new one)
        return page.Locator(".tm-notion-paragraph[contenteditable='true']").Last;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  @@ Mention menu (People / Pages / Date)
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Typing @ at a word boundary opens the full mention menu with tabs")]
    public async Task MentionMenu_DoubleAt_Opens()
    {
        var page = await OpenNotionEditorAsync();

        // New empty block — no word-boundary prefix needed
        await TypeTriggerAsync(page, "@");

        var menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await menu.IsVisibleAsync(), "Mention menu should appear after typing @");

        // Full-mode: tabs (People / Pages / Date) must be visible
        var tabs = menu.Locator(".tm-nmm__tabs").First;
        Assert.IsTrue(await tabs.IsVisibleAsync(), "Tab bar should be visible in full mention mode");

        await TakeScreenshotAsync(page, "mention_menu_open");
    }

    [TestMethod]
    [Description("Typing in the mention search input filters the user list")]
    public async Task MentionMenu_TypeName_FiltersUsers()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "@");

        var menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // The search input is auto-focused, but click it to be safe
        var searchInput = menu.Locator(".tm-nmm__search-input").First;
        await searchInput.ClickAsync();
        await searchInput.FillAsync("Al");
        await page.WaitForTimeoutAsync(500);

        var items = menu.Locator(".tm-nmm__item");
        var count = await items.CountAsync();
        Assert.IsTrue(count >= 1, $"At least 1 result should match 'Al', got {count}");

        var firstTitle = await items.First.Locator(".tm-nmm__item-title").First.InnerTextAsync();
        Assert.IsTrue(firstTitle.Contains("Al", StringComparison.OrdinalIgnoreCase),
            $"First result title should contain 'Al', got '{firstTitle}'");

        await TakeScreenshotAsync(page, "mention_menu_filtered");
    }

    [TestMethod]
    [Description("Clicking a user in the mention menu inserts a user mention chip into the block")]
    public async Task MentionMenu_Click_InsertsUserMention()
    {
        var page  = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "@");

        var menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Wait for items to load, then select the first item via keyboard Enter.
        // The search input is auto-focused by Blazor after menu opens, so Enter
        // reliably routes through HandleKeyDownAsync → SelectCurrentAsync → insertMentionChip.
        var firstItem = menu.Locator(".tm-nmm__item").First;
        await firstItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.Keyboard.PressAsync("Enter");

        // Menu closes after selection
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 8000 });
        await page.WaitForTimeoutAsync(500); // allow JS chip insertion to settle

        // Chip is inserted into the contenteditable by JS (search page-wide, not scoped to block)
        var chip = page.Locator(".tm-notion-mention.tm-notion-mention--user").First;
        Assert.IsTrue(await chip.IsVisibleAsync(), "User mention chip should be visible inside the block");

        await TakeScreenshotAsync(page, "mention_chip_inserted");
    }

    [TestMethod]
    [Description("Pressing Escape while the mention menu is open closes it")]
    public async Task MentionMenu_Escape_Closes()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "@");

        var menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await page.WaitForTimeoutAsync(400); // let auto-focus complete

        await page.Keyboard.PressAsync("Escape");

        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
        Assert.IsFalse(await menu.IsVisibleAsync(), "Mention menu should be closed after pressing Escape");

        await TakeScreenshotAsync(page, "mention_menu_escaped");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  [[ Page link menu (PagesOnly)
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Typing [[ opens the page link menu in PagesOnly mode (no tabs)")]
    public async Task PageLinkMenu_DoubleBracket_Opens()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "[[");

        var menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await menu.IsVisibleAsync(), "Page link menu should appear after typing [[");

        // PagesOnly mode: tab bar must NOT be rendered
        var tabCount = await menu.Locator(".tm-nmm__tabs").CountAsync();
        Assert.AreEqual(0, tabCount, "Tab bar should not be present in PagesOnly mode");

        await TakeScreenshotAsync(page, "pagelink_menu_open");
    }

    [TestMethod]
    [Description("Typing a page name in the [[ search input filters the page list")]
    public async Task PageLinkMenu_TypeName_FiltersPages()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "[[");

        var menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var searchInput = menu.Locator(".tm-nmm__search-input").First;
        await searchInput.ClickAsync();
        await searchInput.FillAsync("Getting");
        await page.WaitForTimeoutAsync(500);

        var items = menu.Locator(".tm-nmm__item");
        var count = await items.CountAsync();
        Assert.IsTrue(count >= 1, $"At least 1 page should match 'Getting', got {count}");

        var firstTitle = await items.First.Locator(".tm-nmm__item-title").First.InnerTextAsync();
        Assert.IsTrue(firstTitle.Contains("Getting", StringComparison.OrdinalIgnoreCase),
            $"First result title should contain 'Getting', got '{firstTitle}'");

        await TakeScreenshotAsync(page, "pagelink_menu_filtered");
    }

    [TestMethod]
    [Description("Clicking a page in the [[ menu inserts a page link chip into the block")]
    public async Task PageLinkMenu_Click_InsertsPageLink()
    {
        var page  = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "[[");

        var menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Wait for items then select via keyboard Enter (search input is auto-focused)
        var firstItem = menu.Locator(".tm-nmm__item").First;
        await firstItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.Keyboard.PressAsync("Enter");

        // Menu closes after selection
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 8000 });
        await page.WaitForTimeoutAsync(500); // allow JS chip insertion to settle

        // Page chip is inserted into the contenteditable by JS (search page-wide)
        var chip = page.Locator(".tm-notion-mention.tm-notion-mention--page").First;
        Assert.IsTrue(await chip.IsVisibleAsync(), "Page mention chip should be visible inside the block");

        await TakeScreenshotAsync(page, "pagelink_chip_inserted");
    }

    [TestMethod]
    [Description("Pressing Escape while the page link menu is open closes it")]
    public async Task PageLinkMenu_Escape_Closes()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "[[");

        var menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Explicitly click the search input to ensure it has focus before pressing Escape
        var searchInput = menu.Locator(".tm-nmm__search-input").First;
        await searchInput.ScrollIntoViewIfNeededAsync();
        await searchInput.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        await page.Keyboard.PressAsync("Escape");

        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
        Assert.IsFalse(await menu.IsVisibleAsync(), "Page link menu should be closed after pressing Escape");

        await TakeScreenshotAsync(page, "pagelink_menu_escaped");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB5: captures @ mention menu, no-results state, diacritics search, and inserted user chip")]
    public async Task EB5_MentionMenuNoResultsDiacriticsAndUserChip_CaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedMentionTokenPageAsync(page);

        await TypeTriggerAsync(page, "@");
        var menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await AssertWithinViewportAsync(menu, "EB5 @ mention menu");
        await CaptureBaselineAsync(page, "mention-menu", "eb5-at-menu-open", menu);

        var input = menu.Locator(".tm-nmm__search-input").First;
        await input.FillAsync("definitely-no-mention-match");
        var emptyState = menu.Locator(".tm-nmm__status").First;
        await emptyState.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await emptyState.IsVisibleAsync(), "@ mention no-results state should be visible.");
        await CaptureBaselineAsync(page, "mention-menu", "eb5-at-no-results", menu);

        await input.FillAsync("Žaneta");
        var zaneta = menu.Locator(".tm-nmm__item").Filter(new LocatorFilterOptions { HasText = "Žaneta Černá" }).First;
        await zaneta.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.Keyboard.PressAsync("Enter");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 8000 });

        var chip = page.Locator(".tm-notion-mention.tm-notion-mention--user").Filter(new LocatorFilterOptions { HasText = "Žaneta Černá" }).First;
        await chip.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await CaptureBaselineAsync(page, "mention-menu", "eb5-user-chip", chip);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB5: captures [[ page-link menu, no-results state, and long page title chip truncation")]
    public async Task EB5_PageLinkMenuNoResultsAndLongTitleChip_CaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedMentionTokenPageAsync(page);

        await TypeTriggerAsync(page, "[[");
        var menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.AreEqual(0, await menu.Locator(".tm-nmm__tabs").CountAsync(), "Page-link menu should not show mention tabs.");
        await AssertWithinViewportAsync(menu, "EB5 page-link menu");
        await CaptureBaselineAsync(page, "mention-menu", "eb5-page-link-menu-open", menu);

        var input = menu.Locator(".tm-nmm__search-input").First;
        await input.FillAsync("definitely-no-page-match");
        var emptyState = menu.Locator(".tm-nmm__status").First;
        await emptyState.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await emptyState.IsVisibleAsync(), "Page-link no-results state should be visible.");
        await CaptureBaselineAsync(page, "mention-menu", "eb5-page-link-no-results", menu);

        await page.Keyboard.PressAsync("Escape");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 8000 });

        await TypeTriggerAsync(page, "[[");
        menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        input = menu.Locator(".tm-nmm__search-input").First;
        await input.FillAsync("Very Long Page Link");
        var longPageItem = menu.Locator(".tm-nmm__item").Filter(new LocatorFilterOptions { HasText = "Very Long Page Link Title" }).First;
        await longPageItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.Keyboard.PressAsync("Enter");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 8000 });

        var chip = page.Locator(".tm-notion-mention.tm-notion-mention--page").Filter(new LocatorFilterOptions { HasText = "Very Long Page Link Title" }).First;
        await chip.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var chipFits = await chip.EvaluateAsync<bool>("el => el.getBoundingClientRect().right <= window.innerWidth && el.scrollWidth <= Math.max(el.clientWidth, el.getBoundingClientRect().width) + 1");
        Assert.IsTrue(chipFits, "Long page-link chip should stay inside the viewport without widening the editor shell.");
        await CaptureBaselineAsync(page, "mention-menu", "eb5-long-page-chip", chip);
    }

    private static async Task SeedMentionTokenPageAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "methodName => window.tmNotionDemo && typeof window.tmNotionDemo[methodName] === 'function'",
            "seedMentionTokenPage",
            new PageWaitForFunctionOptions { Timeout = 60000 });
        await page.EvaluateAsync("async methodName => await window.tmNotionDemo[methodName]()", "seedMentionTokenPage");
        await page.ReloadAsync(new PageReloadOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync("[data-block-id='eb500000-0000-0000-0000-000000000003'] .tm-notion-token[data-key='unknown.invoice_deadline']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    private async Task CaptureBaselineAsync(IPage page, string area, string state, ILocator region)
    {
        var outputDir = GetBaselineDirectory(area);
        var safeState = SanitizePathPart(state);
        var fullPath = Path.Combine(outputDir, $"{safeState}.png");
        var regionPath = Path.Combine(outputDir, $"{safeState}.region.png");

        await page.WaitForTimeoutAsync(250);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            Type = ScreenshotType.Png,
            FullPage = true
        });

        await region.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = regionPath,
            Type = ScreenshotType.Png,
            OmitBackground = false
        });

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(regionPath);
    }

    private static async Task AssertWithinViewportAsync(ILocator locator, string label)
    {
        var metrics = await locator.EvaluateAsync<ViewportBoxMetrics>("""
            el => {
                const rect = el.getBoundingClientRect();
                return {
                    Left: rect.left,
                    Top: rect.top,
                    Right: rect.right,
                    Bottom: rect.bottom,
                    ViewportWidth: window.innerWidth,
                    ViewportHeight: window.innerHeight
                };
            }
            """);

        Assert.IsTrue(metrics.Left >= 0, $"{label} should not overflow the left viewport edge. Left={metrics.Left}.");
        Assert.IsTrue(metrics.Top >= 0, $"{label} should not overflow the top viewport edge. Top={metrics.Top}.");
        Assert.IsTrue(metrics.Right <= metrics.ViewportWidth, $"{label} should not overflow the right viewport edge. Right={metrics.Right}, Viewport={metrics.ViewportWidth}.");
        Assert.IsTrue(metrics.Bottom <= metrics.ViewportHeight, $"{label} should not overflow the bottom viewport edge. Bottom={metrics.Bottom}, Viewport={metrics.ViewportHeight}.");
    }

    /// <summary>
    /// Routed through <see cref="BaselineOutput"/>: without TM_WRITE_BASELINES the capture lands in
    /// TestResults, not under the baseline root. The redirect is deliberately NOT a skip — the
    /// tests around these captures assert behaviour.
    /// </summary>
    private string GetBaselineDirectory(string area) =>
        BaselineOutput.DirectoryFor(TestContext, "notion", SanitizePathPart(area));

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : char.ToLowerInvariant(ch)).ToArray();
        return new string(chars);
    }

    private sealed class ViewportBoxMetrics
    {
        public double Left { get; set; }

        public double Top { get; set; }

        public double Right { get; set; }

        public double Bottom { get; set; }

        public double ViewportWidth { get; set; }

        public double ViewportHeight { get; set; }
    }
}
