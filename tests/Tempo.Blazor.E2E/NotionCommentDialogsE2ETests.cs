using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 4 batch B functional checks for the block / text comment surfaces (plan c8d4bcd2, P3/NO1):
/// a centred core dialog at 1440 when the presentation is pinned to Dialog, a bottom sheet docked
/// to the viewport edge at 390/320 with the composer visible, no ← back affordance on a zero-thread
/// sheet, Escape with focus return, and the entry delete confirm.
/// </summary>
[TestClass]
public sealed class NotionCommentDialogsE2ETests : NotionE2ETestBase
{
    private async Task<IPage> OpenTouchEditorAsync(int width, int height, string query = "", string? seed = null)
    {
        // The demo's JS seeds reload the page through the base Page; touch tests have no base
        // page, so seed through the direct HTTP route BEFORE navigating.
        using var http = DemoApiTestHttp.CreateClient();
        using var reset = await http.PostAsync("/api/notion/reset", null);
        reset.EnsureSuccessStatusCode();
        if (seed is not null)
        {
            using var seeded = await http.PostAsync($"/api/notion/e2e/seed/{Uri.EscapeDataString(seed)}", null);
            seeded.EnsureSuccessStatusCode();
        }

        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            IsMobile = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor{query}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000,
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000,
        });
        await page.WaitForSelectorAsync(".tm-notion-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000,
        });
        return page;
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Block comment pinned to Dialog at 1440: centred core TmModal, visible composer, Close footer.")]
    public async Task BlockComment_DialogAt1440_Centred_ComposerVisible()
    {
        var page = await OpenNotionEditorAsync(1440, 900, "?comments=dialog");
        await SeedCommentsPageAsync();
        await OpenBlockCommentAsync(page);

        var overlay = page.Locator(".tm-modal-overlay").First;
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var card = page.Locator(".tm-modal-overlay .tm-nbcp").First;
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var box = await overlay.BoundingBoxAsync() ?? throw new AssertFailedException("dialog has no box");
        var centerX = box.X + box.Width / 2;
        Assert.IsTrue(Math.Abs(centerX - 720) < 60, $"dialog must be centred (center {centerX:F0})");

        Assert.IsTrue(await page.Locator(".tm-nbcp__reply .tm-nbcp__reply-input").First.IsVisibleAsync(),
            "the comment composer stays visible in the dialog");
        StringAssert.Contains(await page.Locator(".tm-modal-footer").First.InnerTextAsync(), "Close");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Block comment sheet at 390 (touch): docked to the bottom edge, composer visible above the edge, Escape closes.")]
    public async Task BlockComment_SheetAt390_BottomEdge_ComposerVisible()
    {
        var page = await OpenTouchEditorAsync(390, 844, seed: "seedCommentsPage");
        await OpenBlockCommentAsync(page);
        await AssertSheetDockedAsync(page, 844);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Block comment sheet at 320 (touch): docked to the bottom edge with the composer visible.")]
    public async Task BlockComment_SheetAt320_BottomEdge()
    {
        var page = await OpenTouchEditorAsync(320, 700, seed: "seedCommentsPage");
        await OpenBlockCommentAsync(page);
        await AssertSheetDockedAsync(page, 700);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("A zero-thread comment sheet hides the ← back button — there is no list to go back to.")]
    public async Task BlockComment_SheetWithZeroThreads_HidesBackButton()
    {
        var page = await OpenTouchEditorAsync(390, 844, seed: "seedCommentlessPage");
        await OpenBlockCommentAsync(page);

        var sheet = page.Locator(".tm-modal--sheet .tm-nbcp").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.AreEqual(0, await sheet.Locator(".tm-nbcp__back-btn").CountAsync(),
            "the zero-thread sheet must not render a back button");
        Assert.IsTrue(await sheet.Locator(".tm-nbcp__reply-input").First.IsVisibleAsync(),
            "the new-thread composer replaces the list");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Escape closes the comment sheet and returns focus to the block handle — not to the body.")]
    public async Task BlockComment_Escape_ClosesSheetAndReturnsFocusToHandle()
    {
        var page = await OpenTouchEditorAsync(390, 844, seed: "seedCommentsPage");
        await OpenBlockCommentAsync(page);
        var sheet = page.Locator(".tm-modal--sheet").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        await page.Keyboard.PressAsync("Escape");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });

        var active = await page.EvaluateAsync<string>(
            """
            () => {
                const el = document.activeElement;
                if (!el || el === document.body) return '<body>';
                const host = el.closest('[data-notion-block]');
                const cls = typeof el.className === 'string' ? el.className : '';
                return host ? `block:${host.getAttribute('data-block-id')}:${cls.split(/\s+/)[0]}` : cls.split(/\s+/)[0];
            }
            """);
        TestContext.WriteLine($"focus after Escape: {active}");
        StringAssert.Contains(active, "tm-notion-handle__btn",
            "focus must return to the block's handle button, not the body");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Deleting a comment entry raises the core confirm dialog; confirming removes the entry.")]
    public async Task BlockComment_DeleteEntry_ShowsConfirmAndRemovesIt()
    {
        var page = await OpenNotionEditorAsync(1440, 900, "?comments=dialog");
        await SeedCommentsPageAsync();
        await OpenBlockCommentAsync(page);
        var card = page.Locator(".tm-modal-overlay .tm-nbcp").First;
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // The seed only creates blocks — create a thread so the detail has an entry to delete.
        await AddBlockCommentAsync(page, "Thread to delete from the phase-4 dialog capture.");

        await page.Locator(".tm-nbcp__entry").First.HoverAsync();
        // Delete lives in the ⋯ entry menu (core TmDropdown), not as a standalone hover button.
        var deleteItem = await NotionCommentsE2ETests.OpenCommentEntryMenuAsync(page, "tm-nbcp", "delete");
        await deleteItem.ClickAsync();

        var dialog = page.Locator(".tm-dialog").First;
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.AreEqual("alertdialog", await dialog.GetAttributeAsync("role"));
        await page.Locator(".tm-dialog-btn-ok").First.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        Assert.AreEqual(0, await page.Locator(".tm-nbcp__entry").CountAsync(),
            "the entry disappears after the delete confirm");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Text comment at 1440 stays an anchored popover — positioned inside the viewport, closable via its ×.")]
    public async Task TextComment_PopoverAt1440_PositionedInsideViewport()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await SeedCommentsPageAsync();
        await OpenTextCommentAsync(page);

        var panel = page.Locator(".tm-ntcp").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var overlay = page.Locator(".tm-overlay-panel:has(.tm-ntcp)").First;
        var box = await overlay.BoundingBoxAsync() ?? throw new AssertFailedException("popover has no box");
        Assert.IsTrue(box.X >= 0 && box.Y >= 0 && box.X + box.Width <= 1440,
            $"the popover must sit inside the viewport ({box.X:F0},{box.Y:F0} {box.Width:F0}x{box.Height:F0})");

        await panel.Locator(".tm-ntcp__close-btn").First.ClickAsync();
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Text comment sheet at 390 (touch): docked to the bottom edge with the whole quote visible.")]
    public async Task TextComment_SheetAt390_BottomEdge_QuoteShown()
    {
        var page = await OpenTouchEditorAsync(390, 844, seed: "seedCommentsPage");
        await OpenTextCommentAsync(page);

        var sheet = page.Locator(".tm-modal--sheet .tm-ntcp").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var box = await page.Locator(".tm-modal--sheet").First.BoundingBoxAsync()
            ?? throw new AssertFailedException("sheet has no box");
        Assert.IsTrue(Math.Abs(box.Y + box.Height - 844) < 2,
            $"sheet bottom must sit at the viewport bottom edge (bottom {box.Y + box.Height:F0})");
        Assert.IsTrue(await sheet.Locator(".tm-ntcp__highlight-preview").First.IsVisibleAsync(),
            "the dialog surface shows the whole highlighted quote");

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-modal--sheet .tm-ntcp").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000,
        });
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("On a coarse pointer the composer's Ctrl+Enter hint is hidden (touch keyboards have no Ctrl).")]
    public async Task CommentComposer_CoarsePointer_HidesCtrlEnterHint()
    {
        var page = await OpenTouchEditorAsync(390, 844, seed: "seedCommentsPage");
        await OpenBlockCommentAsync(page);

        var hint = page.Locator(".tm-modal--sheet .tm-nbcp__reply-hint").First;
        await hint.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        var display = await hint.EvaluateAsync<string>("el => getComputedStyle(el).display");
        Assert.AreEqual("none", display, "the Ctrl+Enter hint hides on (pointer: coarse)");

        var send = page.Locator(".tm-modal--sheet .tm-nbcp__reply-send").First;
        var minHeight = await send.EvaluateAsync<string>("el => getComputedStyle(el).minHeight");
        Assert.AreEqual("44px", minHeight, "the composer submit grows to the 44px touch target");
    }

    private async Task AssertSheetDockedAsync(IPage page, int viewportHeight)
    {
        var sheet = page.Locator(".tm-modal--sheet .tm-nbcp").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var sheetBox = await page.Locator(".tm-modal--sheet").First.BoundingBoxAsync()
            ?? throw new AssertFailedException("sheet has no box");
        Assert.IsTrue(Math.Abs(sheetBox.Y + sheetBox.Height - viewportHeight) < 2,
            $"sheet bottom must sit at the viewport bottom edge (bottom {sheetBox.Y + sheetBox.Height:F0})");

        var composer = page.Locator(".tm-modal--sheet .tm-nbcp__reply").First;
        await composer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        // The sheet body scrolls: at 320 the composer can sit just below the first fold, so prove
        // reachability — scroll it into view and confirm the input is interactable.
        var input = page.Locator(".tm-modal--sheet .tm-nbcp__reply-input").First;
        await input.ScrollIntoViewIfNeededAsync();
        Assert.IsTrue(await input.IsVisibleAsync(),
            "the composer input is interactable without leaving the sheet");

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-modal--sheet .tm-nbcp").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000,
        });
    }

    private static async Task OpenBlockCommentAsync(IPage page)
    {
        var firstBlock = page.Locator("[data-notion-block]").First;
        await firstBlock.ScrollIntoViewIfNeededAsync();
        await firstBlock.HoverAsync();
        await firstBlock.Locator(".tm-notion-handle__btn").Last.ClickAsync();
        // DOM click: at 320 the sticky mobile action bar overlaps the menu item's hit target.
        await page.Locator(".tm-notion-ctx__item:has-text('Comment')").First
            .EvaluateAsync("el => el.click()");
    }

    private static async Task OpenTextCommentAsync(IPage page)
    {
        var firstEditable = page.Locator(".tm-notion-paragraph.tm-notion-editable").First;
        await firstEditable.ScrollIntoViewIfNeededAsync();
        await firstEditable.ClickAsync();
        await firstEditable.EvaluateAsync("el => { el.focus(); document.execCommand('selectAll', false, null); }");
        var toolbar = page.Locator(".tm-notion-inline-toolbar").First;
        await toolbar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await toolbar.Locator("button[title*='Comment'], button[title*='Komentář']").First.ClickAsync();
    }

    private static async Task AddBlockCommentAsync(IPage page, string text)
    {
        var input = page.Locator(".tm-nbcp__reply-input").First;
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await input.FillAsync(text);
        await page.Locator(".tm-nbcp__reply-send").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);
    }
}
