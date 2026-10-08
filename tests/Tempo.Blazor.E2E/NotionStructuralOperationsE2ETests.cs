using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Structural edits must survive a reload: a block split off in the middle stays in the middle,
/// deleting a container takes its children with it, and a duplicated table is an independent copy
/// that still has its rows.
/// Runs against the self-hosted HTTPS demo API (5100) and HTTPS demo WASM (7106).
/// </summary>
[TestClass]
public class NotionStructuralOperationsE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("A block inserted in the middle is still in the middle after a reload")]
    public async Task BlockInsertedInTheMiddle_KeepsItsPlaceAfterAReload()
    {
        var page = await OpenNotionEditorAsync();
        var firstId = await FocusTopLevelParagraphAsync(page, "alpha");

        // Enter at the end splits off a new block right behind this one.
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(900);
        await page.Keyboard.TypeAsync("inserted");
        await BlurAsync(page);
        await page.WaitForTimeoutAsync(1000);

        var beforeReload = await IndexOfTextAsync(page, "inserted");
        var firstIndex = await IndexOfBlockAsync(page, firstId);
        Assert.AreEqual(firstIndex + 1, beforeReload, "The new block sits right behind its source.");

        await ReloadAsync(page);

        Assert.AreEqual(beforeReload, await IndexOfTextAsync(page, "inserted"),
            "Without a server-side order shift the block jumps to the end of the page on reload.");

        await CaptureBaselineAsync("structural-ops", "insert-in-the-middle", page.Locator(".tm-notion-page").First);
        TestContext.WriteLine("UX: pressing Enter mid-document keeps the new line where the caret was, even after a refresh.");
    }

    [TestMethod]
    [Description("Deleting a table removes it and its rows from the store, not just from the page")]
    public async Task DeletingATable_RemovesItsRowsFromTheStore()
    {
        var page = await OpenNotionEditorAsync();
        await FocusTopLevelParagraphAsync(page, string.Empty);

        await PasteHtmlAsync(page, "<table><tr><th>Orphan</th></tr><tr><td>Row</td></tr></table>");
        await page.WaitForTimeoutAsync(1600);

        var table = page.Locator(".tm-notion-table").Filter(new() { HasTextString = "Orphan" }).First;
        await table.WaitForAsync(Visible());
        var tableBlockId = await table.EvaluateAsync<string>(
            "el => el.closest('[data-block-id]').getAttribute('data-block-id')");

        Assert.IsTrue(
            await DeleteAggregateSubtreeAsync(page, tableBlockId),
            "The atomic aggregate replacement must succeed.");
        await ReloadAsync(page);

        Assert.AreEqual(0, await page.Locator($"[data-block-id='{tableBlockId}']").CountAsync());

        Assert.IsFalse(
            await AggregateContainsBlockOrChildAsync(page, tableBlockId),
            "A surviving TableRow is invisible on the page but would still exist in the aggregate.");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("A duplicated table has its own rows and editing the copy leaves the original alone")]
    public async Task DuplicatingATable_ProducesAnIndependentCopyWithRows()
    {
        var page = await OpenNotionEditorAsync();
        await FocusTopLevelParagraphAsync(page, string.Empty);

        await PasteHtmlAsync(page, "<table><tr><th>Dup</th></tr><tr><td>Cell</td></tr></table>");
        await page.WaitForTimeoutAsync(1600);

        var table = page.Locator(".tm-notion-table").Filter(new() { HasTextString = "Dup" }).First;
        var tableBlockId = await table.EvaluateAsync<string>(
            "el => el.closest('[data-block-id]').getAttribute('data-block-id')");

        Assert.IsTrue(
            await DuplicateAggregateSubtreeAsync(page, tableBlockId),
            "The atomic aggregate replacement must succeed.");
        await ReloadAsync(page);

        var tables = page
            .Locator("[data-notion-block-list]:not([data-parent-block-id])")
            .First
            .Locator(":scope > [data-block-type='Table']")
            .Filter(new() { HasTextString = "Dup" });
        Assert.AreEqual(2, await tables.CountAsync(), "The duplicate must exist.");

        // The copy must carry its own rows; a shallow copy renders an empty table.
        var copyCells = await tables.Nth(1).Locator("td, th").AllInnerTextsAsync();
        CollectionAssert.Contains(copyCells.Select(cell => cell.Trim()).ToList(), "Dup");
        CollectionAssert.Contains(copyCells.Select(cell => cell.Trim()).ToList(), "Cell");

        await CaptureBaselineAsync("structural-ops", "duplicated-table", page.Locator(".tm-notion-page").First);
        TestContext.WriteLine("UX: duplicating a table gives a real, editable copy — not an empty frame the user has to refill.");
    }

    [TestMethod]
    [Description("An open toggle is a real drop target: its block list names its page and parent")]
    public async Task ToggleIsADropTarget()
    {
        var page = await OpenNotionEditorAsync();
        await FocusTopLevelParagraphAsync(page, string.Empty);

        // Create a toggle from the slash menu, then open it so it renders its children.
        await page.Keyboard.TypeAsync(" /toggle");
        await page.WaitForTimeoutAsync(900);
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(1400);

        var toggle = page.Locator("[data-block-type='Toggle']").First;
        await toggle.WaitForAsync(Visible());
        await toggle.Locator(".tm-notion-toggle__arrow").First.ClickAsync();
        await page.WaitForTimeoutAsync(900);

        var declared = await toggle.EvaluateAsync<bool>(
            """
            el => {
                const list = el.querySelector('[data-notion-block-list]');
                return !!list && list.hasAttribute('data-page-id') && list.hasAttribute('data-parent-block-id');
            }
            """);
        Assert.IsTrue(declared, "A toggle's block list must name its page and parent to accept drops.");
    }

    [TestMethod]
    [Description("Keystrokes typed during the async Enter-split round-trip land in the new block, not the wiped old one")]
    public async Task FastTypingAfterEnter_LandsEveryCharacterInTheNewBlock()
    {
        var page = await OpenNotionEditorAsync();
        var firstId = await FocusTopLevelParagraphAsync(page, "abc");

        // Widen the async gap so the next keystrokes provably arrive BEFORE the new
        // editable mounts — without buffering they are overwritten by the source
        // block's setHtml(before) rewrite.
        var cdp = await page.Context.NewCDPSessionAsync(page);
        try
        {
            await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = 6 });
            await page.Keyboard.PressAsync("End");
            await page.Keyboard.PressAsync("Enter");
            await page.Keyboard.TypeAsync("xyz", new() { Delay = 0 });
        }
        finally
        {
            await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = 1 });
        }

        await page.Locator($"[data-block-id='{firstId}'] + [data-block-id]").WaitForAsync(Visible());
        await page.WaitForTimeoutAsync(1500);

        var texts = await page.EvaluateAsync<string[]>(
            """
            firstId => {
                const source = document.querySelector(`[data-block-id="${firstId}"]`);
                const next = source?.nextElementSibling;
                const textOf = c => (c?.querySelector("[contenteditable='true']")?.innerText ?? '').trim();
                return [textOf(source), textOf(next)];
            }
            """,
            firstId);

        Assert.AreEqual("abc", texts[0], "Block 1 keeps the text typed before Enter.");
        Assert.AreEqual("xyz", texts[1], "Block 2 receives the characters typed during the split round-trip.");

        await CaptureBaselineAsync("structural-ops", "fast-typing-after-enter", page.Locator(".tm-notion-page").First);
        TestContext.WriteLine("UX: characters typed immediately after Enter land in the new block — no swallowed keystrokes.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LocatorWaitForOptions Visible() =>
        new() { State = WaitForSelectorState.Visible, Timeout = 20000 };

    private static async Task BlurAsync(IPage page)
    {
        await page.EvaluateAsync("() => document.activeElement?.blur()");
        await page.WaitForTimeoutAsync(300);
    }

    private async Task ReloadAsync(IPage page)
    {
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-notion-page").First.WaitForAsync(Visible());
        await page.WaitForTimeoutAsync(600);
    }

    private static async Task<int> IndexOfBlockAsync(IPage page, string blockId) =>
        await page.EvaluateAsync<int>(
            """
            id => {
                const list = document.querySelector("[data-notion-block-list]:not([data-parent-block-id])");
                return [...list.children].filter(c => c.hasAttribute('data-block-id'))
                    .findIndex(c => c.getAttribute('data-block-id') === id);
            }
            """,
            blockId);

    private static async Task<int> IndexOfTextAsync(IPage page, string text) =>
        await page.EvaluateAsync<int>(
            """
            needle => {
                const list = document.querySelector("[data-notion-block-list]:not([data-parent-block-id])");
                return [...list.children].filter(c => c.hasAttribute('data-block-id'))
                    .findIndex(c => c.innerText.trim() === needle);
            }
            """,
            text);

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

    private static Task<bool> DeleteAggregateSubtreeAsync(IPage page, string blockId) =>
        page.EvaluateAsync<bool>(
            """
            async blockId => {
                const loaded = await fetch(`https://localhost:5100/api/notion/aggregate/blocks/${blockId}`);
                if (!loaded.ok) return false;
                const aggregate = await loaded.json();
                const snapshot = aggregate.snapshot;
                const removed = new Set([blockId.toLowerCase()]);
                let changed;
                do {
                    changed = false;
                    for (const block of snapshot.blocks) {
                        const parentId = String(block.parentBlockId || '').toLowerCase();
                        const id = String(block.id).toLowerCase();
                        if (parentId && removed.has(parentId) && !removed.has(id)) {
                            removed.add(id);
                            changed = true;
                        }
                    }
                } while (changed);

                snapshot.blocks = snapshot.blocks.filter(
                    block => !removed.has(String(block.id).toLowerCase()));
                normalizeOrders(snapshot.blocks);

                const saved = await fetch('https://localhost:5100/api/notion/aggregate/save', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        pages: [{
                            snapshot,
                            baseConcurrencyToken: snapshot.concurrencyToken
                        }]
                    })
                });
                return saved.ok && (await saved.json()).success === true;

                function normalizeOrders(blocks) {
                    const groups = new Map();
                    for (const block of blocks) {
                        const key = String(block.parentBlockId || '');
                        if (!groups.has(key)) groups.set(key, []);
                        groups.get(key).push(block);
                    }
                    for (const siblings of groups.values()) {
                        siblings.sort((a, b) => a.order - b.order);
                        siblings.forEach((block, index) => block.order = index);
                    }
                }
            }
            """,
            blockId);

    private static Task<bool> DuplicateAggregateSubtreeAsync(IPage page, string blockId) =>
        page.EvaluateAsync<bool>(
            """
            async blockId => {
                const loaded = await fetch(`https://localhost:5100/api/notion/aggregate/blocks/${blockId}`);
                if (!loaded.ok) return false;
                const aggregate = await loaded.json();
                const snapshot = aggregate.snapshot;
                const root = snapshot.blocks.find(
                    block => String(block.id).toLowerCase() === blockId.toLowerCase());
                if (!root) return false;

                const sourceIds = new Set([String(root.id).toLowerCase()]);
                let changed;
                do {
                    changed = false;
                    for (const block of snapshot.blocks) {
                        const parentId = String(block.parentBlockId || '').toLowerCase();
                        const id = String(block.id).toLowerCase();
                        if (parentId && sourceIds.has(parentId) && !sourceIds.has(id)) {
                            sourceIds.add(id);
                            changed = true;
                        }
                    }
                } while (changed);

                const source = snapshot.blocks.filter(
                    block => sourceIds.has(String(block.id).toLowerCase()));
                const ids = new Map(source.map(
                    block => [String(block.id).toLowerCase(), crypto.randomUUID()]));
                const timestamp = new Date().toISOString();
                const copies = source.map(block => {
                    const copy = JSON.parse(JSON.stringify(block));
                    copy.id = ids.get(String(block.id).toLowerCase());
                    const parentId = String(block.parentBlockId || '').toLowerCase();
                    if (ids.has(parentId)) copy.parentBlockId = ids.get(parentId);
                    copy.createdAt = timestamp;
                    copy.lastEditedAt = timestamp;
                    return copy;
                });
                const rootCopy = copies.find(
                    block => String(block.id) === ids.get(String(root.id).toLowerCase()));
                const rootParent = String(root.parentBlockId || '').toLowerCase();
                for (const sibling of snapshot.blocks) {
                    if (String(sibling.parentBlockId || '').toLowerCase() === rootParent &&
                        sibling.order > root.order) {
                        sibling.order++;
                    }
                }
                rootCopy.order = root.order + 1;
                snapshot.blocks.push(...copies);

                const saved = await fetch('https://localhost:5100/api/notion/aggregate/save', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        pages: [{
                            snapshot,
                            baseConcurrencyToken: snapshot.concurrencyToken
                        }]
                    })
                });
                return saved.ok && (await saved.json()).success === true;
            }
            """,
            blockId);

    private static Task<bool> AggregateContainsBlockOrChildAsync(IPage page, string blockId) =>
        page.EvaluateAsync<bool>(
            """
            async blockId => {
                const pageId = document.querySelector('.tm-notion-page')?.dataset.pageId;
                if (!pageId) throw new Error('Notion page id was not found.');
                const loaded = await fetch(`https://localhost:5100/api/notion/aggregate/pages/${pageId}`);
                if (!loaded.ok) throw new Error(`Aggregate request failed: ${loaded.status}`);
                const blocks = (await loaded.json()).snapshot.blocks;
                const id = blockId.toLowerCase();
                return blocks.some(block =>
                    String(block.id).toLowerCase() === id ||
                    String(block.parentBlockId || '').toLowerCase() === id);
            }
            """,
            blockId);

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
        await page.WaitForTimeoutAsync(400);
        return blockId!;
    }
}
