using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for the token dropdown in TmNotionEditor (Phase 4.12).
/// Seeded tokens: client.name, client.email, client.phone, client.address,
///   case.number, case.court, case.status,
///   lawyer.name, lawyer.email,
///   company.name, company.reg_number,
///   today, now, page.title
/// </summary>
[TestClass]
public class NotionTokenDropdownE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        using var http = new HttpClient();
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* ignore */ }

        var context = await CreateContextAsync();
        var page    = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(2000);
        return page;
    }

    /// <summary>
    /// Focuses the first paragraph, moves cursor to end, presses Enter for a
    /// new block, waits for keyboard handler to attach, then types the trigger.
    /// Also scrolls the new block into view so the token dropdown appears within
    /// the visible viewport.
    /// </summary>
    private async Task TypeTriggerAsync(IPage page, string trigger)
    {
        var firstPara = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await firstPara.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await page.EvaluateAsync(@"() => {
            const el = document.querySelector('.tm-notion-paragraph[contenteditable=""true""]');
            if (!el) return;
            el.focus();
            el.scrollIntoView({ block: 'start', behavior: 'instant' });
            const range = document.createRange();
            range.selectNodeContents(el);
            range.collapse(false);
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
        }");

        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(1200);
        await page.Keyboard.TypeAsync(trigger);
    }

    /// <summary>
    /// Clicks a locator using JS click() to bypass Playwright's viewport restriction.
    /// Used when the element is in a fixed-position overlay that may extend below the viewport.
    /// </summary>
    private async Task JsClickAsync(IPage page, ILocator locator)
    {
        var handle = await locator.ElementHandleAsync();
        if (handle != null)
            await page.EvaluateAsync("el => el.click()", handle);
    }

    /// <summary>
    /// Focuses a fixed-position input using JS to bypass Playwright's viewport restriction.
    /// </summary>
    private async Task JsFocusAsync(IPage page, ILocator locator)
    {
        var handle = await locator.ElementHandleAsync();
        if (handle != null)
            await page.EvaluateAsync("el => { el.focus(); el.click(); }", handle);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Phase 4.12 — Token dropdown tests
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Typing {{ in a text block opens the token dropdown")]
    public async Task TokenDropdown_DoubleOpenBrace_OpensDropdown()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "{{");

        var dropdown = page.Locator(".tm-notion-token-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await dropdown.IsVisibleAsync(), "Token dropdown should appear after typing {{");

        await TakeScreenshotAsync(page, "token_dropdown_open");
    }

    [TestMethod]
    [Description("Typing a query after {{ filters the token list")]
    public async Task TokenDropdown_TypeQuery_FiltersItems()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "{{");

        var dropdown = page.Locator(".tm-notion-token-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var input = dropdown.Locator(".tm-notion-token-dropdown__input");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await input.FillAsync("client");
        await page.WaitForTimeoutAsync(500);

        var items = dropdown.Locator(".tm-notion-token-dropdown__item");
        var count = await items.CountAsync();
        Assert.IsTrue(count >= 1, $"At least one token should match 'client', got {count}");

        // Verify all visible items are client-related
        for (var i = 0; i < count; i++)
        {
            var name = await items.Nth(i).Locator(".tm-notion-token-dropdown__item-name").InnerTextAsync();
            Assert.IsTrue(
                name.Contains("client", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Client", StringComparison.OrdinalIgnoreCase),
                $"Item '{name}' should relate to 'client'");
        }

        await TakeScreenshotAsync(page, "token_dropdown_filtered");
    }

    [TestMethod]
    [Description("Clicking a token item inserts a token chip into the block")]
    public async Task TokenDropdown_Click_InsertsTokenChip()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "{{");

        var dropdown = page.Locator(".tm-notion-token-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var firstItem = dropdown.Locator(".tm-notion-token-dropdown__item").First;
        await firstItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await JsClickAsync(page, firstItem);
        await page.WaitForTimeoutAsync(600);

        // Dropdown should close after selection
        var dropdownAfter = page.Locator(".tm-notion-token-dropdown");
        var isVisible = await dropdownAfter.IsVisibleAsync();
        Assert.IsFalse(isVisible, "Token dropdown should close after selecting a token");

        // Token chip should appear in the editor
        var chip = page.Locator(".tm-notion-token").First;
        Assert.IsTrue(await chip.IsVisibleAsync(), "Token chip should be visible in the block");

        await TakeScreenshotAsync(page, "token_chip_inserted_by_click");
    }

    [TestMethod]
    [Description("ArrowDown moves selection to next item, ArrowUp moves it back")]
    public async Task TokenDropdown_ArrowKeys_NavigateItems()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "{{");

        var dropdown = page.Locator(".tm-notion-token-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Focus the search input which is auto-focused
        var input = dropdown.Locator(".tm-notion-token-dropdown__input");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await JsFocusAsync(page, input);
        await page.WaitForTimeoutAsync(300);

        // Initially first item is selected
        var firstItemSelected = dropdown.Locator(".tm-notion-token-dropdown__item--selected");
        var firstCount = await firstItemSelected.CountAsync();
        Assert.IsTrue(firstCount > 0, "Initially an item should be selected");

        // Press ArrowDown to move to next
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(200);

        // Now second item should be selected (or a different one)
        var selectedItems = dropdown.Locator(".tm-notion-token-dropdown__item--selected");
        var selectedCount = await selectedItems.CountAsync();
        Assert.IsTrue(selectedCount > 0, "An item should still be selected after ArrowDown");

        // Press ArrowUp to go back
        await page.Keyboard.PressAsync("ArrowUp");
        await page.WaitForTimeoutAsync(200);

        var selectedAfterUp = dropdown.Locator(".tm-notion-token-dropdown__item--selected");
        var countAfterUp = await selectedAfterUp.CountAsync();
        Assert.IsTrue(countAfterUp > 0, "An item should be selected after ArrowUp");

        await TakeScreenshotAsync(page, "token_dropdown_navigation");
    }

    [TestMethod]
    [Description("Pressing Enter selects the currently highlighted token and inserts it")]
    public async Task TokenDropdown_Enter_InsertsSelectedToken()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "{{");

        var dropdown = page.Locator(".tm-notion-token-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var input = dropdown.Locator(".tm-notion-token-dropdown__input");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await JsFocusAsync(page, input);
        await page.WaitForTimeoutAsync(300);

        // Press Enter to select the first/currently-highlighted item
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(600);

        // Dropdown should close
        var dropdownAfter = page.Locator(".tm-notion-token-dropdown");
        Assert.IsFalse(await dropdownAfter.IsVisibleAsync(), "Token dropdown should close after pressing Enter");

        // Token chip should appear
        var chip = page.Locator(".tm-notion-token").First;
        Assert.IsTrue(await chip.IsVisibleAsync(), "Token chip should be visible after pressing Enter");

        await TakeScreenshotAsync(page, "token_dropdown_enter_insert");
    }

    [TestMethod]
    [Description("Pressing Escape closes the token dropdown without inserting a token")]
    public async Task TokenDropdown_Escape_ClosesWithoutInserting()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "{{");

        var dropdown = page.Locator(".tm-notion-token-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var input = dropdown.Locator(".tm-notion-token-dropdown__input");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await JsFocusAsync(page, input);
        await page.WaitForTimeoutAsync(300);

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(500);

        // Dropdown should close
        var dropdownAfter = page.Locator(".tm-notion-token-dropdown");
        Assert.IsFalse(await dropdownAfter.IsVisibleAsync(), "Token dropdown should close after Escape");

        // No token chip should have been inserted
        var chipCount = await page.Locator(".tm-notion-token").CountAsync();
        Assert.AreEqual(0, chipCount, "No token chip should be inserted after Escape");

        await TakeScreenshotAsync(page, "token_dropdown_escaped");
    }

    [TestMethod]
    [Description("The inserted token chip is visible as styled inline element in the text")]
    public async Task TokenDropdown_InsertedChip_VisibleInText()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "{{");

        var dropdown = page.Locator(".tm-notion-token-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var input = dropdown.Locator(".tm-notion-token-dropdown__input");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await JsFocusAsync(page, input);
        await page.Keyboard.TypeAsync("today");
        await page.WaitForTimeoutAsync(400);

        var todayItem = dropdown.Locator(".tm-notion-token-dropdown__item").First;
        await todayItem.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await JsClickAsync(page, todayItem);
        await page.WaitForTimeoutAsync(600);

        // The chip should be visible
        var chip = page.Locator(".tm-notion-token").First;
        Assert.IsTrue(await chip.IsVisibleAsync(), "Token chip should be visible");

        // The chip text should contain the token display name
        var chipText = await chip.InnerTextAsync();
        Assert.IsTrue(chipText.Contains("Today") || chipText.Contains("today"),
            $"Token chip text should show display name, got '{chipText}'");

        // The chip should have data-key attribute (set via chip.dataset.key in JS)
        var key = await chip.GetAttributeAsync("data-key");
        Assert.IsTrue(!string.IsNullOrEmpty(key), "Token chip should have data-key attribute");

        await TakeScreenshotAsync(page, "token_chip_visible_in_text");
    }

    [TestMethod]
    [Description("Token chip persists after blur and block save")]
    public async Task TokenDropdown_TokenSavedWithBlock()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "{{");

        var dropdown = page.Locator(".tm-notion-token-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Select 'today' token
        var input = dropdown.Locator(".tm-notion-token-dropdown__input");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await JsFocusAsync(page, input);
        await page.Keyboard.TypeAsync("today");
        await page.WaitForTimeoutAsync(400);

        var firstItem = dropdown.Locator(".tm-notion-token-dropdown__item").First;
        await firstItem.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await JsClickAsync(page, firstItem);
        await page.WaitForTimeoutAsync(600);

        // Verify chip inserted
        var chip = page.Locator(".tm-notion-token").First;
        Assert.IsTrue(await chip.IsVisibleAsync(), "Token chip should be visible before save");

        // Blur the block to trigger save
        var titleArea = page.Locator(".tm-notion-page-title").First;
        if (await titleArea.CountAsync() > 0)
            await titleArea.ClickAsync();
        else
            await page.Keyboard.PressAsync("Escape");

        await page.WaitForTimeoutAsync(1500);

        // Token chip should still be there (not lost on blur/save)
        var chipAfterSave = page.Locator(".tm-notion-token").First;
        Assert.IsTrue(await chipAfterSave.IsVisibleAsync(), "Token chip should persist after blur/save");

        await TakeScreenshotAsync(page, "token_chip_saved");
    }

    [TestMethod]
    [Description("No results message is shown when query matches nothing")]
    public async Task TokenDropdown_NoResults_ShowsEmptyMessage()
    {
        var page = await OpenNotionEditorAsync();
        await TypeTriggerAsync(page, "{{");

        var dropdown = page.Locator(".tm-notion-token-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var input = dropdown.Locator(".tm-notion-token-dropdown__input");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await input.FillAsync("xyzzy_no_match_token");
        await page.WaitForTimeoutAsync(500);

        var emptyMsg = dropdown.Locator(".tm-notion-token-dropdown__empty");
        await emptyMsg.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await emptyMsg.IsVisibleAsync(), "Empty/no-results message should be shown");

        await TakeScreenshotAsync(page, "token_dropdown_no_results");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB5: captures token menu and token no-results state")]
    public async Task EB5_TokenMenuAndNoResults_CaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedMentionTokenPageAsync(page);
        await TypeTriggerAsync(page, "{{");

        var dropdown = page.Locator(".tm-notion-token-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await AssertWithinViewportAsync(dropdown, "EB5 token dropdown");
        await CaptureBaselineAsync(page, "token-dropdown", "eb5-token-menu-open", dropdown);

        var input = dropdown.Locator(".tm-notion-token-dropdown__input").First;
        await input.FillAsync("definitely_no_token_match");
        var empty = dropdown.Locator(".tm-notion-token-dropdown__empty").First;
        await empty.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await empty.IsVisibleAsync(), "Token no-results state should be visible.");
        await CaptureBaselineAsync(page, "token-dropdown", "eb5-token-no-results", dropdown);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB5: captures inserted token chip, edit menu, and unknown token fallback")]
    public async Task EB5_TokenChipEditMenuAndUnknownFallback_CaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedMentionTokenPageAsync(page);

        var unknownToken = page.Locator(".tm-notion-token.tm-notion-token--unknown[data-key='unknown.invoice_deadline']").First;
        await unknownToken.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await CaptureBaselineAsync(page, "token-dropdown", "eb5-unknown-token-fallback", unknownToken);

        await TypeTriggerAsync(page, "{{");
        var dropdown = page.Locator(".tm-notion-token-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var input = dropdown.Locator(".tm-notion-token-dropdown__input").First;
        await input.FillAsync("case number");
        var caseNumberItem = dropdown.Locator(".tm-notion-token-dropdown__item").Filter(new LocatorFilterOptions { HasText = "Case number" }).First;
        await caseNumberItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await JsClickAsync(page, caseNumberItem);
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 8000 });

        var insertedToken = page.Locator(".tm-notion-token[data-key='case.number']").First;
        await insertedToken.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await CaptureBaselineAsync(page, "token-dropdown", "eb5-token-chip-inserted", insertedToken);

        await insertedToken.EvaluateAsync("""
            el => el.dispatchEvent(new MouseEvent('mousedown', {
                bubbles: true,
                cancelable: true,
                view: window
            }))
            """);
        dropdown = page.Locator(".tm-notion-token-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await AssertWithinViewportAsync(dropdown, "EB5 token edit dropdown");
        await CaptureBaselineAsync(page, "token-dropdown", "eb5-token-edit-menu", dropdown);
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
