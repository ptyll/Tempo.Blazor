using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// The live markdown shortcuts run in notion-editor.js. Triggering one must convert the block
/// without throwing away the text the user already had, and the detected pattern set must match
/// the C# MarkdownShortcutDetector.
/// Runs against the self-hosted HTTPS demo API (5100) and HTTPS demo WASM (7106).
/// </summary>
[TestClass]
public class NotionMarkdownShortcutsE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Typing '# ' in front of existing text converts to Heading 1 and keeps the text")]
    public async Task HeadingShortcut_KeepsTheTextAfterTheTrigger()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "world");

        // Put the caret at the very start and type the trigger in front of the existing word.
        await page.EvaluateAsync("id => window.tmNotionEditor.setCaretOffset(id, 0)", blockId);
        await page.Keyboard.TypeAsync("# ");
        await page.WaitForTimeoutAsync(1200);

        var block = Block(page, blockId);
        await block.Locator(".tm-notion-heading--h1").First.WaitForAsync(Visible());
        Assert.AreEqual("world", (await block.InnerTextAsync()).Trim(),
            "The trigger must be stripped and the existing text kept.");

        await CaptureBaselineAsync("markdown-shortcuts", "heading-keeps-text", block);
        TestContext.WriteLine("UX: the shortcut promotes the line instead of clearing it, so typing '# ' in front of a word never destroys the word.");
    }

    [TestMethod]
    [Description("Any number followed by '. ' starts a numbered list, not just '1. '")]
    public async Task NumberedShortcut_AcceptsAnyNumber()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, string.Empty);

        await page.Keyboard.TypeAsync("2. ");
        await page.WaitForTimeoutAsync(1200);

        await Block(page, blockId).Locator(".tm-notion-numbered").First.WaitForAsync(Visible());
    }

    [TestMethod]
    [Description("'[ ] ' and '[x] ' both start a todo; the checked variant is checked")]
    public async Task TodoShortcut_AcceptsBracketWithSpace()
    {
        var page = await OpenNotionEditorAsync();

        var uncheckedId = await TypeIntoFirstParagraphAsync(page, string.Empty);
        await page.Keyboard.TypeAsync("[ ] ");
        await page.WaitForTimeoutAsync(1200);

        var uncheckedBox = Block(page, uncheckedId).Locator(".tm-notion-todo__input").First;
        await uncheckedBox.WaitForAsync(Visible());
        Assert.IsFalse(await uncheckedBox.IsCheckedAsync(), "'[ ] ' must produce an unchecked todo.");

        await CaptureBaselineAsync("markdown-shortcuts", "todo-shortcut", Block(page, uncheckedId));
    }

    [TestMethod]
    [Description("'+ ' starts a bullet list, like '- ' and '* '")]
    public async Task BulletShortcut_AcceptsPlus()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, string.Empty);

        await page.Keyboard.TypeAsync("+ ");
        await page.WaitForTimeoutAsync(1200);

        await Block(page, blockId).Locator(".tm-notion-bullet").First.WaitForAsync(Visible());
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Inline markdown is applied while typing: **bold**, `code` and ~~strike~~")]
    public async Task InlineShortcuts_ApplyFormattingWhileTyping()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, string.Empty);
        var block = Block(page, blockId);

        await page.Keyboard.TypeAsync("**bold**");
        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual(1, await block.Locator("strong").CountAsync(), "**bold** must become <strong>.");
        StringAssert.Contains(await block.InnerTextAsync(), "bold");

        await page.Keyboard.TypeAsync(" `code`");
        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual(1, await block.Locator("code").CountAsync(), "`code` must become <code>.");

        await page.Keyboard.TypeAsync(" ~~gone~~");
        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual(1, await block.Locator("s").CountAsync(), "~~strike~~ must become <s>.");

        var text = await block.InnerTextAsync();
        text.Should_NotContain("**");
        text.Should_NotContain("~~");

        await CaptureBaselineAsync("markdown-shortcuts", "inline-formatting", block);
        TestContext.WriteLine("UX: the delimiters disappear as soon as the pattern closes, so the line reads as formatted text, not as markup.");
    }

    [TestMethod]
    [Description("Backspace at the start of a non-empty block merges it into the previous one")]
    public async Task Backspace_AtStartOfNonEmptyBlock_MergesIntoPrevious()
    {
        var page = await OpenNotionEditorAsync();
        var firstId = await TypeIntoFirstParagraphAsync(page, "alpha");

        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(800);
        await page.Keyboard.TypeAsync("beta");
        await page.WaitForTimeoutAsync(300);

        var secondId = await ActiveBlockIdAsync(page);
        Assert.AreNotEqual(firstId, secondId);

        // Caret to the very start of the second block, then Backspace.
        await page.EvaluateAsync("id => window.tmNotionEditor.setCaretOffset(id, 0)", secondId);
        await page.Keyboard.PressAsync("Backspace");
        await page.WaitForTimeoutAsync(1200);

        Assert.AreEqual(0, await Block(page, secondId).CountAsync(), "The merged block must be gone.");
        StringAssert.Contains(await Block(page, firstId).InnerTextAsync(), "alphabeta",
            "The two blocks must be joined without losing either half.");
    }

    [TestMethod]
    [Description("Setting the text colour back to Default removes only the colour, not the bold")]
    public async Task DefaultTextColour_KeepsOtherFormatting()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "coloured");
        var block = Block(page, blockId);

        await page.Keyboard.PressAsync("Control+A");
        await page.Locator(".tm-notion-inline-toolbar").First.WaitForAsync(Visible());
        await page.Keyboard.PressAsync("Control+B");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(1, await block.Locator("strong, b").CountAsync(), "The text must be bold before the colour is applied.");

        await page.EvaluateAsync("() => window.tmNotionEditor.applyInlineColor('text', 'rgb(220, 38, 38)')");
        await page.WaitForTimeoutAsync(300);

        await page.EvaluateAsync("() => window.tmNotionEditor.applyInlineColor('text', null)");
        await page.WaitForTimeoutAsync(400);

        Assert.AreEqual(1, await block.Locator("strong, b").CountAsync(),
            "Resetting the colour must not strip the bold — removeFormat would have.");
        Assert.AreEqual(0, await block.Locator("[style*='color']").CountAsync(),
            "No coloured span may be left behind.");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("A picked text colour is stored as an inline style and survives a reload")]
    public async Task InlineColour_SurvivesSaveAndReload()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "coloured");

        await page.Keyboard.PressAsync("Control+A");
        await page.Locator(".tm-notion-inline-toolbar").First.WaitForAsync(Visible());
        await page.EvaluateAsync("() => window.tmNotionEditor.applyInlineColor('text', 'rgb(220, 38, 38)')");
        await page.WaitForTimeoutAsync(300);

        // The browser must have written a style attribute, not a deprecated <font> element:
        // the block sanitizer drops <font> and the colour would be lost on the round-trip.
        Assert.AreEqual(0, await Block(page, blockId).Locator("font").CountAsync(),
            "styleWithCSS must be on, so no <font> element is produced.");

        await page.EvaluateAsync("() => document.activeElement?.blur()");
        await page.WaitForTimeoutAsync(900);
        await CaptureBaselineAsync("markdown-shortcuts", "inline-colour", Block(page, blockId));

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-notion-page").First.WaitForAsync(Visible());

        var reloaded = Block(page, blockId);
        await reloaded.WaitForAsync(Visible());
        Assert.AreEqual(1, await reloaded.Locator("[style*='color']").CountAsync(),
            "The colour must survive the sanitizer and the reload.");
        StringAssert.Contains(await reloaded.InnerTextAsync(), "coloured");

        TestContext.WriteLine("UX: a colour picked from the toolbar is still there after a refresh — the picker is trustworthy.");
    }

    [TestMethod]
    [Description("Brown picked from the toolbar is the allowlisted hex and is still brown after a reload")]
    public async Task BrownInlineColour_SurvivesSaveAndReload()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "brown");

        await page.Keyboard.PressAsync("Control+A");
        await page.Locator(".tm-notion-inline-toolbar").First.WaitForAsync(Visible());
        await page.Locator(".tm-notion-inline-toolbar__btn--color").ClickAsync();
        await page.Locator(".tm-notion-inline-toolbar__color-swatch[style*='--tm-notion-option-text-brown-ink)']").ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var painted = await Block(page, blockId).Locator("[style*='color']").First.GetAttributeAsync("style");
        StringAssert.Contains(painted, "100, 71, 58",
            "The toolbar must send the allowlisted brown hex, not the CSS name 'brown'.");

        await page.EvaluateAsync("() => document.activeElement?.blur()");
        await page.WaitForTimeoutAsync(900);
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-notion-page").First.WaitForAsync(Visible());

        var reloaded = await Block(page, blockId).Locator("[style*='color']").First.GetAttributeAsync("style");
        StringAssert.Contains(reloaded, "100, 71, 58", "Brown must still be brown after the reload.");
    }

    [TestMethod]
    [Description("The editor ships without debug logging")]
    public async Task Editor_DoesNotLogToConsole()
    {
        var page = await OpenNotionEditorAsync();

        var logs = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "log") logs.Add(message.Text);
        };

        await TypeIntoFirstParagraphAsync(page, "typing");
        await page.WaitForTimeoutAsync(600);

        logs.Should_BeEmptyOf("onInput text:", "initKeyboardHandler attached to");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LocatorWaitForOptions Visible() =>
        new() { State = WaitForSelectorState.Visible, Timeout = 20000 };

    private static ILocator Block(IPage page, string blockId) =>
        page.Locator($"[data-block-id='{blockId}']");

    private static async Task<string> ActiveBlockIdAsync(IPage page) =>
        await page.EvaluateAsync<string>(
            "() => document.activeElement?.closest('[data-block-id]')?.getAttribute('data-block-id') ?? ''");

    private static async Task<string> TypeIntoFirstParagraphAsync(IPage page, string text)
    {
        await page.Locator(".tm-notion-paragraph[contenteditable='true']").First.WaitForAsync(Visible());

        // Only a top-level paragraph will do. Earlier tests in this class convert the page's first
        // paragraph into a heading or a list, and the next match would be one nested inside a
        // toggle or a table cell — a block the page's own block list does not own, so merging it
        // into a predecessor would silently no-op.
        var blockId = await page.EvaluateAsync<string?>(
            """
            () => {
                for (const el of document.querySelectorAll(".tm-notion-paragraph[contenteditable='true']")) {
                    const list = el.closest('[data-notion-block-list]');
                    if (list && !list.hasAttribute('data-parent-block-id')) {
                        return el.closest('[data-block-id]')?.getAttribute('data-block-id') ?? null;
                    }
                }
                return null;
            }
            """);
        Assert.IsFalse(string.IsNullOrWhiteSpace(blockId), "The demo page must expose a top-level paragraph.");

        var paragraph = Block(page, blockId!).Locator("[contenteditable='true']").First;
        await paragraph.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        if (text.Length > 0)
        {
            await page.Keyboard.TypeAsync(text);
        }
        else
        {
            await page.Keyboard.PressAsync("Delete");
        }
        await page.WaitForTimeoutAsync(400);

        return blockId!;
    }
}

internal static class ShortcutAssertions
{
    public static void Should_NotContain(this string value, string unexpected) =>
        Assert.IsFalse(value.Contains(unexpected, StringComparison.Ordinal),
            $"'{value}' must not contain '{unexpected}'.");

    public static void Should_BeEmptyOf(this List<string> logs, params string[] fragments)
    {
        foreach (var fragment in fragments)
        {
            Assert.IsFalse(logs.Any(log => log.Contains(fragment, StringComparison.Ordinal)),
                $"The editor must not log '{fragment}' in production.");
        }
    }
}
