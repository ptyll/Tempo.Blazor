using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Ctrl+Z undoes structural edits: a block conversion comes back with its text, a deleted table
/// comes back with its rows. While the caret is inside a block the user is still typing into, the
/// browser keeps Ctrl+Z for its own character-level undo.
/// Runs against the self-hosted HTTPS demo API (5100) and HTTPS demo WASM (7106).
/// </summary>
[TestClass]
public class NotionUndoRedoE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Undo of a Heading 1 -> Heading 2 conversion restores the type and the text")]
    public async Task UndoOfAConversion_RestoresTheTypeAndTheText()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await FocusTopLevelParagraphAsync(page, "survives");

        // Paragraph -> Heading 1, then Heading 1 -> Heading 2.
        await ConvertAsync(page, blockId, "# ");
        await ConvertAsync(page, blockId, "## ");

        var block = Block(page, blockId);
        await block.Locator(".tm-notion-heading--h2").First.WaitForAsync(Visible());

        await BlurAsync(page);
        await page.Keyboard.PressAsync("Control+Z");
        await page.WaitForTimeoutAsync(1500);

        await block.Locator(".tm-notion-heading--h1").First.WaitForAsync(Visible());
        StringAssert.Contains(await block.InnerTextAsync(), "survives",
            "Undo must bring back the text, not just the block type.");

        await CaptureBaselineAsync("undo-redo", "conversion-undone", page.Locator(".tm-notion-page").First);
        TestContext.WriteLine("UX: a mistaken heading demotion is one Ctrl+Z away and the sentence is intact.");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Undo of deleting a table brings the table back with its rows")]
    public async Task UndoOfDeletingATable_RestoresItsRows()
    {
        var page = await OpenNotionEditorAsync();
        var anchorId = await FocusTopLevelParagraphAsync(page, string.Empty);

        // The helper blurs; the paste needs a focused editable to fire on.
        await page.Locator($"[data-block-id='{anchorId}'] [contenteditable='true']").First.ClickAsync();
        await page.WaitForTimeoutAsync(300);
        await PasteHtmlAsync(page, "<table><tr><th>Undo</th></tr><tr><td>Row</td></tr></table>");
        await page.WaitForTimeoutAsync(1700);

        var table = page.Locator(".tm-notion-table").Filter(new() { HasTextString = "Undo" }).First;
        await table.WaitForAsync(Visible());
        var tableBlockId = await table.EvaluateAsync<string>(
            "el => el.closest('[data-block-id]').getAttribute('data-block-id')");

        await DeleteBlockThroughTheEditorAsync(page, tableBlockId);
        Assert.AreEqual(0, await page.Locator($"[data-block-id='{tableBlockId}']").CountAsync());

        await BlurAsync(page);
        await page.Keyboard.PressAsync("Control+Z");
        await page.WaitForTimeoutAsync(1800);

        var restored = page.Locator($"[data-block-id='{tableBlockId}']");
        await restored.First.WaitForAsync(Visible());

        var cells = (await restored.First.Locator("td, th").AllInnerTextsAsync())
            .Select(cell => cell.Trim()).ToList();
        CollectionAssert.Contains(cells, "Undo");
        CollectionAssert.Contains(cells, "Row", "An empty restored table means the rows were not brought back.");

        await CaptureBaselineAsync("undo-redo", "table-restored", page.Locator(".tm-notion-page").First);
        TestContext.WriteLine("UX: undoing a delete restores the whole block, rows included — nothing to retype.");
    }

    [TestMethod]
    [Description("Redo re-applies an undone conversion")]
    public async Task RedoReappliesTheConversion()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await FocusTopLevelParagraphAsync(page, "redo me");

        await ConvertAsync(page, blockId, "# ");
        await BlurAsync(page);

        await page.Keyboard.PressAsync("Control+Z");
        await page.WaitForTimeoutAsync(1400);
        Assert.AreEqual(0, await Block(page, blockId).Locator(".tm-notion-heading--h1").CountAsync());

        await page.Keyboard.PressAsync("Control+Y");
        await page.WaitForTimeoutAsync(1400);
        await Block(page, blockId).Locator(".tm-notion-heading--h1").First.WaitForAsync(Visible());
    }

    [TestMethod]
    [Description("While typing, Ctrl+Z belongs to the browser and undoes the last characters")]
    public async Task CtrlZWhileTyping_DoesNotUndoTheStructuralEdit()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await FocusTopLevelParagraphAsync(page, "base");

        await ConvertAsync(page, blockId, "# ");
        await Block(page, blockId).Locator(".tm-notion-heading--h1").First.WaitForAsync(Visible());

        // Type into the heading, then Ctrl+Z without leaving it.
        await Block(page, blockId).Locator("[contenteditable='true']").First.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" extra");
        await page.WaitForTimeoutAsync(300);
        await page.Keyboard.PressAsync("Control+Z");
        await page.WaitForTimeoutAsync(900);

        Assert.AreEqual(1, await Block(page, blockId).Locator(".tm-notion-heading--h1").CountAsync(),
            "The heading must survive: the browser owns Ctrl+Z while the block is being typed into.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LocatorWaitForOptions Visible() =>
        new() { State = WaitForSelectorState.Visible, Timeout = 20000 };

    private static ILocator Block(IPage page, string blockId) =>
        page.Locator($"[data-block-id='{blockId}']").First;

    private static async Task BlurAsync(IPage page)
    {
        await page.EvaluateAsync("() => document.activeElement?.blur()");
        await page.WaitForTimeoutAsync(700);
    }

    /// <summary>
    /// Converts through the editor's own markdown shortcut, so the command stack records it.
    /// Going through the API and reloading would throw the page's history away.
    /// </summary>
    private static async Task ConvertAsync(IPage page, string blockId, string trigger)
    {
        await page.Locator($"[data-block-id='{blockId}'] [contenteditable='true']").First.ClickAsync();
        await page.EvaluateAsync("id => window.tmNotionEditor.setCaretOffset(id, 0)", blockId);
        await page.Keyboard.TypeAsync(trigger);
        await page.WaitForTimeoutAsync(1300);
    }

    /// <summary>Deletes through the block handle menu, the path that records an undo step.</summary>
    private static async Task DeleteBlockThroughTheEditorAsync(IPage page, string blockId)
    {
        var block = page.Locator($"[data-block-id='{blockId}']").First;
        await block.HoverAsync();
        await page.WaitForTimeoutAsync(500);

        // The handle has two buttons: add, and the options menu inside the menu anchor.
        await block.Locator(".tm-notion-handle__menu-anchor .tm-notion-handle__btn").First.ClickAsync();
        await page.WaitForTimeoutAsync(700);

        await page.Locator(".tm-notion-ctx__item--danger").First.ClickAsync();
        await page.WaitForTimeoutAsync(1600);
    }

    private static async Task PasteHtmlAsync(IPage page, string html)
    {
        await page.EvaluateAsync(
            """
            html => {
                const data = new DataTransfer();
                data.setData('text/html', html);
                data.setData('text/plain', html.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim());
                document.activeElement.dispatchEvent(new ClipboardEvent('paste', {
                    clipboardData: data, bubbles: true, cancelable: true
                }));
            }
            """,
            html);
    }

    private static async Task<string> FocusTopLevelParagraphAsync(IPage page, string text)
    {
        await page.Locator(".tm-notion-paragraph[contenteditable='true']").First.WaitForAsync(Visible());

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
        Assert.IsFalse(string.IsNullOrWhiteSpace(blockId));

        await page.Locator($"[data-block-id='{blockId}'] [contenteditable='true']").First.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        if (text.Length > 0) await page.Keyboard.TypeAsync(text);
        else await page.Keyboard.PressAsync("Delete");
        await BlurAsync(page);
        return blockId!;
    }
}
