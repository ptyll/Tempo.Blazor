using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Shared deterministic-parking helper for the CF3 (plan 98c80bc3) Notion editor E2Es: the
/// slash menu anchors to the caret, so tests that prove "Enter at the fold still shows the
/// menu" must first place the caret block at a KNOWN position — flush with the fold.
/// </summary>
internal static class NotionEditorTestHelper
{
    /// <summary>
    /// Parks the LAST paragraph deterministically flush with the fold — its bottom edge lands
    /// at <c>innerHeight − 2</c> — and returns the viewport height. The nearest scrollable
    /// ancestor moves (the same walk the editor's own caret-reserve scroll does); the window
    /// moves when no inner scroller exists. The landed bottom is asserted here, so the
    /// pre-Enter state is a fact, not a hope.
    /// </summary>
    internal static async Task<int> ParkLastParagraphAtTheFoldAsync(IPage page)
    {
        var innerHeight = await page.EvaluateAsync<int>("() => window.innerHeight");
        var bottom = await page.EvaluateAsync<double>(
            @"
() => {
    const para = [...document.querySelectorAll("".tm-notion-paragraph[contenteditable='true']"")].pop();
    if (!para) return -1;
    let node = para.parentElement;
    let scroller = null;
    while (node && node !== document.body) {
        const style = getComputedStyle(node);
        // Same walk the editor's own caret-reserve scroll does: a scroll-styled wrapper
        // whose content never overflows must not win — only a live scrollbar scrolls.
        if (/(auto|scroll)/.test(style.overflowY || '') && node.clientHeight > 0
            && node.scrollHeight > node.clientHeight + 1) { scroller = node; break; }
        node = node.parentElement;
    }
    const target = window.innerHeight - 2;
    const delta = para.getBoundingClientRect().bottom - target;
    if (scroller && typeof scroller.scrollBy === 'function') scroller.scrollBy({ top: delta, behavior: 'instant' });
    else window.scrollBy({ top: delta, behavior: 'instant' });
    return para.getBoundingClientRect().bottom;
}");
        Assert.IsTrue(Math.Abs(bottom - (innerHeight - 2)) <= 1.5,
            $"precondition: the last paragraph sits flush with the fold (bottom {bottom}, expected {innerHeight - 2})");
        return innerHeight;
    }
}
