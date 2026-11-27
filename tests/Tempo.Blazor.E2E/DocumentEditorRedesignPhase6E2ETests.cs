using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 6 (DE-2.2 .. DE-2.5): behavioural assertions of the inspector panels - the contextual inspector (image,
/// table, find), the revision cards with their previews, the version preview strip and flow. Writes NO files; the screenshots are
/// <see cref="DocumentEditorRedesignPhase6CaptureE2ETests"/> (a gated generator).
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase6E2ETests : WasmTestBase
{
    private const string ContractDocumentId = "contract-demo";

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("Selecting the inline image opens the image context: no tab bar, visible wrap labels, a paragraph text as the anchor, unit suffixes; back returns the tabs")]
    public async Task ImageSelection_IsTheContextualInspector_AndBackReturnsTheTabs()
    {
        var page = await OpenContractAsync(1440, 900, touch: false);
        await SelectInlineImageAsync(page);

        var panel = page.GetByTestId("document-side-panel");
        await Assertions.Expect(panel).ToHaveAttributeAsync("data-context", "image", new() { Timeout = 10_000 });
        Assert.AreEqual(0, await panel.Locator("[role='tablist']").CountAsync(), "the context replaces the tab bar");
        await Assertions.Expect(page.GetByTestId("document-side-panel-context-header").Locator("h2")).ToHaveTextAsync("Image tools");

        // The wrap tiles carry visible text.
        var tile = page.GetByTestId("document-image-inspector-wrap-square").Locator(".tm-document-image-inspector__tile-label");
        await Assertions.Expect(tile).ToBeVisibleAsync();
        var box = await tile.BoundingBoxAsync();
        Assert.IsTrue(box is { Width: > 8, Height: > 6 }, "the wrap label is rendered, not clipped screen-reader text");

        // The anchor reads as text of the paragraph, never as a block id.
        var anchor = await page.GetByTestId("document-image-inspector-anchor-paragraph").InnerTextAsync();
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(anchor, "[0-9a-f]{8}-[0-9a-f]{4}|block-[0-9a-f]{6,}"), $"the anchor is no id: '{anchor}'");

        // Every number has its unit.
        Assert.AreEqual("px", (await page.GetByTestId("document-image-inspector-width").Locator("xpath=following-sibling::span[contains(@class,'suffix')]").InnerTextAsync()).Trim());
        Assert.AreEqual("°", (await page.GetByTestId("document-image-inspector-rotation").Locator("xpath=following-sibling::span[contains(@class,'suffix')]").InnerTextAsync()).Trim());

        // Editing through the inspector keeps the context.
        await page.GetByTestId("document-image-inspector-wrap-tight").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-image-inspector-wrap-tight")).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 10_000 });
        await Assertions.Expect(panel).ToHaveAttributeAsync("data-context", "image");

        await page.GetByTestId("document-side-panel-context-back").ClickAsync();
        await Assertions.Expect(panel.Locator("[role='tablist']")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.AreEqual(0, await page.GetByTestId("document-side-panel-context-header").CountAsync(), "the context header is gone");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The image context folds a section with its chevron and keeps the control state")]
    public async Task ImageInspector_SectionsFoldWithTheChevron()
    {
        var page = await OpenContractAsync(1440, 900, touch: false);
        await SelectInlineImageAsync(page);

        var toggle = page.GetByTestId("document-image-inspector-section-size-toggle");
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true", new() { Timeout = 10_000 });
        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(page.GetByTestId("document-image-inspector-width")).ToBeHiddenAsync();
        await toggle.ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-image-inspector-width")).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A caret in a table cell opens ONE 'Table' inspector with two foldable sections, a colour picker for the background and a preset border; X closes the context")]
    public async Task TableSelection_IsOneInspector_WithColourPickerAndBorderPreset()
    {
        var page = await OpenContractAsync(1440, 900, touch: false);
        var cell = await DocumentEditorRedesignPhase2CaptureE2ETests.ScrollToCanvasElementAsync(page, "[data-canvas-table-cell][data-cell-id='contract-pricing-table-r1-item']");
        await page.Mouse.ClickAsync((float)cell.X, (float)cell.Y);

        var panel = page.GetByTestId("document-side-panel");
        await Assertions.Expect(panel).ToHaveAttributeAsync("data-context", "table", new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-side-panel-context-header").Locator("h2")).ToHaveTextAsync("Table");
        await Assertions.Expect(page.GetByTestId("document-table-properties-panel")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("document-cell-properties-panel")).ToBeVisibleAsync();
        Assert.AreEqual(0, await panel.Locator("input[type='color']").CountAsync(), "no native colour input with a made-up value");
        Assert.AreEqual(2, await page.GetByTestId("document-table-selection-properties-panel").Locator(".tm-document-inspector-form__section-toggle").CountAsync());

        await page.GetByTestId("document-table-properties-align-left").ClickAsync();
        // An edit hands the focus back to the canvas, which reports an empty selection: the context must survive it - for longer than any grace window.
        await page.WaitForTimeoutAsync(3500);
        await Assertions.Expect(panel).ToHaveAttributeAsync("data-context", "table");
        // The colour picker opens INSIDE the panel's reach (top layer): a swatch click applies and the trigger shows the hex.
        var picker = page.GetByTestId("document-table-properties-background");
        await picker.Locator(".tm-color-picker-trigger").ClickAsync();
        var dropdown = page.Locator(".tm-color-picker-dropdown:visible").First;
        await Assertions.Expect(dropdown).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var dropdownBox = await dropdown.BoundingBoxAsync();
        var viewport = page.ViewportSize!;
        Assert.IsTrue(dropdownBox is not null && dropdownBox.X >= 0 && dropdownBox.X + dropdownBox.Width <= viewport.Width + 1, "the picker popover stays in the viewport");
        await dropdown.Locator("[role='option'], .tm-color-palette-swatch, button[data-color]").First.ClickAsync();
        await Assertions.Expect(picker.Locator(".tm-color-picker-trigger-text")).Not.ToHaveTextAsync("No color", new() { Timeout = 10_000 });
        await Assertions.Expect(panel).ToHaveAttributeAsync("data-context", "table");

        await page.GetByTestId("document-table-properties-border").SelectOptionAsync("strong");
        await Assertions.Expect(page.GetByTestId("document-table-properties-border")).ToHaveValueAsync("strong");

        await page.GetByTestId("document-side-panel-context-close").ClickAsync();
        await Assertions.Expect(panel.Locator("[role='tablist']")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Find is a panel of the inspector: it opens without shifting the ribbon, shows 'Results (N)' with marked context, the replace toggle needs no Ctrl+H, a click on a result moves the canvas")]
    public async Task Find_IsAnInspectorPanel_WithoutLayoutShift_AndAResultClickScrollsTheCanvas()
    {
        var page = await OpenContractAsync(1440, 900, touch: false);
        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        var ribbonBefore = await page.GetByTestId("document-ribbon-toolbar").BoundingBoxAsync();
        var headerBefore = await page.GetByTestId("document-header").BoundingBoxAsync();

        await page.Keyboard.PressAsync("Control+F");
        var find = page.GetByTestId("document-find-panel");
        await Assertions.Expect(find).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("[data-region='inspector'] [data-testid='document-find-panel']")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("document-side-panel")).ToHaveAttributeAsync("data-context", "find");

        var ribbonAfter = await page.GetByTestId("document-ribbon-toolbar").BoundingBoxAsync();
        var headerAfter = await page.GetByTestId("document-header").BoundingBoxAsync();
        Assert.AreEqual(ribbonBefore!.Y, ribbonAfter!.Y, 1, "opening find must not move the ribbon");
        Assert.AreEqual(ribbonBefore.Height, ribbonAfter.Height, 1, "opening find must not resize the ribbon");
        Assert.AreEqual(headerBefore!.Height, headerAfter!.Height, 1, "opening find must not resize the header");

        await page.GetByTestId("document-find-input").FillAsync("the");
        var heading = page.GetByTestId("document-find-results-heading");
        await Assertions.Expect(heading).ToContainTextAsync("Results (", new() { Timeout = 10_000 });
        await Assertions.Expect(page.GetByTestId("document-find-result").First.Locator("mark")).ToBeVisibleAsync();
        var results = await page.GetByTestId("document-find-result").CountAsync();
        Assert.IsTrue(results >= 2, $"the contract has several matches for 'the' ({results})");

        // The replace field is one toggle away - no Ctrl+H.
        Assert.AreEqual(0, await page.GetByTestId("document-replace-input").CountAsync());
        await page.GetByTestId("document-find-replace-toggle").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-replace-input")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("document-find-replace-one")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-btn-primary"));

        // Clicking the LAST result moves the canvas to its match (the scroll position changes) and makes it the active one.
        var scrollBefore = await page.EvaluateAsync<double>("() => window.scrollY");
        await page.GetByTestId("document-find-result").Last.ClickAsync();
        await WaitForPredicateAsync(page, $$"""
            before => Math.abs(window.scrollY - Number(before)) > 40
                && document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-search-active-index') === '{{results - 1}}'
            """, scrollBefore, 10_000);
        await Assertions.Expect(page.GetByTestId("document-find-result").Last).ToHaveAttributeAsync("aria-current", "true");

        // Closing find returns the tabs.
        await page.GetByTestId("document-find-close").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByTestId("document-side-panel").Locator("[role='tablist']")).ToBeVisibleAsync();
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Edge cases of find: an invalid regex shows no results and no error, an empty query clears, Escape closes, a query without a hit says so")]
    public async Task Find_EdgeCases_InvalidRegexEmptyQueryAndNoHits()
    {
        var page = await OpenContractAsync(1440, 900, touch: false);
        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await page.Keyboard.PressAsync("Control+F");
        await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByTestId("document-find-regex").CheckAsync();
        await page.GetByTestId("document-find-input").FillAsync("([unclosed");
        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual(0, await page.GetByTestId("document-find-result").CountAsync(), "an invalid regex matches nothing");
        Assert.AreEqual(0, await page.GetByTestId("document-find-results-heading").CountAsync());

        await page.GetByTestId("document-find-regex").UncheckAsync();
        await page.GetByTestId("document-find-input").FillAsync("zzzz-no-such-text");
        await Assertions.Expect(page.GetByTestId("document-find-count")).ToHaveTextAsync("0 results", new() { Timeout = 10_000 });

        await page.GetByTestId("document-find-clear").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-find-input")).ToHaveValueAsync(string.Empty);

        await page.GetByTestId("document-find-input").PressAsync("Escape");
        await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeHiddenAsync(new() { Timeout = 10_000 });
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("On a phone find is a floating panel (a sheet) over the document, not an inspector context")]
    public async Task Find_OnAPhone_IsAFloatingSheet()
    {
        var page = await OpenContractAsync(390, 844, touch: true);
        await page.GetByTestId("document-header-search").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.AreEqual(0, await page.Locator("[data-region='inspector'] [data-testid='document-find-panel']").CountAsync(), "not in the inspector");
        var box = await page.GetByTestId("document-find-panel").BoundingBoxAsync();
        Assert.IsTrue(box is not null && box.X >= -1 && box.X + box.Width <= 391, "the panel fits the 390 px viewport");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The revision cards show the inserted text in an ins, the author as an avatar with initials and a relative time")]
    public async Task Revisions_CardsShowTheInsertedText_Avatar_AndRelativeTime()
    {
        var page = await OpenAsync(RevisionsDocumentId, 1440, 900, touch: false, "/canvas-engine-host");
        await page.GetByTestId("document-side-panel-tab-revisions").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-revision-item").First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var insertion = page.GetByTestId("document-revision-item").Filter(new() { Has = page.Locator("ins") }).First;
        await Assertions.Expect(insertion).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var inserted = (await insertion.Locator("ins").InnerTextAsync()).Trim();
        Assert.IsTrue(inserted.Length > 0, "the card shows the text that was inserted");
        Assert.IsFalse(inserted.StartsWith('{') || inserted.StartsWith('['), "never the raw payload");

        var first = page.GetByTestId("document-revision-item").First;
        await Assertions.Expect(first.Locator(".tm-avatar-fallback, img.tm-avatar-image").First).ToBeVisibleAsync();
        var time = first.Locator("time");
        await Assertions.Expect(time).ToBeVisibleAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(await time.GetAttributeAsync("title")), "the absolute time rides the tooltip");
        Assert.IsTrue(await page.GetByTestId("document-revision-accept-all").EvaluateAsync<bool>("el => el.classList.contains('tm-btn-primary')"), "Accept all is the primary action");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Version flow: create -> preview (strip in the doc region, visible with the panel closed) -> restore with an inline confirmation")]
    public async Task Versions_CreatePreviewRestore_ThroughTheDocRegionStrip()
    {
        var page = await OpenAsync(HistoryDocumentId, 1440, 900, touch: false, "/document-editor");
        await ClickRibbonAsync(page, "document-ribbon-tab-view");
        await ClickRibbonAsync(page, "document-open-versions");
        await Assertions.Expect(page.GetByTestId("document-version-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var create = page.GetByTestId("document-version-create-open");
        Assert.IsTrue(await create.EvaluateAsync<bool>("el => el.classList.contains('tm-btn-primary')"), "Create version is primary");
        await create.ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-version-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Enter in the label field submits the form.
        await page.GetByTestId("document-version-label").FillAsync("Phase six");
        await page.GetByTestId("document-version-label").PressAsync("Enter");
        await Assertions.Expect(page.GetByTestId("document-version-dialog")).ToBeHiddenAsync(new() { Timeout = 10_000 });
        var item = page.GetByTestId("document-version-item").First;
        await Assertions.Expect(item).ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.AreEqual(0, await page.GetByTestId("document-version-preview-state").CountAsync(), "no preview before one is chosen");
        Assert.AreEqual(0, await page.Locator(".tm-document-version-panel__badge").CountAsync());

        await item.ClickAsync();
        var strip = page.Locator("[data-region='doc'] [data-testid='document-doc-banners'] [data-testid='document-version-preview-state']");
        await Assertions.Expect(strip).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(strip).ToContainTextAsync("Phase six");
        await Assertions.Expect(page.Locator(".tm-document-version-panel__badge")).ToHaveTextAsync("Current preview");

        // The strip belongs to the document: it stays with the inspector closed.
        await page.GetByTestId("document-side-panel-close").ClickAsync();
        await Assertions.Expect(strip).ToBeVisibleAsync();

        await page.GetByTestId("document-version-restore").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-version-restore-confirm")).ToBeVisibleAsync();
        // The pressed button left the DOM: the focus is handed to the confirm button, and back to Restore on Cancel.
        await Assertions.Expect(page.GetByTestId("document-version-restore-confirm-button")).ToBeFocusedAsync(new() { Timeout = 5_000 });
        await page.GetByTestId("document-version-restore-cancel").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-version-restore")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("document-version-restore")).ToBeFocusedAsync(new() { Timeout = 5_000 });

        await page.GetByTestId("document-version-restore").ClickAsync();
        await page.GetByTestId("document-version-restore-confirm-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-version-preview-state")).ToBeHiddenAsync(new() { Timeout = 10_000 });
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The review summary is ONE row above the canvas in the doc region and its buttons open the panels")]
    public async Task ReviewSummary_IsOneRowInTheDocRegion()
    {
        var page = await OpenAsync(RevisionsDocumentId, 1440, 900, touch: false, "/canvas-engine-host");
        var summary = page.Locator("[data-region='doc'] [data-testid='document-review-summary']");
        await Assertions.Expect(summary).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var box = await summary.BoundingBoxAsync();
        Assert.IsTrue(box is { Height: < 90 }, $"a single-row strip ({box?.Height}px)");
        await page.GetByTestId("document-review-summary-revisions").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-revision-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The text tabs scroll sideways instead of wrapping; a tab activated from CODE (the review summary buttons) is scrolled inside the row and the overflow cue is published")]
    public async Task TextTabs_ActivatedFromCode_AreScrolledIntoTheRow_AndTheOverflowIsCued()
    {
        var page = await OpenAsync(RevisionsDocumentId, 1440, 900, touch: false, "/canvas-engine-host");
        await Assertions.Expect(page.GetByTestId("document-review-summary")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var tablist = page.GetByTestId("document-side-panel").Locator("[role='tablist']");
        await Assertions.Expect(tablist).ToBeVisibleAsync();

        async Task AssertActiveTabInsideTheRowAsync(string what)
        {
            await page.WaitForTimeoutAsync(400);
            var geometry = await tablist.EvaluateAsync<double[]>(
                """
                list => {
                    const tab = list.querySelector('[role="tab"][aria-selected="true"]').getBoundingClientRect();
                    const row = list.getBoundingClientRect();
                    return [tab.left - row.left, row.right - tab.right, list.scrollWidth - list.clientWidth];
                }
                """);
            Assert.IsTrue(geometry[0] >= -1 && geometry[1] >= -1, $"{what}: the active tab is inside the scroller (left gap {geometry[0]:0.#}, right gap {geometry[1]:0.#})");
            if (geometry[2] > 1)
            {
                var start = await tablist.GetAttributeAsync("data-overflow-start");
                var end = await tablist.GetAttributeAsync("data-overflow-end");
                Assert.IsTrue(start == "true" || end == "true", $"{what}: the row overflows by {geometry[2]:0.#}px and says so (start={start}, end={end})");
            }
        }

        await page.GetByTestId("document-review-summary-revisions").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-revision-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await AssertActiveTabInsideTheRowAsync("Revisions");

        await page.GetByTestId("document-review-summary-comments").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-side-panel-tab-comments")).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
        await AssertActiveTabInsideTheRowAsync("Comments");
        await page.Context.CloseAsync();

        // The farthest tab: Versions, opened from the ribbon (a document that has a history provider).
        page = await OpenAsync(HistoryDocumentId, 1440, 900, touch: false, "/document-editor");
        tablist = page.GetByTestId("document-side-panel").Locator("[role='tablist']");
        await ClickRibbonAsync(page, "document-ribbon-tab-view");
        await ClickRibbonAsync(page, "document-open-versions");
        await Assertions.Expect(page.GetByTestId("document-version-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await AssertActiveTabInsideTheRowAsync("Versions");
        await page.Context.CloseAsync();
    }

    private async Task<IPage> OpenContractAsync(int width, int height, bool touch)
        => await OpenAsync(ContractDocumentId, width, height, touch, "/document-editor");

    private async Task<IPage> OpenAsync(string documentId, int width, int height, bool touch, string route)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        var url = route == "/document-editor" && documentId == ContractDocumentId
            ? $"{BaseUrl}{route}?documentId={documentId}"
            : OpenUrl(BaseUrl, documentId, route);
        await GotoAppReadyAsync(page, url, WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static async Task SelectInlineImageAsync(IPage page)
    {
        var image = await DocumentEditorRedesignPhase2CaptureE2ETests.ScrollToCanvasElementAsync(page, "[data-canvas-object][data-object-id='contract-inline-image']");
        await page.Mouse.ClickAsync((float)image.X, (float)image.Y);
        await WaitForPredicateAsync(page, """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-object-selected') === 'true'
                    && root?.getAttribute('data-canvas-object-id') === 'contract-inline-image';
            }
            """, 10_000);
        await Assertions.Expect(page.GetByTestId("document-image-inspector")).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
