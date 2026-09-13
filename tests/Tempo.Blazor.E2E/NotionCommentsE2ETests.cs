using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests covering all comment features in the Notion editor:
/// block comments, page-level comments, and text-anchor (inline) comments.
/// </summary>
[TestClass]
public class NotionCommentsE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Setup
    // ══════════════════════════════════════════════════════════════════════════

    private async Task ClearNotificationsAsync(IPage page)
    {
        await page.EvaluateAsync("async () => { if (typeof DotNet !== 'undefined') await DotNet.invokeMethodAsync('Tempo.Blazor.Demo', 'ClearDemoNotificationsAsync'); }");
        await page.WaitForTimeoutAsync(200);
    }

    private async Task<IPage> OpenNotionEditorAsync()
    {
        using var http = new HttpClient();
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

    // ══════════════════════════════════════════════════════════════════════════
    //  Block Comments
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Open block comment panel from block context menu")]
    public async Task BlockComment_OpenPanel()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);

        var panel = page.Locator(".tm-nbcp").First;
        Assert.IsTrue(await panel.IsVisibleAsync(), "Block comment panel should be visible");
        await TakeScreenshotAsync(page, "block_comment_open");
    }

    [TestMethod]
    [Description("Add a new block comment")]
    public async Task BlockComment_AddComment()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);
        await AddBlockCommentAsync(page, "Test block comment");

        var entryText = page.Locator(".tm-nbcp__entry-text").Filter(new() { HasText = "Test block comment" }).First;
        await entryText.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await entryText.IsVisibleAsync(), "New block comment should appear in thread");
        await TakeScreenshotAsync(page, "block_comment_add");
    }

    [TestMethod]
    [Description("Reply to an existing block comment")]
    public async Task BlockComment_Reply()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);
        await AddBlockCommentAsync(page, "Initial comment");
        await AddBlockCommentAsync(page, "Reply text");

        var replyText = page.Locator(".tm-nbcp__entry-text").Filter(new() { HasText = "Reply text" }).First;
        await replyText.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await replyText.IsVisibleAsync(), "Reply should appear in thread");
        await TakeScreenshotAsync(page, "block_comment_reply");
    }

    [TestMethod]
    [Description("Resolve and unresolve a block comment")]
    public async Task BlockComment_ResolveUnresolve()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);
        await AddBlockCommentAsync(page, "Comment to resolve");

        // Resolve
        var resolveBtn = page.Locator(".tm-nbcp__resolve-btn").Filter(new() { HasText = "Resolve" }).First;
        await resolveBtn.ClickAsync();

        var resolvedBanner = page.Locator(".tm-nbcp__resolved-banner").First;
        await resolvedBanner.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await resolvedBanner.IsVisibleAsync(), "Resolved banner should appear");

        // Unresolve
        var unresolveBtn = page.Locator(".tm-nbcp__resolve-btn--unresolve").First;
        await unresolveBtn.ClickAsync();

        var textarea = page.Locator(".tm-nbcp__reply-input").First;
        await textarea.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await textarea.IsVisibleAsync(), "Reply input should re-appear after unresolve");
        await TakeScreenshotAsync(page, "block_comment_resolve_unresolve");
    }

    [TestMethod]
    [Description("Edit a block comment entry")]
    public async Task BlockComment_Edit()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);
        await AddBlockCommentAsync(page, "Comment to edit");

        var editBtn = page.Locator(".tm-nbcp__entry-action").Filter(new() { HasText = "Edit" }).First;
        await editBtn.ClickAsync();

        var editInput = page.Locator(".tm-nbcp__edit-input").First;
        await editInput.FillAsync("Edited comment");

        var saveBtn = page.Locator(".tm-nbcp__edit-save").First;
        await saveBtn.ClickAsync();

        var editedText = page.Locator(".tm-nbcp__entry-text").Filter(new() { HasText = "Edited comment" }).First;
        await editedText.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await editedText.IsVisibleAsync(), "Edited text should be displayed");
        await TakeScreenshotAsync(page, "block_comment_edit");
    }

    [TestMethod]
    [Description("Delete a block comment entry with confirm dialog")]
    public async Task BlockComment_Delete()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);
        await AddBlockCommentAsync(page, "Comment to delete");

        var entryBefore = page.Locator(".tm-nbcp__entry-text").Filter(new() { HasText = "Comment to delete" }).First;
        Assert.IsTrue(await entryBefore.IsVisibleAsync());

        var deleteBtn = page.Locator(".tm-nbcp__entry-action--danger").First;
        await deleteBtn.ClickAsync();

        var okBtn = page.Locator(".tm-dialog-btn-ok").First;
        await okBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await okBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var entryAfter = page.Locator(".tm-nbcp__entry-text").Filter(new() { HasText = "Comment to delete" });
        Assert.IsTrue(await entryAfter.CountAsync() == 0, "Deleted comment should disappear");
        await TakeScreenshotAsync(page, "block_comment_delete");
    }

    [TestMethod]
    [Description("Close block comment panel")]
    public async Task BlockComment_ClosePanel()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);

        var closeBtn = page.Locator(".tm-nbcp__close-btn").First;
        await closeBtn.ClickAsync();

        var panel = page.Locator(".tm-nbcp").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
        Assert.IsFalse(await panel.IsVisibleAsync(), "Panel should be closed");
        await TakeScreenshotAsync(page, "block_comment_close");
    }

    [TestMethod]
    [Description("Margin thread badge appears after adding a block comment")]
    public async Task BlockComment_MarginThread_Visible()
    {
        var page = await OpenNotionEditorAsync();
        var firstBlock = page.Locator("[data-notion-block]").First;
        await firstBlock.HoverAsync();
        var menuBtn = firstBlock.Locator(".tm-notion-handle__btn").Last;
        await menuBtn.ClickAsync();
        var commentBtn = page.Locator(".tm-notion-ctx__item:has-text('Comment')").First;
        await commentBtn.ClickAsync();

        await AddBlockCommentAsync(page, "Margin thread test");

        // Close panel
        var closeBtn = page.Locator(".tm-nbcp__close-btn").First;
        await closeBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Verify margin thread badge is visible on the block.
        // New unread comments render a dot indicator instead of a number.
        var badge = firstBlock.Locator(".tm-notion-block__comment-thread").First;
        await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var className = await badge.EvaluateAsync<string>("el => el.className");
        Assert.IsTrue(className.Contains("tm-notion-block__comment-thread--unread"), "Badge should have unread class for new comment");
        await TakeScreenshotAsync(page, "block_comment_margin_thread_visible");
    }

    [TestMethod]
    [Description("Resolved block comment shows gray checkmark badge")]
    public async Task BlockComment_ResolvedBadge_ShowsCheckmark()
    {
        var page = await OpenNotionEditorAsync();
        var firstBlock = page.Locator("[data-notion-block]").First;
        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "Comment to resolve");

        // Resolve
        var resolveBtn = page.Locator(".tm-nbcp__resolve-btn").Filter(new() { HasText = "Resolve" }).First;
        await resolveBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Close panel
        var closeBtn = page.Locator(".tm-nbcp__close-btn").First;
        await closeBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Verify resolved checkmark badge
        var resolvedBadge = firstBlock.Locator(".tm-notion-block__comment-thread--resolved").First;
        await resolvedBadge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await resolvedBadge.IsVisibleAsync(), "Resolved checkmark badge should be visible");
        await TakeScreenshotAsync(page, "block_comment_resolved_badge");
    }

    [TestMethod]
    [Description("Mark as read hides the resolved badge")]
    public async Task BlockComment_MarkAsRead_HidesBadge()
    {
        var page = await OpenNotionEditorAsync();
        var firstBlock = page.Locator("[data-notion-block]").First;
        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "Comment to mark as read");

        // Resolve
        var resolveBtn = page.Locator(".tm-nbcp__resolve-btn").Filter(new() { HasText = "Resolve" }).First;
        await resolveBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Close panel
        var closeBtn = page.Locator(".tm-nbcp__close-btn").First;
        await closeBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Verify resolved badge exists (resolved but still unread)
        var resolvedBadge = firstBlock.Locator(".tm-notion-block__comment-thread--resolved").First;
        await resolvedBadge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Click resolved badge to reopen panel — panel auto-marks threads as read on open
        await resolvedBadge.ClickAsync();
        await page.Locator(".tm-nbcp").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.WaitForTimeoutAsync(500);

        // Close panel again
        await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Verify badge is gone (auto-mark-as-read cleared the resolved-unread count)
        var badgeAfter = firstBlock.Locator(".tm-notion-block__comment-thread");
        Assert.IsTrue(await badgeAfter.CountAsync() == 0, "Resolved badge should disappear after auto-mark-as-read on panel open");
        await TakeScreenshotAsync(page, "block_comment_mark_as_read");
    }

    [TestMethod]
    [Description("Hovering margin thread badge shows tooltip with author and preview")]
    public async Task BlockComment_HoverTooltip_ShowsAuthorAndPreview()
    {
        var page = await OpenNotionEditorAsync();
        var firstBlock = page.Locator("[data-notion-block]").First;
        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "Tooltip preview test comment");

        // Close panel
        var closeBtn = page.Locator(".tm-nbcp__close-btn").First;
        await closeBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Hover badge and wait for debounce + animation
        var badge = firstBlock.Locator(".tm-notion-block__comment-thread").First;
        await badge.HoverAsync();
        await page.WaitForTimeoutAsync(450);

        // Verify tooltip is visible
        var tooltip = firstBlock.Locator(".tm-notion-block__thread-tooltip").First;
        await tooltip.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await tooltip.IsVisibleAsync(), "Tooltip should appear on hover");

        // Verify author name
        var author = tooltip.Locator(".tm-notion-block__thread-tooltip__author").First;
        var authorText = await author.TextContentAsync();
        Assert.IsFalse(string.IsNullOrEmpty(authorText), "Tooltip should show author name");

        // Verify preview text
        var preview = tooltip.Locator(".tm-notion-block__thread-tooltip__text").First;
        var previewText = await preview.TextContentAsync();
        StringAssert.Contains(previewText, "Tooltip preview test comment");

        await TakeScreenshotAsync(page, "block_comment_hover_tooltip");

        // Move mouse away — tooltip should disappear
        await page.Mouse.MoveAsync(0, 0);
        await page.WaitForTimeoutAsync(200);
        Assert.IsFalse(await tooltip.IsVisibleAsync(), "Tooltip should hide on mouse leave");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Page Comments
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Page comment panel shows unresolved count badge")]
    public async Task PageComment_ShowsBadge()
    {
        var page = await OpenNotionEditorAsync();
        var badge = page.Locator(".tm-npcp__badge").First;
        await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var count = await badge.TextContentAsync();
        Assert.IsFalse(string.IsNullOrEmpty(count), "Badge should show unresolved count");
        await TakeScreenshotAsync(page, "page_comment_badge");
    }

    [TestMethod]
    [Description("Expand page comment panel and add new comment")]
    public async Task PageComment_AddComment()
    {
        var page = await OpenNotionEditorAsync();
        await ExpandPageCommentPanelAsync(page);

        var textarea = page.Locator(".tm-npcp__new-comment .tm-npcp__reply-input").First;
        await textarea.FillAsync("New page comment");

        var sendBtn = page.Locator(".tm-npcp__reply-send").First;
        await sendBtn.ClickAsync();

        var entryText = page.Locator(".tm-npcp__entry-text").Filter(new() { HasText = "New page comment" }).First;
        await entryText.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await entryText.IsVisibleAsync(), "New page comment should appear");
        await TakeScreenshotAsync(page, "page_comment_add");
    }

    [TestMethod]
    [Description("Reply to a page comment thread")]
    public async Task PageComment_Reply()
    {
        var page = await OpenNotionEditorAsync();
        await ExpandPageCommentPanelAsync(page);

        // Click reply trigger on first thread
        var replyTrigger = page.Locator(".tm-npcp__reply-trigger").First;
        await replyTrigger.ClickAsync();

        var textarea = page.Locator(".tm-npcp__thread-reply .tm-npcp__reply-input").First;
        await textarea.FillAsync("Page reply");

        var sendBtn = page.Locator(".tm-npcp__thread-reply .tm-npcp__reply-send").First;
        await sendBtn.ClickAsync();

        var replyText = page.Locator(".tm-npcp__entry-text").Filter(new() { HasText = "Page reply" }).First;
        await replyText.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await replyText.IsVisibleAsync(), "Reply should appear in thread");
        await TakeScreenshotAsync(page, "page_comment_reply");
    }

    [TestMethod]
    [Description("Resolve and unresolve a page comment")]
    public async Task PageComment_ResolveUnresolve()
    {
        var page = await OpenNotionEditorAsync();
        await ExpandPageCommentPanelAsync(page);
        await AddPageCommentAsync(page, "Comment to resolve");

        // Find the thread containing our new comment and resolve it
        var ourThread = page.Locator(".tm-npcp__thread").Filter(new() { HasText = "Comment to resolve" }).First;
        var resolveBtn = ourThread.Locator(".tm-npcp__thread-action").Filter(new() { HasText = "Resolve" }).First;
        await resolveBtn.ClickAsync();

        // Wait for the thread to gain resolved styling
        await ourThread.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        var threadClass = await ourThread.EvaluateAsync<string>("el => el.className");
        Assert.IsTrue(threadClass.Contains("tm-npcp__thread--resolved"), "Thread should be marked resolved");

        // Unresolve the same thread
        var unresolveBtn = ourThread.Locator(".tm-npcp__thread-action").Filter(new() { HasText = "Re-open" }).First;
        await unresolveBtn.ClickAsync();

        // Verify resolved class is removed
        await page.WaitForTimeoutAsync(500);
        threadClass = await ourThread.EvaluateAsync<string>("el => el.className");
        Assert.IsFalse(threadClass.Contains("tm-npcp__thread--resolved"), "Thread should no longer be resolved after unresolve");
        await TakeScreenshotAsync(page, "page_comment_resolve_unresolve");
    }

    [TestMethod]
    [Description("Edit a page comment entry")]
    public async Task PageComment_Edit()
    {
        var page = await OpenNotionEditorAsync();
        await ExpandPageCommentPanelAsync(page);
        // Pre-seeded comments have CanEdit=false; add a new one first.
        await AddPageCommentAsync(page, "Page comment to edit");

        var editBtn = page.Locator(".tm-npcp__entry-action").Filter(new() { HasText = "Edit" }).First;
        await editBtn.ClickAsync();

        var editInput = page.Locator(".tm-npcp__edit-input").First;
        await editInput.FillAsync("Edited page comment");

        var saveBtn = page.Locator(".tm-npcp__edit-save").First;
        await saveBtn.ClickAsync();

        var editedText = page.Locator(".tm-npcp__entry-text").Filter(new() { HasText = "Edited page comment" }).First;
        await editedText.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await editedText.IsVisibleAsync(), "Edited page comment should be displayed");
        await TakeScreenshotAsync(page, "page_comment_edit");
    }

    [TestMethod]
    [Description("Delete a page comment entry with confirm dialog")]
    public async Task PageComment_Delete()
    {
        var page = await OpenNotionEditorAsync();
        await ExpandPageCommentPanelAsync(page);
        await AddPageCommentAsync(page, "Page comment to delete");

        var entryBefore = page.Locator(".tm-npcp__entry-text").Filter(new() { HasText = "Page comment to delete" }).First;
        Assert.IsTrue(await entryBefore.IsVisibleAsync());

        var deleteBtn = page.Locator(".tm-npcp__entry-action--danger").First;
        await deleteBtn.ClickAsync();

        var okBtn = page.Locator(".tm-dialog-btn-ok").First;
        await okBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await okBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var entryAfter = page.Locator(".tm-npcp__entry-text").Filter(new() { HasText = "Page comment to delete" });
        Assert.IsTrue(await entryAfter.CountAsync() == 0, "Deleted page comment should disappear");
        await TakeScreenshotAsync(page, "page_comment_delete");
    }

    [TestMethod]
    [Description("Mark all page comments as read via panel toolbar")]
    public async Task PageComment_MarkAllAsRead()
    {
        var page = await OpenNotionEditorAsync();
        await ExpandPageCommentPanelAsync(page);

        // Click "Mark all as read" button in panel toolbar
        var markAllBtn = page.Locator(".tm-npcp__mark-all-read").First;
        await markAllBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await markAllBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Verify no error is shown
        var error = page.Locator(".tm-npcp__error").First;
        Assert.IsFalse(await error.IsVisibleAsync(), "Mark all as read should not produce an error");
        await TakeScreenshotAsync(page, "page_comment_mark_all_as_read");
    }

    [TestMethod]
    [Description("Mark all comments as read via page settings menu")]
    public async Task PageComment_MarkAllAsRead_SettingsMenu()
    {
        var page = await OpenNotionEditorAsync();

        // Open page settings menu (three dots)
        var settingsBtn = page.Locator(".tm-npsm-trigger").First;
        await settingsBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await settingsBtn.ClickAsync();

        // Click "Mark all comments as read"
        var markAllBtn = page.Locator(".tm-npsm__item").Filter(new() { HasText = "Mark all comments as read" }).First;
        await markAllBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await markAllBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        // Verify success toast appears
        var toast = page.Locator(".tm-npsm__toast").First;
        await toast.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var toastText = await toast.TextContentAsync();
        StringAssert.Contains(toastText, "All comments marked as read");
        await TakeScreenshotAsync(page, "page_comment_mark_all_as_read_settings");
    }

    [TestMethod]
    [Description("Header badge shows unresolved comment count next to page title")]
    public async Task PageComment_HeaderBadge_ShowsCount()
    {
        var page = await OpenNotionEditorAsync();

        // The demo page is pre-seeded with page comments; badge should be visible.
        var headerBadge = page.Locator(".tm-notion-header-comment-badge").First;
        await headerBadge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var countText = await headerBadge.TextContentAsync();
        Assert.IsFalse(string.IsNullOrEmpty(countText), "Header badge should show a count");
        Assert.IsTrue(int.TryParse(countText, out var count) && count > 0,
            "Header badge should show a positive unresolved count");
        await TakeScreenshotAsync(page, "page_comment_header_badge");
    }

    [TestMethod]
    [Description("Clicking header badge scrolls to first unresolved comment block")]
    public async Task PageComment_HeaderBadge_ClickScrollsToFirstUnresolved()
    {
        var page = await OpenNotionEditorAsync();

        // First add a block comment so we have an unresolved block to scroll to
        var firstBlock = page.Locator("[data-notion-block]").First;
        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "Scroll target comment");
        await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Scroll to top first
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await page.WaitForTimeoutAsync(300);

        // Click header badge
        var headerBadge = page.Locator(".tm-notion-header-comment-badge").First;
        await headerBadge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await headerBadge.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        // Verify first unresolved block is visible in viewport
        var firstUnresolved = page.Locator(".tm-notion-block__comment-thread").First;
        Assert.IsTrue(await firstUnresolved.IsVisibleAsync(), "First unresolved comment block should be visible after scroll");
        await TakeScreenshotAsync(page, "page_comment_header_badge_scroll");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Text / Inline Comments
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Select text and open text comment panel from inline toolbar")]
    public async Task TextComment_OpenPanel()
    {
        var page = await OpenNotionEditorAsync();
        await OpenTextCommentPanelAsync(page);

        var panel = page.Locator(".tm-ntcp").First;
        Assert.IsTrue(await panel.IsVisibleAsync(), "Text comment panel should be visible");
        await TakeScreenshotAsync(page, "text_comment_open");
    }

    [TestMethod]
    [Description("Add a comment to selected text")]
    public async Task TextComment_AddComment()
    {
        var page = await OpenNotionEditorAsync();
        await OpenTextCommentPanelAsync(page);
        await AddTextCommentAsync(page, "Text anchor comment");

        var entryText = page.Locator(".tm-ntcp__entry-text").Filter(new() { HasText = "Text anchor comment" }).First;
        await entryText.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await entryText.IsVisibleAsync(), "Text comment should appear");
        await TakeScreenshotAsync(page, "text_comment_add");
    }

    [TestMethod]
    [Description("Resolve a text comment")]
    public async Task TextComment_Resolve()
    {
        var page = await OpenNotionEditorAsync();
        await OpenTextCommentPanelAsync(page);
        await AddTextCommentAsync(page, "Comment to resolve");

        var resolveBtn = page.Locator(".tm-ntcp__resolve-btn").Filter(new() { HasText = "Resolve" }).First;
        await resolveBtn.ClickAsync();

        // Text comment panel closes automatically after resolve
        var panel = page.Locator(".tm-ntcp").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
        Assert.IsFalse(await panel.IsVisibleAsync(), "Text comment panel should close after resolve");
        await TakeScreenshotAsync(page, "text_comment_resolve");
    }

    [TestMethod]
    [Description("Delete a text comment entry with confirm dialog")]
    public async Task TextComment_Delete()
    {
        var page = await OpenNotionEditorAsync();

        // Inject a spy to detect if unwrapCommentHighlight is called
        await page.EvaluateAsync("""
            window.__unwrapCalled = false;
            window.__unwrapId = null;
            const orig = tmNotionEditor.unwrapCommentHighlight;
            tmNotionEditor.unwrapCommentHighlight = function(id) {
                window.__unwrapCalled = true;
                window.__unwrapId = id;
                return orig(id);
            };
            """);
        await OpenTextCommentPanelAsync(page);
        await AddTextCommentAsync(page, "Text comment to delete");

        var entryBefore = page.Locator(".tm-ntcp__entry-text").Filter(new() { HasText = "Text comment to delete" }).First;
        Assert.IsTrue(await entryBefore.IsVisibleAsync());

        // Verify the yellow highlight mark exists in DOM before delete
        var markBefore = page.Locator("mark.tm-notion-comment-highlight");
        Assert.IsTrue(await markBefore.CountAsync() > 0, "Highlight mark should exist before delete");

        // Verify unwrapCommentHighlight is exported
        var hasUnwrap = await page.EvaluateAsync<bool>("() => typeof tmNotionEditor !== 'undefined' && typeof tmNotionEditor.unwrapCommentHighlight === 'function'");
        Assert.IsTrue(hasUnwrap, "tmNotionEditor.unwrapCommentHighlight should be exported");

        // Remember the commentId from the mark
        var commentId = await markBefore.First.EvaluateAsync<string>("el => el.dataset.commentId");
        Assert.IsFalse(string.IsNullOrEmpty(commentId), "Mark should have data-comment-id");

        var deleteBtn = page.Locator(".tm-ntcp__entry-action--danger").First;
        await deleteBtn.ClickAsync();

        var okBtn = page.Locator(".tm-dialog-btn-ok").First;
        await okBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await okBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var entryAfter = page.Locator(".tm-ntcp__entry-text").Filter(new() { HasText = "Text comment to delete" });
        Assert.IsTrue(await entryAfter.CountAsync() == 0, "Deleted text comment should disappear");

        // Verify unwrapCommentHighlight was actually called by Blazor with correct ID
        var unwrapCalled = await page.EvaluateAsync<bool>("() => window.__unwrapCalled === true");
        var unwrapId = await page.EvaluateAsync<string>("() => window.__unwrapId ?? ''");
        Assert.IsTrue(unwrapCalled, "unwrapCommentHighlight should be called by Blazor");
        Assert.AreEqual(commentId, unwrapId, "Blazor should call unwrap with same ID as DOM mark");

        // Verify the yellow highlight mark was removed from DOM
        var markAfter = page.Locator("mark.tm-notion-comment-highlight");
        Assert.IsTrue(await markAfter.CountAsync() == 0, "Highlight mark should be removed from DOM after delete");

        await TakeScreenshotAsync(page, "text_comment_delete");
    }

    [TestMethod]
    [Description("Close text comment panel")]
    public async Task TextComment_ClosePanel()
    {
        var page = await OpenNotionEditorAsync();
        await OpenTextCommentPanelAsync(page);

        var closeBtn = page.Locator(".tm-ntcp__close-btn").First;
        await closeBtn.ClickAsync();

        var panel = page.Locator(".tm-ntcp").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
        Assert.IsFalse(await panel.IsVisibleAsync(), "Text comment panel should be closed");
        await TakeScreenshotAsync(page, "text_comment_close");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Nested Replies
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Reply to a specific block comment entry (nested reply)")]
    public async Task BlockComment_NestedReply()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);
        await AddBlockCommentAsync(page, "Parent comment");

        var replyBtn = page.Locator(".tm-nbcp__entry-action").Filter(new() { HasText = "Reply to this" }).First;
        await replyBtn.ClickAsync();

        var inlineReply = page.Locator(".tm-nbcp__inline-reply .tm-nbcp__reply-input").First;
        await inlineReply.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        await inlineReply.FillAsync("Nested reply text");
        var sendBtn = page.Locator(".tm-nbcp__inline-reply .tm-nbcp__reply-send").First;
        await sendBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var nestedEntry = page.Locator(".tm-nbcp__entry--level-1").Filter(new() { HasText = "Nested reply text" }).First;
        await nestedEntry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await nestedEntry.IsVisibleAsync(), "Nested reply should appear with indent");
        await TakeScreenshotAsync(page, "block_comment_nested_reply");
    }

    [TestMethod]
    [Description("Quote reply auto-inserts citation of parent entry")]
    public async Task BlockComment_QuoteReply()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);
        await AddBlockCommentAsync(page, "Parent comment for quote");

        var replyBtn = page.Locator(".tm-nbcp__entry-action").Filter(new() { HasText = "Reply to this" }).First;
        await replyBtn.ClickAsync();

        var inlineReply = page.Locator(".tm-nbcp__inline-reply .tm-nbcp__reply-input").First;
        await inlineReply.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var quoteText = await inlineReply.InputValueAsync();
        StringAssert.Contains(quoteText, ">", "Quote reply should contain > blockquote marker");
        StringAssert.Contains(quoteText, "Parent comment for quote", "Quote reply should cite parent text");
        await TakeScreenshotAsync(page, "block_comment_quote_reply");
    }

    [TestMethod]
    [Description("Cancel inline reply hides the textarea")]
    public async Task BlockComment_NestedReply_Cancel()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);
        await AddBlockCommentAsync(page, "Parent comment");

        var replyBtn = page.Locator(".tm-nbcp__entry-action").Filter(new() { HasText = "Reply to this" }).First;
        await replyBtn.ClickAsync();

        var inlineReply = page.Locator(".tm-nbcp__inline-reply").First;
        await inlineReply.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var cancelBtn = page.Locator(".tm-nbcp__inline-reply .tm-nbcp__reply-cancel").First;
        await cancelBtn.ClickAsync();

        await inlineReply.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
        Assert.IsFalse(await inlineReply.IsVisibleAsync(), "Inline reply should be hidden after cancel");
        await TakeScreenshotAsync(page, "block_comment_nested_reply_cancel");
    }

    [TestMethod]
    [Description("Reply to a specific page comment entry (nested reply)")]
    public async Task PageComment_NestedReply()
    {
        var page = await OpenNotionEditorAsync();
        await ExpandPageCommentPanelAsync(page);
        await AddPageCommentAsync(page, "Parent page comment");

        var replyBtn = page.Locator(".tm-npcp__entry-action").Filter(new() { HasText = "Reply to this" }).First;
        await replyBtn.ClickAsync();

        var inlineReply = page.Locator(".tm-npcp__inline-reply .tm-npcp__reply-input").First;
        await inlineReply.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        await inlineReply.FillAsync("Nested page reply");
        var sendBtn = page.Locator(".tm-npcp__inline-reply .tm-npcp__reply-send").First;
        await sendBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var nestedEntry = page.Locator(".tm-npcp__entry--level-1").Filter(new() { HasText = "Nested page reply" }).First;
        await nestedEntry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await nestedEntry.IsVisibleAsync(), "Nested page reply should appear with indent");
        await TakeScreenshotAsync(page, "page_comment_nested_reply");
    }

    [TestMethod]
    [Description("Reply to a specific text comment entry (nested reply)")]
    public async Task TextComment_NestedReply()
    {
        var page = await OpenNotionEditorAsync();
        await OpenTextCommentPanelAsync(page);
        await AddTextCommentAsync(page, "Parent text comment");

        var replyBtn = page.Locator(".tm-ntcp__entry-action").Filter(new() { HasText = "Reply to this" }).First;
        await replyBtn.ClickAsync();

        var inlineReply = page.Locator(".tm-ntcp__inline-reply .tm-ntcp__reply-input").First;
        await inlineReply.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        await inlineReply.FillAsync("Nested text reply");
        var sendBtn = page.Locator(".tm-ntcp__inline-reply .tm-ntcp__reply-send").First;
        await sendBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var nestedEntry = page.Locator(".tm-ntcp__entry--level-1").Filter(new() { HasText = "Nested text reply" }).First;
        await nestedEntry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await nestedEntry.IsVisibleAsync(), "Nested text reply should appear with indent");
        await TakeScreenshotAsync(page, "text_comment_nested_reply");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task OpenBlockCommentPanelAsync(IPage page)
    {
        var firstBlock = page.Locator("[data-notion-block]").First;
        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock);
    }

    private async Task OpenBlockCommentPanelOnBlockAsync(IPage page, ILocator block)
    {
        await block.HoverAsync();
        var menuBtn = block.Locator(".tm-notion-handle__btn").Last;
        await menuBtn.ClickAsync();
        var commentBtn = page.Locator(".tm-notion-ctx__item:has-text('Comment')").First;
        await commentBtn.ClickAsync();
        await page.Locator(".tm-nbcp").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
    }

    private async Task OpenNewThreadPanelOnBlockAsync(IPage page, ILocator block)
    {
        await block.HoverAsync();
        var menuBtn = block.Locator(".tm-notion-handle__btn").Last;
        await menuBtn.ClickAsync();
        var newThreadBtn = page.Locator(".tm-notion-ctx__item:has-text('New thread')").First;
        await newThreadBtn.ClickAsync();
        await page.Locator(".tm-nbcp").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
    }

    private async Task AddBlockCommentAsync(IPage page, string text)
    {
        var textarea = page.Locator(".tm-nbcp__reply-input").First;
        await textarea.FillAsync(text);
        var sendBtn = page.Locator(".tm-nbcp__reply-send").First;
        await sendBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);
    }

    private async Task ExpandPageCommentPanelAsync(IPage page)
    {
        var toggle = page.Locator(".tm-npcp__toggle").First;
        await toggle.ClickAsync();
        await page.Locator(".tm-npcp__body").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
    }

    private async Task AddPageCommentAsync(IPage page, string text)
    {
        var textarea = page.Locator(".tm-npcp__new-comment .tm-npcp__reply-input").First;
        await textarea.FillAsync(text);
        var sendBtn = page.Locator(".tm-npcp__reply-send").First;
        await sendBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);
    }

    private async Task OpenTextCommentPanelAsync(IPage page)
    {
        var firstEditable = page.Locator(".tm-notion-editable").First;
        await firstEditable.ClickAsync();
        await firstEditable.EvaluateAsync("el => { el.focus(); document.execCommand('selectAll', false, null); }");
        var toolbar = page.Locator(".tm-notion-inline-toolbar").First;
        await toolbar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var commentBtn = toolbar.Locator("button[title*='Comment'], button[title*='Komentář']").First;
        await commentBtn.ClickAsync();
        await page.Locator(".tm-ntcp").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
    }

    private async Task AddTextCommentAsync(IPage page, string text)
    {
        var textarea = page.Locator(".tm-ntcp__reply-input").First;
        await textarea.FillAsync(text);
        var sendBtn = page.Locator(".tm-ntcp__reply-send").First;
        await sendBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Reactions
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Toggle reaction on block comment entry")]
    public async Task BlockComment_ToggleReaction()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);
        await AddBlockCommentAsync(page, "Reaction test comment");

        var firstEntry = page.Locator(".tm-nbcp__entry").First;
        var addBtn = firstEntry.Locator(".tm-comment-reaction--add").First;
        await addBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Add 👍 via picker first
        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(200);
        var picker = page.Locator(".tm-comment-reaction-picker__popover").First;
        await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var thumbsUpBtn = picker.Locator(".tm-comment-reaction-picker__item").First;
        await thumbsUpBtn.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        // Verify reaction is visible and active
        var thumbsUp = firstEntry.Locator(".tm-comment-reaction").First;
        await thumbsUp.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await thumbsUp.EvaluateAsync<bool>("el => el.classList.contains('tm-comment-reaction--active')"), "Reaction should be active after adding via picker");

        // Click to toggle off
        await thumbsUp.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        // Verify the same element is now inactive (or removed)
        var reactionsAfterToggle = firstEntry.Locator(".tm-comment-reaction");
        var countAfter = await reactionsAfterToggle.CountAsync();
        if (countAfter > 0)
        {
            var firstReaction = reactionsAfterToggle.First;
            var isActive = await firstReaction.EvaluateAsync<bool>("el => el.classList.contains('tm-comment-reaction--active')");
            Assert.IsFalse(isActive, "Reaction should be inactive after toggle off");
        }

        // Re-add via picker to verify full cycle
        var addBtn2 = firstEntry.Locator(".tm-comment-reaction--add").First;
        await addBtn2.ClickAsync();
        await page.WaitForTimeoutAsync(200);
        var picker2 = page.Locator(".tm-comment-reaction-picker__popover").First;
        await picker2.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await picker2.Locator(".tm-comment-reaction-picker__item").First.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        var thumbsUp2 = firstEntry.Locator(".tm-comment-reaction").First;
        await thumbsUp2.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await thumbsUp2.EvaluateAsync<bool>("el => el.classList.contains('tm-comment-reaction--active')"), "Reaction should be active after re-adding via picker");
        await TakeScreenshotAsync(page, "block_comment_toggle_reaction");
    }

    [TestMethod]
    [Description("Add a new reaction via picker on block comment entry")]
    public async Task BlockComment_AddReactionViaPicker()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);
        await AddBlockCommentAsync(page, "Picker test comment");

        var firstEntry = page.Locator(".tm-nbcp__entry").First;
        var addBtn = firstEntry.Locator(".tm-comment-reaction--add").First;
        await addBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        await addBtn.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        var picker = page.Locator(".tm-comment-reaction-picker__popover").First;
        await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var party = picker.Locator(".tm-comment-reaction-picker__item").Filter(new() { HasText = "🎉" }).First;
        await party.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var newReaction = firstEntry.Locator(".tm-comment-reaction").Filter(new() { HasText = "🎉" }).First;
        await newReaction.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await newReaction.IsVisibleAsync(), "New reaction should appear after picker selection");
        await TakeScreenshotAsync(page, "block_comment_add_reaction_picker");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Mentions
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Mention autocomplete dropdown appears and inserts a mention")]
    public async Task BlockComment_MentionAutocomplete()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);

        var textarea = page.Locator(".tm-nbcp__reply-input").First;
        await textarea.FillAsync("Hi @ali");
        await page.WaitForTimeoutAsync(400);

        var dropdown = page.Locator(".tm-comment-mention-dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await dropdown.IsVisibleAsync(), "Mention dropdown should appear");

        // Use keyboard to select the first user
        await textarea.PressAsync("ArrowDown");
        await textarea.PressAsync("Enter");
        await page.WaitForTimeoutAsync(400);

        // Verify the textarea contains the inserted mention
        var textareaValue = await textarea.InputValueAsync();
        StringAssert.Contains(textareaValue, "@Alice");

        // Submit comment
        var sendBtn = page.Locator(".tm-nbcp__reply-send").First;
        await sendBtn.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        var mention = page.Locator(".tm-nbcp__entry-text .tm-mention").First;
        await mention.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await mention.IsVisibleAsync(), "Mention should be rendered in comment HTML");
        await TakeScreenshotAsync(page, "block_comment_mention_autocomplete");
    }

    [TestMethod]
    [Description("Mention is highlighted as a styled span in rendered comment HTML")]
    public async Task BlockComment_MentionHighlight()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);

        // Type a simple mention directly and submit
        var textarea = page.Locator(".tm-nbcp__reply-input").First;
        await textarea.FillAsync("Hello @alice");
        await textarea.PressAsync("Escape"); // close dropdown if open
        await page.WaitForTimeoutAsync(200);
        var sendBtn = page.Locator(".tm-nbcp__reply-send").First;
        await sendBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var mention = page.Locator(".tm-nbcp__entry-text .tm-mention").First;
        await mention.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var dataUserId = await mention.GetAttributeAsync("data-user-id");
        Assert.IsFalse(string.IsNullOrEmpty(dataUserId), "Mention span should have data-user-id attribute");
        await TakeScreenshotAsync(page, "block_comment_mention_highlight");
    }

    [TestMethod]
    [Description("Mention in block comment generates a notification")]
    public async Task BlockComment_Mention_Notification()
    {
        var page = await OpenNotionEditorAsync();
        await ClearNotificationsAsync(page);
        await OpenBlockCommentPanelAsync(page);

        // Add a mention comment
        var textarea = page.Locator(".tm-nbcp__reply-input").First;
        await textarea.FillAsync("Hey @alice check this out");
        await textarea.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);
        var sendBtn = page.Locator(".tm-nbcp__reply-send").First;
        await sendBtn.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        // Close panel so backdrop doesn't intercept bell click
        await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Verify bell badge shows 1 unread notification
        var badge = page.Locator(".tm-notification-bell__badge").First;
        await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var count = await badge.InnerTextAsync();
        Assert.AreEqual("1", count, "Bell badge should show 1 unread mention notification");

        // Open notification dropdown
        var bellBtn = page.Locator(".tm-notification-bell__button").First;
        await bellBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var dropdown = page.Locator(".tm-notification-bell__dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Notification is stored for user "alice" (the mentioned user), not "demo"
        // so the dropdown may be empty for current user, but the badge count proves
        // the mention notification was generated.
        await TakeScreenshotAsync(page, "block_comment_mention_notification");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Notifications
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Notification bell shows unread count after mention")]
    public async Task Notification_Bell_ShowsCount()
    {
        var page = await OpenNotionEditorAsync();
        await ClearNotificationsAsync(page);
        await OpenBlockCommentPanelAsync(page);

        // Add a mention comment to generate a notification
        var textarea = page.Locator(".tm-nbcp__reply-input").First;
        await textarea.FillAsync("Hey @alice check this out");
        await textarea.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);
        var sendBtn = page.Locator(".tm-nbcp__reply-send").First;
        await sendBtn.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        var badge = page.Locator(".tm-notification-bell__badge").First;
        await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var count = await badge.InnerTextAsync();
        Assert.AreEqual("1", count, "Bell badge should show 1 unread notification");
        await TakeScreenshotAsync(page, "notification_bell_shows_count");
    }

    [TestMethod]
    [Description("Clicking notification bell opens dropdown panel")]
    public async Task Notification_Bell_ClickOpensPanel()
    {
        var page = await OpenNotionEditorAsync();
        await ClearNotificationsAsync(page);
        await OpenBlockCommentPanelAsync(page);

        // Generate a notification via mention
        var textarea = page.Locator(".tm-nbcp__reply-input").First;
        await textarea.FillAsync("Hey @alice");
        await textarea.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);
        var sendBtn = page.Locator(".tm-nbcp__reply-send").First;
        await sendBtn.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        // Close panel so backdrop doesn't intercept bell click
        await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Click the bell
        var bellBtn = page.Locator(".tm-notification-bell__button").First;
        await bellBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var dropdown = page.Locator(".tm-notification-bell__dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await dropdown.IsVisibleAsync(), "Notification dropdown should be visible");
        await TakeScreenshotAsync(page, "notification_bell_click_opens");
    }

    [TestMethod]
    [Description("Mark all as read clears notification bell badge")]
    public async Task Notification_Panel_MarkAllAsRead()
    {
        var page = await OpenNotionEditorAsync();
        await ClearNotificationsAsync(page);

        // Inject a notification for the current user (demo)
        await page.EvaluateAsync("async () => { if (typeof DotNet !== 'undefined') await DotNet.invokeMethodAsync('Tempo.Blazor.Demo', 'NotifyDemoAsync', 'Mark all read test', '/notion-editor'); }");
        await page.WaitForTimeoutAsync(500);

        // Open bell dropdown
        var bellBtn = page.Locator(".tm-notification-bell__button").First;
        await bellBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Click Mark all as read
        var markAllBtn = page.Locator(".tm-notification-bell__mark-all").First;
        await markAllBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await markAllBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Badge should disappear
        var badge = page.Locator(".tm-notification-bell__badge").First;
        Assert.AreEqual(0, await badge.CountAsync(), "Badge should disappear after mark all as read");
        await TakeScreenshotAsync(page, "notification_panel_mark_all_read");
    }

    [TestMethod]
    [Description("Watch/Unwatch thread button toggles subscription")]
    public async Task BlockComment_SubscribeUnsubscribe()
    {
        var page = await OpenNotionEditorAsync();
        await OpenBlockCommentPanelAsync(page);
        await AddBlockCommentAsync(page, "Subscribe test comment");

        // Should show "Unwatch" because author is auto-subscribed
        var unwatchBtn = page.Locator(".tm-nbcp__watch-btn--watching").First;
        await unwatchBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await unwatchBtn.IsVisibleAsync(), "Unwatch button should be visible for auto-subscribed author");

        // Click Unwatch
        await unwatchBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Should now show "Watch"
        var watchBtn = page.Locator(".tm-nbcp__watch-btn").First;
        await watchBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await watchBtn.IsVisibleAsync(), "Watch button should appear after unsubscribing");

        // Click Watch again
        await watchBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var unwatchBtn2 = page.Locator(".tm-nbcp__watch-btn--watching").First;
        await unwatchBtn2.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await unwatchBtn2.IsVisibleAsync(), "Unwatch button should reappear after subscribing");
        await TakeScreenshotAsync(page, "block_comment_subscribe_unsubscribe");
    }

    [TestMethod]
    [Description("Watch/Unwatch thread button toggles subscription in page comment panel")]
    public async Task PageComment_SubscribeUnsubscribe()
    {
        var page = await OpenNotionEditorAsync();
        await ExpandPageCommentPanelAsync(page);
        await AddPageCommentAsync(page, "Page subscribe test");

        // Should show "Unwatch" because author is auto-subscribed
        var unwatchBtn = page.Locator(".tm-npcp__watch-btn--watching").First;
        await unwatchBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await unwatchBtn.IsVisibleAsync(), "Unwatch button should be visible for auto-subscribed author");

        // Click Unwatch
        await unwatchBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Should now show "Watch"
        var watchBtn = page.Locator(".tm-npcp__watch-btn").First;
        await watchBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await watchBtn.IsVisibleAsync(), "Watch button should appear after unsubscribing");

        // Click Watch again
        await watchBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var unwatchBtn2 = page.Locator(".tm-npcp__watch-btn--watching").First;
        await unwatchBtn2.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await unwatchBtn2.IsVisibleAsync(), "Unwatch button should reappear after subscribing");
        await TakeScreenshotAsync(page, "page_comment_subscribe_unsubscribe");
    }

    [TestMethod]
    [Description("Watch/Unwatch thread button toggles subscription in text comment panel")]
    public async Task TextComment_SubscribeUnsubscribe()
    {
        var page = await OpenNotionEditorAsync();
        await OpenTextCommentPanelAsync(page);
        await AddTextCommentAsync(page, "Text subscribe test");

        // Should show "Unwatch" because author is auto-subscribed
        var unwatchBtn = page.Locator(".tm-ntcp__watch-btn--watching").First;
        await unwatchBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await unwatchBtn.IsVisibleAsync(), "Unwatch button should be visible for auto-subscribed author");

        // Click Unwatch
        await unwatchBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Should now show "Watch"
        var watchBtn = page.Locator(".tm-ntcp__watch-btn").First;
        await watchBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await watchBtn.IsVisibleAsync(), "Watch button should appear after unsubscribing");

        // Click Watch again
        await watchBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var unwatchBtn2 = page.Locator(".tm-ntcp__watch-btn--watching").First;
        await unwatchBtn2.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await unwatchBtn2.IsVisibleAsync(), "Unwatch button should reappear after subscribing");
        await TakeScreenshotAsync(page, "text_comment_subscribe_unsubscribe");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Notification Navigation (2D.6)
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking a notification item navigates to its DeepLink")]
    public async Task Notification_Panel_ClickNavigates()
    {
        var page = await OpenNotionEditorAsync();
        await ClearNotificationsAsync(page);

        // Inject a demo notification via JS interop
        await page.EvaluateAsync("async () => { if (typeof DotNet !== 'undefined') await DotNet.invokeMethodAsync('Tempo.Blazor.Demo', 'NotifyDemoAsync', 'Navigate test', '/notion-editor#nav-test'); }");
        await page.WaitForTimeoutAsync(500);

        // Open bell dropdown
        var bellBtn = page.Locator(".tm-notification-bell__button").First;
        await bellBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var dropdown = page.Locator(".tm-notification-bell__dropdown").First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Click the notification item
        var item = page.Locator(".tm-notification-bell__item").First;
        await item.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await item.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Verify URL changed to include the hash
        var url = page.Url;
        StringAssert.Contains(url, "#nav-test", "Clicking notification should navigate to its DeepLink");
        await TakeScreenshotAsync(page, "notification_panel_click_navigates");
    }

    [TestMethod]
    [Description("New notification shows a toast when panel is closed")]
    public async Task Notification_Toast_Appears()
    {
        var page = await OpenNotionEditorAsync();
        await ClearNotificationsAsync(page);

        // Ensure panel is closed — no-op, fresh page
        // Inject a demo notification
        await page.EvaluateAsync("async () => { if (typeof DotNet !== 'undefined') await DotNet.invokeMethodAsync('Tempo.Blazor.Demo', 'NotifyDemoAsync', 'Toast test', '/notion-editor'); }");
        await page.WaitForTimeoutAsync(600);

        // Verify toast appears
        var toast = page.Locator(".tm-notification-toast").First;
        await toast.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await toast.IsVisibleAsync(), "Toast should appear for new notification");

        // Verify toast message
        var msg = toast.Locator(".tm-notification-toast__message").First;
        var text = await msg.TextContentAsync();
        StringAssert.Contains(text, "Toast test");

        await TakeScreenshotAsync(page, "notification_toast_appears");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Unread Indicators (2D.4)
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("New block comment shows unread dot on margin thread badge")]
    public async Task BlockComment_UnreadIndicator()
    {
        var page = await OpenNotionEditorAsync();
        var firstBlock = page.Locator("[data-notion-block]").First;

        // Open block comment panel via context menu (block has no comments yet)
        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock);

        // Add a comment — panel stays open, no read-mark happens because
        // GetBlockCommentsAsync returned empty before the comment was created.
        await AddBlockCommentAsync(page, "Unread dot test");

        // Close panel
        var closeBtn = page.Locator(".tm-nbcp__close-btn").First;
        await closeBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Verify unread dot is visible on the block badge
        var dot = firstBlock.Locator(".tm-notion-block__comment-thread-dot").First;
        await dot.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await dot.IsVisibleAsync(), "Unread dot should appear after adding a comment and closing panel");
        await TakeScreenshotAsync(page, "block_comment_unread_indicator");
    }

    [TestMethod]
    [Description("Opening block comment panel clears the unread dot")]
    public async Task BlockComment_MarkAsRead_ClearsIndicator()
    {
        var page = await OpenNotionEditorAsync();
        var firstBlock = page.Locator("[data-notion-block]").First;

        // 1. Open panel, add comment, close panel → dot visible
        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "Mark as read test");
        var closeBtn = page.Locator(".tm-nbcp__close-btn").First;
        await closeBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var dotBefore = firstBlock.Locator(".tm-notion-block__comment-thread-dot").First;
        await dotBefore.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await dotBefore.IsVisibleAsync(), "Dot should be visible before re-opening panel");

        // 2. Re-open panel → auto-mark-read clears the dot
        await firstBlock.Locator(".tm-notion-block__comment-thread").First.ClickAsync();
        await page.Locator(".tm-nbcp").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.WaitForTimeoutAsync(300);

        // 3. Close panel
        await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // 4. Verify dot is gone
        var dotAfter = firstBlock.Locator(".tm-notion-block__comment-thread-dot");
        Assert.AreEqual(0, await dotAfter.CountAsync(), "Unread dot should disappear after opening panel (auto-mark-read)");
        await TakeScreenshotAsync(page, "block_comment_mark_as_read_clears");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Multiple Threads per Block (Phase 3)
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Create a second thread on the same block via New thread menu")]
    public async Task BlockComment_MultipleThreads_Create()
    {
        var page = await OpenNotionEditorAsync();
        var firstBlock = page.Locator("[data-notion-block]").First;

        // 1. Create first thread
        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "First thread");
        await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // 2. Create second thread via New thread menu
        await OpenNewThreadPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "Second thread");
        await page.WaitForTimeoutAsync(500);

        // Panel shows detail of the newly created thread; click back to see list
        var backBtn = page.Locator(".tm-nbcp__back-btn").First;
        await backBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await backBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // 3. Thread list should show both threads
        var threadCards = page.Locator(".tm-nbcp__thread-card");
        await threadCards.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var cardCount = await threadCards.CountAsync();
        Assert.AreEqual(2, cardCount, "Thread list should show 2 threads");

        // 4. Verify previews
        var preview1 = threadCards.Filter(new() { HasText = "First thread" }).First;
        var preview2 = threadCards.Filter(new() { HasText = "Second thread" }).First;
        Assert.IsTrue(await preview1.IsVisibleAsync(), "First thread preview should be visible");
        Assert.IsTrue(await preview2.IsVisibleAsync(), "Second thread preview should be visible");

        await TakeScreenshotAsync(page, "block_comment_multiple_threads_create");
    }

    [TestMethod]
    [Description("Switch between threads in the panel list and detail views")]
    public async Task BlockComment_MultipleThreads_Switch()
    {
        var page = await OpenNotionEditorAsync();
        var firstBlock = page.Locator("[data-notion-block]").First;

        // 1. Create two threads
        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "Thread A");
        await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        await OpenNewThreadPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "Thread B");
        await page.WaitForTimeoutAsync(500);

        // Go back to thread list
        var backBtn = page.Locator(".tm-nbcp__back-btn").First;
        await backBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await backBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var threadCards = page.Locator(".tm-nbcp__thread-card");
        await threadCards.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // 2. Click first thread card → detail view
        var cardA = threadCards.Filter(new() { HasText = "Thread A" }).First;
        await cardA.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var entryA = page.Locator(".tm-nbcp__entry-text").Filter(new() { HasText = "Thread A" }).First;
        await entryA.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await entryA.IsVisibleAsync(), "Thread A detail should be visible");

        // 3. Click back → list view
        var backBtn2 = page.Locator(".tm-nbcp__back-btn").First;
        await backBtn2.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var listAfterBack = page.Locator(".tm-nbcp__thread-list").First;
        Assert.IsTrue(await listAfterBack.IsVisibleAsync(), "Thread list should reappear after back");

        // 4. Click second thread card → detail view
        var cardB = threadCards.Filter(new() { HasText = "Thread B" }).First;
        await cardB.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var entryB = page.Locator(".tm-nbcp__entry-text").Filter(new() { HasText = "Thread B" }).First;
        await entryB.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await entryB.IsVisibleAsync(), "Thread B detail should be visible");

        await TakeScreenshotAsync(page, "block_comment_multiple_threads_switch");
    }

    [TestMethod]
    [Description("Resolve and unresolve a thread directly from the thread list")]
    public async Task BlockComment_ResolveUnresolve_FromThreadList()
    {
        var page = await OpenNotionEditorAsync();
        var firstBlock = page.Locator("[data-notion-block]").First;

        // 1. Create first thread
        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "Thread to resolve");
        await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // 2. Create second thread
        await OpenNewThreadPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "Second thread");
        await page.WaitForTimeoutAsync(500);

        // Panel shows detail of new thread; click back to see list
        var backBtn = page.Locator(".tm-nbcp__back-btn").First;
        await backBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await backBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // 3. Thread list should show both threads
        var threadCards = page.Locator(".tm-nbcp__thread-card");
        await threadCards.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.AreEqual(2, await threadCards.CountAsync(), "Thread list should show 2 threads");

        // 4. Resolve first thread from list
        var firstCard = threadCards.Filter(new() { HasText = "Thread to resolve" }).First;
        var resolveBtn = firstCard.Locator(".tm-nbcp__thread-card__action").Filter(new() { HasText = "Resolve" }).First;
        await resolveBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await resolveBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // 5. Verify resolved badge appears on the card
        var resolvedBadge = firstCard.Locator(".tm-nbcp__thread-card__resolved-badge").First;
        await resolvedBadge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await resolvedBadge.IsVisibleAsync(), "Resolved badge should appear on resolved thread card");

        // 6. Unresolve the same thread from list
        var unresolveBtn = firstCard.Locator(".tm-nbcp__thread-card__action--unresolve").First;
        await unresolveBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await unresolveBtn.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // 7. Verify resolved badge is gone
        var cardAfter = threadCards.Filter(new() { HasText = "Thread to resolve" }).First;
        var resolvedAfter = cardAfter.Locator(".tm-nbcp__thread-card__resolved-badge");
        Assert.AreEqual(0, await resolvedAfter.CountAsync(), "Resolved badge should disappear after unresolve");

        await TakeScreenshotAsync(page, "block_comment_resolve_from_list");
    }

    [TestMethod]
    [Description("Margin badge shows correct thread count when multiple threads exist")]
    public async Task BlockComment_MultipleThreads_BadgeCount()
    {
        var page = await OpenNotionEditorAsync();
        var firstBlock = page.Locator("[data-notion-block]").First;

        // 1. Create first thread
        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "Badge thread 1");
        await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // 2. Create second thread
        await OpenNewThreadPanelOnBlockAsync(page, firstBlock);
        await AddBlockCommentAsync(page, "Badge thread 2");
        await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // 3. Open panel via badge to mark threads as read (clears unread dot)
        var badge = firstBlock.Locator(".tm-notion-block__comment-thread").First;
        await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await badge.ClickAsync();
        await page.Locator(".tm-nbcp").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.WaitForTimeoutAsync(300);

        // 4. Close panel — badge should now show count instead of dot
        await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // 5. Verify badge shows "2" (unresolved count)
        var countSpan = badge.Locator(".tm-notion-block__comment-thread-count").First;
        await countSpan.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var countText = await countSpan.TextContentAsync();
        Assert.AreEqual("2", countText, "Badge should show unresolved count 2 when 2 threads exist");

        await TakeScreenshotAsync(page, "block_comment_multiple_threads_badge");
    }
}

