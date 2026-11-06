using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// End-to-end coverage for the Markdown preview toggle on TmNotionCodeBlock.
/// Runs against the self-hosted HTTPS demo API (5100) and HTTPS demo WASM (7106).
/// </summary>
[TestClass]
public class NotionCodeBlockMarkdownPreviewE2ETests : NotionE2ETestBase
{
    private const string TableMarkdown = "| Name | Status |\n| :--- | ---: |\n| CF26 | Ready |\n| CF27 | Draft |";

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("A Markdown code block exposes a preview toggle that renders a GFM table, and toggling back keeps the source")]
    public async Task MarkdownCodeBlock_PreviewTogglesAndRendersTable()
    {
        var page = await OpenNotionEditorAsync();
        var block = await CreateMarkdownCodeBlockAsync(page, TableMarkdown);

        var toggle = block.Locator("[data-testid='notion-code-preview-toggle']");
        await toggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual("false", await toggle.GetAttributeAsync("aria-pressed"), "The block must open in editor mode.");
        await CaptureBaselineAsync("code-markdown-preview", "editor-mode");

        await toggle.ClickAsync();
        var preview = block.Locator("[data-testid='notion-code-preview']");
        await preview.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        Assert.AreEqual("true", await toggle.GetAttributeAsync("aria-pressed"));
        Assert.AreEqual(1, await preview.Locator("table").CountAsync(), "Preview should render exactly one table.");
        Assert.AreEqual(2, await preview.Locator("th").CountAsync(), "Preview table should have two header cells.");
        Assert.AreEqual(4, await preview.Locator("td").CountAsync(), "Preview table should have four body cells.");
        StringAssert.Contains(await preview.InnerTextAsync(), "CF27");
        Assert.AreEqual(0, await block.Locator("textarea.tm-notion-code-block__content").CountAsync(), "Editor textarea must be hidden while previewing.");

        // The separator row said ":--- | ---:", so the second column must render right-aligned.
        Assert.AreEqual("left", await preview.Locator("th").Nth(0).EvaluateAsync<string>("el => getComputedStyle(el).textAlign"));
        Assert.AreEqual("right", await preview.Locator("th").Nth(1).EvaluateAsync<string>("el => getComputedStyle(el).textAlign"));
        Assert.AreEqual("right", await preview.Locator("td").Nth(1).EvaluateAsync<string>("el => getComputedStyle(el).textAlign"));

        await CaptureBaselineAsync("code-markdown-preview", "preview-table-light", block);

        await SetThemeAsync(page, dark: true);
        await CaptureBaselineAsync("code-markdown-preview", "preview-table-dark", block);
        await SetThemeAsync(page, dark: false);

        // Toggling back must restore the editor with the original source intact.
        await toggle.ClickAsync();
        var textarea = block.Locator("textarea.tm-notion-code-block__content");
        await textarea.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        StringAssert.Contains(await textarea.InputValueAsync(), "| CF26 | Ready |");

        TestContext.WriteLine("UX: the toggle sits next to Copy in the block header, reads Preview/Edit, and carries aria-pressed. The rendered table reuses the same border and header shading as TmNotionTableBlock, so a previewed table is visually continuous with a real table block. Dark mode inherits --tm-* tokens.");
    }

    [TestMethod]
    [Description("The preview toggle only exists for the Markdown language")]
    public async Task CodeBlock_PreviewToggleHiddenForOtherLanguages()
    {
        var page = await OpenNotionEditorAsync();
        var block = await InsertCodeBlockAsync(page, "console.log(1);");

        // Default language is Plain Text — no toggle.
        Assert.AreEqual(0, await block.Locator("[data-testid='notion-code-preview-toggle']").CountAsync());

        await SelectLanguageAsync(block, "JavaScript");
        Assert.AreEqual(0, await block.Locator("[data-testid='notion-code-preview-toggle']").CountAsync(), "JavaScript must not offer a Markdown preview.");

        await SelectLanguageAsync(block, "Markdown");
        await block.Locator("[data-testid='notion-code-preview-toggle']").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
    }

    [TestMethod]
    [Description("Switching away from Markdown while previewing drops the preview instead of leaving stale HTML")]
    public async Task MarkdownCodeBlock_LeavingMarkdownClosesPreview()
    {
        var page = await OpenNotionEditorAsync();
        var block = await CreateMarkdownCodeBlockAsync(page, TableMarkdown);

        await block.Locator("[data-testid='notion-code-preview-toggle']").ClickAsync();
        await block.Locator("[data-testid='notion-code-preview']").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        await SelectLanguageAsync(block, "Python");

        await block.Locator("[data-testid='notion-code-preview']").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 10000
        });
        Assert.AreEqual(0, await block.Locator("[data-testid='notion-code-preview-toggle']").CountAsync());
        Assert.AreEqual(1, await block.Locator("textarea.tm-notion-code-block__content").CountAsync());
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Edge cases: empty markdown and an invalid table render an empty or degraded preview without crashing")]
    public async Task MarkdownCodeBlock_EmptyAndInvalidMarkdownDoNotCrash()
    {
        var page = await OpenNotionEditorAsync();

        var empty = await CreateMarkdownCodeBlockAsync(page, string.Empty);
        await empty.Locator("[data-testid='notion-code-preview-toggle']").ClickAsync();
        var emptyPreview = empty.Locator("[data-testid='notion-code-preview']");
        await emptyPreview.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual(string.Empty, (await emptyPreview.InnerTextAsync()).Trim(), "Empty markdown should render an empty preview.");

        var broken = await CreateMarkdownCodeBlockAsync(page, "| broken | table\nnot a separator\n| x |");
        await broken.Locator("[data-testid='notion-code-preview-toggle']").ClickAsync();
        var brokenPreview = broken.Locator("[data-testid='notion-code-preview']");
        await brokenPreview.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual(0, await brokenPreview.Locator("table").CountAsync(), "An invalid delimiter row must not produce a table.");
        StringAssert.Contains(await brokenPreview.InnerTextAsync(), "broken");

        await CaptureBaselineAsync("code-markdown-preview", "edge-empty-and-invalid");
        TestContext.WriteLine("UX: an empty preview collapses to a thin band rather than an empty box with borders; a malformed table degrades to plain paragraphs, so no content is silently lost.");
    }

    [TestMethod]
    [Description("Markdown containing script or javascript: links renders inert in the preview")]
    public async Task MarkdownCodeBlock_PreviewIsSanitized()
    {
        var page = await OpenNotionEditorAsync();
        var block = await CreateMarkdownCodeBlockAsync(page, "<script>window.__pwned = true;</script>\n\n[x](javascript:window.__pwned = true)");

        await block.Locator("[data-testid='notion-code-preview-toggle']").ClickAsync();
        var preview = block.Locator("[data-testid='notion-code-preview']");
        await preview.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        Assert.AreEqual(0, await preview.Locator("script").CountAsync(), "No script element may reach the preview.");
        var html = await preview.InnerHTMLAsync();
        StringAssert.DoesNotMatch(html, new System.Text.RegularExpressions.Regex("javascript:", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        Assert.IsNull(await page.EvaluateAsync<bool?>("() => window.__pwned ?? null"), "Preview markup must not execute.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<ILocator> CreateMarkdownCodeBlockAsync(IPage page, string markdown)
    {
        var block = await InsertCodeBlockAsync(page, markdown);
        await SelectLanguageAsync(block, "Markdown");
        return block;
    }

    private static async Task<ILocator> InsertCodeBlockAsync(IPage page, string code)
    {
        var block = await InsertBlockViaSlashMenuAsync(page, "code");

        var codeArea = block.Locator("textarea.tm-notion-code-block__content");
        await codeArea.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await codeArea.ClickAsync();
        if (!string.IsNullOrEmpty(code))
        {
            await codeArea.FillAsync(code);
        }

        // Blur so the code is committed to the block content before any toggle. Blur via JS rather
        // than a page click: as code blocks highlight in, the page shifts under a positional click.
        await page.Keyboard.PressAsync("Escape");
        await page.EvaluateAsync("() => document.activeElement?.blur()");
        await page.WaitForTimeoutAsync(400);

        return block;
    }

    private static async Task SelectLanguageAsync(ILocator block, string language)
    {
        var select = block.Locator("select.tm-notion-code-block__lang-select");
        await select.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await select.SelectOptionAsync(language);
        await select.Page.WaitForTimeoutAsync(400);
    }

    private static async Task<ILocator> InsertBlockViaSlashMenuAsync(IPage page, string searchTerm)
    {
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").Last;
        await para.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await para.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(1000);

        await page.Keyboard.TypeAsync("/");
        await page.WaitForSelectorAsync(".tm-notion-slash", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 8000
        });

        var searchInput = page.Locator(".tm-notion-slash__input");
        await searchInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await searchInput.FillAsync(searchTerm);
        await page.WaitForTimeoutAsync(400);

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

    private static async Task SetThemeAsync(IPage page, bool dark)
    {
        await page.EvaluateAsync(
            """
            dark => {
                if (dark) {
                    document.documentElement.setAttribute('data-theme', 'dark');
                    document.body.classList.add('tm-dark');
                } else {
                    document.documentElement.removeAttribute('data-theme');
                    document.body.classList.remove('tm-dark');
                }
            }
            """,
            dark);
        await page.WaitForTimeoutAsync(250);
    }
}
