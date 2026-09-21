using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests covering basic block editing: text input, block creation/deletion,
/// heading levels, list types, todo items, toggles, code blocks, and equations.
/// </summary>
[TestClass]
public class NotionBlockEditingE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync(bool grantClipboard = false)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var http = new HttpClient(handler);
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* API may already be in a clean state when running against a different host. */ }

        var context = await CreateContextAsync();
        if (grantClipboard)
            await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(2000);
        return page;
    }

    /// <summary>
    /// Clicks at end of the first editable paragraph, presses Enter to create a new
    /// empty block, then types / to open the slash menu, types searchTerm to filter,
    /// and clicks the first matching item.
    /// </summary>
    private async Task<ILocator> InsertBlockViaSlashMenuAsync(IPage page, string searchTerm)
    {
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await para.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        // Wait for new block to render and for initKeyboardHandler to run
        await page.WaitForTimeoutAsync(1000);

        // Type "/" to trigger the slash menu — must be in the focused (new) contenteditable
        await page.Keyboard.TypeAsync("/");
        // Slash menu container class is .tm-notion-slash (NOT .tm-notion-slash-menu)
        await page.WaitForSelectorAsync(".tm-notion-slash",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // After the menu opens, its own search input gets focused — type there
        var searchInput = page.Locator(".tm-notion-slash__input");
        await searchInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await searchInput.FillAsync(searchTerm);
        await page.WaitForTimeoutAsync(400);

        // Click first matching item
        var firstItem = page.Locator(".tm-notion-slash__item").First;
        await firstItem.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await firstItem.ClickAsync();
        await page.WaitForTimeoutAsync(800);

        var focusedBlock = page.Locator(".tm-notion-block--focused").First;
        await focusedBlock.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        var blockId = await focusedBlock.GetAttributeAsync("data-block-id");
        Assert.IsFalse(string.IsNullOrWhiteSpace(blockId), "Inserted block should expose data-block-id.");
        return page.Locator($"[data-block-id='{blockId}']").First;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Text block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Typing into the first text block updates the DOM content")]
    public async Task TextBlock_Type_UpdatesContent()
    {
        var page = await OpenNotionEditorAsync();

        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" E2E-TYPETEST");
        await page.WaitForTimeoutAsync(500);

        var content = await para.InnerTextAsync();
        Assert.IsTrue(content.Contains("E2E-TYPETEST"),
            $"Typed text should appear in paragraph. Actual content: {content}");
        await TakeScreenshotAsync(page, "text_block_type");
    }

    [TestMethod]
    [Description("Pressing Enter in a text block creates a new block")]
    public async Task TextBlock_Enter_CreatesNewBlock()
    {
        var page = await OpenNotionEditorAsync();
        // Use paragraph count (more stable than total block count which includes lazy-loaded column children)
        var initialCount = await page.Locator(".tm-notion-paragraph").CountAsync();

        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        // Wait for new block to render and column children to re-load
        await page.WaitForTimeoutAsync(1500);

        var newCount = await page.Locator(".tm-notion-paragraph").CountAsync();
        Assert.IsTrue(newCount > initialCount,
            $"Paragraph count should increase after Enter. Before: {initialCount}, After: {newCount}");
        await TakeScreenshotAsync(page, "text_block_enter");
    }

    [TestMethod]
    [Description("Pressing Backspace on an empty block deletes it")]
    public async Task TextBlock_BackspaceOnEmpty_DeletesBlock()
    {
        var page = await OpenNotionEditorAsync();

        // Create a new empty block via Enter
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        // Wait long enough for column children to reload so the count is stable
        await page.WaitForTimeoutAsync(1500);

        var afterEnterCount = await page.Locator(".tm-notion-paragraph").CountAsync();

        // Backspace on the focused (new) empty block deletes it
        await page.Keyboard.PressAsync("Backspace");
        await page.WaitForTimeoutAsync(1000);

        var afterBackspaceCount = await page.Locator(".tm-notion-paragraph").CountAsync();
        Assert.IsTrue(afterBackspaceCount < afterEnterCount,
            $"Paragraph count should decrease after Backspace on empty. Before Backspace: {afterEnterCount}, After: {afterBackspaceCount}");
        await TakeScreenshotAsync(page, "text_block_backspace_empty");
    }

    [TestMethod]
    [Description("Shift+Enter inserts a soft line break without creating a new block")]
    public async Task TextBlock_ShiftEnter_SoftLineBreak()
    {
        var page = await OpenNotionEditorAsync();
        var initialCount = await page.Locator("[data-notion-block]").CountAsync();

        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Shift+Enter");
        await page.WaitForTimeoutAsync(400);

        var newCount = await page.Locator("[data-notion-block]").CountAsync();
        Assert.AreEqual(initialCount, newCount,
            "Shift+Enter should not create a new block");
        await TakeScreenshotAsync(page, "text_block_shift_enter");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Headings
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("H1 heading block is rendered in the pre-seeded editor")]
    public async Task Heading1_Type_RendersH1()
    {
        var page = await OpenNotionEditorAsync();

        var h1 = page.Locator(".tm-notion-heading--h1").First;
        await h1.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        Assert.IsTrue(await h1.IsVisibleAsync(), "H1 heading block should be visible");
        await TakeScreenshotAsync(page, "heading_h1");
    }

    [TestMethod]
    [Description("H2 heading block is rendered in the pre-seeded editor")]
    public async Task Heading2_Type_RendersH2()
    {
        var page = await OpenNotionEditorAsync();

        var h2 = page.Locator(".tm-notion-heading--h2").First;
        await h2.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        Assert.IsTrue(await h2.IsVisibleAsync(), "H2 heading block should be visible");
        await TakeScreenshotAsync(page, "heading_h2");
    }

    [TestMethod]
    [Description("Inserting an H3 block via slash menu renders an h3 element")]
    public async Task Heading3_Type_RendersH3()
    {
        var page = await OpenNotionEditorAsync();

        await InsertBlockViaSlashMenuAsync(page, "h3");

        var h3 = page.Locator(".tm-notion-heading--h3").Last;
        await h3.WaitForAsync(new LocatorWaitForOptions
            { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await h3.IsVisibleAsync(), "H3 heading block should be visible after slash menu insertion");
        await TakeScreenshotAsync(page, "heading_h3");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Lists
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Pressing Enter in a bullet list block creates another bullet item")]
    public async Task BulletList_Enter_ContinuesList()
    {
        var page = await OpenNotionEditorAsync();

        // Find a non-empty bullet (innerText > "") to avoid "exit list on empty Enter" behavior
        var bullets = page.Locator(".tm-notion-bullet__body[contenteditable='true']");
        await bullets.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        ILocator? targetBullet = null;
        var count = await bullets.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var b = bullets.Nth(i);
            var text = await b.InnerTextAsync();
            if (!string.IsNullOrWhiteSpace(text)) { targetBullet = b; break; }
        }
        Assert.IsNotNull(targetBullet, "No non-empty bullet block found in seed data");

        await targetBullet.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        // Wait for the new bullet block to render and receive focus
        await page.WaitForTimeoutAsync(1500);

        // After Enter in a non-empty bullet, the focused element should be a new bullet body
        // (Blazor calls focusAtStart on the newly created block)
        var focusedBulletBody = page.Locator(".tm-notion-bullet__body[contenteditable='true']:focus");
        await focusedBulletBody.WaitForAsync(new LocatorWaitForOptions
            { State = WaitForSelectorState.Attached, Timeout = 5000 });
        Assert.IsTrue(await focusedBulletBody.CountAsync() > 0,
            "After Enter in a bullet block, the new bullet body should have focus");
        await TakeScreenshotAsync(page, "bullet_enter");
    }

    [TestMethod]
    [Description("Three numbered list items are created and rendered sequentially")]
    public async Task NumberedList_Renders_SequentialNumbers()
    {
        var page = await OpenNotionEditorAsync();

        await InsertBlockViaSlashMenuAsync(page, "numbered");

        // The new block is focused — type text and press Enter twice for 3 items
        await page.Keyboard.TypeAsync("First item");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);
        await page.Keyboard.TypeAsync("Second item");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);
        await page.Keyboard.TypeAsync("Third item");
        await page.WaitForTimeoutAsync(400);

        var numberedBlocks = page.Locator(".tm-notion-numbered");
        var count = await numberedBlocks.CountAsync();
        Assert.IsTrue(count >= 3, $"At least 3 numbered list items should be present. Actual: {count}");

        var numbers = await page.Locator(".tm-notion-numbered__number").AllInnerTextsAsync();
        Assert.IsTrue(numbers.Any(n => n.Contains("1")), "Should show number 1");
        Assert.IsTrue(numbers.Any(n => n.Contains("2")), "Should show number 2");
        Assert.IsTrue(numbers.Any(n => n.Contains("3")), "Should show number 3");
        await TakeScreenshotAsync(page, "numbered_list");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Todo item
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the checkbox on a todo block marks it checked")]
    public async Task TodoItem_Click_TogglesCheckbox()
    {
        var page = await OpenNotionEditorAsync();

        var todoBlock = await InsertBlockViaSlashMenuAsync(page, "todo");

        // The checkbox input is inside an aria-hidden span; use Force to bypass actionability
        var checkInput = todoBlock.Locator(".tm-notion-todo__input").First;
        await checkInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await checkInput.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForTimeoutAsync(600);

        var cls = await todoBlock.Locator(".tm-notion-todo").First.GetAttributeAsync("class") ?? "";
        Assert.IsTrue(cls.Contains("tm-notion-todo--checked"),
            $"Todo item should be checked. Classes: {cls}");
        await TakeScreenshotAsync(page, "todo_checked");
    }

    [TestMethod]
    [Description("Clicking the checkbox twice on a todo block returns it to unchecked")]
    public async Task TodoItem_Click_Twice_UnchecksCheckbox()
    {
        var page = await OpenNotionEditorAsync();

        var todoBlock = await InsertBlockViaSlashMenuAsync(page, "todo");

        var checkInput = todoBlock.Locator(".tm-notion-todo__input").First;
        await checkInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        // Click once to check
        await checkInput.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForTimeoutAsync(400);

        // Click again to uncheck
        await checkInput.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForTimeoutAsync(600);

        var cls = await todoBlock.Locator(".tm-notion-todo").First.GetAttributeAsync("class") ?? "";
        Assert.IsFalse(cls.Contains("tm-notion-todo--checked"),
            $"Todo item should be unchecked after second click. Classes: {cls}");
        await TakeScreenshotAsync(page, "todo_unchecked");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Toggle block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking the arrow on a toggle block expands its children")]
    public async Task Toggle_Click_ExpandsCollapse()
    {
        var page = await OpenNotionEditorAsync();

        await InsertBlockViaSlashMenuAsync(page, "toggle");

        var toggle = page.Locator(".tm-notion-toggle").Last;
        await toggle.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        // Initially should be closed
        var clsBefore = await toggle.GetAttributeAsync("class") ?? "";
        Assert.IsFalse(clsBefore.Contains("tm-notion-toggle--open"),
            "Toggle should be closed initially");

        var arrow = toggle.Locator(".tm-notion-toggle__arrow").First;
        await arrow.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var clsAfter = await toggle.GetAttributeAsync("class") ?? "";
        Assert.IsTrue(clsAfter.Contains("tm-notion-toggle--open"),
            $"Toggle should be open after clicking arrow. Classes: {clsAfter}");
        await TakeScreenshotAsync(page, "toggle_open");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Quote & Callout
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Inserting a quote block renders the tm-notion-quote class")]
    public async Task QuoteBlock_Type_HasQuoteStyle()
    {
        var page = await OpenNotionEditorAsync();

        await InsertBlockViaSlashMenuAsync(page, "quote");

        var quote = page.Locator(".tm-notion-quote").Last;
        await quote.WaitForAsync(new LocatorWaitForOptions
            { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await quote.IsVisibleAsync(), "Quote block should be visible");
        await TakeScreenshotAsync(page, "quote_block");
    }

    [TestMethod]
    [Description("Inserting a callout block renders the emoji icon")]
    public async Task CalloutBlock_EmojiVisible()
    {
        var page = await OpenNotionEditorAsync();

        await InsertBlockViaSlashMenuAsync(page, "callout");

        var icon = page.Locator(".tm-notion-callout__icon").Last;
        await icon.WaitForAsync(new LocatorWaitForOptions
            { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await icon.IsVisibleAsync(), "Callout emoji icon should be visible");
        await TakeScreenshotAsync(page, "callout_emoji");
    }

    [TestMethod]
    [Description("Typing into a callout block updates its text content")]
    public async Task CalloutBlock_Type_UpdatesContent()
    {
        var page = await OpenNotionEditorAsync();

        await InsertBlockViaSlashMenuAsync(page, "callout");

        var calloutBody = page.Locator(".tm-notion-callout__body[contenteditable='true']").Last;
        await calloutBody.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await calloutBody.ClickAsync();
        await page.Keyboard.TypeAsync("Callout E2E test content");
        await page.WaitForTimeoutAsync(500);

        var content = await calloutBody.InnerTextAsync();
        Assert.IsTrue(content.Contains("Callout E2E test content"),
            $"Callout text should contain typed content. Actual: {content}");
        await TakeScreenshotAsync(page, "callout_type");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Code block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Typing into a code block shows code in the textarea")]
    public async Task CodeBlock_Type_ShowsCode()
    {
        var page = await OpenNotionEditorAsync();

        await InsertBlockViaSlashMenuAsync(page, "code");

        var codeArea = page.Locator(".tm-notion-code-block__content").Last;
        await codeArea.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await codeArea.ClickAsync();
        await codeArea.FillAsync("console.log('hello e2e');");
        await page.WaitForTimeoutAsync(500);

        var value = await codeArea.InputValueAsync();
        Assert.IsTrue(value.Contains("hello e2e"),
            $"Code block textarea should contain typed code. Actual: {value}");
        await TakeScreenshotAsync(page, "code_block_type");
    }

    [TestMethod]
    [Description("Clicking the copy button on a code block shows copied state")]
    public async Task CodeBlock_CopyButton_CopiesCode()
    {
        // Grant clipboard-write permission so navigator.clipboard.writeText succeeds
        var page = await OpenNotionEditorAsync(grantClipboard: true);

        await InsertBlockViaSlashMenuAsync(page, "code");

        var codeArea = page.Locator(".tm-notion-code-block__content").Last;
        await codeArea.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await codeArea.ClickAsync();
        await codeArea.FillAsync("const x = 42;");
        await page.WaitForTimeoutAsync(300);

        // Click copy button — poll for the copied class (clipboard write → state → render
        // can exceed a fixed delay under load)
        var copyBtn = page.Locator(".tm-notion-code-block").Last
            .Locator(".tm-notion-code-block__copy");
        await copyBtn.ClickAsync();
        await Assertions.Expect(copyBtn).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("tm-notion-code-block__copy--copied"),
            new LocatorAssertionsToHaveClassOptions { Timeout = 5000 });

        await TakeScreenshotAsync(page, "code_block_copy");
    }

    [TestMethod]
    [Description("Selecting Python in the language selector updates the displayed language")]
    public async Task CodeBlock_LanguageSelector_ChangesLanguage()
    {
        var page = await OpenNotionEditorAsync();

        await InsertBlockViaSlashMenuAsync(page, "code");

        var langSelect = page.Locator(".tm-notion-code-block__lang-select").Last;
        await langSelect.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await langSelect.SelectOptionAsync(new SelectOptionValue { Label = "Python" });
        await page.WaitForTimeoutAsync(500);

        var selectedValue = await langSelect.InputValueAsync();
        Assert.AreEqual("Python", selectedValue, "Language should be changed to Python");
        await TakeScreenshotAsync(page, "code_block_language");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Divider
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("The divider block renders a visible hr element")]
    public async Task DividerBlock_Renders()
    {
        var page = await OpenNotionEditorAsync();

        var divider = page.Locator(".tm-notion-divider").First;
        await divider.WaitForAsync(new LocatorWaitForOptions
            { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await divider.IsVisibleAsync(), "Divider HR element should be visible");
        await TakeScreenshotAsync(page, "divider_renders");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Equation block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Clicking an equation block opens the LaTeX editor")]
    public async Task EquationBlock_Click_OpensEditor()
    {
        var page = await OpenNotionEditorAsync();

        await InsertBlockViaSlashMenuAsync(page, "equation");

        // The equation block in view mode — click to open editor
        var equationBlock = page.Locator(".tm-notion-equation-block").Last;
        await equationBlock.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await equationBlock.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var editing = page.Locator(".tm-notion-equation-block--editing").Last;
        await editing.WaitForAsync(new LocatorWaitForOptions
            { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await editing.IsVisibleAsync(), "Equation editor should open on click");
        await TakeScreenshotAsync(page, "equation_editor_open");
    }

    [TestMethod]
    [Description("Typing LaTeX in the equation editor renders a preview")]
    public async Task EquationBlock_TypeLatex_RendersPreview()
    {
        var page = await OpenNotionEditorAsync();

        await InsertBlockViaSlashMenuAsync(page, "equation");

        var equationBlock = page.Locator(".tm-notion-equation-block").Last;
        await equationBlock.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await equationBlock.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Type LaTeX in the input textarea
        var input = page.Locator(".tm-notion-equation-block__input").Last;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await input.FillAsync("E = mc^2");
        await page.WaitForTimeoutAsync(600);

        // Preview should not be empty
        var preview = page.Locator(".tm-notion-equation-block__preview").Last;
        var previewCls = await preview.GetAttributeAsync("class") ?? "";
        Assert.IsFalse(previewCls.Contains("tm-notion-equation-block__preview--empty"),
            "Preview should not be empty after typing LaTeX");
        await TakeScreenshotAsync(page, "equation_preview");
    }

    [TestMethod]
    [Description("Clicking Done in the equation editor closes the editor and shows the rendered equation")]
    public async Task EquationBlock_Enter_Saves()
    {
        var page = await OpenNotionEditorAsync();

        await InsertBlockViaSlashMenuAsync(page, "equation");

        var equationBlock = page.Locator(".tm-notion-equation-block").Last;
        await equationBlock.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await equationBlock.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var input = page.Locator(".tm-notion-equation-block__input").Last;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await input.FillAsync("\\sqrt{x}");
        await page.WaitForTimeoutAsync(300);

        // Click Done button to save
        var doneBtn = page.Locator(".tm-notion-equation-block__done-btn").Last;
        await doneBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        // Editor should be closed
        var editingElements = page.Locator(".tm-notion-equation-block--editing");
        var editingCount = await editingElements.CountAsync();
        Assert.AreEqual(0, editingCount, "Equation editor should close after clicking Done");
        await TakeScreenshotAsync(page, "equation_saved");
    }
}
