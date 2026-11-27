using System.Text.Json;
using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 4 (DE-1.2): the compact ribbon on the core <c>TmToolbar</c>. ONE icon row without group
/// captions, no horizontal scroll at 1440 and 1280, one row plus "More" at 1024 touch, the commands that do not fit run from the
/// core More menu (a <c>role="menu"</c> with grouped sections and typeahead), toggles keep their state there, the roving keys skip
/// hidden items and reach More, and the View tab's debug tools take REAL clicks. Writes no files; the screenshots are
/// <see cref="DocumentEditorRedesignPhase4CaptureE2ETests"/> (a gated generator).
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[TestCategory("DocumentEditor:Ribbon")]
[DoNotParallelize]
public sealed class DocumentEditorRibbonE2ETests : WasmTestBase
{
    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("At 1440 and 1280 the Home tab is ONE row without a horizontal scroll or any group caption")]
    public async Task Home_IsOneRow_WithoutHorizontalScroll_AndWithoutGroupCaptions_At1440And1280()
    {
        foreach (var width in new[] { 1440, 1280 })
        {
            var (page, errors) = await OpenAsync(width, 900, touch: false);
            var probe = await ReadRibbonAsync(page);

            Assert.IsFalse(probe.GroupsScroll, $"{width}: the ribbon panel never scrolls horizontally ({probe.PanelScrollWidth} > {probe.PanelClientWidth})");
            Assert.IsFalse(probe.BarScrolls, $"{width}: the toolbar row never overflows its own box ({probe.BarScrollWidth} > {probe.BarClientWidth})");
            Assert.IsFalse(probe.PanelOverflowX is "auto" or "scroll", $"{width}: the ribbon panel is no horizontal scroller (overflow-x {probe.PanelOverflowX})");
            Assert.IsTrue(probe.RowSpread <= 8, $"{width}: every visible control sits in one row (vertical spread {probe.RowSpread}px)");
            Assert.AreEqual(0, probe.CaptionedGroups, $"{width}: a ribbon group prints no caption");
            Assert.IsTrue(probe.VisibleDividers >= 1, $"{width}: the groups are separated by vertical rules ({probe.VisibleDividers})");
            Assert.IsTrue(probe.ButtonHeight is >= 31 and <= 37, $"{width}: buttons are 32-36 px high on a fine pointer ({probe.ButtonHeight})");
            // The demo editor is ~1080 px at these viewports: the labels give way before the font / size / alignment / list controls do
            // (all four or none - a half-labelled row would be a bug).
            Assert.IsTrue(probe.LargeActionsWithText is 0 or 4, $"{width}: Link, Comment, Image and Table show their label together or not at all ({probe.LargeActionsWithText})");
            Assert.AreEqual(0, probe.LabelledIconButtons, $"{width}: every other button is icon-only");
            Assert.IsTrue(probe.IconButtonsWithName, $"{width}: every icon button has an accessible name and a tooltip");
            var style = await page.EvaluateAsync<string>("() => { const s = document.querySelector('[data-testid=\"document-block-style\"]'); return s ? (s.selectedOptions[0]?.textContent ?? '') + '|' + s.value : 'absent'; }");
            Assert.IsFalse(string.IsNullOrWhiteSpace(style.Split('|')[0]), $"{width}: the style select shows the current style ('{style}')");
            AssertNoClientErrors(errors, $"ribbon {width}");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("At the UNMODIFIED 1024x768 touch viewport the row is one line plus More, Link and Comment stay visible, and a command that did not fit runs from More; a 720 px host is the extra case")]
    public async Task At1024Touch_OneRowPlusMore_HiddenCommandsRunFromMore()
    {
        var (page, errors) = await OpenAsync(1024, 768, touch: true);
        await page.WaitForTimeoutAsync(500);

        var probe = await ReadRibbonAsync(page);
        Assert.IsTrue(probe.RowSpread <= 8, $"1024 touch: one row (spread {probe.RowSpread}px)");
        Assert.IsFalse(probe.GroupsScroll, "1024 touch: the ribbon panel does not scroll");
        Assert.IsTrue(probe.HasMore, "1024 touch: More exists (the row cannot hold everything)");
        Assert.IsTrue(probe.CollapsedCount > 0, "1024 touch: something moved into More");
        Assert.IsTrue(probe.ButtonHeight >= 43.5, $"a coarse pointer gets the 44px touch target ({probe.ButtonHeight})");
        Assert.IsTrue(await page.Locator("button[data-testid='document-link']:visible").CountAsync() == 1, "1024 touch: Link stays on the bar");
        Assert.IsTrue(await page.Locator("button[data-testid='document-add-comment']:visible").CountAsync() == 1, "1024 touch: Comment stays on the bar");

        // The command that is collapsed runs from the More menu, by its real test id.
        var collapsedTestId = await page.EvaluateAsync<string>(
            "() => document.querySelector('[data-testid=\"document-ribbon-toolbar\"] .tm-toolbar-btn.tm-toolbar-item--collapsed[data-testid]')?.getAttribute('data-testid') ?? ''");
        Assert.IsFalse(string.IsNullOrEmpty(collapsedTestId), "a collapsed bar command exists");
        await OpenMoreAsync(page);
        var entry = page.Locator($"[role='menu'] [data-menu-testid='{collapsedTestId}'], [role='menu'] [data-testid='{collapsedTestId}']");
        Assert.AreEqual(1, await entry.CountAsync(), $"{collapsedTestId} has exactly one More entry");
        await page.Keyboard.PressAsync("Escape");

        // Extra case: a narrower host.
        await page.EvaluateAsync("() => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = '720px'; host.style.maxWidth = '720px'; }");
        await page.WaitForTimeoutAsync(600);
        var narrow = await ReadRibbonAsync(page);
        Assert.IsTrue(narrow.RowSpread <= 8 && !narrow.GroupsScroll && narrow.HasMore, "720 host: still one row plus More without a scroll");
        AssertNoClientErrors(errors, "ribbon 1024");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("On a wide editor (1920 viewport) Link, Comment, Image and Table carry their label and nothing is collapsed on the Home row")]
    public async Task WideEditor_ShowsTheFourLabels_AndNothingLeavesTheBar()
    {
        var (page, errors) = await OpenAsync(2400, 1000, touch: false);
        await WidenEditorAsync(page, 1800);
        await page.WaitForTimeoutAsync(800);
        var probe = await ReadRibbonAsync(page);
        Assert.AreEqual(4, probe.LargeActionsWithText, "wide: the four calls to action carry their label");
        Assert.AreEqual(0, probe.LabelledIconButtons, "wide: every other button is icon-only");
        Assert.IsFalse(probe.GroupsScroll, "wide: no scroll");
        AssertNoClientErrors(errors, "wide ribbon");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("At 1440 the demo editor is about 1080 px wide: the font, size, alignment and list controls are on the bar")]
    public async Task At1440_FrequentControls_AreOnTheBar_AndColourTriggersAreGlyphs()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);

        foreach (var selector in new[]
        {
            "select[data-testid='document-font-family']:visible", "select[data-testid='document-font-size']:visible",
            "[data-testid='document-align'] button:visible", "button[data-testid='document-bullet-list']:visible",
            "button[data-testid='document-numbered-list']:visible", "button[data-testid='document-bold']:visible",
        })
        {
            Assert.AreEqual(1, await page.Locator(selector).CountAsync(), $"1440: {selector} is on the bar");
        }

        Assert.AreEqual(0, await page.Locator("[data-testid='document-ribbon-toolbar'] .tm-color-picker-trigger-bg").CountAsync(), "no checkerboard swatch in the ribbon");
        Assert.AreEqual(2, await page.Locator("[data-testid='document-ribbon-toolbar'] .tm-color-picker-trigger-glyph svg").CountAsync(), "two glyph triggers");
        var label = await page.Locator("[data-testid='document-font-color-trigger'] .tm-color-picker-trigger").GetAttributeAsync("aria-label");
        Assert.IsTrue(label is not null && label.Contains(':'), $"the colour trigger names its colour ({label})");
        AssertNoClientErrors(errors, "1440 frequent");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The labels of Link / Comment / Image / Table give way before any command leaves the bar, at every host width")]
    public async Task Labels_GiveWayBeforeAnyCommandLeavesTheBar()
    {
        var (page, errors) = await OpenAsync(2400, 1000, touch: false);
        var sawLabelsHidden = false;
        var sawLabelsShown = false;
        foreach (var width in new[] { 1800, 1600, 1500, 1400, 1300, 1200, 1100, 1000, 900, 800, 740, 700 })
        {
            await WidenEditorAsync(page, width);
            await page.WaitForTimeoutAsync(700);
            var state = await page.EvaluateAsync<string>(
                """
                () => {
                    const bar = document.querySelector('[data-testid="document-ribbon-toolbar"]');
                    const collapsed = bar.querySelectorAll('.tm-toolbar-item--collapsed').length;
                    const labelled = Array.from(bar.querySelectorAll('.tm-toolbar-btn[data-large="true"]'))
                        .filter(b => !b.closest('.tm-toolbar-item--collapsed'))
                        .some(b => { const t = b.querySelector('.tm-toolbar-btn-text'); return t && getComputedStyle(t).display !== 'none'; });
                    return JSON.stringify({ collapsed, labelled, marker: bar.hasAttribute('data-tm-labels-collapsed') });
                }
                """);
            using var doc = JsonDocument.Parse(state);
            var collapsed = doc.RootElement.GetProperty("collapsed").GetInt32();
            var labelled = doc.RootElement.GetProperty("labelled").GetBoolean();
            if (collapsed > 0)
            {
                Assert.IsFalse(labelled, $"host {width}: a command left the bar while a label still showed");
            }

            sawLabelsHidden |= !labelled;
            sawLabelsShown |= labelled;
        }

        Assert.IsTrue(sawLabelsHidden, "narrow hosts drop the labels");
        Assert.IsTrue(sawLabelsShown, "wide hosts keep them");
        // Widening takes the labels back.
        await WidenEditorAsync(page, 1800);
        await page.WaitForTimeoutAsync(900);
        Assert.AreEqual(4, await page.Locator("[data-testid='document-ribbon-toolbar'] .tm-toolbar-btn[data-large='true']:visible .tm-toolbar-btn-text:visible").CountAsync(), "the labels come back when the host widens");
        AssertNoClientErrors(errors, "labels first");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The More menu opens below the ribbon row (never over the header or the tabs) and scrolls inside its own cap")]
    public async Task MoreMenu_OpensBelowTheRibbonRow_WithinItsCap()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);
        await page.EvaluateAsync("() => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = '640px'; host.style.maxWidth = '640px'; }");
        await page.WaitForTimeoutAsync(700);
        // The editor sits below the demo page's own header: bring it to the top of the viewport like a reader would.
        await page.EvaluateAsync("() => { const host = document.querySelector('[data-testid=\"document-editor-host\"]'); window.scrollTo(0, window.scrollY + host.getBoundingClientRect().top - 8); }");
        await page.WaitForTimeoutAsync(300);
        await page.Locator("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").ClickAsync();
        var menu = page.Locator("[role='menu']").First;
        await Assertions.Expect(menu).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(300);
        var geometry = await page.EvaluateAsync<string>(
            """
            () => {
                const row = document.querySelector('[data-testid="document-ribbon-toolbar"]').getBoundingClientRect();
                const panel = document.querySelector('[role="menu"]').closest('.tm-toolbar-more__panel, .tm-overlay-panel');
                const r = panel.getBoundingClientRect();
                return JSON.stringify({ rowBottom: row.bottom, top: r.top, height: r.height, view: innerHeight, scrolls: panel.scrollHeight > panel.clientHeight + 1 || document.querySelector('[role="menu"]').scrollHeight > document.querySelector('[role="menu"]').clientHeight + 1 });
            }
            """);
        using var doc = JsonDocument.Parse(geometry);
        var top = doc.RootElement.GetProperty("top").GetDouble();
        var rowBottom = doc.RootElement.GetProperty("rowBottom").GetDouble();
        Assert.IsTrue(top >= rowBottom - 4, $"the menu starts below the ribbon row, not over the header or the tabs ({top} >= {rowBottom} - 4)");
        Assert.IsTrue(doc.RootElement.GetProperty("height").GetDouble() <= 28 * 16 + 40, "the menu keeps its 28rem cap");
        AssertNoClientErrors(errors, "more menu geometry");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A header action works while a ribbon popup is open and closes the popup (Undo with the page layout inspector open)")]
    public async Task HeaderActions_WorkWhileAPopupIsOpen_AndCloseIt()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);
        await page.GetByTestId("document-ribbon-tab-layout").ClickAsync();
        await page.GetByTestId("document-page-layout").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-page-layout-inspector")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The header (Save here: the document is unchanged, so the click itself is the proof) is not covered by the popup's backdrop:
        // Playwright's actionability check would time out on an intercepting backdrop.
        var save = page.GetByTestId("document-save");
        await save.ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-page-layout-inspector")).ToBeHiddenAsync(new() { Timeout = 5_000 });
        Assert.AreEqual(0, await page.GetByTestId("document-toolbar-floating-backdrop").CountAsync(), "the popup (and its backdrop) closed with the header action");
        AssertNoClientErrors(errors, "header while popup");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phone fallback: the More trigger stays outside the scrolling start container and never overlaps a control")]
    public async Task Phone_MoreTrigger_NeverOverlapsAControl()
    {
        var (page, errors) = await OpenAsync(390, 844, touch: true);
        var overlap = await page.EvaluateAsync<string>(
            """
            () => {
                const more = document.querySelector('[data-testid="document-ribbon-toolbar"] .tm-toolbar-more');
                const start = document.querySelector('[data-testid="document-ribbon-toolbar"] .tm-toolbar-start');
                if (!more) return JSON.stringify({ more: false });
                const m = more.getBoundingClientRect(); const s = start.getBoundingClientRect();
                const bar = document.querySelector('[data-testid="document-ribbon-toolbar"]').getBoundingClientRect();
                return JSON.stringify({ more: true, gap: m.left - s.right, insideBar: m.right <= bar.right + 1 && m.left >= bar.left, scroller: getComputedStyle(start).overflowX });
            }
            """);
        using var doc = JsonDocument.Parse(overlap);
        if (doc.RootElement.GetProperty("more").GetBoolean())
        {
            Assert.IsTrue(doc.RootElement.GetProperty("gap").GetDouble() >= -1, $"the More trigger starts where the controls end ({doc.RootElement.GetProperty("gap").GetDouble()})");
            Assert.IsTrue(doc.RootElement.GetProperty("insideBar").GetBoolean(), "the More trigger is inside the bar (always visible)");
        }

        AssertNoClientErrors(errors, "phone more");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A Secondary command leaves before any Primary one when the host narrows, and returns when it widens")]
    public async Task Priority_SecondaryLeavesBeforePrimary_AndTheRowRecoversWhenWidened()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);
        string Priorities() => "() => JSON.stringify(Array.from(document.querySelectorAll('[data-testid=\"document-ribbon-toolbar\"] .tm-toolbar-btn.tm-toolbar-item--collapsed')).map(b => b.getAttribute('data-priority')))";

        await page.EvaluateAsync("() => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = '640px'; host.style.maxWidth = '640px'; }");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-ribbon-toolbar\"] .tm-toolbar-more') !== null", 15_000);
        await page.WaitForTimeoutAsync(400);
        var collapsed = JsonSerializer.Deserialize<string[]>(await page.EvaluateAsync<string>(Priorities()))!;
        Assert.IsTrue(collapsed.Contains("secondary"), "narrow: the Secondary commands left the bar");
        // Only the buttons the toolbar itself ranks count: a button inside a popup-owning item is part of a pinned item.
        const string Ranked = "[data-testid=\"document-ribbon-toolbar\"] .tm-toolbar-btn:not(.tm-toolbar-item--collapsed)";
        var visiblePrimary = await page.EvaluateAsync<int>($"() => Array.from(document.querySelectorAll('{Ranked}')).filter(b => !b.closest('.tm-toolbar-item') && b.getAttribute('data-priority') === 'primary').length");
        Assert.IsTrue(visiblePrimary > 0, "narrow: a Primary command still stands on the bar");
        if (collapsed.Contains("primary"))
        {
            var visibleSecondary = await page.EvaluateAsync<int>($"() => Array.from(document.querySelectorAll('{Ranked}')).filter(b => !b.closest('.tm-toolbar-item') && b.getAttribute('data-priority') === 'secondary').length");
            Assert.AreEqual(0, visibleSecondary, "a Primary command only leaves once no Secondary command is left on the bar");
        }

        await page.EvaluateAsync("() => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = ''; host.style.maxWidth = ''; }");
        await page.WaitForTimeoutAsync(800);
        var widened = JsonSerializer.Deserialize<string[]>(await page.EvaluateAsync<string>(Priorities()))!;
        Assert.IsTrue(widened.Length < collapsed.Length, $"widening brings commands back ({collapsed.Length} -> {widened.Length})");
        AssertNoClientErrors(errors, "priority");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The More menu is the core role=menu: grouped sections, check-marked toggles, typeahead, Escape returns focus to the trigger")]
    public async Task MoreMenu_IsTheCoreMenu_WithGroupsToggleStateTypeaheadAndFocusReturn()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);
        await page.EvaluateAsync("() => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = '640px'; host.style.maxWidth = '640px'; }");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-ribbon-toolbar\"] .tm-toolbar-more') !== null", 15_000);
        await page.WaitForTimeoutAsync(400);

        var trigger = page.Locator("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more");
        await trigger.ClickAsync();
        var menu = page.Locator("[role='menu']").First;
        await Assertions.Expect(menu).ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.IsTrue(await menu.Locator("[role='group']").CountAsync() >= 2, "the collapsed members keep their groups as labelled sections");
        Assert.IsTrue(await menu.Locator("[role='menuitemcheckbox']").CountAsync() > 0, "a toggle is a menuitemcheckbox");
        Assert.AreEqual(0, await menu.Locator("input[type='search'], [data-testid='document-toolbar-more-search']").CountAsync(), "the old search box is gone (groups and typeahead replace it)");

        // Typeahead: typing the first letters of an entry focuses it (the arrow keys and typeahead belong to the core menu).
        var firstLabel = (await menu.Locator("[role^='menuitem']").First.InnerTextAsync()).Trim();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.TypeAsync(firstLabel[..1]);
        var focused = await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('role') ?? ''");
        Assert.IsTrue(focused.StartsWith("menuitem", StringComparison.Ordinal), $"focus stays on a menu item after the typeahead ({focused})");

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("[role='menu']")).ToHaveCountAsync(0, new() { Timeout = 5_000 });
        var returned = await page.EvaluateAsync<bool>("() => document.activeElement?.classList.contains('tm-toolbar-more') === true");
        Assert.IsTrue(returned, "Escape returns the focus to the More trigger");
        AssertNoClientErrors(errors, "more menu");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The roving keys of the ribbon skip every hidden item and continue into More")]
    public async Task RovingKeys_SkipCollapsedItems_AndReachMore()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);
        await page.EvaluateAsync("() => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = '640px'; host.style.maxWidth = '640px'; }");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-ribbon-toolbar\"] .tm-toolbar-more') !== null", 15_000);
        await page.WaitForTimeoutAsync(400);

        // Tab from the active ribbon tab lands on the toolbar's single roving tab stop.
        await page.GetByTestId("document-ribbon-tab-home").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        var inToolbar = await page.EvaluateAsync<bool>("() => document.activeElement?.closest('[data-testid=\"document-ribbon-toolbar\"]') !== null");
        Assert.IsTrue(inToolbar, "Tab from the tab strip enters the ribbon toolbar");

        // A native select keeps its own arrow keys (it is its own tab stop); the roving walk starts from a button.
        await page.GetByTestId("document-bold").FocusAsync();
        await page.Keyboard.PressAsync("End");
        var onMore = await page.EvaluateAsync<bool>("() => document.activeElement?.classList.contains('tm-toolbar-more') === true");
        Assert.IsTrue(onMore, "End goes to the last roving control: the More trigger");

        var visited = new List<string>();
        for (var step = 0; step < 40; step++)
        {
            await page.Keyboard.PressAsync("ArrowLeft");
            var state = await page.EvaluateAsync<string>(
                "() => { const a = document.activeElement; return JSON.stringify({ hidden: !!a?.closest('.tm-toolbar-item--collapsed, [inert], [hidden]'), id: a?.getAttribute('data-testid') || a?.className || '' }); }");
            using var doc = JsonDocument.Parse(state);
            Assert.IsFalse(doc.RootElement.GetProperty("hidden").GetBoolean(), $"ArrowLeft never lands on a hidden item (step {step}: {doc.RootElement.GetProperty("id").GetString()})");
            visited.Add(doc.RootElement.GetProperty("id").GetString() ?? string.Empty);
        }

        Assert.IsTrue(visited.Distinct().Count() > 3, "the roving walk passes several distinct visible controls");
        AssertNoClientErrors(errors, "roving");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The View tab's debug tools take REAL clicks: no sibling intercepts the pointer (they live in More)")]
    public async Task ViewTab_DebugTools_TakeRealClicks()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);
        await page.GetByTestId("document-ribbon-tab-view").ClickAsync();

        // OverflowOnly: never a bar button, so nothing can overlap or intercept it.
        Assert.AreEqual(0, await page.Locator("button[data-testid='document-view-json']:visible").CountAsync(), "the JSON tool is not on the bar");
        await OpenMoreAsync(page);
        await page.Locator("[role='menu'] [data-testid='document-view-json']").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-json-debug-modal")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-json-debug-close").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-json-debug-modal")).ToBeHiddenAsync(new() { Timeout = 5_000 });

        // The ordinary View commands are real bar clicks and nothing covers them (Playwright's actionability check is the proof).
        await page.GetByTestId("document-open-versions").ClickAsync(new() { Timeout = 10_000 });
        AssertNoClientErrors(errors, "view tab");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A toggle keeps its state in More: bold from More toggles the selection and the entry reads aria-checked")]
    public async Task ToggleInMore_KeepsItsState()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);
        await SelectTextAsync(page);
        await ClickRibbonAsync(page, "document-superscript");
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-command-superscript-state') === 'active'", 10_000);
        await OpenMoreAsync(page);
        await Assertions.Expect(page.Locator("[role='menu'] [data-testid='document-superscript']")).ToHaveAttributeAsync("aria-checked", "true", new() { Timeout = 5_000 });
        await page.Keyboard.PressAsync("Escape");
        AssertNoClientErrors(errors, "toggle in more");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The popup items stay on the bar and their popups open un-clipped (table grid, equation menu) in the new row")]
    public async Task PopupItems_StayOnTheBar_AndOpenUnclipped()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);
        await page.GetByTestId("document-ribbon-tab-insert").ClickAsync();
        await page.GetByTestId("document-toolbar-table").ClickAsync();
        var grid = page.Locator("[data-testid='document-table-grid-picker']");
        await Assertions.Expect(grid).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var box = (await grid.BoundingBoxAsync())!;
        Assert.IsTrue(box.X >= 0 && box.Y >= 0 && box.X + box.Width <= 1440 && box.Y + box.Height <= 900,
            $"the table grid is a fixed popup centred in the viewport, not confined by the toolbar ({box.X},{box.Y},{box.Width},{box.Height})");
        await page.Keyboard.PressAsync("Escape");

        await page.GetByTestId("document-toolbar-image").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-image-insert-menu")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.Keyboard.PressAsync("Escape");
        AssertNoClientErrors(errors, "popups");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Edge: the narrowest hosts (420 / 320) keep the editor inside its box; the row falls back to a scroll only on the phone layout")]
    public async Task NarrowHosts_KeepTheEditorInsideItsBox()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);
        foreach (var hostWidth in new[] { 800, 420, 320 })
        {
            await page.EvaluateAsync("w => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = w + 'px'; host.style.maxWidth = w + 'px'; }", hostWidth);
            await page.WaitForTimeoutAsync(700);
            var probe = await page.EvaluateAsync<string>(
                """
                () => {
                    const editor = document.querySelector('.tm-document-editor');
                    const panel = document.querySelector('.tm-document-editor__ribbon-groups');
                    return JSON.stringify({
                        layout: editor.getAttribute('data-layout'),
                        editorOverflows: editor.scrollWidth > editor.clientWidth + 1,
                        panelOverflowX: getComputedStyle(panel).overflowX,
                    });
                }
                """);
            using var doc = JsonDocument.Parse(probe);
            Assert.IsFalse(doc.RootElement.GetProperty("editorOverflows").GetBoolean(), $"host {hostWidth}: the editor does not overflow its box");
            if (doc.RootElement.GetProperty("layout").GetString() != "mobile")
            {
                Assert.AreNotEqual("auto", doc.RootElement.GetProperty("panelOverflowX").GetString(), $"host {hostWidth}: only the phone layout may scroll the row");
            }
        }

        AssertNoClientErrors(errors, "narrow hosts");
        await page.Context.CloseAsync();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Sizes the editor host to exactly <paramref name="width"/> and lifts the max-width the demo page puts on its ancestors.</summary>
    private static Task WidenEditorAsync(IPage page, int width)
        => page.EvaluateAsync(
            "w => { let el = document.querySelector('.tm-document-editor-host'); const host = el; while (el && el !== document.body) { el.style.maxWidth = 'none'; el = el.parentElement; } host.style.width = w + 'px'; }",
            width);

    private static void AssertNoClientErrors(List<string> errors, string context)
        => Assert.AreEqual(0, errors.Count, $"{context}: no console or page error expected, got: {string.Join(" | ", errors.Take(5))}");

    private async Task<(IPage Page, List<string> Errors)> OpenAsync(int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && !message.Text.Contains("favicon", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(message.Text);
            }
        };
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId=contract-demo", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-toolbar"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return (page, errors);
    }

    private static async Task SelectTextAsync(IPage page)
    {
        var point = await page.EvaluateAsync<double[]>(
            """
            () => {
                const rects = Array.from(document.querySelectorAll('[data-canvas-text-rect]'))
                    .map(node => node.getBoundingClientRect()).filter(r => r.width > 20 && r.height > 5);
                const r = rects[0];
                return [r.left + 2, r.top + r.height / 2, r.left + Math.min(r.width - 2, 80), r.top + r.height / 2];
            }
            """);
        await page.Mouse.MoveAsync((float)point[0], (float)point[1]);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)point[2], (float)point[3], new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-selection-collapsed') === 'false'", 10_000);
    }

    private static async Task<RibbonProbe> ReadRibbonAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>(
            """
            () => {
                const panel = document.querySelector('[data-testid="document-ribbon-panel"]');
                const bar = document.querySelector('[data-testid="document-ribbon-toolbar"]');
                const visible = el => { const r = el.getBoundingClientRect(); const s = getComputedStyle(el); return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && !el.closest('.tm-toolbar-item--collapsed'); };
                const controls = Array.from(bar.querySelectorAll('.tm-toolbar-btn, .tm-document-editor__ribbon-select, .tm-toolbar-more')).filter(visible);
                const tops = controls.map(el => { const r = el.getBoundingClientRect(); return r.top + r.height / 2; });
                const buttons = Array.from(bar.querySelectorAll('.tm-toolbar-btn')).filter(visible);
                const large = buttons.filter(b => b.getAttribute('data-large') === 'true' && b.querySelector('.tm-toolbar-btn-text'));
                const labelled = buttons.filter(b => b.getAttribute('data-large') !== 'true' && b.querySelector('.tm-toolbar-btn-text'));
                const iconButtons = buttons.filter(b => !b.querySelector('.tm-toolbar-btn-text'));
                const captioned = Array.from(bar.querySelectorAll('.tm-document-editor__ribbon-group'))
                    .filter(g => { const c = getComputedStyle(g, '::after').content; return c && c !== 'none' && c !== 'normal'; });
                const dividers = Array.from(bar.querySelectorAll('.tm-toolbar-divider')).filter(d => getComputedStyle(d).visibility !== 'hidden' && d.getBoundingClientRect().width > 0);
                return JSON.stringify({
                    panelScrollWidth: panel.scrollWidth, panelClientWidth: panel.clientWidth, panelOverflowX: getComputedStyle(panel).overflowX,
                    barScrollWidth: bar.scrollWidth, barClientWidth: bar.clientWidth,
                    rowSpread: tops.length ? Math.max(...tops) - Math.min(...tops) : 0,
                    captionedGroups: captioned.length,
                    visibleDividers: dividers.length,
                    buttonHeight: buttons.length ? buttons[0].getBoundingClientRect().height : 0,
                    largeActionsWithText: large.length,
                    labelledIconButtons: labelled.length,
                    iconButtonsWithName: iconButtons.every(b => (b.getAttribute('aria-label') || '').length > 0 && (b.getAttribute('title') || '').length > 0),
                    hasMore: !!bar.querySelector('.tm-toolbar-more'),
                    collapsedCount: bar.querySelectorAll('.tm-toolbar-item--collapsed').length,
                });
            }
            """);
        return JsonSerializer.Deserialize<RibbonProbe>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private sealed class RibbonProbe
    {
        public double PanelScrollWidth { get; set; }
        public double PanelClientWidth { get; set; }
        public string PanelOverflowX { get; set; } = string.Empty;
        public double BarScrollWidth { get; set; }
        public double BarClientWidth { get; set; }
        public double RowSpread { get; set; }
        public int CaptionedGroups { get; set; }
        public int VisibleDividers { get; set; }
        public double ButtonHeight { get; set; }
        public int LargeActionsWithText { get; set; }
        public int LabelledIconButtons { get; set; }
        public bool IconButtonsWithName { get; set; }
        public bool HasMore { get; set; }
        public int CollapsedCount { get; set; }

        public bool GroupsScroll => PanelScrollWidth > PanelClientWidth + 1;

        public bool BarScrolls => BarScrollWidth > BarClientWidth + 1;
    }
}

