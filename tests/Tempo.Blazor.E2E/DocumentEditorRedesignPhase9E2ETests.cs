using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 9 (the final UX pass): the polish items the phase-8 review carried forward, asserted on the live page - the 320 px
/// action-bar tiles keep every label in one piece, the header kebab sheet is a column of equal rows, the phone diff switch owns its row, a tablet's
/// side sheet reads its tabs as text, and the command palette goes fullscreen for a pinned phone layout on a wide screen.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[TestCategory("DocumentEditor:ResponsiveShell")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase9E2ETests : WasmTestBase
{
    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("At 320 and 390 every action-bar label is ONE unbroken word (no mid-word wrap) and no tile overflows its box")]
    public async Task Phone_ActionBarLabels_AreNotBrokenMidWord()
    {
        foreach (var (width, height) in new[] { (320, 640), (360, 740), (390, 844) })
        {
            var page = await OpenAsync(RevisionsDocumentId, width, height, touch: true);
            var labels = await page.Locator("[data-testid='document-mobile-action-bar'] .tm-mobile-action-bar__label-text").EvaluateAllAsync<string[]>(
                """
                els => els.map(el => {
                    // a single word that wraps shows as more than one line box; a multi-word label may use two lines but never splits a word
                    const range = document.createRange();
                    const words = el.textContent.trim().split(/\s+/);
                    const node = el.firstChild;
                    let text = node.textContent;
                    let offset = 0;
                    const broken = [];
                    for (const word of words) {
                        const start = text.indexOf(word, offset);
                        range.setStart(node, start);
                        range.setEnd(node, start + word.length);
                        const rects = Array.from(range.getClientRects()).map(r => Math.round(r.top));
                        if (new Set(rects).size > 1) broken.push(word);
                        offset = start + word.length;
                    }
                    return el.textContent.trim() + ':' + (broken.length ? 'BROKEN(' + broken.join(',') + ')' : 'ok') + ':' + (el.scrollWidth <= el.clientWidth + 1 ? 'fits' : 'overflow');
                })
                """);
            foreach (var label in labels)
            {
                StringAssert.Contains(label, ":ok:fits", $"{width}: '{label}'");
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The phone Home row never leaves a gap where a control fits: 320 px = B I U + alignment, 390 px = B I U + alignment + list")]
    public async Task Phone_HomeRow_FillsTheRow_At320And390()
    {
        foreach (var (width, height, withList) in new[] { (320, 640, false), (390, 844, true) })
        {
            var page = await OpenAsync("contract-demo", width, height, touch: true);
            var onBar = await page.Locator("[data-testid='document-ribbon-toolbar'] [data-testid]:not(.tm-toolbar-item--collapsed)").EvaluateAllAsync<string[]>(
                "els => els.filter(e => !e.closest('.tm-toolbar-item--collapsed') && e.getBoundingClientRect().width > 0).map(e => e.getAttribute('data-testid'))");
            foreach (var id in new[] { "document-bold", "document-italic", "document-underline", "document-align" })
            {
                CollectionAssert.Contains(onBar, id, $"{width}: {id} is on the bar");
            }

            Assert.AreEqual(withList, onBar.Contains("document-bullet-list"), $"{width}: the list is on the bar only where it fits");
            CollectionAssert.DoesNotContain(onBar, "document-strikethrough", $"{width}: strikethrough is in More on a phone");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The header kebab sheet is a column of full-width rows of one touch height - the built-in export and the host's action alike")]
    public async Task Phone_KebabSheet_RowsAreEqual()
    {
        foreach (var (width, height) in new[] { (390, 844), (320, 640) })
        {
            var page = await OpenAsync("contract-demo", width, height, touch: true);
            await page.GetByTestId("document-editor-header-slots").CheckAsync();
            await Assertions.Expect(page.GetByTestId("document-header-kebab")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByTestId("document-header-kebab").ClickAsync();
            var rows = await page.Locator("[data-testid='document-header-kebab-panel'] .tm-btn").EvaluateAllAsync<double[][]>(
                "els => els.map(e => { const r = e.getBoundingClientRect(); return [r.width, r.height]; })");
            Assert.IsTrue(rows.Length >= 2, $"{width}: the export and the host action");
            Assert.IsTrue(rows.All(r => Math.Abs(r[1] - rows[0][1]) < 1), $"{width}: equal heights ({string.Join(", ", rows.Select(r => r[1]))})");
            Assert.IsTrue(rows.All(r => Math.Abs(r[0] - rows[0][0]) < 1), $"{width}: equal widths ({string.Join(", ", rows.Select(r => r[0]))})");
            Assert.IsTrue(rows.All(r => r[1] >= 43.5), $"{width}: touch height");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("A tablet's side sheet reads its tabs as text, one row, and the tabs are finger-sized")]
    public async Task Tablet_SideSheet_HasTextTabs()
    {
        var page = await OpenAsync(RevisionsDocumentId, 1024, 768, touch: true);
        Assert.AreEqual("tablet", await LayoutAsync(page));
        await page.GetByTestId("document-side-panel-edge-toggle").ClickAsync();
        var panel = page.GetByTestId("document-side-panel");
        await Assertions.Expect(panel).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(panel).ToHaveAttributeAsync("data-tab-style", "text");
        var tab = page.GetByTestId("document-side-panel-tab-comments");
        StringAssert.Contains(await tab.InnerTextAsync(), "Comments");
        var box = await tab.BoundingBoxAsync();
        Assert.IsTrue(box!.Height >= 43.5, $"a finger-sized tab ({box.Height})");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The phone's Older / Newer / Differences switch fills the row and never truncates a label, even at 320")]
    public async Task Phone_DiffSwitch_FillsTheRow_WithoutTruncation()
    {
        var context = await CreateContextAsync(ContextOptions(1440, 900, false));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId=contract-demo", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        await DocumentEditorRedesignPhase7E2ETests.CreateTwoVersionsAsync(page);
        await page.SetViewportSizeAsync(320, 640);
        await page.WaitForFunctionAsync("() => document.querySelector('.tm-document-editor')?.dataset.layout === 'mobile'");
        await page.WaitForTimeoutAsync(500);
        await OpenInspectorTabAsync(page, "versions");
        await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Draft").GetByTestId("document-version-diff-base").ClickAsync();
        await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Final").GetByTestId("document-version-diff-compare").ClickAsync();
        await page.GetByTestId("document-version-show-diff").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var segments = page.GetByTestId("document-diff-segments");
        var row = await page.Locator(".tm-document-diff__controls").Filter(new() { Has = page.GetByTestId("document-diff-segments") }).BoundingBoxAsync();
        var box = await segments.BoundingBoxAsync();
        Assert.IsTrue(box!.Width >= row!.Width - 2, $"the switch owns the row ({box.Width} of {row.Width})");
        var truncated = await segments.Locator(".tm-segmented__option").EvaluateAllAsync<string[]>(
            "els => els.map(e => { const t = e.querySelector('span:last-child') ?? e; return e.textContent.trim() + (t.scrollWidth > t.clientWidth + 1 ? ':TRUNCATED' : ':ok'); })");
        foreach (var option in truncated)
        {
            StringAssert.EndsWith(option, ":ok", $"'{option}'");
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A phone layout pinned on a WIDE screen puts the command palette fullscreen (the rule keys on the editor layout, not on the viewport)")]
    public async Task ForcedMobileLayout_OnAWideScreen_MakesThePaletteFullscreen()
    {
        var page = await OpenAsync(RevisionsDocumentId, 1280, 800, touch: false);
        await page.GetByTestId("document-editor-layout-mode").SelectOptionAsync("Mobile");
        await page.WaitForFunctionAsync("() => document.querySelector('.tm-document-editor')?.dataset.layout === 'mobile'");
        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
        await page.Keyboard.PressAsync("Control+Shift+P");
        var palette = page.Locator(".tm-document-command-palette .tm-command-palette");
        await Assertions.Expect(palette).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var box = await palette.BoundingBoxAsync();
        Assert.IsTrue(box!.Width >= 1279 && box.Height >= 799, $"fullscreen ({box.Width} x {box.Height})");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Edge: a panel command of the palette (comments / revisions / versions) while the comparison is open closes the comparison and shows the panel beside the document")]
    public async Task Desktop_PaletteOpenPanelCommands_WhileComparing_LeaveTheComparisonAndShowThePanel()
    {
        var context = await CreateContextAsync(ContextOptions(1440, 900, false));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId=contract-demo", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        await DocumentEditorRedesignPhase7E2ETests.CreateTwoVersionsAsync(page);
        await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Draft").GetByTestId("document-version-diff-base").ClickAsync();
        await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Final").GetByTestId("document-version-diff-compare").ClickAsync();
        await page.GetByTestId("document-version-show-diff").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        foreach (var (command, tab) in new[] { ("openRevisions", "revisions"), ("openComments", "comments"), ("openVersions", "versions") })
        {
            // The page surface is hidden in the comparison, so the shortcut is pressed from where the reader's focus is: inside the diff viewer.
            await page.GetByTestId("document-diff-next").FocusAsync();
            await page.Keyboard.PressAsync("Control+Shift+P");
            await Assertions.Expect(page.GetByTestId("document-command-palette")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByTestId("document-command-palette-search").FillAsync(command);
            var item = page.Locator($"[data-testid='document-command-palette-item'][data-command='{command}']");
            await Assertions.Expect(item).ToBeEnabledAsync(new() { Timeout = 10_000 });
            await item.ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-side-panel")).ToHaveAttributeAsync("data-active-tab", tab, new() { Timeout = 10_000 });
            await Assertions.Expect(page.GetByTestId("document-side-panel")).ToBeVisibleAsync();
            Assert.AreEqual(0, await page.GetByTestId("document-diff-viewer").CountAsync(), "the panel needs the document back: the comparison closed");
            // back into the comparison for the next command
            if (command != "openVersions")
            {
                await page.GetByTestId("document-side-panel-tab-versions").ClickAsync();
                await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Draft").GetByTestId("document-version-diff-base").ClickAsync();
                await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Final").GetByTestId("document-version-diff-compare").ClickAsync();
                await page.GetByTestId("document-version-show-diff").ClickAsync();
                await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToBeVisibleAsync(new() { Timeout = 15_000 });
            }
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The compare dialog's empty state names what is missing (no 'Ready' over a disabled button) and the disabled Compare is described by it")]
    public async Task Desktop_CompareDialog_EmptyState_NamesWhatIsMissing()
    {
        var page = await OpenAsync(RevisionsDocumentId, 1440, 900, touch: false);
        await ClickPopupItemAsync(page, "document-ribbon-tab-review", "");
        await ClickRibbonAsync(page, "document-compare-open");
        await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await Assertions.Expect(page.GetByTestId("document-compare-empty").Locator("h4")).ToHaveTextAsync("Choose both sources to compare.");
        await Assertions.Expect(page.GetByTestId("document-compare-target-label")).ToHaveTextAsync("No document selected");
        var run = page.GetByTestId("document-compare-run");
        await Assertions.Expect(run).ToBeDisabledAsync();
        Assert.IsNull(await run.GetAttributeAsync("title"), "a disabled button's title is never read");
        var describedBy = await run.GetAttributeAsync("aria-describedby");
        Assert.IsFalse(string.IsNullOrEmpty(describedBy));
        Assert.AreEqual("Choose both sources to compare.", (await page.Locator($"[id='{describedBy}']").InnerTextAsync()).Trim());
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The docked inspector's text tabs fit their row at 1440: every tab lies inside the tablist (none half hidden behind the fade)")]
    public async Task Desktop_InspectorTabs_AreInsideTheTablist()
    {
        var page = await OpenAsync(RevisionsDocumentId, 1440, 900, touch: false);
        await OpenInspectorTabAsync(page, "revisions");
        var tablist = page.GetByTestId("document-side-panel").Locator("[role='tablist']");
        var list = await tablist.BoundingBoxAsync();
        Assert.IsNotNull(list);
        var tabs = await page.GetByTestId("document-side-panel").Locator("[role='tab']").AllAsync();
        Assert.IsTrue(tabs.Count >= 3, "comments, revisions, versions at least");
        foreach (var tab in tabs)
        {
            var box = await tab.BoundingBoxAsync();
            Assert.IsNotNull(box);
            Assert.IsTrue(box.X >= list.X - 1 && box.X + box.Width <= list.X + list.Width + 1, $"a tab sticks out of the row ({box.X:0}+{box.Width:0} vs {list.X:0}+{list.Width:0})");
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A 320 px phone header keeps room for the title: Undo and Redo are in the kebab; at 390 they stay on the row")]
    public async Task Phone_Header_MovesUndoRedoToTheKebab_OnA320Phone()
    {
        var narrow = await OpenAsync("contract-demo", 320, 640, touch: true);
        Assert.AreEqual(0, await narrow.GetByTestId("document-header-actions").GetByTestId("document-undo").CountAsync());
        var title = await narrow.GetByTestId("document-title").BoundingBoxAsync();
        Assert.IsNotNull(title);
        Assert.IsTrue(title.Width >= 100, $"the title keeps at least 100 px at 320 ({title.Width:0})");
        await narrow.GetByTestId("document-header-kebab").ClickAsync();
        await Assertions.Expect(narrow.GetByTestId("document-header-kebab-panel").GetByTestId("document-undo")).ToBeVisibleAsync();
        await Assertions.Expect(narrow.GetByTestId("document-header-kebab-panel").GetByTestId("document-redo")).ToBeVisibleAsync();
        await narrow.Context.CloseAsync();

        var wide = await OpenAsync("contract-demo", 390, 844, touch: true);
        Assert.AreEqual(1, await wide.GetByTestId("document-header-actions").GetByTestId("document-undo").CountAsync());
        await wide.Context.CloseAsync();
    }

    private static async Task<string> LayoutAsync(IPage page)
        => await page.Locator(".tm-document-editor").First.GetAttributeAsync("data-layout") ?? string.Empty;

    private async Task<IPage> OpenAsync(string documentId, int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId={documentId}", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return page;
    }
}
