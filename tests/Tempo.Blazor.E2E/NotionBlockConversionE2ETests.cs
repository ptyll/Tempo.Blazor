using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Converting a block must not lose the text the user has typed but not yet saved,
/// must not orphan the block's children, and must keep typed data across a round-trip.
/// Every assertion is scoped to the block under test — the demo page already contains
/// headings, callouts and tables of its own.
/// Runs against the self-hosted HTTPS demo API (5100) and HTTPS demo WASM (7106).
/// </summary>
[TestClass]
public class NotionBlockConversionE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Text typed but never blurred survives Paragraph -> Heading 1 -> Heading 2 (the reported bug)")]
    public async Task TurnInto_KeepsUnsavedTextAcrossHeadingConversion()
    {
        var page = await OpenNotionEditorAsync();

        // Type into a paragraph and convert WITHOUT blurring — the text lives only in the DOM.
        var blockId = await TypeIntoFirstParagraphAsync(page, "unsaved heading text");
        var block = Block(page, blockId);

        await TurnIntoAsync(page, block, "Heading 1");
        await block.Locator(".tm-notion-heading--h1").First.WaitForAsync(Visible());
        StringAssert.Contains(await block.InnerTextAsync(), "unsaved heading text");
        await CaptureBaselineAsync("block-conversion", "heading1-keeps-text", block);

        // H1 -> H2, again without blurring.
        await TurnIntoAsync(page, block, "Heading 2");
        await block.Locator(".tm-notion-heading--h2").First.WaitForAsync(Visible());
        StringAssert.Contains(await block.InnerTextAsync(), "unsaved heading text");

        await CaptureBaselineAsync("block-conversion", "heading2-keeps-text", block);
        TestContext.WriteLine("UX: the heading keeps its text and the caret stays inside the block, so Turn-into never feels destructive.");
    }

    [TestMethod]
    [Description("The caret returns into the converted block instead of being lost to the document")]
    public async Task TurnInto_RestoresCaretIntoConvertedBlock()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "abcdef");

        await TurnIntoAsync(page, Block(page, blockId), "Heading 3");
        await Block(page, blockId).Locator(".tm-notion-heading--h3").First.WaitForAsync(Visible());

        var caret = await page.EvaluateAsync<int>(
            """
            blockId => {
                const s = window.getSelection();
                if (!s || s.rangeCount === 0) return -1;
                const r = s.getRangeAt(0);
                const node = r.startContainer;
                const host = (node.nodeType === 1 ? node : node.parentElement)
                    ?.closest('[contenteditable="true"]');
                if (!host) return -1;
                if (host.closest('[data-block-id]')?.getAttribute('data-block-id') !== blockId) return -2;
                const probe = r.cloneRange();
                probe.selectNodeContents(host);
                probe.setEnd(r.startContainer, r.startOffset);
                return probe.toString().length;
            }
            """,
            blockId);

        // Turn-into starts from a selection, so the restored caret sits at the selection's start.
        Assert.AreEqual(0, caret, "The caret must be restored inside the converted block, not lost.");
    }

    [TestMethod]
    [Description("The caret helpers read and write a plain-text offset inside a block")]
    public async Task CaretHelpers_ReadAndWriteOffset()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "abcdef");

        var written = await page.EvaluateAsync<bool>(
            "id => window.tmNotionEditor.setCaretOffset(id, 3)", blockId);
        Assert.IsTrue(written);

        var read = await page.EvaluateAsync<int?>(
            "id => window.tmNotionEditor.getCaretOffset(id)", blockId);
        Assert.AreEqual(3, read);

        // An offset past the end clamps to the end rather than throwing.
        await page.EvaluateAsync("id => window.tmNotionEditor.setCaretOffset(id, 999)", blockId);
        Assert.AreEqual(6, await page.EvaluateAsync<int?>("id => window.tmNotionEditor.getCaretOffset(id)", blockId));

        // A block that has no editable yields null instead of blowing up.
        Assert.IsNull(await page.EvaluateAsync<string?>("() => window.tmNotionEditor.getEditableHtml('no-such-block')"));
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Converting a Toggle with nested blocks to a Paragraph re-parents the children instead of orphaning them")]
    public async Task TurnInto_ToggleToParagraphKeepsChildren()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "toggle parent");
        var block = Block(page, blockId);

        await TurnIntoAsync(page, block, "Toggle");
        await block.Locator(".tm-notion-toggle").First.WaitForAsync(Visible());

        var blocksBefore = await page.Locator("[data-block-id]").CountAsync();

        await TurnIntoAsync(page, block, "Text");
        await page.WaitForTimeoutAsync(800);

        Assert.AreEqual(0, await block.Locator(".tm-notion-toggle").CountAsync(), "The toggle must be gone.");
        Assert.IsTrue(
            await page.Locator("[data-block-id]").CountAsync() >= blocksBefore - 1,
            "No block may vanish: children move up to the toggle's parent rather than being orphaned.");

        await CaptureBaselineAsync("block-conversion", "toggle-to-paragraph", block);
        TestContext.WriteLine("UX: children reappear at the toggle's own level instead of silently disappearing, so no content is lost.");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Callout -> Text -> Callout keeps the icon; a fresh Callout gets the default icon")]
    public async Task TurnInto_CalloutRoundTripKeepsIcon()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "callout body");
        var block = Block(page, blockId);

        await TurnIntoAsync(page, block, "Callout");
        var callout = block.Locator(".tm-notion-callout").First;
        await callout.WaitForAsync(Visible());
        StringAssert.Contains(await callout.InnerTextAsync(), "💡", "A fresh callout must get the default icon.");
        await CaptureBaselineAsync("block-conversion", "callout-default-icon", block);

        await TurnIntoAsync(page, block, "Text");
        await page.WaitForTimeoutAsync(600);
        Assert.AreEqual(0, await block.Locator(".tm-notion-callout").CountAsync(), "The callout must be gone.");

        await TurnIntoAsync(page, block, "Callout");
        var back = block.Locator(".tm-notion-callout").First;
        await back.WaitForAsync(Visible());
        var text = await back.InnerTextAsync();
        StringAssert.Contains(text, "callout body");
        StringAssert.Contains(text, "💡");

        TestContext.WriteLine("UX: converting away and back restores the callout's icon and variant, so the round-trip is non-destructive.");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Paragraph -> Table puts the text in the first cell and creates exactly two rows")]
    public async Task SlashMenu_ParagraphToTablePutsTextInFirstCell()
    {
        var page = await OpenNotionEditorAsync();
        var blockId = await TypeIntoFirstParagraphAsync(page, "header cell");
        var block = Block(page, blockId);

        // Table is not offered by the Turn-into panel; it is reached through the slash menu.
        await ConvertViaSlashMenuAsync(page, "table");
        var table = block.Locator(".tm-notion-table").First;
        await table.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        var rows = table.Locator("tbody tr.tm-notion-table-row");
        await page.WaitForTimeoutAsync(1000);
        Assert.AreEqual(2, await rows.CountAsync(), "A converted table starts with exactly two rows.");
        StringAssert.Contains(await rows.Nth(0).InnerTextAsync(), "header cell", "The paragraph text lands in the first cell.");

        await CaptureBaselineAsync("block-conversion", "paragraph-to-table", block);
        TestContext.WriteLine("UX: the text is not thrown away — it becomes the table's first cell, so the conversion reads as a promotion rather than a reset.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LocatorWaitForOptions Visible() =>
        new() { State = WaitForSelectorState.Visible, Timeout = 15000 };

    private static ILocator Block(IPage page, string blockId) =>
        page.Locator($"[data-block-id='{blockId}']").First;

    /// <summary>Types into the first paragraph and returns the id of the block that owns it.</summary>
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
        Assert.IsFalse(string.IsNullOrWhiteSpace(blockId), "The paragraph must live inside a block with a data-block-id.");
        return blockId!;
    }

    /// <summary>Converts the focused block through the slash menu, which offers types the toolbar does not.</summary>
    private static async Task ConvertViaSlashMenuAsync(IPage page, string searchTerm)
    {
        // The slash menu only triggers when "/" sits on a word boundary.
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" /");
        await page.WaitForSelectorAsync(".tm-notion-slash", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 8000
        });

        var search = page.Locator(".tm-notion-slash__input");
        await search.WaitForAsync(Visible());
        await search.FillAsync(searchTerm);
        await page.WaitForTimeoutAsync(400);

        await page.Locator(".tm-notion-slash__item").First.ClickAsync();
        await page.WaitForTimeoutAsync(1200);
    }

    /// <summary>Opens the inline toolbar's Turn-into panel and picks an entry, without blurring the block.</summary>
    private static async Task TurnIntoAsync(IPage page, ILocator block, string itemLabel)
    {
        // Focus can be lost to <body> between conversions (baseline captures resize the
        // viewport, which some Chromium builds answer by dropping focus). Re-establish it
        // in the block's editable before selecting, like a user would.
        await block.Locator("[contenteditable='true']").First.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        try
        {
            await page.Locator(".tm-notion-inline-toolbar").First.WaitForAsync(Visible());
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Turn-into '{itemLabel}': the inline toolbar did not appear after Ctrl+A.", ex);
        }

        await page.Locator("button[title='Turn into']").First.ClickAsync();
        var item = page.Locator(".tm-notion-inline-toolbar__turninto-item")
            .Filter(new LocatorFilterOptions { HasText = itemLabel })
            .First;
        await item.WaitForAsync(Visible());
        await item.ClickAsync();
        await page.WaitForTimeoutAsync(1000);
    }
}
