using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 5 of the Spreadsheet redesign — ribbon v2, the opt-in document header, the formula bar polish
/// (permanent fx, ✓ / ✗, named-range name box) and the sheet tabs (close buttons, overflow arrows).
/// <para>
/// Screenshots land in <c>__screenshots__/spreadsheet/</c> as <c>phase5-*.png</c> and are reviewed against
/// output/imagegen/tempo-reimagined/Tempo.Blazor.Spreadsheet/spreadsheet--desktop.png. The demo hosts two
/// instances: the canvas renderer (<c>spreadsheet-demo</c>, no header → the legacy File tab) and the
/// document-header instance (<c>spreadsheet-header-demo</c>, header on → no File tab).
/// </para>
/// </summary>
public partial class SpreadsheetE2ETests
{
    private static readonly string[] Phase5ForbiddenGlyphs =
    [
        "←|", "|→", ".0→", ".00→", "☐", "⊞", "▣", "◼", "▬", "▰", "═", "⊟", "▭", "▏", "▕", "⚙", "▾", "▴"
    ];

    private static ILocator Phase5HeaderComponent(IPage page)
        => page.Locator("[data-testid='spreadsheet-header-demo']");

    // NOTE the distinct name: a parameterless OpenPhase5DemoAsync already exists in the phase-5
    // data-validation partial and C# picks the parameterless overload for a no-argument call, which
    // silently skipped the desktop viewport this suite must screenshot at.
    private async Task<IPage> OpenPhase5RibbonDemoAsync(int width = 1440, int height = 960)
    {
        var page = await CreatePageAsync();
        await page.SetViewportSizeAsync(width, height);
        await GotoAppReadyAsync(page, $"{BaseUrl}/spreadsheet");
        await WaitForCanvasGridReadyAsync(page, DemoGrid(page));
        return page;
    }

    /// <summary>
    /// True when the element is the topmost hit at the centre of its own box — i.e. no ancestor's
    /// overflow clips it and nothing overlays it.
    /// </summary>
    private static Task<bool> IsTopmostAsync(ILocator locator, string hitTestExpression)
        => locator.EvaluateAsync<bool>(
            "(el) => { const rect = el.getBoundingClientRect();" +
            " const hit = document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2);" +
            " return !!hit && (" + hitTestExpression + "); }");

    /// <summary>The computed surface of an open menu panel: it must be an opaque card, not just text.</summary>
    private static Task<MenuSurface> ReadMenuSurfaceAsync(ILocator panel)
        => panel.EvaluateAsync<MenuSurface>(@"(el) => {
            const cs = getComputedStyle(el);
            return {
                Background: cs.backgroundColor,
                BorderTopWidth: cs.borderTopWidth,
                BorderRadius: cs.borderTopLeftRadius,
                BoxShadow: cs.boxShadow,
                Display: cs.display
            };
        }");

    /// <summary>The first non-transparent background painted by the element or one of its ancestors.</summary>
    private static Task<string?> FirstOpaqueBackgroundAsync(ILocator locator)
        => locator.EvaluateAsync<string?>(@"(el) => {
            let node = el;
            while (node) {
                const bg = getComputedStyle(node).backgroundColor;
                if (bg && bg !== 'transparent' && bg !== 'rgba(0, 0, 0, 0)') return bg;
                node = node.parentElement;
            }
            return null;
        }");

    /// <summary>An item's computed box: pins that the scoped item rules still reach third-party panels.</summary>
    private static Task<MenuSurface> ReadItemStyleAsync(ILocator item)
        => item.EvaluateAsync<MenuSurface>(@"(el) => {
            const cs = getComputedStyle(el);
            return {
                Display: cs.display,
                PaddingLeft: cs.paddingLeft,
                TextAlign: cs.textAlign,
                Background: cs.backgroundColor
            };
        }");

    private sealed class MenuSurface
    {
        public string? Background { get; set; }
        public string? BorderTopWidth { get; set; }
        public string? BorderRadius { get; set; }
        public string? BoxShadow { get; set; }
        public string? Display { get; set; }
        public string? PaddingLeft { get; set; }
        public string? TextAlign { get; set; }
    }