[TestClass]
public class NotionCommentsRecoveryE2ETests : NotionE2ETestBase
{
    private const string CommentBlockId = "eb100010-0000-0000-0000-000000000002";
    private const string SecondaryBlockId = "eb100010-0000-0000-0000-000000000003";

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    public async Task EB10_BlockCommentStates_AreCaptured()
    {
        var page = await OpenNotionEditorAsync();
        await SeedCommentsPageAsync();

        var firstBlock = page.Locator($"[data-block-id='{CommentBlockId}']").First;
        var secondBlock = page.Locator($"[data-block-id='{SecondaryBlockId}']").First;

        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock, createNewThread: true);
        await AddBlockCommentAsync(page, "First unresolved thread for EB10 margin review.");
        await CloseBlockCommentPanelAsync(page);

        await OpenBlockCommentPanelOnBlockAsync(page, secondBlock, createNewThread: true);
        await AddBlockCommentAsync(page, "Second thread that will be resolved but still visible for review.");
        await page.Locator(".tm-nbcp__resolve-btn").Filter(new LocatorFilterOptions { HasText = "Resolve" }).First.ClickAsync();
        await page.WaitForTimeoutAsync(500);
        await CloseBlockCommentPanelAsync(page);

        await firstBlock.Locator(".tm-notion-block__comment-thread").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await secondBlock.Locator(".tm-notion-block__comment-thread--resolved").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await CaptureBaselineAsync("comments", "margin-threads-unread-resolved", page.Locator(".tm-notion-page").First);

