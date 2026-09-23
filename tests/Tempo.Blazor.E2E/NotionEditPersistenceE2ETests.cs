using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Edits must survive: a change to another block must not overwrite an unsaved block,
/// inline DOM surgery must still be committed, and stored HTML must never execute.
/// Runs against the self-hosted HTTPS demo API (5100) and HTTPS demo WASM (7106).
/// </summary>
[TestClass]
public class NotionEditPersistenceE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Inline code applied through the toolbar is committed on blur and survives a reload")]
    public async Task InlineCode_IsCommittedAndSurvivesReload()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "make this code");

        // Commit the typed text first. Otherwise the block is already dirty from typing and the
        // test would pass even if the inline-code surgery never marked it dirty.
        await BlurAsync(page);
        await page.WaitForTimeoutAsync(700);
        await Block(page, blockId).Locator("[contenteditable='true']").First.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Select the text and toggle inline code — pure DOM surgery, no `input` event of its own.
        await page.Keyboard.PressAsync("Control+A");
        await page.Locator(".tm-notion-inline-toolbar").First.WaitForAsync(Visible());
        await page.Locator("button[title='Inline code']").First.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        Assert.AreEqual(1, await Block(page, blockId).Locator("code").CountAsync(), "The inline code element must exist in the DOM.");

        await BlurAsync(page);
        await page.WaitForTimeoutAsync(900);
        await CaptureBaselineAsync("edit-persistence", "inline-code-before-reload", Block(page, blockId));

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-notion-page").First.WaitForAsync(Visible());

        var reloaded = Block(page, blockId);
        await reloaded.WaitForAsync(Visible());
        Assert.AreEqual(1, await reloaded.Locator("code").CountAsync(), "Inline code must survive the reload — the blur commit ran.");
        StringAssert.Contains(await reloaded.InnerTextAsync(), "make this code");

        await CaptureBaselineAsync("edit-persistence", "inline-code-after-reload", reloaded);
        TestContext.WriteLine("UX: formatting applied from the toolbar persists exactly like typed text, so the toolbar never feels unreliable.");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Typing in one block is not overwritten when another block above it changes")]
    public async Task UnsavedEdit_SurvivesChangeToAnotherBlock()
    {
        var page = await OpenNotionEditorAsync();

        var firstId = await TypeIntoFirstParagraphAsync(page, "first block");

        // Create a second block and type into it WITHOUT blurring. The Enter split creates the
        // new block asynchronously — WaitForBlockFocusToMoveAsync waits until the new editable
        // owns focus before typing, so keystrokes cannot be swallowed by the source block's
        // setHtml(before) rewrite (observed loss: "ed second block").
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await WaitForBlockFocusToMoveAsync(page, firstId);
        await page.Keyboard.TypeAsync("unsaved second block");
        await page.WaitForTimeoutAsync(300);

        var secondId = await page.EvaluateAsync<string>(
            """
            () => document.activeElement?.closest('[data-block-id]')?.getAttribute('data-block-id') ?? ''
            """);
        Assert.IsFalse(string.IsNullOrWhiteSpace(secondId));
        Assert.AreNotEqual(firstId, secondId);

        // Mutate the FIRST block from the outside; without @key this re-renders the second one
        // by index and blows its unsaved DOM away.
        await page.EvaluateAsync(
            """
            id => {
                const el = document.querySelector(`[data-block-id="${id}"] [contenteditable="true"]`);
                el.focus();
            }
            """,
            firstId);
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" edited");
        await BlurAsync(page);
        await page.WaitForTimeoutAsync(1000);

        var secondText = await Block(page, secondId).InnerTextAsync();
        StringAssert.Contains(secondText, "unsaved second block", "The unsaved block must keep its text.");

        await CaptureBaselineAsync("edit-persistence", "sibling-edit-preserved");
        TestContext.WriteLine("UX: editing one block never disturbs a neighbour that is still being written — no silent data loss.");
    }

    [TestMethod]
    [Description("Enter splits the block and the source half keeps only the text before the caret")]
    public async Task EnterSplit_LeavesOnlyTheTextBeforeTheCaret()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "beforeafter");

        // Place the caret between "before" and "after".
        await page.EvaluateAsync(
            "id => window.tmNotionEditor.setCaretOffset(id, 6)", blockId);
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(900);

        Assert.AreEqual("before", (await Block(page, blockId).InnerTextAsync()).Trim(),
            "The source block must show only the half before the caret.");
    }

    [TestMethod]
    [Description("Stored HTML with an onerror payload never executes when the block renders")]
    public async Task StoredHtml_IsSanitizedOnRender()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "payload");

        // Inject a stored-XSS payload straight into the editable, then commit it with a blur.
        await page.EvaluateAsync(
            """
            id => {
                const el = document.querySelector(`[data-block-id="${id}"] [contenteditable="true"]`);
                el.innerHTML = 'x<img src=q onerror="window.__pwned = true">';
                el.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """,
            blockId);
        await BlurAsync(page);
        await page.WaitForTimeoutAsync(900);

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-notion-page").First.WaitForAsync(Visible());

        var block = Block(page, blockId);
        await block.WaitForAsync(Visible());
        Assert.AreEqual(0, await block.Locator("img").CountAsync(), "The img must never reach the DOM.");
        Assert.IsNull(await page.EvaluateAsync<bool?>("() => window.__pwned ?? null"), "The payload must not execute.");
        StringAssert.Contains(await block.InnerTextAsync(), "x", "The surrounding text survives sanitization.");
    }

    [TestMethod]
    [Description("Sanitization keeps the editor's own inline chrome — a status chip survives a reload")]
    public async Task StatusChip_SurvivesSanitizationAndReload()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "status ");

        await page.EvaluateAsync(
            """
            id => {
                const el = document.querySelector(`[data-block-id="${id}"] [contenteditable="true"]`);
                el.innerHTML += '<span contenteditable="false" class="tm-notion-status tm-notion-status--green" data-status-label="Done" data-status-color="green"><span class="tm-notion-status__label">Done</span></span>';
                el.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """,
            blockId);
        await BlurAsync(page);
        await page.WaitForTimeoutAsync(900);

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-notion-page").First.WaitForAsync(Visible());

        var chip = Block(page, blockId).Locator(".tm-notion-status");
        await chip.First.WaitForAsync(Visible());
        Assert.AreEqual(1, await chip.CountAsync(), "Sanitizing must not destroy the editor's own chips.");
        // The chip label is uppercased by CSS, so compare the DOM text, not the rendered text.
        var label = await chip.First.EvaluateAsync<string>("el => el.textContent.trim()");
        Assert.AreEqual("Done", label);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LocatorWaitForOptions Visible() =>
        new() { State = WaitForSelectorState.Visible, Timeout = 20000 };

    private static ILocator Block(IPage page, string blockId) =>
        page.Locator($"[data-block-id='{blockId}']").First;

    private static async Task BlurAsync(IPage page)
    {
        await page.EvaluateAsync("() => document.activeElement?.blur()");
        await page.WaitForTimeoutAsync(300);
    }

    private static async Task<string> TypeIntoFirstParagraphAsync(IPage page, string text)
    {
        var paragraph = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await paragraph.WaitForAsync(Visible());
        await paragraph.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.TypeAsync(text);
        await page.WaitForTimeoutAsync(400);

        var blockId = await paragraph.EvaluateAsync<string?>(
            "el => el.closest('[data-block-id]')?.getAttribute('data-block-id')");
        Assert.IsFalse(string.IsNullOrWhiteSpace(blockId));
        return blockId!;
    }
}