/// <summary>
/// Phase-4 "after" screenshots for the UX review: the ribbon at 1440, 1280 and 1024 (touch) in light and dark, the open More
/// menu, the View tab and the popup items. Gated by <c>TM_WRITE_BASELINES</c>; the PNGs land in
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/document-editor-redesign/phase-4/</c> (override the folder name with
/// <c>TM_DOCUMENT_EDITOR_BASELINE_DIR</c>).
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase4CaptureE2ETests : BaselineGeneratorTestBase
{
    private static string OutputDirectory
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_DOCUMENT_EDITOR_BASELINE_DIR");
            var name = string.IsNullOrWhiteSpace(target) ? "phase-4" : target.Trim();
            var directory = Path.Combine(
                FindRepositoryRootDirectory(),
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-redesign", name);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("The ribbon (header + tabs + row) at 1440 / 1280 / 1024 touch in light and dark, every tab at 1440")]
    public async Task CaptureRibbon()
    {
        foreach (var (name, width, height, touch) in new[] { ("1920", 1920, 1000, false), ("1440", 1440, 900, false), ("1280", 1280, 800, false), ("1024", 1024, 768, true) })
        {
            var page = await OpenAsync(width, height, touch);
            foreach (var dark in new[] { false, true })
            {
                await ApplyThemeAsync(page, dark, indigo: false);
                var theme = dark ? "dark" : "light";
                await RibbonShot(page, $"ribbon-home-{theme}-{name}.png");
            }

            await page.Context.CloseAsync();
        }

        var tabs = await OpenAsync(1440, 900, false);
        foreach (var tab in new[] { "insert", "layout", "references", "review", "view" })
        {
            await tabs.GetByTestId($"document-ribbon-tab-{tab}").ClickAsync();
            await tabs.WaitForTimeoutAsync(400);
            await RibbonShot(tabs, $"ribbon-{tab}-light-1440.png");
        }

        await ApplyThemeAsync(tabs, dark: true, indigo: false);
        await tabs.GetByTestId("document-ribbon-tab-home").ClickAsync();
        await tabs.WaitForTimeoutAsync(400);
        await RibbonShot(tabs, "ribbon-home-dark-1440-after-tabs.png");
        await tabs.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The More menu open: a tablet-width host in light and dark, plus the narrow phone row")]
    public async Task CaptureMoreMenu()
    {
        foreach (var dark in new[] { false, true })
        {
            var page = await OpenAsync(1440, 900, false);
            await ApplyThemeAsync(page, dark, indigo: false);
            await page.EvaluateAsync("() => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = '760px'; host.style.maxWidth = '760px'; }");
            await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-ribbon-toolbar\"] .tm-toolbar-more') !== null", 15_000);
            await page.WaitForTimeoutAsync(500);
            await RibbonShot(page, $"ribbon-narrow-760-{(dark ? "dark" : "light")}.png");
            await page.Locator("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").ClickAsync();
            await Assertions.Expect(page.Locator("[role='menu']").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.WaitForTimeoutAsync(300);
            await RibbonShot(page, $"more-menu-760-{(dark ? "dark" : "light")}.png", tall: true);
            await page.Context.CloseAsync();
        }

        var wide = await OpenAsync(1440, 900, false);
        await wide.Locator("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").ClickAsync();
        await wide.WaitForTimeoutAsync(300);
        await RibbonShot(wide, "more-menu-1440-light.png", tall: true);
        await wide.Context.CloseAsync();

        var phone = await OpenAsync(390, 844, true);
        await RibbonShot(phone, "ribbon-home-light-390.png");
        await phone.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The popup items in the new row: table grid, image menu, equation menu, style gallery")]
    public async Task CapturePopups()
    {
        var page = await OpenAsync(1440, 900, false);
        await page.GetByTestId("document-ribbon-tab-insert").ClickAsync();
        await page.GetByTestId("document-toolbar-image").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        await RibbonShot(page, "popup-image-menu-1440.png", tall: true);
        await page.Keyboard.PressAsync("Escape");
        await page.GetByTestId("document-toolbar-table").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        await RibbonShot(page, "popup-table-grid-1440.png", tall: true);
        await page.Context.CloseAsync();
    }

    private async Task<IPage> OpenAsync(int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId=contract-demo", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-toolbar"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return page;
    }

    /// <summary>The header, the tabs and the command row: the top of the editor, clipped to the part a reviewer judges.</summary>
    private static async Task RibbonShot(IPage page, string fileName, bool tall = false)
    {
        var host = page.GetByTestId("document-editor-host");
        await host.ScrollIntoViewIfNeededAsync();
        await page.EvaluateAsync(
            """
            () => {
                const host = document.querySelector('[data-testid="document-editor-host"]');
                window.scrollTo(0, window.scrollY + host.getBoundingClientRect().top - 8);
                for (const el of document.querySelectorAll('body *')) {
                    if (host.contains(el) || el.contains(host)) continue;
                    const position = getComputedStyle(el).position;
                    if (position === 'fixed' || position === 'sticky') el.style.visibility = 'hidden';
                }
            }
            """);
        await page.WaitForTimeoutAsync(400);
        var box = (await host.BoundingBoxAsync())!;
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, fileName),
            Type = ScreenshotType.Png,
            Clip = new Clip { X = (float)box.X, Y = (float)box.Y, Width = (float)box.Width, Height = (float)Math.Min(box.Height, tall ? 640 : 220) }
        });
    }
}
