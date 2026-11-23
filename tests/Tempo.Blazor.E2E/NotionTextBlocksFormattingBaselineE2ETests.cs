using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// EB1 screenshot recovery coverage for Notion text blocks and inline formatting.
/// </summary>
[TestClass]
public class NotionTextBlocksFormattingBaselineE2ETests : NotionE2ETestBase
{
    private const string FormattingBlockId = "eb100000-0000-0000-0000-000000000003";
    private const string LongLineBlockId = "eb100000-0000-0000-0000-000000000004";
    private const string Heading1BlockId = "eb100000-0000-0000-0000-000000000005";
    private const string Heading2BlockId = "eb100000-0000-0000-0000-000000000006";
    private const string Heading3BlockId = "eb100000-0000-0000-0000-000000000007";
    private const string QuoteBlockId = "eb100000-0000-0000-0000-000000000008";
    private const string CalloutBlockId = "eb100000-0000-0000-0000-000000000009";
    private const string DividerBlockId = "eb100000-0000-0000-0000-000000000010";
    private const string CodeBlockId = "eb100000-0000-0000-0000-000000000011";

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB1: captures desktop and mobile baselines for heading, quote, callout, code, and divider text blocks")]
    public async Task EB1_TextBlocks_DesktopAndMobile_CaptureBaselineScreenshots()
    {
        await SetViewportAsync(1280, 720);
        var page = await OpenNotionEditorAsync();
        await SeedTextFormattingPageAsync();

        foreach (var block in TextBlockStates("desktop"))
        {
            await CaptureVisibleBlockAsync(block.Area, block.State, block.BlockId, block.RegionSelector);
        }

        await CaptureBaselineAsync("text-blocks", "ux-review-checkpoint", page.Locator(".tm-notion-page").First);

        await SetViewportAsync(390, 844);
        page = await OpenNotionEditorAsync();
        await SeedTextFormattingPageAsync();
        await AssertNoHorizontalOverflowAsync(page, ".tm-notion-editor", "EB1 mobile editor");

        foreach (var block in TextBlockStates("mobile"))
        {
            await CaptureVisibleBlockAsync(block.Area, block.State, block.BlockId, block.RegionSelector);
        }
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB1: captures long-line wrapping, active inline toolbar states, and code language/tab/overflow baselines")]
    public async Task EB1_TextFormattingAndCodeEdges_CaptureBaselineScreenshots()
    {
        await SetViewportAsync(1280, 720);
        var page = await OpenNotionEditorAsync();
        await SeedTextFormattingPageAsync();

        var longLineBlock = page.Locator($"[data-block-id='{LongLineBlockId}']").First;
        await CenterLocatorAsync(longLineBlock);
        await AssertNoHorizontalOverflowAsync(page, ".tm-notion-editor", "EB1 long-line editor");
        await CaptureBaselineAsync("text-blocks", "long-unbroken-line", longLineBlock);

        await OpenNotionInlineToolbarForBlockAsync(page, FormattingBlockId, ".tm-notion-paragraph");
        await SelectInlineCodeContentsAsync(page);
        var activeToolbarButtons = page.Locator(".tm-notion-inline-toolbar__btn--active");
        await WaitForPredicateAsync(page, "() => document.querySelectorAll('.tm-notion-inline-toolbar__btn--active').length >= 5", 5000);
        Assert.IsTrue(await activeToolbarButtons.CountAsync() >= 5, "Combined formatted text should activate bold, italic, underline, strikethrough, and inline code toolbar buttons.");
        foreach (var title in new[] { "Bold", "Italic", "Underline", "Strikethrough", "Inline code" })
        {
            Assert.AreEqual(1, await page.Locator($".tm-notion-inline-toolbar__btn--active[title='{title}']").CountAsync(), $"{title} toolbar button should be active.");
        }
        await CaptureBaselineAsync("text-formatting", "combined-toolbar-active", page.Locator(".tm-notion-inline-toolbar").First);

        var codeBlock = page.Locator($"[data-block-id='{CodeBlockId}'] .tm-notion-code-block").First;
        await CenterLocatorAsync(codeBlock);
        var languageSelect = codeBlock.Locator(".tm-notion-code-block__lang-select").First;
        await languageSelect.SelectOptionAsync("TypeScript");

        var code = codeBlock.Locator(".tm-notion-code-block__content").First;
        await code.FillAsync("""
const description = "This deliberately long TypeScript line should remain inside the code block scroll area without widening the Notion editor shell or clipping the copy button.";
function renderBaseline() {
    return description.repeat(2);
}
""");
        await code.FocusAsync();
        await code.EvaluateAsync("el => el.setSelectionRange(0, 0)");
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForTimeoutAsync(200);
        var codeValue = await code.InputValueAsync();
        Assert.IsTrue(codeValue.StartsWith("    const", StringComparison.Ordinal), "Tab should insert four spaces at the start of the focused code line.");
        var hasHorizontalScroll = await code.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 2");
        Assert.IsTrue(hasHorizontalScroll, "Long code line should create horizontal scroll inside the code textarea.");

        await AssertNoHorizontalOverflowAsync(page, ".tm-notion-editor", "EB1 code editor");
        await CaptureBaselineAsync("text-blocks", "code-language-tab-long-scroll", codeBlock);
    }

    private static IEnumerable<(string Area, string State, string BlockId, string RegionSelector)> TextBlockStates(string viewport)
    {
        yield return ("text-blocks", $"heading1-{viewport}", Heading1BlockId, ".tm-notion-heading--h1");
        yield return ("text-blocks", $"heading2-{viewport}", Heading2BlockId, ".tm-notion-heading--h2");
        yield return ("text-blocks", $"heading3-{viewport}", Heading3BlockId, ".tm-notion-heading--h3");
        yield return ("text-blocks", $"quote-{viewport}", QuoteBlockId, ".tm-notion-quote");
        yield return ("text-blocks", $"callout-{viewport}", CalloutBlockId, ".tm-notion-callout");
        yield return ("text-blocks", $"code-{viewport}", CodeBlockId, ".tm-notion-code-block");
        yield return ("text-blocks", $"divider-{viewport}", DividerBlockId, ".tm-notion-divider-block");
    }

    private async Task CaptureVisibleBlockAsync(string area, string state, string blockId, string regionSelector)
    {
        var block = Page.Locator($"[data-block-id='{blockId}']").First;
        await block.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await CenterLocatorAsync(block);

        var region = Page.Locator($"[data-block-id='{blockId}'] {regionSelector}").First;
        await region.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await CaptureBaselineAsync(area, state, region);
    }

    private static async Task CenterLocatorAsync(ILocator locator)
    {
        await locator.EvaluateAsync("el => el.scrollIntoView({ block: 'center', inline: 'nearest' })");
    }

    private static async Task SelectInlineCodeContentsAsync(IPage page)
    {
        await page.Locator($"[data-block-id='{FormattingBlockId}'] code").First.EvaluateAsync(
            """
            el => {
                const range = document.createRange();
                range.selectNodeContents(el);
                const selection = window.getSelection();
                selection.removeAllRanges();
                selection.addRange(range);
                document.dispatchEvent(new Event('selectionchange'));
            }
            """);
    }

    private static async Task AssertNoHorizontalOverflowAsync(IPage page, string selector, string label)
    {
        var overflow = await page.Locator(selector).First.EvaluateAsync<double>(
            "el => Math.max(0, el.scrollWidth - el.clientWidth)");
        Assert.IsTrue(overflow <= 2, $"{label} should not horizontally overflow its own shell. Overflow={overflow}.");
    }
}
