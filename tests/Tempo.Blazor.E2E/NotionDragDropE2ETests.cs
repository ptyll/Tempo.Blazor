using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests covering drag-and-drop block reordering in the Notion editor.
/// Phase 6: Move blocks up/down and verify the drag indicator.
/// Uses JS-based HTML5 drag-event simulation because Playwright's DragToAsync
/// does not reliably trigger HTML5 D&amp;D listeners registered on container elements.
/// </summary>
[TestClass]
public class NotionDragDropE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/notion-editor");
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(2000);
        return page;
    }

    /// <summary>
    /// Returns all visible top-level blocks in the page.
    /// </summary>
    private ILocator Blocks(IPage page) =>
        page.Locator("[data-notion-block]");

    /// <summary>
    /// Simulates a full HTML5 drag-and-drop sequence between two blocks via JS.
    /// Dispatches events directly from the drag handle so that <c>e.target</c>
    /// is the real handle element (required by <c>closest()</c> in the handler).
    /// </summary>
    private async Task SimulateBlockDragDropAsync(IPage page, int sourceIndex, int targetIndex, bool dropBelow = true)
    {
        await page.EvaluateAsync(
            """
            (args) => {
                const blocks = Array.from(document.querySelectorAll('[data-notion-block]'));
                const srcBlock = blocks[args.sourceIndex];
                const tgtBlock = blocks[args.targetIndex];
                if (!srcBlock || !tgtBlock) return;

                const handle = srcBlock.querySelector('[data-notion-drag-handle]');
                if (!handle) return;

                const container = document.querySelector('[data-page-blocks]');
                if (!container) return;

                const rect = tgtBlock.getBoundingClientRect();
                const clientY = args.dropBelow
                    ? rect.bottom - 2
                    : rect.top + 2;
                const clientX = rect.left + rect.width / 2;

                function makeDt() {
                    const dt = new DataTransfer();
                    if (typeof dt.setDragImage !== 'function') {
                        dt.setDragImage = function() {};
                    }
                    return dt;
                }

                // DragStart – dispatch from handle so e.target == handle
                handle.dispatchEvent(new DragEvent('dragstart', {
                    bubbles: true, cancelable: true, dataTransfer: makeDt()
                }));

                // DragOver – dispatch from target block so handler sees correct clientY
                tgtBlock.dispatchEvent(new DragEvent('dragover', {
                    bubbles: true, cancelable: true,
                    clientX, clientY, dataTransfer: makeDt()
                }));

                // Drop
                tgtBlock.dispatchEvent(new DragEvent('drop', {
                    bubbles: true, cancelable: true,
                    clientX, clientY, dataTransfer: makeDt()
                }));

                // DragEnd – dispatch from handle
                handle.dispatchEvent(new DragEvent('dragend', {
                    bubbles: true, cancelable: true, dataTransfer: makeDt()
                }));
            }
            """,
            new { sourceIndex, targetIndex, dropBelow });
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Drag & Drop — Reorder
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Dragging the second block below the third block reorders them")]
    public async Task BlockDragDrop_MoveBlock_Down()
    {
        var page = await OpenNotionEditorAsync();
        var blocks = Blocks(page);
        var count = await blocks.CountAsync();
        if (count < 3)
        {
            Assert.Inconclusive("Need at least 3 blocks to test drag-down reorder");
            return;
        }

        // Capture the text of the first three blocks before reordering
        var text0Before = await blocks.Nth(0).InnerTextAsync();
        var text1Before = await blocks.Nth(1).InnerTextAsync();
        var text2Before = await blocks.Nth(2).InnerTextAsync();

        // Simulate drag: move block 1 below block 2
        await SimulateBlockDragDropAsync(page, sourceIndex: 1, targetIndex: 2, dropBelow: true);
        await page.WaitForTimeoutAsync(1200);

        // After dragging block 1 below block 2, the order should be:
        // [0] = original 0, [1] = original 2, [2] = original 1
        var text0After = await blocks.Nth(0).InnerTextAsync();
        var text1After = await blocks.Nth(1).InnerTextAsync();
        var text2After = await blocks.Nth(2).InnerTextAsync();

        Assert.AreEqual(text0Before, text0After, "First block should stay the same");
        Assert.AreEqual(text2Before, text1After, "Second block should now be the original third block");
        Assert.AreEqual(text1Before, text2After, "Third block should now be the original second block");

        await TakeScreenshotAsync(page, "drag_drop_move_down");
    }

    [TestMethod]
    [Description("Dragging the third block above the first block reorders them")]
    public async Task BlockDragDrop_MoveBlock_Up()
    {
        var page = await OpenNotionEditorAsync();
        var blocks = Blocks(page);
        var count = await blocks.CountAsync();
        if (count < 3)
        {
            Assert.Inconclusive("Need at least 3 blocks to test drag-up reorder");
            return;
        }

        // Capture the text of the first three blocks before reordering
        var text0Before = await blocks.Nth(0).InnerTextAsync();
        var text1Before = await blocks.Nth(1).InnerTextAsync();
        var text2Before = await blocks.Nth(2).InnerTextAsync();

        // Simulate drag: move block 2 above block 0
        await SimulateBlockDragDropAsync(page, sourceIndex: 2, targetIndex: 0, dropBelow: false);
        await page.WaitForTimeoutAsync(1200);

        // After dragging block 2 above block 0, the order should be:
        // [0] = original 2, [1] = original 0, [2] = original 1
        var text0After = await blocks.Nth(0).InnerTextAsync();
        var text1After = await blocks.Nth(1).InnerTextAsync();
        var text2After = await blocks.Nth(2).InnerTextAsync();

        Assert.AreEqual(text2Before, text0After, "First block should now be the original third block");
        Assert.AreEqual(text0Before, text1After, "Second block should now be the original first block");
        Assert.AreEqual(text1Before, text2After, "Third block should now be the original second block");

        await TakeScreenshotAsync(page, "drag_drop_move_up");
    }

    [TestMethod]
    [Description("A visual drop indicator appears while dragging a block")]
    public async Task BlockDragDrop_DragIndicator_ShowsDuringDrag()
    {
        var page = await OpenNotionEditorAsync();
        var blocks = Blocks(page);
        var count = await blocks.CountAsync();
        if (count < 2)
        {
            Assert.Inconclusive("Need at least 2 blocks to test drag indicator");
            return;
        }

        // Verify no indicator is present before drag
        var indicatorBefore = page.Locator(".tm-notion-drop-indicator");
        var indicatorCountBefore = await indicatorBefore.CountAsync();
        if (indicatorCountBefore > 0)
        {
            var visibleBefore = await indicatorBefore.First.IsVisibleAsync();
            Assert.IsFalse(visibleBefore, "Drop indicator should not be visible before drag");
        }

        // Simulate dragstart + dragover to trigger the indicator, but no drop
        await page.EvaluateAsync(
            """
            () => {
                const blocks = Array.from(document.querySelectorAll('[data-notion-block]'));
                const srcBlock = blocks[0];
                const tgtBlock = blocks[1];
                if (!srcBlock || !tgtBlock) return;

                const handle = srcBlock.querySelector('[data-notion-drag-handle]');
                if (!handle) return;

                const container = document.querySelector('[data-page-blocks]');
                if (!container) return;

                const rect = tgtBlock.getBoundingClientRect();
                const clientY = rect.bottom - 2;
                const clientX = rect.left + rect.width / 2;

                function makeDt() {
                    const dt = new DataTransfer();
                    if (typeof dt.setDragImage !== 'function') {
                        dt.setDragImage = function() {};
                    }
                    return dt;
                }

                // DragStart – from handle
                handle.dispatchEvent(new DragEvent('dragstart', {
                    bubbles: true, cancelable: true, dataTransfer: makeDt()
                }));

                // DragOver – from target block
                tgtBlock.dispatchEvent(new DragEvent('dragover', {
                    bubbles: true, cancelable: true,
                    clientX, clientY, dataTransfer: makeDt()
                }));
            }
            """);

        // The drop indicator should now be visible
        await page.WaitForTimeoutAsync(300);
        var indicator = page.Locator(".tm-notion-drop-indicator");
        var isIndicatorVisible = await indicator.IsVisibleAsync().ContinueWith(t => t.IsCompletedSuccessfully && t.Result);
        Assert.IsTrue(isIndicatorVisible, "Drop indicator should be visible during drag-over");

        // Fire dragend to clean up
        await page.EvaluateAsync(
            """
            () => {
                const blocks = Array.from(document.querySelectorAll('[data-notion-block]'));
                const srcBlock = blocks[0];
                if (!srcBlock) return;
                const handle = srcBlock.querySelector('[data-notion-drag-handle]');
                if (!handle) return;

                function makeDt() {
                    const dt = new DataTransfer();
                    if (typeof dt.setDragImage !== 'function') {
                        dt.setDragImage = function() {};
                    }
                    return dt;
                }

                handle.dispatchEvent(new DragEvent('dragend', {
                    bubbles: true, cancelable: true, dataTransfer: makeDt()
                }));
            }
            """);
        await page.WaitForTimeoutAsync(300);

        // Indicator should be hidden after dragend
        var isIndicatorVisibleAfter = await indicator.IsVisibleAsync().ContinueWith(t => t.IsCompletedSuccessfully && t.Result);
        Assert.IsFalse(isIndicatorVisibleAfter, "Drop indicator should be hidden after dragend");

        await TakeScreenshotAsync(page, "drag_drop_indicator");
    }
}