        await OpenBlockCommentPanelOnBlockAsync(page, firstBlock, createNewThread: true);
        await AddBlockCommentAsync(page, "Additional EB10 thread for list density.");
        await page.Locator(".tm-nbcp__back-btn").First.ClickAsync();
        await page.Locator(".tm-nbcp__thread-card").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await CaptureBaselineAsync("comments", "block-panel-thread-list", page.Locator(".tm-nbcp").First);

        await page.Locator(".tm-nbcp__thread-card").Filter(new LocatorFilterOptions { HasText = "First unresolved thread" }).First.ClickAsync();
        await page.Locator(".tm-nbcp__entry").First.Locator(".tm-comment-reaction--add").First.ClickAsync();
        await page.Locator(".tm-comment-reaction-picker__popover").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await page.Locator(".tm-comment-reaction-picker__item").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);
        await CaptureBaselineAsync("comments", "block-panel-reactions", page.Locator(".tm-nbcp").First);

        await AddNestedReplyAsync(page,
            "Nested reply with a longer EB10 review note that verifies wrapping, indentation, author metadata and composer spacing inside a busy block comment panel.");
        await AddNestedReplyAsync(page,
            "Second nested reply keeps the thread tall enough to reveal vertical rhythm without forcing horizontal overflow.");
        await CaptureBaselineAsync("comments", "block-panel-long-thread", page.Locator(".tm-nbcp").First);

        await page.Locator(".tm-nbcp__resolve-btn").Filter(new LocatorFilterOptions { HasText = "Resolve" }).First.ClickAsync();
        await page.Locator(".tm-nbcp__resolved-banner").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await CaptureBaselineAsync("comments", "block-panel-resolved", page.Locator(".tm-nbcp").First);

        await page.Locator(".tm-nbcp__entry-action--danger").Last.ClickAsync();
        await page.Locator(".tm-dialog-btn-ok").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await page.Locator(".tm-dialog-btn-ok").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);
        await CaptureBaselineAsync("comments", "block-panel-after-delete", page.Locator(".tm-nbcp").First);

        await AssertNoHorizontalOverflowAsync(page);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    public async Task EB10_TextAnchorPagePanelAndNoProviderStates_AreCaptured()
    {
        var page = await OpenNotionEditorAsync();
        await SeedCommentsPageAsync();

        var editable = page.Locator($"[data-block-id='{CommentBlockId}'] .tm-notion-editable").First;
        await editable.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await SelectLocatorContentsAsync(page, editable);
        await ClickNotionToolbarButtonAsync(page, "Comment");
        await page.Locator(".tm-ntcp").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await AddTextCommentAsync(page, "Inline EB10 anchor comment.");
        await page.Locator("mark.tm-notion-comment-highlight").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await CaptureBaselineAsync("comments", "text-anchor-panel-and-mark", page.Locator(".tm-notion-editor").First);

        await page.Locator(".tm-ntcp__close-btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        await ExpandPageCommentPanelAsync(page);
        await page.Locator(".tm-npcp__thread").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await CaptureBaselineAsync("comments", "page-comment-panel", page.Locator(".tm-npcp").First);

        page = await OpenNotionEditorAsync("?disableCommentProvider=true");
        await SeedCommentlessPageAsync();
        Assert.AreEqual(0, await page.Locator(".tm-notion-page__comments").CountAsync(), "Page comment section should be absent without a comment provider.");

        var block = page.Locator("[data-notion-block]").First;
        await OpenBlockContextMenuAsync(page, block);
        Assert.AreEqual(0, await page.Locator(".tm-notion-ctx__item").Filter(new LocatorFilterOptions { HasText = "Comment" }).CountAsync(),
            "Block context menu should not offer comment actions without a comment provider.");
        await CaptureBaselineAsync("comments", "no-comment-provider", page.Locator(".tm-notion-editor").First);

        await AssertNoHorizontalOverflowAsync(page);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    public async Task CF10_PageCommentsStates_AreCaptured()
    {
        var page = await OpenNotionEditorAsync("?disableTemplateProvider=true");
        await SeedCommentlessPageAsync();
        await CreateBlankPageFromTemplateGalleryAsync(page);

        await ExpandPageCommentPanelAsync(page);
        await page.Locator(".tm-npcp__status").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        Assert.AreEqual(0, await page.Locator(".tm-npcp__thread").CountAsync(), "The empty page-comment baseline should not contain comment threads.");
        await CaptureBaselineAsync("comments", "cf10-page-comments-empty", page.Locator(".tm-npcp").First);

        await AddPageCommentAsync(page, "CF10 page-level comment separates page discussion from block annotations.");
        var pageCommentPanel = page.Locator(".tm-npcp").First;
        await pageCommentPanel.Locator(".tm-npcp__entry-text").Filter(new LocatorFilterOptions { HasText = "CF10 page-level comment" }).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });

        await AddPageCommentReactionAsync(pageCommentPanel);
        await AddPageThreadReplyAsync(pageCommentPanel, "Reply keeps the page-level conversation close to the page footer without looking like an inline block comment.");
        await CaptureBaselineAsync("comments", "cf10-page-comments-section", pageCommentPanel);

        await AddInlinePageReplyAsync(pageCommentPanel,
            "Nested page reply with a longer CF10 review note that checks wrapping, indentation, author metadata and mention-capable composer spacing in a busy page-level thread.");
        await AddInlinePageReplyAsync(pageCommentPanel,
            "Second nested page reply keeps the thread tall enough for vertical rhythm review while preserving a calm footer layout.");
        await SetViewportAsync(1280, 1100);
        var pageCommentThread = pageCommentPanel.Locator(".tm-npcp__thread").First;
        await CenterLocatorInViewportAsync(pageCommentThread);
        await CaptureViewportClipBaselineAsync("comments", "cf10-page-comments-long-thread", pageCommentThread);

        var resolveButton = pageCommentPanel.Locator(".tm-npcp__thread-action").Filter(new LocatorFilterOptions { HasText = "Resolve" }).First;
        await resolveButton.ClickAsync();
        await pageCommentPanel.Locator(".tm-npcp__thread--resolved").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        var resolvedThread = pageCommentPanel.Locator(".tm-npcp__thread--resolved").First;
        await CenterLocatorInViewportAsync(resolvedThread);
        await CaptureViewportClipBaselineAsync("comments", "cf10-page-comments-resolved", resolvedThread);
        await SetViewportAsync(1280, 720);

        var providerless = await OpenNotionEditorAsync("?disableCommentProvider=true");
        await SeedCommentlessPageAsync();
        Assert.AreEqual(0, await providerless.Locator(".tm-notion-page__comments").CountAsync(), "Page comment section should be hidden without a comment provider.");
        await CaptureBaselineAsync("comments", "cf10-page-comments-providerless-hidden", providerless.Locator(".tm-notion-editor").First);

        await AssertNoHorizontalOverflowAsync(page);
        await AssertNoHorizontalOverflowAsync(providerless);
        TestContext.WriteLine("UX CF10 review: page-level comments stay visually separated from block comments, empty/providerless states avoid misleading affordances, replies and reactions remain accessible, and resolved threads preserve readable context.");
    }

    private static async Task CreateBlankPageFromTemplateGalleryAsync(IPage page)
    {
        await page.Locator(".tm-ns-btn-new").First.ClickAsync();
        await page.WaitForSelectorAsync(".tm-ntg", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        await page.Locator("[data-template-id='blank'] .tm-ntg__use").ClickAsync();
        await page.WaitForSelectorAsync(".tm-ntg", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 10000
        });

        await page.Locator(".tm-notion-page__empty-hint").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
    }

    private static async Task CenterLocatorInViewportAsync(ILocator locator)
    {
        await locator.EvaluateAsync(
            """
            element => {
                const main = element.closest('.tm-notion-main');
                if (!main) {
                    element.scrollIntoView({ block: 'center', inline: 'nearest' });
                    return;
                }

                const topbar = main.querySelector('.tm-notion-topbar');
                const mainRect = main.getBoundingClientRect();
                const elementRect = element.getBoundingClientRect();
                const topbarHeight = topbar ? topbar.getBoundingClientRect().height : 0;
                const topPadding = topbarHeight + 16;
                main.scrollTop += elementRect.top - mainRect.top - topPadding;
            }
            """);
        await locator.Page.WaitForTimeoutAsync(200);
    }

    private async Task<NotionBaselineCapture> CaptureViewportClipBaselineAsync(string area, string state, ILocator region)
    {
        var outputDir = BaselineOutput.DirectoryFor(TestContext, "notion",
            SanitizeBaselinePart(area));
        Directory.CreateDirectory(outputDir);

        var safeState = SanitizeBaselinePart(state);
        var fullPath = Path.Combine(outputDir, $"{safeState}.png");
        var regionPath = Path.Combine(outputDir, $"{safeState}.region.png");

        await Page.WaitForTimeoutAsync(250);
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            Type = ScreenshotType.Png,
            FullPage = true
        });

        var clip = await region.EvaluateAsync<ViewportClip>(
            """
            element => {
                const rect = element.getBoundingClientRect();
                const main = element.closest('.tm-notion-main');
                const topbar = main ? main.querySelector('.tm-notion-topbar') : null;
                const topbarRect = topbar ? topbar.getBoundingClientRect() : null;
                const mainRect = main ? main.getBoundingClientRect() : null;
                const topbarBottom = topbarRect ? topbarRect.bottom : 0;
                const padding = 8;
                const x = Math.max(0, rect.left);
                const y = Math.max(0, Math.max(rect.top, topbarBottom + padding));
                const right = Math.min(window.innerWidth, rect.right);
                const bottom = Math.min(window.innerHeight, rect.bottom, mainRect ? mainRect.bottom - padding : window.innerHeight);

                return {
                    x,
                    y,
                    width: Math.max(1, right - x),
                    height: Math.max(1, bottom - y)
                };
            }
            """);

        Assert.IsTrue(clip.Width > 1 && clip.Height > 1, $"Baseline region for {state} must have a visible viewport clip.");
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = regionPath,
            Type = ScreenshotType.Png,
            Clip = new Clip
            {
                X = (float)clip.X,
                Y = (float)clip.Y,
                Width = (float)clip.Width,
                Height = (float)clip.Height
            }
        });

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(regionPath);
        return new NotionBaselineCapture(fullPath, regionPath);
    }

    private static string SanitizeBaselinePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(character => invalid.Contains(character) || char.IsWhiteSpace(character) ? '-' : char.ToLowerInvariant(character))
            .ToArray();
        return new string(chars);
    }

    private sealed class ViewportClip
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private static async Task OpenBlockContextMenuAsync(IPage page, ILocator block)
    {
        await block.ScrollIntoViewIfNeededAsync();
        await block.HoverAsync();
        var menuButton = block.Locator(".tm-notion-handle__btn").Last;
        await menuButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await menuButton.ClickAsync();
        await page.Locator(".tm-notion-ctx").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    private static async Task OpenBlockCommentPanelOnBlockAsync(IPage page, ILocator block, bool createNewThread = false)
    {
        await OpenBlockContextMenuAsync(page, block);
        var actionText = createNewThread ? "New thread" : "Comment";
        await page.Locator(".tm-notion-ctx__item").Filter(new LocatorFilterOptions { HasText = actionText }).First.ClickAsync();
        await page.Locator(".tm-nbcp").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    private static async Task CloseBlockCommentPanelAsync(IPage page)
    {
        await page.Locator(".tm-nbcp__close-btn").First.ClickAsync();
        await page.Locator(".tm-nbcp").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
    }

    private static async Task AddBlockCommentAsync(IPage page, string text)
    {
        var input = page.Locator(".tm-nbcp__reply-input").First;
        await input.FillAsync(text);
        await page.Locator(".tm-nbcp__reply-send").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);
    }

    private static async Task AddNestedReplyAsync(IPage page, string text)
    {
        await page.Locator(".tm-nbcp__entry-action").Filter(new LocatorFilterOptions { HasText = "Reply to this" }).First.ClickAsync();
        var input = page.Locator(".tm-nbcp__inline-reply .tm-nbcp__reply-input").First;
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await input.FillAsync(text);
        await page.Locator(".tm-nbcp__inline-reply .tm-nbcp__reply-send").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);
    }

    private static async Task AddTextCommentAsync(IPage page, string text)
    {
        var input = page.Locator(".tm-ntcp__reply-input").First;
        await input.FillAsync(text);
        await page.Locator(".tm-ntcp__reply-send").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);
    }

    private static async Task AddPageCommentAsync(IPage page, string text)
    {
        var input = page.Locator(".tm-npcp__new-comment .tm-npcp__reply-input").First;
        await input.FillAsync(text);
        await page.Locator(".tm-npcp__new-comment .tm-npcp__reply-send").First.ClickAsync();
        await page.Locator(".tm-npcp__entry-text").Filter(new LocatorFilterOptions { HasText = text }).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    private static async Task AddPageThreadReplyAsync(ILocator panel, string text)
    {
        await panel.Locator(".tm-npcp__reply-trigger").First.ClickAsync();
        var input = panel.Locator(".tm-npcp__thread-reply .tm-npcp__reply-input").First;
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await input.FillAsync(text);
        await panel.Locator(".tm-npcp__thread-reply .tm-npcp__reply-send").First.ClickAsync();
        await panel.Locator(".tm-npcp__entry-text").Filter(new LocatorFilterOptions { HasText = text }).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    private static async Task AddInlinePageReplyAsync(ILocator panel, string text)
    {
        await panel.Locator(".tm-npcp__entry-action").Filter(new LocatorFilterOptions { HasText = "Reply to this" }).First.ClickAsync();
        var input = panel.Locator(".tm-npcp__inline-reply .tm-npcp__reply-input").First;
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await input.FillAsync(text);
        await panel.Locator(".tm-npcp__inline-reply .tm-npcp__reply-send").First.ClickAsync();
        await panel.Locator(".tm-npcp__entry-text").Filter(new LocatorFilterOptions { HasText = text }).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    private static async Task AddPageCommentReactionAsync(ILocator panel)
    {
        await panel.Locator(".tm-comment-reaction--add").First.ClickAsync();
        await panel.Page.Locator(".tm-comment-reaction-picker__popover").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await panel.Page.Locator(".tm-comment-reaction-picker__item").First.ClickAsync();
        await panel.Locator(".tm-comment-reaction").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    private static async Task ExpandPageCommentPanelAsync(IPage page)
    {
        await page.Locator(".tm-npcp__toggle").First.ClickAsync();
        await page.Locator(".tm-npcp__body").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    private static async Task AssertNoHorizontalOverflowAsync(IPage page)
    {
        var hasOverflow = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.IsFalse(hasOverflow, "EB10 comment screenshots should not introduce document-level horizontal overflow.");
    }
}