    private async Task ShotPhase5Async(IPage page, string name)
    {
        var path = ShotPath("phase5-" + name);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    private static async Task ClickPhase5TabAsync(IPage page, string tab)
    {
        await DemoComponent(page).Locator($"[data-tm-tab='{tab}'][role='tab']").First.ClickAsync();
        await page.WaitForTimeoutAsync(150);
    }

    private static async Task<string[]> Phase5GroupLabelsAsync(IPage page)
    {
        var labels = await DemoComponent(page).Locator(".tm-spreadsheet-toolbar__group-label").AllTextContentsAsync();
        return labels.Select(text => text.Trim()).ToArray();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_HomeRibbon_RendersTheLabelledGroupsOfTheReference()
    {
        var page = await OpenPhase5RibbonDemoAsync();
        var component = DemoComponent(page);

        var labels = await Phase5GroupLabelsAsync(page);
        CollectionAssert.IsSubsetOf(new[] { "Clipboard", "Font", "Alignment", "Number", "Cells", "Insert" }, labels,
            "the Home ribbon names its groups under the buttons: " + string.Join(", ", labels));

        var groups = await component.Locator("[data-tm-group]").EvaluateAllAsync<string[]>(
            "els => els.map(el => el.getAttribute('data-tm-group'))");
        CollectionAssert.AreEqual(new[] { "clipboard", "font", "alignment", "number", "cells", "insert" }, groups,
            "the frozen data-tm-group contract describes the Home ribbon");

        var tab = await component.Locator("[role='tab'][aria-selected='true']").GetAttributeAsync("data-tm-tab");
        Assert.AreEqual("Home", tab);
        Assert.AreEqual("Home", await component.Locator("[data-tm-tab='Home'][role='tabpanel']").GetAttributeAsync("data-tm-tab"));

        // Every group paints a body + its name.
        var groupCount = await component.Locator(".tm-spreadsheet-toolbar__group").CountAsync();
        var bodyCount = await component.Locator(".tm-spreadsheet-toolbar__group-body").CountAsync();
        var labelCount = await component.Locator(".tm-spreadsheet-toolbar__group-label").CountAsync();
        Assert.AreEqual(groupCount, bodyCount, "every group has a body");
        Assert.AreEqual(groupCount, labelCount, "every group has a visible name");

        // Large buttons (icon above the label) exist for the reference's big commands.
        foreach (var title in new[] { "Undo", "Redo", "Paste", "Insert row", "Insert column", "Delete", "Insert link", "Insert image" })
        {
            var button = component.Locator($".tm-spreadsheet-toolbar__button[title='{title}']").First;
            Assert.IsTrue(await button.IsVisibleAsync(), $"the large button '{title}' is on the Home ribbon");
            Assert.IsTrue(await button.EvaluateAsync<bool>("el => el.classList.contains('tm-spreadsheet-toolbar__button--large')"),
                $"'{title}' is a large ribbon button");
            Assert.AreEqual(1, await button.Locator("svg").CountAsync(), $"'{title}' paints an SVG icon");
            Assert.AreEqual(1, await button.Locator(".tm-spreadsheet-toolbar__button-label").CountAsync(),
                $"'{title}' paints its visible label");
        }

        // No text glyph anywhere in the ribbon (collapsed dropdown panels excluded: they are closed).
        var toolbarText = await component.Locator(".tm-spreadsheet-toolbar").InnerTextAsync();
        foreach (var glyph in Phase5ForbiddenGlyphs)
        {
            Assert.IsFalse(toolbarText.Contains(glyph, StringComparison.Ordinal),
                $"'{glyph}' is a text glyph, not an icon");
        }

        await ShotPhase5Async(page, "ribbon-home--desktop");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_EveryRibbonTab_RendersItsOwnContent()
    {
        var page = await OpenPhase5RibbonDemoAsync();
        var component = DemoComponent(page);

        await ClickPhase5TabAsync(page, "Insert");
        Assert.AreEqual(1, await component.Locator(".tm-spreadsheet-toolbar__button[title='Insert link']").CountAsync(),
            "the Insert tab carries the Insert group");
        await ShotPhase5Async(page, "ribbon-insert--desktop");

        await ClickPhase5TabAsync(page, "Data");
        foreach (var title in new[] { "Filter", "Sort descending", "Remove duplicates", "Text to columns", "Data validation..." })
        {
            Assert.AreEqual(1, await component.Locator($".tm-spreadsheet-toolbar__button[title='{title}']").CountAsync(),
                $"the Data tab command '{title}' keeps its E2E selector title");
        }
        Assert.AreEqual("data", await component.Locator("[data-tm-group]").First.GetAttributeAsync("data-tm-group"),
            "the Data tab exposes the frozen data-tm-group id");

        // The Data group is one long row of text commands: it wraps inside the ribbon instead of
        // pushing the last buttons under the component's clipped edge (the phase-4 review finding).
        var componentBox = await component.BoundingBoxAsync();
        Assert.IsNotNull(componentBox);
        foreach (var button in await component.Locator(".tm-spreadsheet-toolbar__button--text").AllAsync())
        {
            var buttonBox = await button.BoundingBoxAsync();
            if (buttonBox is null) continue;
            Assert.IsTrue(buttonBox.X + buttonBox.Width <= componentBox.X + componentBox.Width + 1,
                $"the Data command '{await button.GetAttributeAsync("title")}' stays inside the ribbon");
        }
        await ShotPhase5Async(page, "ribbon-data--desktop");

        await ClickPhase5TabAsync(page, "View");
        Assert.AreEqual(1, await component.Locator(".tm-spreadsheet-toolbar__button[title='Grid lines']").CountAsync());
        Assert.AreEqual(0, await component.Locator(".tm-spreadsheet-toolbar__button[title='Merge cells']").CountAsync(),
            "Merge cells moved to the Home tab's Alignment group");
        await ShotPhase5Async(page, "ribbon-view--desktop");

        await ClickPhase5TabAsync(page, "File");
        Assert.AreEqual(1, await component.Locator(".tm-spreadsheet-toolbar__button[title='Open file']").CountAsync(),
            "without a header the legacy File tab keeps Open / Download");
        await ShotPhase5Async(page, "ribbon-file--desktop");

        await ClickPhase5TabAsync(page, "Home");
        Assert.AreEqual(1, await component.Locator(".tm-spreadsheet-toolbar__button[title='Merge cells']").CountAsync(),
            "Merge cells lives in the Home tab's Alignment group");
        var alignmentGroup = component.Locator(".tm-spreadsheet-toolbar__group")
            .Filter(new LocatorFilterOptions { Has = page.Locator(".tm-spreadsheet-toolbar__group-label", new() { HasTextString = "Alignment" }) });
        Assert.AreEqual(1, await alignmentGroup.Locator(".tm-spreadsheet-toolbar__button[title='Merge cells']").CountAsync(),
            "the merge command sits inside the Alignment group");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_DocumentHeader_OwnsTheFileActionsAndTheGridToggle()
    {
        var page = await OpenPhase5RibbonDemoAsync();
        var headerComponent = Phase5HeaderComponent(page);
        await headerComponent.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(200);

        var header = headerComponent.Locator(".tm-spreadsheet__header");
        Assert.AreEqual(1, await header.CountAsync(), "the header instance renders the document header");
        Assert.AreEqual("Projekt Aurora", (await header.Locator(".tm-spreadsheet__title").InnerTextAsync()).Trim());
        Assert.AreEqual(1, await header.Locator("[data-testid='spreadsheet-header-doc-switcher']").CountAsync(),
            "HeaderStart renders in the header");
        Assert.IsTrue(await header.Locator("button", new() { HasTextString = "Sdílet" }).CountAsync() >= 1,
            "HeaderActions renders in the header");
        Assert.AreEqual(1, await header.Locator("[data-testid='tm-spreadsheet-header-open']").CountAsync());
        Assert.AreEqual(1, await header.Locator("[data-testid='tm-spreadsheet-header-download']").CountAsync());

        // The grid toggle is the core TmToggle (role=switch) with the localized label.
        var toggle = header.Locator("input.tm-toggle-input[role='switch']");
        Assert.AreEqual(1, await toggle.CountAsync(), "the header grid toggle is the core TmToggle");
        Assert.AreEqual("true", await toggle.GetAttributeAsync("aria-checked"));
        Assert.IsTrue((await header.InnerTextAsync()).Contains("Show grid lines", StringComparison.Ordinal),
            "the toggle is named 'Show grid lines'");

        // While the header owns the file actions the legacy File tab is not rendered.
        Assert.AreEqual(0, await headerComponent.Locator(".tm-spreadsheet-toolbar__tab", new() { HasTextString = "File" }).CountAsync(),
            "the header replaces the toolbar's File tab");

        await ShotPhase5Async(page, "header--light");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_HeaderGridToggle_TurnsTheGridLinesOff()
    {
        var page = await OpenPhase5RibbonDemoAsync();
        var headerComponent = Phase5HeaderComponent(page);
        await headerComponent.ScrollIntoViewIfNeededAsync();

        var grid = headerComponent.Locator(".tm-spreadsheet-canvas-grid");
        await WaitForCanvasGridReadyAsync(page, grid);
        // The core TmToggle's input is visually hidden (the label paints the track), so the click
        // targets the wrapper — Playwright refuses to click the input itself.
        var toggle = headerComponent.Locator(".tm-toggle-label");
        await toggle.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(200);
        await toggle.ClickAsync(new LocatorClickOptions { Position = new() { X = 20, Y = 10 } });
        await page.WaitForTimeoutAsync(400);

        Assert.AreEqual("false", await headerComponent.Locator("input.tm-toggle-input[role='switch']").GetAttributeAsync("aria-checked"));
        var showGridLines = await grid.EvaluateAsync<bool>(
            "el => Boolean(el.__tmSpreadsheetCanvas?.model?.showGridLines ?? el.__tmSpreadsheetCanvas?.sheetState?.showGridLines)");
        Assert.IsFalse(showGridLines, "the toggle drives the active sheet's ShowGridLines through the canvas");
        await ShotPhase5Async(page, "header-grid-off--desktop");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_RibbonAndHeader_DarkMode_Render()
    {
        var page = await OpenPhase5RibbonDemoAsync();
        await ToggleDarkModeAsync(page);
        await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(300);

        await ShotPhase5Async(page, "ribbon-home--dark");

        var headerComponent = Phase5HeaderComponent(page);
        await headerComponent.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(200);
        await ShotPhase5Async(page, "header--dark");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_NarrowDesktop1280_RibbonScrollsWithoutOverflowingThePage()
    {
        // Redesign phase 6 moved the layout decision from the VIEWPORT to the CONTAINER
        // (TmLayoutBreakpoints.Default: mobile <640, tablet 640-1023, desktop >=1024), so a 1280-wide page
        // whose content column is ~910px is the TABLET chrome. The intent of this spec is unchanged — the
        // chrome must never push the page wide — so it is asserted on BOTH sides of the breakpoint.
        var page = await OpenPhase5RibbonDemoAsync(1280, 860);
        var component = DemoComponent(page);

        var pageWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        var viewportWidth = await page.EvaluateAsync<int>("() => window.innerWidth");
        Assert.IsTrue(pageWidth <= viewportWidth + 1,
            $"the chrome must not overflow the page at 1280 (scrollWidth {pageWidth} vs viewport {viewportWidth})");
        Assert.AreEqual("tablet", await component.Locator(".tm-spreadsheet").First.GetAttributeAsync("data-layout"),
            "a ~910px container inside a 1280 viewport is the tablet chrome");
        Assert.AreEqual(0, await component.Locator(".tm-spreadsheet-toolbar__tabs").CountAsync(),
            "the tablet chrome has no ribbon tab strip");
        Assert.AreEqual(2, await component.Locator("[data-toolbar-row]").CountAsync(),
            "the tablet chrome is the two compact rows");
        await ShotPhase5Async(page, "ribbon-home--narrow-1280");
        await page.CloseAsync();

        // The desktop ribbon (a >=1024px container) still may not push the page wide; it scrolls inside
        // its own row when the group set is wider than the container.
        var wide = await OpenPhase5RibbonDemoAsync(1440, 900);
        var wideComponent = DemoComponent(wide);
        var widePageWidth = await wide.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        var wideViewportWidth = await wide.EvaluateAsync<int>("() => window.innerWidth");
        Assert.IsTrue(widePageWidth <= wideViewportWidth + 1,
            $"the desktop ribbon must not overflow the page (scrollWidth {widePageWidth} vs viewport {wideViewportWidth})");

        var tabContent = wideComponent.Locator(".tm-spreadsheet-toolbar__tab-content").First;
        var box = await tabContent.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box.X + box.Width <= wideViewportWidth + 1,
            $"the ribbon row ends inside the viewport (right edge {box.X + box.Width})");

        var rowOverflow = await tabContent.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1");
        if (rowOverflow)
        {
            Assert.AreEqual("auto", await tabContent.EvaluateAsync<string>("el => getComputedStyle(el).overflowX"),
                "a ribbon row wider than its container scrolls inside itself");
        }

        await wide.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_EmbeddedMode_HasNoRibbonAndNoHeader()
    {
        var page = await OpenPhase5RibbonDemoAsync();
        var embedded = page.Locator("[data-testid='spreadsheet-embedded']");
        await embedded.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(200);

        Assert.AreEqual(0, await embedded.Locator(".tm-spreadsheet-toolbar").CountAsync(),
            "the embedded (Notion/block) mode keeps its flat chrome: no ribbon");
        Assert.AreEqual(0, await embedded.Locator(".tm-spreadsheet__header").CountAsync(),
            "the embedded mode has no document header");
        Assert.AreEqual(1, await embedded.Locator(".tm-spreadsheet-sheet-tabs").CountAsync(),
            "the sheet tabs stay in the embedded mode");

        await ShotPhase5Async(page, "embedded--desktop");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_FormulaBar_FxIsPermanent_AndConfirmCancelCommitAndRevert()
    {
        var page = await OpenPhase5RibbonDemoAsync();
        var component = DemoComponent(page);

        // The fx mark is part of the bar before editing starts.
        Assert.AreEqual("fx", (await component.Locator(".tm-spreadsheet-formula-bar__fx").InnerTextAsync()).Trim());
        Assert.AreEqual(0, await component.Locator(".tm-spreadsheet-formula-bar__confirm").CountAsync(),
            "the ✓ button only exists while the bar is editing");

        await component.Locator(".tm-spreadsheet-formula-bar__display").ClickAsync();
        var input = page.Locator("[data-testid='tm-spreadsheet-formula-bar-input']").First;
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        Assert.AreEqual(1, await component.Locator(".tm-spreadsheet-formula-bar__confirm").CountAsync());
        Assert.AreEqual(1, await component.Locator(".tm-spreadsheet-formula-bar__cancel").CountAsync());
        Assert.AreEqual("Confirm formula", await component.Locator(".tm-spreadsheet-formula-bar__confirm").GetAttributeAsync("aria-label"));
        Assert.AreEqual("Cancel formula", await component.Locator(".tm-spreadsheet-formula-bar__cancel").GetAttributeAsync("aria-label"));
        await ShotPhase5Async(page, "formula-bar-editing--desktop");

        // ✗ reverts: the editor closes and nothing is committed.
        await input.FillAsync("Discarded value");
        await component.Locator(".tm-spreadsheet-formula-bar__cancel").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual(0, await page.Locator("[data-testid='tm-spreadsheet-formula-bar-input']").CountAsync(),
            "the ✗ closes the edit session");

        // ✓ commits the typed text.
        await component.Locator(".tm-spreadsheet-formula-bar__display").ClickAsync();
        var secondInput = page.Locator("[data-testid='tm-spreadsheet-formula-bar-input']").First;
        await secondInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await secondInput.FillAsync("Committed value");
        await component.Locator(".tm-spreadsheet-formula-bar__confirm").ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var display = (await component.Locator(".tm-spreadsheet-formula-bar__display").InnerTextAsync()).Trim();
        Assert.AreEqual("Committed value", display, "the ✓ commits the edited text");
        await ShotPhase5Async(page, "formula-bar-committed--desktop");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_DeleteSplit_OffersRowAndColumn()
    {
        var page = await OpenPhase5RibbonDemoAsync();
        var component = DemoComponent(page);

        await component.Locator(".tm-spreadsheet-toolbar__button[title='Delete options']").ClickAsync();
        var menu = component.Locator(".tm-spreadsheet-toolbar__split-menu");
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        Assert.AreEqual(1, await menu.Locator("[title='Delete row']").CountAsync());
        Assert.AreEqual(1, await menu.Locator("[title='Delete column']").CountAsync());
        await ShotPhase5Async(page, "delete-split--desktop");

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);
        Assert.AreEqual(0, await component.Locator(".tm-spreadsheet-toolbar__split-menu").CountAsync(),
            "Escape closes the delete menu");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_SheetTabs_CloseButtonsAndOverflowArrows()
    {
        var page = await OpenPhase5RibbonDemoAsync(1280, 860);
        var component = DemoComponent(page);

        var initialTabs = await component.Locator(".tm-spreadsheet-sheet-tab").CountAsync();
        Assert.IsTrue(initialTabs >= 1, "the demo workbook has at least one sheet");
        Assert.AreEqual(0, await component.Locator(".tm-spreadsheet-sheet-tabs__scroll-button").CountAsync(),
            "three tabs fit, so no overflow arrows");

        // Add sheets until the strip overflows.
        var add = component.Locator(".tm-spreadsheet-sheet-tabs__add");
        for (var i = 0; i < 12; i++)
        {
            await add.ClickAsync();
            await page.WaitForTimeoutAsync(150);
        }

        var arrows = component.Locator(".tm-spreadsheet-sheet-tabs__scroll-button");
        await arrows.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual(2, await arrows.CountAsync(), "an overflowing strip gets both scroll arrows");

        var scroller = component.Locator(".tm-spreadsheet-sheet-tabs__scroll");
        var before = await scroller.EvaluateAsync<double>("el => el.scrollLeft");
        await component.Locator(".tm-spreadsheet-sheet-tabs__scroll-button[data-direction='right']").ClickAsync();
        await page.WaitForTimeoutAsync(500);
        var after = await scroller.EvaluateAsync<double>("el => el.scrollLeft");
        Assert.IsTrue(after > before, $"the right arrow scrolls the strip (before {before}, after {after})");

        await ShotPhase5Async(page, "sheet-tabs-overflow--desktop");

        // Close buttons are the default and deleting still works from the context menu.
        var addedTabs = await component.Locator(".tm-spreadsheet-sheet-tab").CountAsync();
        Assert.AreEqual(addedTabs, await component.Locator(".tm-spreadsheet-sheet-tab__close").CountAsync(),
            "every tab paints its × close affordance (the default)");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_SheetTabs_ContextMenu_DeletesWithoutTheCloseButton()
    {
        var page = await OpenPhase5RibbonDemoAsync();
        var component = DemoComponent(page);

        // A workbook must keep at least one sheet, so add one before deleting it.
        await component.Locator(".tm-spreadsheet-sheet-tabs__add").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        var tabsBefore = await component.Locator(".tm-spreadsheet-sheet-tab").CountAsync();
        Assert.AreEqual(2, tabsBefore, "the added sheet renders");
        await component.Locator(".tm-spreadsheet-sheet-tab").Last.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });
        // Review round: the tab menu is rendered by the HOST (TmSpreadsheetContextMenu, a sibling of the
        // layout container) so its fixed surface is viewport-anchored; the entries are the shared
        // role=menuitem buttons. The class hook is kept for host/CSS compatibility.
        var contextMenu = component.Locator(".tm-spreadsheet-sheet-tabs__context-menu");
        await contextMenu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await contextMenu.Locator(".tm-spreadsheet-context-menu__item, .tm-spreadsheet-sheet-tabs__context-item",
                new() { HasTextString = "Delete" })
            .First.DispatchEventAsync("click");
        await page.WaitForTimeoutAsync(400);

        Assert.AreEqual(tabsBefore - 1, await component.Locator(".tm-spreadsheet-sheet-tab").CountAsync(),
            "the context menu deletes a sheet even though the × is only one of two paths");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_RibbonMenus_AreNotClippedByTheRibbonRow()
    {
        var page = await OpenPhase5RibbonDemoAsync();
        var component = DemoComponent(page);

        // The ribbon row scrolls horizontally; a menu painted inside it would be clipped. Every menu is
        // a top-layer panel now, so the centre of its LAST item must be the topmost element at that point.
        // Delete split: the last entry is Delete column.
        await component.Locator("[title='Delete options']").ClickAsync();
        var deleteColumn = component.Locator(".tm-spreadsheet-toolbar__split-menu [title='Delete column']");
        await deleteColumn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        Assert.IsTrue(await IsTopmostAsync(deleteColumn, "!!el.closest(\"[title='Delete column']\")"),
            "Delete column is the topmost element at its centre (the menu is not clipped by the ribbon row)");

        var deletePanel = component.Locator(".tm-spreadsheet-toolbar__split-menu");
        var deleteSurface = await ReadMenuSurfaceAsync(deletePanel);
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", deleteSurface.Background,
            $"the delete menu paints an opaque surface (got '{deleteSurface.Background}')");
        Assert.AreNotEqual("0px", deleteSurface.BorderTopWidth, "the menu card has a border");
        Assert.IsFalse(string.IsNullOrEmpty(deleteSurface.BoxShadow) || deleteSurface.BoxShadow == "none",
            "the menu card carries the popover shadow");
        var deleteItemStyle = await ReadItemStyleAsync(deleteColumn);
        Assert.AreEqual("flex", deleteItemStyle.Display, "the destination item keeps its scoped layout");
        Assert.IsTrue(double.Parse(deleteItemStyle.PaddingLeft!.Replace("px", string.Empty), CultureInfo.InvariantCulture) > 0,
            "the item keeps its scoped padding");
        await ShotPhase5Async(page, "menus-delete--desktop");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);

        // Border picker: the last entry is More Borders...
        await component.Locator("[title='Border picker']").ClickAsync();
        var moreBorders = component.Locator(".tm-border-picker__dropdown .tm-border-picker__item").Last;
        await moreBorders.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        Assert.IsTrue(await IsTopmostAsync(moreBorders, "el.classList.contains(\"tm-border-picker__item\")"),
            "the last border entry is the topmost element at its centre");

        var borderPanel = component.Locator(".tm-border-picker__dropdown");
        var borderSurface = await ReadMenuSurfaceAsync(borderPanel);
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", borderSurface.Background,
            $"the border picker paints an opaque surface (got '{borderSurface.Background}')");
        Assert.AreEqual("flex", borderSurface.Display, "the border picker is a column card");
        Assert.AreEqual("auto", await borderPanel.EvaluateAsync<string>("el => getComputedStyle(el).overflowY"),
            "a long border list scrolls inside its card");
        var itemBackground = await FirstOpaqueBackgroundAsync(moreBorders);
        Assert.IsNotNull(itemBackground, "the last border item sits on a non-transparent ancestor surface");
        var borderItemStyle = await ReadItemStyleAsync(moreBorders);
        Assert.AreEqual("flex", borderItemStyle.Display, "the border item keeps its scoped layout");
        Assert.AreEqual("start", borderItemStyle.TextAlign, "the border item keeps its scoped text alignment");
        await ShotPhase5Async(page, "menus-border--desktop");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);

        // Text colour: the last palette swatch.
        await component.Locator("[title='Text color picker']").ClickAsync();
        var swatches = component.Locator(".tm-color-picker-inline__dropdown [data-palette-index]");
        await swatches.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        Assert.IsTrue(await IsTopmostAsync(swatches.Last, "!!el.closest(\".tm-color-palette\")"),
            "the last colour swatch is the topmost element at its centre");
        var colourSurface = await ReadMenuSurfaceAsync(component.Locator(".tm-color-picker-inline__dropdown"));
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", colourSurface.Background,
            $"the colour popover paints an opaque surface (got '{colourSurface.Background}')");
        Assert.AreNotEqual("0px", colourSurface.BorderTopWidth, "the colour popover has a border");
        await ShotPhase5Async(page, "menus-text-color--desktop");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);

        // Fill colour: same contract.
        await component.Locator("[title='Fill color picker']").ClickAsync();
        var fillSwatches = component.Locator(".tm-color-picker-inline__dropdown [data-palette-index]");
        await fillSwatches.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await ShotPhase5Async(page, "menus-fill-color--desktop");
        await page.Keyboard.PressAsync("Escape");

        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase5_FontSizeField_ShowsBothDigits_WithoutClipping()
    {
        var page = await OpenPhase5RibbonDemoAsync();
        var component = DemoComponent(page);

        var sizeSelect = component.Locator("[data-tm-group='font'] select").Nth(1);
        Assert.AreEqual("11", await sizeSelect.EvaluateAsync<string>("el => el.options[el.selectedIndex]?.text ?? ''"),
            "the font-size select shows the active size");
        Assert.IsTrue(await sizeSelect.EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"),
            "the narrow field must not clip the size text");
        Assert.IsTrue(await sizeSelect.EvaluateAsync<bool>("el => el.clientWidth >= 34"),
            "the size field is wide enough for two digits at the toolbar font size");

        await ShotPhase5Async(page, "font-size-field--desktop");
        await page.CloseAsync();
    }
}
