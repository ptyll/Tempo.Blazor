using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E accessibility and screenshot checks for phase 10 diagram toolbox UX.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class DiagramToolboxAccessibilityPhase10E2ETests : PlaywrightTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";
    private const string DecisionStencilId = "tempo-flowchart.decision";

    /// <inheritdoc />
    protected override string BaseUrl => "http://localhost:5010";

    [TestMethod]
    [Description("Toolbox supports localized labels, keyboard-only stencil insertion, and desktop/mobile screenshots")]
    public async Task Toolbox_KeyboardInsertAndScreenshots_WorkOnDesktopAndMobile()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();

        await page.SetViewportSizeAsync(1600, 900);
        await OpenDiagramEditorAsync(page, requireCanvas: true);
        await ResetDocumentAsync(page);
        await SearchDecisionStencilAsync(page);

        var item = await GetDecisionStencilAsync(page);
        Assert.AreEqual("Insert Decision onto the canvas", await item.GetAttributeAsync("aria-label"));
        Assert.AreEqual("Drag Decision onto the canvas", await item.GetAttributeAsync("title"));
        Assert.AreEqual("true", await item.Locator(".tm-diagram-toolbox__label").GetAttributeAsync("aria-hidden"));

        await FocusDecisionStencilWithKeyboardAsync(page);
        await ExpectFocusRingAsync(page);
        await page.Keyboard.PressAsync("Enter");
        await WaitForStencilCountAsync(page, DecisionStencilId, 1);
        await AssertReadableDecisionNodeAsync(page);
        await AssertToolboxFitsViewportAsync(page);
        await SaveStableScreenshotAsync(page, "desktop");
        await TakeScreenshotAsync(page, "diagram_toolbox_phase10_keyboard_desktop");

        await page.SetViewportSizeAsync(430, 900);
        await OpenDiagramEditorAsync(page, requireCanvas: false);
        // X1 (redesign review phase 6): the mobile Shapes sheet starts CLOSED, so the toolbox is
        // not visible at load. Open the sheet through the action bar's Shapes tile — the one
        // reopen path a user has — before asserting on the toolbox.
        await page.Locator(".tm-mobile-action-bar__action").Filter(new() { HasText = "Shapes" }).First
            .ClickAsync();
        await page.Locator(".tm-editor-shell__sheet").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.Locator(".tm-diagram-toolbox").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await SearchDecisionStencilAsync(page);
        await AssertToolboxFitsViewportAsync(page);
        await SaveStableScreenshotAsync(page, "mobile");
        await TakeScreenshotAsync(page, "diagram_toolbox_phase10_mobile");
    }

    private static async Task OpenDiagramEditorAsync(IPage page, bool requireCanvas)
    {
        await page.GotoAsync("http://localhost:5010" + DiagramEditorUrl);
        // The editor root renders in every band; the toolbox does NOT (on mobile it lives in the
        // closed-at-load panels sheet), so only the root is awaited here.
        await page.WaitForSelectorAsync(".tm-diagram-editor", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });

        if (!requireCanvas)
        {
            await page.WaitForTimeoutAsync(300);
            return;
        }

        await page.WaitForSelectorAsync(".tm-diagram-canvas", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });
        await page.WaitForSelectorAsync(".tm-diagram-toolbox", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });
        await page.WaitForTimeoutAsync(300);
    }

    private static async Task ResetDocumentAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New document" }).ClickAsync();
        await page.WaitForFunctionAsync(
            "stencilId => document.querySelectorAll(`.tm-diagram-node[data-stencil-id='${stencilId}']`).length === 0",
            DecisionStencilId,
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static async Task SearchDecisionStencilAsync(IPage page)
    {
        var search = page.GetByLabel("Search diagram stencils");
        await search.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await search.FillAsync("Decision");
        await page.WaitForTimeoutAsync(300);
    }

    private static async Task<ILocator> GetDecisionStencilAsync(IPage page)
    {
        var item = page.Locator($".tm-diagram-toolbox__item[data-stencil-id='{DecisionStencilId}']").First;
        await item.ScrollIntoViewIfNeededAsync();
        await item.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        return item;
    }

    private static async Task FocusDecisionStencilWithKeyboardAsync(IPage page)
    {
        await page.GetByLabel("Search diagram stencils").FocusAsync();

        for (var index = 0; index < 12; index++)
        {
            await page.Keyboard.PressAsync("Tab");
            var activeStencilId = await page.EvaluateAsync<string?>(
                "() => document.activeElement?.getAttribute('data-stencil-id')");
            if (string.Equals(activeStencilId, DecisionStencilId, StringComparison.Ordinal))
            {
                return;
            }
        }

        Assert.Fail("Keyboard Tab navigation should reach the visible Decision stencil.");
    }

    private static async Task ExpectFocusRingAsync(IPage page)
    {
        var outline = await page.EvaluateAsync<string>(
            """
            () => {
                const active = document.activeElement;
                if (!active || !active.classList.contains('tm-diagram-toolbox__item')) {
                    return '';
                }

                return getComputedStyle(active).outlineStyle;
            }
            """);
        Assert.AreNotEqual("none", outline, "Focused stencil item should expose a visible focus ring.");
    }

    private static async Task WaitForStencilCountAsync(IPage page, string stencilId, int minimumCount)
    {
        await page.WaitForFunctionAsync(
            "args => document.querySelectorAll(`.tm-diagram-node[data-stencil-id='${args.stencilId}']`).length >= args.minimumCount",
            new { stencilId, minimumCount },
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private static async Task AssertReadableDecisionNodeAsync(IPage page)
    {
        var node = page.Locator($".tm-diagram-node[data-stencil-id='{DecisionStencilId}']").Last;
        await node.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        var text = await node.TextContentAsync() ?? string.Empty;
        StringAssert.Contains(text, "Decision");
    }

    private static async Task AssertToolboxFitsViewportAsync(IPage page)
    {
        var result = await page.EvaluateAsync<ToolboxFitResult>(
            """
            () => {
                const toolbox = document.querySelector('.tm-diagram-toolbox');
                const editor = document.querySelector('.tm-diagram-editor');
                const body = editor?.querySelector('.tm-editor-shell__main');
                const rect = toolbox?.getBoundingClientRect();
                return {
                    hasToolbox: !!toolbox,
                    hasBody: !!body,
                    width: rect?.width ?? 0,
                    left: rect?.left ?? 0,
                    right: rect?.right ?? 0,
                    viewportWidth: window.innerWidth,
                    editorScrollWidth: editor?.scrollWidth ?? 0,
                    editorClientWidth: editor?.clientWidth ?? 0,
                    bodyScrollWidth: body?.scrollWidth ?? 0,
                    bodyClientWidth: body?.clientWidth ?? 0
                };
            }
            """);

        Assert.IsTrue(result.HasToolbox, "Toolbox should be rendered.");
        Assert.IsTrue(result.Width > 120, "Toolbox should remain usable and not collapse to an unreadable rail.");
        Assert.IsTrue(result.Left >= 0, "Toolbox should not render outside the left viewport edge.");
        Assert.IsTrue(result.Right <= result.ViewportWidth + 2, "Toolbox should fit inside the visible viewport.");
        Assert.IsTrue(result.HasBody, "Editor shell main region should be rendered (selector must not go stale).");
        Assert.IsTrue(result.EditorScrollWidth <= result.EditorClientWidth + 2, "Diagram editor should not introduce horizontal overflow.");
        Assert.IsTrue(result.BodyScrollWidth <= result.BodyClientWidth + 2, "Diagram body should not introduce horizontal overflow.");
    }

    private static async Task SaveStableScreenshotAsync(IPage page, string suffix)
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "TestResults",
            "phase10-accessibility");
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        await File.WriteAllBytesAsync(Path.Combine(directory, $"diagram_toolbox_phase10_{suffix}.png"), bytes);
    }

    public sealed class ToolboxFitResult
    {
        public bool HasToolbox { get; set; }

        public bool HasBody { get; set; }

        public double Width { get; set; }

        public double Left { get; set; }

        public double Right { get; set; }

        public double ViewportWidth { get; set; }

        public double EditorScrollWidth { get; set; }

        public double EditorClientWidth { get; set; }

        public double BodyScrollWidth { get; set; }

        public double BodyClientWidth { get; set; }
    }
}