[TestClass]
[DoNotParallelize]
public class NotionDragDropRecoveryE2ETests : NotionE2ETestBase
{
    private const string TopListSelector = ".tm-notion-page__blocks > .tm-notion-block-list";
    private const string LeftColumnListSelector = ".tm-notion-block-list[data-parent-block-id='eb160000-0000-0000-0000-000000000011']";
    private const string RightColumnListSelector = ".tm-notion-block-list[data-parent-block-id='eb160000-0000-0000-0000-000000000012']";

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB16: drag indicator, Escape cancel, and top-level reorder capture deterministic UX baselines.")]
    public async Task EB16_DragIndicatorEscapeAndTopLevelReorder_CapturesBaselines()
    {
        var page = await OpenNotionEditorAsync();
        await SeedDragDropPageAsync();

        await BeginDragOverAsync(
            page,
            "[data-block-id='eb160000-0000-0000-0000-000000000002']",
            "[data-block-id='eb160000-0000-0000-0000-000000000003']",
            dropBelow: true);

        await Assertions.Expect(page.Locator(".tm-notion-drop-indicator")).ToBeVisibleAsync();
        await CaptureBaselineAsync("drag-drop", "drag-indicator", page.Locator("body"));

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-notion-drop-indicator")).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        var draggingCount = await page.Locator(".tm-notion-dragging").CountAsync();
        Assert.AreEqual(0, draggingCount, "Escape should cancel the drag visual state without reordering blocks.");

        await DragDropAsync(
            page,
            "[data-block-id='eb160000-0000-0000-0000-000000000002']",
            "[data-block-id='eb160000-0000-0000-0000-000000000003']",
            dropBelow: true);

        await Assertions.Expect(page.Locator($"{TopListSelector} > [data-block-id='eb160000-0000-0000-0000-000000000002']")).ToBeVisibleAsync();
        var topOrder = await ReadDirectBlockIdsAsync(page, TopListSelector);
        Assert.IsTrue(
            Array.IndexOf(topOrder, "eb160000-0000-0000-0000-000000000003") < Array.IndexOf(topOrder, "eb160000-0000-0000-0000-000000000002"),
            $"Bravo should move below Alpha. Actual order: {string.Join(", ", topOrder)}");

        await CaptureBaselineAsync("drag-drop", "top-level-reordered", page.Locator(".tm-notion-page").First);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB16: moving blocks into a column, across columns, and out of a column captures production drag/drop states.")]
    public async Task EB16_ColumnMoves_CapturesIntoOutAndCrossColumnBaselines()
    {
        var page = await OpenNotionEditorAsync();
        await SeedDragDropPageAsync();

        await DragDropAsync(
            page,
            "[data-block-id='eb160000-0000-0000-0000-000000000003']",
            "[data-block-id='eb160000-0000-0000-0000-000000000101']",
            dropBelow: false);
        await Assertions.Expect(page.Locator($"{LeftColumnListSelector} > [data-block-id='eb160000-0000-0000-0000-000000000003']")).ToBeVisibleAsync();
        await CaptureBaselineAsync("drag-drop", "moved-into-column", page.Locator("[data-block-id='eb160000-0000-0000-0000-000000000010']").First);

        await DragDropAsync(
            page,
            "[data-block-id='eb160000-0000-0000-0000-000000000003']",
            "[data-block-id='eb160000-0000-0000-0000-000000000201']",
            dropBelow: true);
        await Assertions.Expect(page.Locator($"{RightColumnListSelector} > [data-block-id='eb160000-0000-0000-0000-000000000003']")).ToBeVisibleAsync();
        await CaptureBaselineAsync("drag-drop", "cross-column-moved", page.Locator("[data-block-id='eb160000-0000-0000-0000-000000000010']").First);

        await DragDropAsync(
            page,
            "[data-block-id='eb160000-0000-0000-0000-000000000003']",
            "[data-block-id='eb160000-0000-0000-0000-000000000020']",
            dropBelow: false);
        await Assertions.Expect(page.Locator($"{TopListSelector} > [data-block-id='eb160000-0000-0000-0000-000000000003']")).ToBeVisibleAsync();
        await CaptureBaselineAsync("drag-drop", "moved-out-of-column", page.Locator(".tm-notion-page").First);
    }

    private static async Task BeginDragOverAsync(IPage page, string sourceSelector, string targetSelector, bool dropBelow)
    {
        await page.EvaluateAsync(
            """
            ({ sourceSelector, targetSelector, dropBelow }) => {
                const source = document.querySelector(sourceSelector);
                const target = document.querySelector(targetSelector);
                if (!source || !target) throw new Error(`Missing drag source or target: ${sourceSelector} -> ${targetSelector}`);

                const handle = source.querySelector('[data-notion-drag-handle]');
                if (!handle) throw new Error(`Missing drag handle for ${sourceSelector}`);

                const rect = target.getBoundingClientRect();
                const clientY = dropBelow ? rect.bottom - 2 : rect.top + 2;
                const clientX = rect.left + rect.width / 2;
                const dataTransfer = new DataTransfer();
                if (typeof dataTransfer.setDragImage !== 'function') dataTransfer.setDragImage = function() {};

                handle.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer }));
                target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, clientX, clientY, dataTransfer }));
            }
            """,
            new { sourceSelector, targetSelector, dropBelow });
        await page.WaitForTimeoutAsync(250);
    }

    private static async Task DragDropAsync(IPage page, string sourceSelector, string targetSelector, bool dropBelow)
    {
        await page.EvaluateAsync(
            """
            ({ sourceSelector, targetSelector, dropBelow }) => {
                const source = document.querySelector(sourceSelector);
                const target = document.querySelector(targetSelector);
                if (!source || !target) throw new Error(`Missing drag source or target: ${sourceSelector} -> ${targetSelector}`);

                const handle = source.querySelector('[data-notion-drag-handle]');
                if (!handle) throw new Error(`Missing drag handle for ${sourceSelector}`);

                const rect = target.getBoundingClientRect();
                const clientY = dropBelow ? rect.bottom - 2 : rect.top + 2;
                const clientX = rect.left + rect.width / 2;
                const dataTransfer = new DataTransfer();
                if (typeof dataTransfer.setDragImage !== 'function') dataTransfer.setDragImage = function() {};

                handle.dispatchEvent(new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer }));
                target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, clientX, clientY, dataTransfer }));
                target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, clientX, clientY, dataTransfer }));
                handle.dispatchEvent(new DragEvent('dragend', { bubbles: true, cancelable: true, dataTransfer }));
            }
            """,
            new { sourceSelector, targetSelector, dropBelow });
        await page.WaitForTimeoutAsync(900);
    }

    private static async Task<string[]> ReadDirectBlockIdsAsync(IPage page, string listSelector)
    {
        return await page.EvaluateAsync<string[]>(
            """
            selector => Array.from(document.querySelector(selector)?.children ?? [])
                .filter(child => child.matches?.('[data-notion-block]'))
                .map(child => child.getAttribute('data-block-id') || '')
            """,
            listSelector);
    }
}
