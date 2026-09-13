using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests covering the inline text formatting toolbar in the Notion editor:
/// bold, italic, underline, strikethrough, inline code, links, colors, and Turn Into.
/// </summary>
[TestClass]
public class NotionFormattingE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        // Reset API mock data so each test starts with a clean slate
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

    private async Task SelectTextInBlockAsync(IPage page, ILocator block)
    {
        await block.ClickAsync();
        await block.EvaluateAsync("el => { el.focus(); document.execCommand('selectAll', false, null); }");
        await page.WaitForTimeoutAsync(300);
    }

    private async Task WaitForInlineToolbarAsync(IPage page)
    {
        var toolbar = page.Locator(".tm-notion-inline-toolbar").First;
        try
        {
            await toolbar.WaitForAsync(new LocatorWaitForOptions
                { State = WaitForSelectorState.Visible, Timeout = 5000 });
        }
        catch (TimeoutException ex)
        {
            var diagnostics = await GetInlineToolbarDiagnosticsAsync(page);
            throw new TimeoutException($"Inline toolbar did not become visible. Diagnostics: {diagnostics}", ex);
        }
    }

    private async Task OpenInlineToolbarAsync(IPage page)
    {
        var block = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await block.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await SelectTextInBlockAsync(page, block);
        await WaitForInlineToolbarAsync(page);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Visibility
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Selecting text in a block shows the inline formatting toolbar")]
    public async Task InlineToolbar_SelectText_Shows()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var toolbar = page.Locator(".tm-notion-inline-toolbar").First;
        Assert.IsTrue(await toolbar.IsVisibleAsync(), "Inline toolbar should be visible after text selection");
        await TakeScreenshotAsync(page, "inline_toolbar_show");
    }

    [TestMethod]
    [Description("Clicking outside the inline toolbar hides it")]
    public async Task InlineToolbar_ClickOutside_Hides()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        // Click on another block (e.g. the page title or second paragraph)
        var otherBlock = page.Locator(".tm-notion-paragraph[contenteditable='true']").Nth(1);
        if (await otherBlock.CountAsync() == 0)
            otherBlock = page.Locator(".tm-notion-page").First;
        await otherBlock.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var toolbar = page.Locator(".tm-notion-inline-toolbar").First;
        Assert.IsFalse(await toolbar.IsVisibleAsync(), "Inline toolbar should hide after clicking outside");
        await TakeScreenshotAsync(page, "inline_toolbar_hide");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Basic formatting (click)
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the Bold button wraps selected text in a bold tag")]
    public async Task InlineToolbar_Bold_AppliesBold()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var boldBtn = page.Locator("button[title='Bold']").First;
        await boldBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var block = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        var html = await block.InnerHTMLAsync();
        Assert.IsTrue(
            html.Contains("<b>") || html.Contains("<strong>"),
            $"Selected text should be wrapped in bold tag. HTML: {html}");
        await TakeScreenshotAsync(page, "inline_toolbar_bold");
    }

    [TestMethod]
    [Description("Clicking the Italic button wraps selected text in an italic tag")]
    public async Task InlineToolbar_Italic_AppliesItalic()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var italicBtn = page.Locator("button[title='Italic']").First;
        await italicBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var block = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        var html = await block.InnerHTMLAsync();
        Assert.IsTrue(
            html.Contains("<i>") || html.Contains("<em>"),
            $"Selected text should be wrapped in italic tag. HTML: {html}");
        await TakeScreenshotAsync(page, "inline_toolbar_italic");
    }

    [TestMethod]
    [Description("Clicking the Underline button applies underline style to selected text")]
    public async Task InlineToolbar_Underline_AppliesUnderline()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var underlineBtn = page.Locator("button[title='Underline']").First;
        await underlineBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var block = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        var html = await block.InnerHTMLAsync();
        Assert.IsTrue(
            html.Contains("<u>") || html.Contains("text-decoration: underline"),
            $"Selected text should have underline style. HTML: {html}");
        await TakeScreenshotAsync(page, "inline_toolbar_underline");
    }

    [TestMethod]
    [Description("Clicking the Strikethrough button applies strikethrough style to selected text")]
    public async Task InlineToolbar_Strikethrough_AppliesStrike()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var strikeBtn = page.Locator("button[title='Strikethrough']").First;
        await strikeBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var block = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        var html = await block.InnerHTMLAsync();
        Assert.IsTrue(
            html.Contains("<s>") || html.Contains("<strike>") || html.Contains("<del>") || html.Contains("line-through"),
            $"Selected text should have strikethrough style. HTML: {html}");
        await TakeScreenshotAsync(page, "inline_toolbar_strikethrough");
    }

    [TestMethod]
    [Description("Clicking the Code button wraps selected text in a code element")]
    public async Task InlineToolbar_InlineCode_AppliesCode()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var codeBtn = page.Locator("button[title='Inline code']").First;
        await codeBtn.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var block = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        var html = await block.InnerHTMLAsync();
        Assert.IsTrue(
            html.Contains("<code>"),
            $"Selected text should be wrapped in <code> tag. HTML: {html}");
        await TakeScreenshotAsync(page, "inline_toolbar_code");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Keyboard shortcuts
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Pressing Ctrl+B applies bold formatting to selected text")]
    public async Task InlineToolbar_Bold_Keyboard_Ctrl_B()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        await page.Keyboard.PressAsync("Control+b");
        await page.WaitForTimeoutAsync(400);

        var block = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        var html = await block.InnerHTMLAsync();
        Assert.IsTrue(
            html.Contains("<b>") || html.Contains("<strong>"),
            $"Selected text should be bold after Ctrl+B. HTML: {html}");
        await TakeScreenshotAsync(page, "inline_toolbar_bold_kb");
    }

    [TestMethod]
    [Description("Pressing Ctrl+I applies italic formatting to selected text")]
    public async Task InlineToolbar_Italic_Keyboard_Ctrl_I()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        await page.Keyboard.PressAsync("Control+i");
        await page.WaitForTimeoutAsync(400);

        var block = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        var html = await block.InnerHTMLAsync();
        Assert.IsTrue(
            html.Contains("<i>") || html.Contains("<em>"),
            $"Selected text should be italic after Ctrl+I. HTML: {html}");
        await TakeScreenshotAsync(page, "inline_toolbar_italic_kb");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Link
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the Link button opens the URL input panel")]
    public async Task InlineToolbar_Link_OpensInput()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var linkBtn = page.Locator("button[title='Link']").First;
        await linkBtn.ClickAsync();

        var input = page.Locator(".tm-notion-inline-toolbar__link-input").First;
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await input.IsVisibleAsync(), "Link URL input should appear");
        await TakeScreenshotAsync(page, "inline_toolbar_link_open");
    }

    [TestMethod]
    [Description("Entering a URL and pressing Enter wraps selected text in an anchor tag")]
    public async Task InlineToolbar_Link_InsertUrl_WrapsWithAnchor()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var linkBtn = page.Locator("button[title='Link']").First;
        await linkBtn.ClickAsync();

        var input = page.Locator(".tm-notion-inline-toolbar__link-input").First;
        await input.FillAsync("https://example.com");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(500);

        var block = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        var html = await block.InnerHTMLAsync();
        Assert.IsTrue(
            html.Contains("<a ") && html.Contains("href="),
            $"Selected text should be wrapped in anchor tag. HTML: {html}");
        await TakeScreenshotAsync(page, "inline_toolbar_link_insert");
    }

    [TestMethod]
    [Description("Clicking Remove link unwraps the anchor tag")]
    public async Task InlineToolbar_Link_Remove_UnwrapsAnchor()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        // Insert link first
        var linkBtn = page.Locator("button[title='Link']").First;
        await linkBtn.ClickAsync();
        var input = page.Locator(".tm-notion-inline-toolbar__link-input").First;
        await input.FillAsync("https://example.com");
        await page.Keyboard.PressAsync("Enter");

        // Re-select the linked text and open toolbar again
        var block = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await SelectTextInBlockAsync(page, block);
        await WaitForInlineToolbarAsync(page);

        // Open link panel again
        var linkBtn2 = page.Locator("button[title='Link']").First;
        await linkBtn2.ClickAsync();

        var input2 = page.Locator(".tm-notion-inline-toolbar__link-input").First;
        await input2.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Click Remove link
        var removeBtn = page.Locator("button[title='Remove link']").First;
        await removeBtn.ClickAsync();

        // Wait for unlink to finish and the anchor to disappear
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('a[href=\"https://example.com\"]').length === 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });

        var html = await block.InnerHTMLAsync();
        Assert.IsFalse(
            html.Contains("<a ") && html.Contains("href="),
            $"Anchor tag should be removed after clicking Remove link. HTML: {html}");
        await TakeScreenshotAsync(page, "inline_toolbar_link_remove");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Color
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the Color button opens the color picker panel")]
    public async Task InlineToolbar_TextColor_OpensPanel()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var colorBtn = page.Locator("button[title='Color']").First;
        await colorBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var panel = page.Locator(".tm-notion-inline-toolbar__color-panel").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await panel.IsVisibleAsync(), "Color picker panel should appear");
        await TakeScreenshotAsync(page, "inline_toolbar_color_panel");
    }

    [TestMethod]
    [Description("Selecting the Red text color applies a red color style to the text")]
    public async Task InlineToolbar_TextColor_Select_AppliesColor()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var colorBtn = page.Locator("button[title='Color']").First;
        await colorBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Click the Red swatch in the Text color row (first row)
        var redSwatch = page.Locator(".tm-notion-inline-toolbar__color-panel .tm-notion-inline-toolbar__color-row").First
            .Locator("button[title='Red']").First;
        await redSwatch.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var block = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        var html = await block.InnerHTMLAsync();
        Assert.IsTrue(
            html.Contains("color=") || html.Contains("color:") || html.Contains("style="),
            $"Text should have a color style applied. HTML: {html}");
        await TakeScreenshotAsync(page, "inline_toolbar_text_color");
    }

    [TestMethod]
    [Description("Selecting a background color applies a background-color style to the text")]
    public async Task InlineToolbar_BgColor_Select_AppliesBackground()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var colorBtn = page.Locator("button[title='Color']").First;
        await colorBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Click the Yellow swatch in the Background color row (second row) via JS to bypass viewport issues
        await page.EvaluateAsync("""
            const panel = document.querySelector('.tm-notion-inline-toolbar__color-panel');
            const row = panel?.querySelectorAll('.tm-notion-inline-toolbar__color-row')[1];
            const swatch = row?.querySelector('button[title="Yellow"]');
            swatch?.click();
            """);
        await page.WaitForTimeoutAsync(400);

        var block = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        var html = await block.InnerHTMLAsync();
        Assert.IsTrue(
            html.Contains("background-color:") || html.Contains("background:"),
            $"Text should have a background-color style applied. HTML: {html}");
        await TakeScreenshotAsync(page, "inline_toolbar_bg_color");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Turn Into
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the Turn Into button opens the block type panel")]
    public async Task InlineToolbar_TurnInto_OpensPanel()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var turnIntoBtn = page.Locator("button[title='Turn into']").First;
        await turnIntoBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var panel = page.Locator(".tm-notion-inline-toolbar__turninto-panel").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await panel.IsVisibleAsync(), "Turn Into panel should appear");
        await TakeScreenshotAsync(page, "inline_toolbar_turninto_panel");
    }

    [TestMethod]
    [Description("Selecting Heading 1 from Turn Into converts the block to H1")]
    public async Task InlineToolbar_TurnInto_Heading1_Converts()
    {
        var page = await OpenNotionEditorAsync();
        await OpenInlineToolbarAsync(page);

        var turnIntoBtn = page.Locator("button[title='Turn into']").First;
        await turnIntoBtn.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var h1Item = page.Locator(".tm-notion-inline-toolbar__turninto-item").Filter(new() { HasText = "Heading 1" }).First;
        await h1Item.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await h1Item.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var h1 = page.Locator(".tm-notion-heading--h1").First;
        await h1.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await h1.IsVisibleAsync(), "Block should be converted to H1 after Turn Into");
        await TakeScreenshotAsync(page, "inline_toolbar_turninto_h1");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB4: captures all main inline toolbar buttons with active bold, italic, underline, strike, and code states")]
    public async Task EB4_InlineToolbar_MainButtonsAndActiveStates_CaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedInlineToolbarPageAsync(page);

        await OpenInlineToolbarForSelectorAsync(page, "[data-block-id='eb400000-0000-0000-0000-000000000002'] code");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-notion-inline-toolbar__btn--active').length >= 5",
            new PageWaitForFunctionOptions { Timeout = 5000 });

        foreach (var title in new[] { "Bold", "Italic", "Underline", "Strikethrough", "Inline code", "Link", "Color", "Turn into", "Align", "Comment", "Inline equation" })
        {
            Assert.AreEqual(1, await page.Locator($".tm-notion-inline-toolbar button[title='{title}']").CountAsync(), $"{title} should be present in the main inline toolbar.");
        }

        foreach (var title in new[] { "Bold", "Italic", "Underline", "Strikethrough", "Inline code" })
        {
            Assert.AreEqual(1, await page.Locator($".tm-notion-inline-toolbar__btn--active[title='{title}']").CountAsync(), $"{title} should render as active for the combined formatted selection.");
        }

        var linkButton = page.Locator(".tm-notion-inline-toolbar button[title='Link']").First;
        await linkButton.HoverAsync();
        await linkButton.FocusAsync();
        await AssertWithinViewportAsync(page.Locator(".tm-notion-inline-toolbar").First, "EB4 main inline toolbar");
        await CaptureBaselineAsync(page, "inline-toolbar", "main-buttons-active-states", page.Locator(".tm-notion-inline-toolbar").First);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB4: captures the inline toolbar above a selection near the viewport bottom")]
    public async Task EB4_InlineToolbar_BottomEdge_CaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedInlineToolbarPageAsync(page);
        await page.SetViewportSizeAsync(520, 360);

        var block = page.Locator("[data-block-id='eb400000-0000-0000-0000-000000000020']").First;
        await block.EvaluateAsync("el => el.scrollIntoView({ block: 'end', inline: 'nearest' })");
        await page.WaitForTimeoutAsync(200);
        await OpenInlineToolbarForSelectorAsync(page, "[data-block-id='eb400000-0000-0000-0000-000000000020'] .tm-notion-paragraph", "end");

        var metrics = await page.EvaluateAsync<InlineToolbarBottomMetrics>("""
            () => {
                const toolbar = document.querySelector('.tm-notion-inline-toolbar').getBoundingClientRect();
                const range = window.getSelection().getRangeAt(0).getBoundingClientRect();
                return {
                    ToolbarTop: toolbar.top,
                    ToolbarBottom: toolbar.bottom,
                    ToolbarLeft: toolbar.left,
                    ToolbarRight: toolbar.right,
                    SelectionTop: range.top,
                    ViewportWidth: window.innerWidth,
                    ViewportHeight: window.innerHeight
                };
            }
            """);
        Assert.IsTrue(metrics.ToolbarTop >= 0, $"Toolbar should stay inside the top viewport edge. Top={metrics.ToolbarTop}.");
        Assert.IsTrue(metrics.ToolbarBottom <= metrics.SelectionTop + 1, $"Toolbar should be above the bottom-edge selection. ToolbarBottom={metrics.ToolbarBottom}, SelectionTop={metrics.SelectionTop}.");
        Assert.IsTrue(metrics.ToolbarLeft >= 0 && metrics.ToolbarRight <= metrics.ViewportWidth, "Toolbar should be horizontally clamped in the viewport.");

        await CaptureBaselineAsync(page, "inline-toolbar", "bottom-edge-above-selection", page.Locator(".tm-notion-inline-toolbar").First);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB4: captures the color panel clamped near the viewport edge with hover and focus-visible states")]
    public async Task EB4_InlineToolbar_ColorPanelViewportEdge_CaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedInlineToolbarPageAsync(page);
        await page.SetViewportSizeAsync(460, 640);

        await OpenInlineToolbarForSelectorAsync(page, "[data-block-id='eb400000-0000-0000-0000-000000000003'] .tm-notion-paragraph");
        var colorButton = page.Locator(".tm-notion-inline-toolbar button[title='Color']").First;
        await colorButton.EvaluateAsync("el => el.click()");
        var panel = page.Locator(".tm-notion-inline-toolbar__color-panel").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var blueSwatch = panel.Locator("button[title='Blue']").First;
        await blueSwatch.HoverAsync();
        await blueSwatch.FocusAsync();
        await AssertWithinViewportAsync(page.Locator(".tm-notion-inline-toolbar").First, "EB4 color panel");
        await CaptureBaselineAsync(page, "inline-toolbar", "color-panel-viewport-edge", page.Locator(".tm-notion-inline-toolbar").First);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB4: captures the Turn Into panel clamped near the viewport edge")]
    public async Task EB4_InlineToolbar_TurnIntoPanelViewportEdge_CaptureBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedInlineToolbarPageAsync(page);
        await page.SetViewportSizeAsync(460, 640);

        await OpenInlineToolbarForSelectorAsync(page, "[data-block-id='eb400000-0000-0000-0000-000000000003'] .tm-notion-paragraph");
        var turnIntoButton = page.Locator(".tm-notion-inline-toolbar button[title='Turn into']").First;
        await turnIntoButton.EvaluateAsync("el => el.click()");
        var panel = page.Locator(".tm-notion-inline-toolbar__turninto-panel").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var todoItem = panel.Locator(".tm-notion-inline-toolbar__turninto-item").Filter(new LocatorFilterOptions { HasText = "To-do list" }).First;
        await todoItem.HoverAsync();
        await todoItem.FocusAsync();
        await AssertWithinViewportAsync(page.Locator(".tm-notion-inline-toolbar").First, "EB4 Turn Into panel");
        await CaptureBaselineAsync(page, "inline-toolbar", "turn-into-panel-viewport-edge", page.Locator(".tm-notion-inline-toolbar").First);
    }

    private async Task SeedInlineToolbarPageAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "methodName => window.tmNotionDemo && typeof window.tmNotionDemo[methodName] === 'function'",
            "seedInlineToolbarPage",
            new PageWaitForFunctionOptions { Timeout = 60000 });
        await page.EvaluateAsync("async methodName => await window.tmNotionDemo[methodName]()", "seedInlineToolbarPage");
        await page.ReloadAsync(new PageReloadOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-block-id='eb400000-0000-0000-0000-000000000002'] code", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    private async Task OpenInlineToolbarForSelectorAsync(IPage page, string selector, string scrollBlock = "center")
    {
        var target = page.Locator(selector).First;
        await target.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForFunctionAsync(
            "() => window.tmNotionEditor?.hasSelectionWatcher?.(document.querySelector('.tm-notion-page')) === true",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await target.EvaluateAsync("(el, block) => el.scrollIntoView({ block, inline: 'nearest' })", scrollBlock);
        await page.WaitForTimeoutAsync(150);
        await target.EvaluateAsync("""
            el => {
                const host = el.closest('[contenteditable="true"]');
                host?.focus?.({ preventScroll: true });
                const range = document.createRange();
                range.selectNodeContents(el);
                const selection = window.getSelection();
                selection.removeAllRanges();
                selection.addRange(range);
                document.dispatchEvent(new Event('selectionchange'));
                document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
            }
            """);
        var forced = await page.EvaluateAsync<bool>(
            "() => window.tmNotionEditor.forceInlineToolbarForSelection(document.querySelector('.tm-notion-page'))");
        if (!forced)
        {
            var diagnostics = await GetInlineToolbarDiagnosticsAsync(page);
            Assert.Fail($"Inline toolbar force helper returned false. Diagnostics: {diagnostics}");
        }
        await WaitForInlineToolbarAsync(page);
        await page.WaitForTimeoutAsync(250);
    }

    private static async Task<string> GetInlineToolbarDiagnosticsAsync(IPage page)
    {
        return await page.EvaluateAsync<string>("""
            () => {
                const pageEl = document.querySelector('.tm-notion-page');
                const sel = window.getSelection();
                const range = sel && sel.rangeCount > 0 ? sel.getRangeAt(0) : null;
                const ancestor = range?.commonAncestorContainer;
                const ancestorEl = ancestor?.nodeType === Node.TEXT_NODE ? ancestor.parentElement : ancestor;
                const toolbar = document.querySelector('.tm-notion-inline-toolbar');
                const target = document.querySelector("[data-block-id='eb400000-0000-0000-0000-000000000002'] code")
                    ?? document.querySelector("[data-block-id='eb400000-0000-0000-0000-000000000003'] .tm-notion-paragraph")
                    ?? document.querySelector("[data-block-id='eb400000-0000-0000-0000-000000000020'] .tm-notion-paragraph");

                return JSON.stringify({
                    hasEditor: !!document.querySelector('.tm-notion-editor'),
                    hasPage: !!pageEl,
                    hasWatcher: !!window.tmNotionEditor?.hasSelectionWatcher?.(pageEl),
                    forceType: typeof window.tmNotionEditor?.forceInlineToolbarForSelection,
                    selectionText: sel?.toString() ?? '',
                    selectionCollapsed: sel?.isCollapsed ?? null,
                    selectionRangeCount: sel?.rangeCount ?? 0,
                    ancestorTag: ancestorEl?.tagName ?? null,
                    ancestorEditable: ancestorEl?.closest?.('[contenteditable="true"]')?.className ?? null,
                    pageContainsSelection: !!(pageEl && ancestor && pageEl.contains(ancestor)),
                    rectCount: range ? Array.from(range.getClientRects()).filter(r => r.width > 0 && r.height > 0).length : 0,
                    targetText: target?.textContent ?? null,
                    targetHtml: target?.innerHTML ?? null,
                    toolbarCount: document.querySelectorAll('.tm-notion-inline-toolbar').length,
                    toolbarDisplay: toolbar ? getComputedStyle(toolbar).display : null,
                    toolbarVisibility: toolbar ? getComputedStyle(toolbar).visibility : null,
                    toolbarClass: toolbar?.className ?? null,
                    lastToolbarError: window.__tmNotionLastToolbarError ?? null
                });
            }
            """);
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
                    ViewportHeight: window.innerHeight,
                    ZIndex: Number.parseInt(getComputedStyle(el).zIndex || '0', 10)
                };
            }
            """);
        Assert.IsTrue(metrics.Left >= 0, $"{label} should not overflow the left viewport edge. Left={metrics.Left}.");
        Assert.IsTrue(metrics.Top >= 0, $"{label} should not overflow the top viewport edge. Top={metrics.Top}.");
        Assert.IsTrue(metrics.Right <= metrics.ViewportWidth, $"{label} should not overflow the right viewport edge. Right={metrics.Right}, Viewport={metrics.ViewportWidth}.");
        Assert.IsTrue(metrics.Bottom <= metrics.ViewportHeight, $"{label} should not overflow the bottom viewport edge. Bottom={metrics.Bottom}, Viewport={metrics.ViewportHeight}.");
        Assert.IsTrue(metrics.ZIndex >= 1000, $"{label} should render above editor content. z-index={metrics.ZIndex}.");
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

    private sealed class InlineToolbarBottomMetrics
    {
        public double ToolbarTop { get; set; }

        public double ToolbarBottom { get; set; }

        public double ToolbarLeft { get; set; }

        public double ToolbarRight { get; set; }

        public double SelectionTop { get; set; }

        public double ViewportWidth { get; set; }

        public double ViewportHeight { get; set; }
    }

    private sealed class ViewportBoxMetrics
    {
        public double Left { get; set; }

        public double Top { get; set; }

        public double Right { get; set; }

        public double Bottom { get; set; }

        public double ViewportWidth { get; set; }

        public double ViewportHeight { get; set; }

        public double ZIndex { get; set; }
    }
}
