using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 6 of the Spreadsheet redesign — the RESPONSIVE CHROME. The layout follows the width of the
/// spreadsheet's own CONTAINER (a container query plus the core <c>TmLayoutObserver</c>), never the viewport,
/// so the assertions here read <c>data-layout</c> from the component's root rather than from the page.
/// <para>
/// Screenshots land in <c>__screenshots__/spreadsheet/</c> as <c>phase6-*.png</c> and are reviewed against
/// output/imagegen/tempo-reimagined/Tempo.Blazor.Spreadsheet/spreadsheet--tablet.png,
/// <c>spreadsheet--mobile.png</c>, <c>spreadsheet-filter-dropdown--{tablet,mobile}.png</c> and
/// <c>spreadsheet-filter-dropdown--filter_button-title--{desktop,tablet,mobile}.png</c>.
/// </para>
/// <para>
/// The tablet/mobile pages are opened with <c>hasTouch</c> — the 44px targets are a
/// <c>(any-pointer: coarse)</c> rule, so a desktop-pointer context could never observe them.
/// </para>
/// </summary>
public partial class SpreadsheetE2ETests
{
    /// <summary>A canvas-space point returned by the JS probe (the filter-button rect centre).</summary>
    private sealed class Phase6Point
    {
        public int X { get; set; }

        public int Y { get; set; }
    }

    private const int Phase6TabletWidth = 1024;
    private const int Phase6MobileWidth = 390;
    private const int Phase6NarrowWidth = 320;
    private const int Phase6DesktopWidth = 1440;

    private async Task<IPage> OpenPhase6DemoAsync(int width, int height, bool hasTouch)
    {
        var page = await CreatePageAsync(hasTouch);
        await page.SetViewportSizeAsync(width, height);
        await GotoAppReadyAsync(page, $"{BaseUrl}/spreadsheet");
        await WaitForCanvasGridReadyAsync(page, DemoGrid(page));
        // The demo page is a long scrolling document; centre the spreadsheet so the whole chrome (the
        // compact rows, the sheet tabs and the mobile action bar) is on screen for the shot AND for the
        // clicks — a sticky bottom bar re-positions while Playwright scrolls, which can loop.
        await page.EvaluateAsync(@"() => {
            const card = document.querySelector(""[data-testid='spreadsheet-demo']"");
            if (card) card.scrollIntoView({ block: 'center' });
        }");
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private async Task ShotPhase6Async(IPage page, string name)
    {
        var path = ShotPath("phase6-" + name);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(path);
    }

    private static Task<string?> DataLayoutAsync(ILocator spreadsheet)
        => spreadsheet.GetAttributeAsync("data-layout");

    /// <summary>The demo's first spreadsheet — the wide canvas-renderer instance of <c>/spreadsheet</c>.</summary>
    private static ILocator Phase6DemoSpreadsheet(IPage page)
        => page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet").First;

    // ── Tablet: two compact rows, no tab strip ───────────────────────────────

    [TestMethod]
    public async Task Phase6_Tablet_1024_TwoCompactRows_NoTabStrip()
    {
        var page = await OpenPhase6DemoAsync(Phase6TabletWidth, 820, hasTouch: true);

        var spreadsheet = Phase6DemoSpreadsheet(page);
        Assert.AreEqual("tablet", await DataLayoutAsync(spreadsheet),
            "the demo's card inside a 1024px viewport is ~660px wide, i.e. the tablet chrome (640-1023px)");

        var toolbar = spreadsheet.Locator(".tm-spreadsheet-toolbar");
        Assert.AreEqual("tablet", await toolbar.GetAttributeAsync("data-toolbar-layout"));
        Assert.AreEqual(0, await toolbar.Locator(".tm-spreadsheet-toolbar__tabs").CountAsync(),
            "the tablet ribbon has no tab strip — the whole command set lives on the two rows");
        Assert.AreEqual(2, await toolbar.Locator("[data-toolbar-row]").CountAsync(),
            "tablet = two rows (primary + formatting)");

        var primary = toolbar.Locator("[data-toolbar-row='primary']");
        var formatting = toolbar.Locator("[data-toolbar-row='formatting']");
        foreach (var command in new[] { "undo", "redo", "paste", "cut", "copy", "format-cells", "insert-image", "insert-link" })
        {
            Assert.IsTrue(await primary.Locator($"[data-command-id='{command}']").CountAsync() > 0,
                $"tablet row 1 must offer '{command}'");
        }

        foreach (var command in new[] { "bold", "italic", "underline", "align-left", "number-format", "percent", "filter" })
        {
            Assert.IsTrue(await formatting.Locator($"[data-command-id='{command}']").CountAsync() > 0,
                $"tablet row 2 must offer '{command}'");
        }

        // Full editing starts at the tablet: an advanced tool is reachable through the row's "More".
        await spreadsheet.Locator(".tm-spreadsheet-compact-toolbar__row .tm-toolbar-more").First.ClickAsync();
        await page.WaitForTimeoutAsync(300);
        var menu = page.Locator(".tm-toolbar-more__menu").First;
        Assert.IsTrue(await menu.Locator("text=Name Manager").CountAsync() > 0,
            "the tablet's More menu carries the advanced data tools (full editing starts at the tablet)");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);

        await ShotPhase6Async(page, "tablet-1024-main");
    }

    // ── Mobile: compact header, one row, action bar, no status bar ───────────

    [TestMethod]
    public async Task Phase6_Mobile_390_CompactChrome_WithActionBar()
    {
        var page = await OpenPhase6DemoAsync(Phase6MobileWidth, 844, hasTouch: true);

        var spreadsheet = Phase6DemoSpreadsheet(page);
        Assert.AreEqual("mobile", await DataLayoutAsync(spreadsheet), "390px of container is the G2 phone chrome");

        // Review round: the compact header renders ONLY when the host gives it content (title / slots); an
        // empty 50px row is worse than none, and undo/redo therefore lead the formatting row.
        Assert.AreEqual(0, await spreadsheet.Locator(".tm-spreadsheet__mobile-header").CountAsync(),
            "the headerless demo instance must not render an empty header row");
        Assert.AreEqual(1, await spreadsheet.Locator(".tm-spreadsheet-toolbar [data-command-id='undo']").CountAsync(),
            "undo lives in the formatting row when there is no header");
        Assert.AreEqual(1, await spreadsheet.Locator(".tm-spreadsheet-toolbar [data-command-id='redo']").CountAsync());
        Assert.AreEqual(1, await spreadsheet.Locator("[data-toolbar-row]").CountAsync(),
            "the phone chrome is ONE basic-formatting row");
        Assert.AreEqual(0, await spreadsheet.Locator(".tm-spreadsheet-toolbar__tabs").CountAsync(),
            "the phone chrome has no ribbon tab strip");
        Assert.AreEqual(0, await spreadsheet.Locator(".tm-spreadsheet-statusbar").CountAsync(),
            "the phone chrome hides the status bar");

        var bar = spreadsheet.Locator(".tm-mobile-action-bar");
        Assert.AreEqual(1, await bar.CountAsync(), "the core mobile action bar is the phone's bottom bar");
        foreach (var action in new[] { "insert", "format", "insert-image" })
        {
            Assert.IsTrue(await bar.Locator($"[data-action-id='{action}']").CountAsync() > 0,
                $"the action bar offers '{action}'");
        }

        // Sheet tabs sit ABOVE the action bar (the reference's order).
        var tabs = await spreadsheet.Locator(".tm-spreadsheet-sheet-tabs").First.BoundingBoxAsync();
        var barBox = await bar.First.BoundingBoxAsync();
        Assert.IsNotNull(tabs);
        Assert.IsNotNull(barBox);
        Assert.IsTrue(barBox!.Y >= tabs!.Y + tabs.Height - 1,
            "the sheet tabs are above the mobile action bar");

        // SP3: the mobile layout fills its container.
        var height = await spreadsheet.EvaluateAsync<string>("(el) => el.style.height");
        Assert.AreEqual("100%", height, "SP3: only the mobile layout fills its container");

        // The More menu carries the sheet/data/zoom groups and the aggregations the status bar used to show.
        // A synthetic click: the bar is STICKY at the bottom of a container that is taller than the phone
        // viewport, so Playwright's scroll-into-view never reaches a stable position for it. The event
        // path (and therefore the Blazor handler) is the same one a finger produces.
        await bar.Locator(".tm-mobile-action-bar__more").DispatchEventAsync("click");
        await page.WaitForTimeoutAsync(300);
        var more = page.Locator(".tm-mobile-action-bar__menu, [role='menu']").First;
        Assert.IsTrue(await more.Locator("text=Add sheet").CountAsync() > 0, "More → the sheet group");
        Assert.IsTrue(await more.Locator("text=Filter").CountAsync() > 0, "More → the data group");
        Assert.IsTrue(await more.Locator("text=Summary").CountAsync() > 0,
            "More → the status-bar aggregations, reactivated under Summary");

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = ShotPath("phase6-mobile-390-more-sheet"),
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(ShotPath("phase6-mobile-390-more-sheet"));
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(250);

        await ShotPhase6Async(page, "mobile-390-main");
    }

    [TestMethod]
    public async Task Phase6_Mobile_390_ContextMenu_IsASheet()
    {
        var page = await OpenPhase6DemoAsync(Phase6MobileWidth, 844, hasTouch: true);

        var grid = Phase6DemoSpreadsheet(page).Locator(".tm-spreadsheet-canvas-grid");
        var box = await grid.BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.ClickAsync(box!.X + box.Width / 2, box.Y + 60,
            new() { Button = MouseButton.Right });

        await page.WaitForSelectorAsync(".tm-spreadsheet-context-sheet", new() { State = WaitForSelectorState.Visible });
        Assert.AreEqual(1, await page.Locator(".tm-spreadsheet-context-menu--sheet").CountAsync(),
            "the grid context menu is a bottom sheet on a phone");

        await ShotPhase6Async(page, "mobile-390-context-sheet");
    }

    // ── Review round: the context menus sit at the gesture, outside the container ──

    [TestMethod]
    public async Task Phase6_Desktop_ContextMenu_IsAtTheGesture_AndItsFirstItemIsHitTestable()
    {
        var page = await OpenPhase6DemoAsync(Phase6DesktopWidth, 1000, hasTouch: false);
        var grid = Phase6DemoSpreadsheet(page).Locator(".tm-spreadsheet-canvas-grid");
        var box = await grid.BoundingBoxAsync();
        Assert.IsNotNull(box);
        var clickX = box!.X + 140;
        var clickY = box.Y + 90;

        await page.Mouse.ClickAsync(clickX, clickY, new() { Button = MouseButton.Right });
        await page.WaitForSelectorAsync(".tm-spreadsheet-context-menu",
            new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });

        var menu = page.Locator(".tm-spreadsheet-context-menu").First;
        var menuBox = await menu.BoundingBoxAsync();
        Assert.IsNotNull(menuBox);
        // The layout container establishes `contain: layout`; before the fix the menu's fixed surface was
        // positioned against the CARD and drifted by hundreds of pixels from the pointer.
        Assert.IsTrue(Math.Abs(menuBox!.X - clickX) < 60 && Math.Abs(menuBox.Y - clickY) < 80,
            $"the menu must open at the gesture (click {clickX:0},{clickY:0} vs menu {menuBox.X:0},{menuBox.Y:0})");

        var hit = await menu.Locator(".tm-spreadsheet-context-menu__item").First.EvaluateAsync<bool>(@"(el) => {
            const r = el.getBoundingClientRect();
            const top = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2);
            return !!top && (top === el || el.contains(top));
        }");
        Assert.IsTrue(hit, "the first menu item must be the topmost element at its own centre (nothing clips it)");

        await ShotPhase6Async(page, "desktop-1440-context-menu");
    }

    [TestMethod]
    public async Task Phase6_Desktop_SheetTabContextMenu_IsAtTheGesture()
    {
        var page = await OpenPhase6DemoAsync(Phase6DesktopWidth, 1000, hasTouch: false);
        var tab = Phase6DemoSpreadsheet(page).Locator(".tm-spreadsheet-sheet-tab").First;
        var box = await tab.BoundingBoxAsync();
        Assert.IsNotNull(box);

        await page.Mouse.ClickAsync(box!.X + box.Width / 2, box.Y + box.Height / 2,
            new() { Button = MouseButton.Right });
        await page.WaitForSelectorAsync(".tm-spreadsheet-sheet-tabs__context-menu",
            new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });

        var menuBox = await page.Locator(".tm-spreadsheet-sheet-tabs__context-menu").BoundingBoxAsync();
        Assert.IsNotNull(menuBox);
        Assert.IsTrue(Math.Abs(menuBox!.X - (box.X + box.Width / 2)) < 80
                      && Math.Abs(menuBox.Y - (box.Y + box.Height / 2)) < 120,
            $"the tab menu opens at the tab (tab {box.X:0},{box.Y:0} vs menu {menuBox.X:0},{menuBox.Y:0})");
    }

    [TestMethod]
    public async Task Phase6_Mobile_ContextSheet_SurvivesGridScroll_AndClosesOnEscape()
    {
        var page = await OpenPhase6DemoAsync(Phase6MobileWidth, 844, hasTouch: true);
        var spreadsheet = Phase6DemoSpreadsheet(page);
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");

        // Scroll the grid down first: the sheet used to be an inline drawer INSIDE the scroller, so it was
        // clipped and off-screen after any scroll.
        await grid.EvaluateAsync("el => { el.scrollTop = 400; el.scrollLeft = 40; }");
        await page.WaitForTimeoutAsync(400);

        var box = await grid.BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.ClickAsync(box!.X + box.Width / 2, box.Y + box.Height / 2,
            new() { Button = MouseButton.Right });
        await page.WaitForSelectorAsync(".tm-spreadsheet-context-sheet",
            new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });

        var firstItemHit = await page.Locator(".tm-spreadsheet-context-menu--sheet .tm-spreadsheet-context-menu__item")
            .First.EvaluateAsync<bool>(@"(el) => {
                const r = el.getBoundingClientRect();
                const top = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2);
                return !!top && (el === top || el.contains(top));
            }");
        Assert.IsTrue(firstItemHit,
            "the first sheet item is reachable — the action bar must not cover the sheet (it is suppressed)");

        await ShotPhase6Async(page, "mobile-390-context-sheet-after-scroll");

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(".tm-spreadsheet-context-sheet",
            new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });
        Assert.AreEqual(0, await page.Locator(".tm-spreadsheet-context-sheet").CountAsync(),
            "Escape closes the phone's cell menu");
    }

    [TestMethod]
    public async Task Phase6_Mobile_FilterSheet_Footer_IsAboveTheActionBar()
    {
        var page = await OpenPhase6DemoAsync(Phase6MobileWidth, 844, hasTouch: true);
        Assert.IsTrue(await TryOpenMobileFilterSheetAsync(page), "the phone filter dropdown opens as a sheet");

        var ok = page.Locator(".tm-spreadsheet-filter-dropdown__btn--ok").First;
        var okHit = await ok.EvaluateAsync<bool>(@"(el) => {
            const r = el.getBoundingClientRect();
            const top = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2);
            return !!top && (el === top || el.contains(top));
        }");
        Assert.IsTrue(okHit, "the sheet's OK is on top (the sticky action bar is suppressed while a sheet is open)");
        await ShotPhase6Async(page, "mobile-390-filter-sheet-ok");
    }

    // ── Review round 2: the resize dialog is host-owned, so it is viewport-anchored ──

    [TestMethod]
    public async Task Phase6_ResizeDialog_FromANarrowHost_IsAnchoredToTheViewport()
    {
        var page = await OpenPhase6DemoAsync(Phase6DesktopWidth, 1000, hasTouch: false);

        // The 420px host inside a 1440 page: its container has `contain: layout`, so a dialog rendered INSIDE
        // it used to be positioned against the card.
        var host = page.Locator("[data-testid='spreadsheet-responsive-mobile']");
        await host.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(600);
        var spreadsheet = host.Locator(".tm-spreadsheet").First;
        Assert.AreEqual("mobile", await DataLayoutAsync(spreadsheet));

        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        var box = await grid.BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.ClickAsync(box!.X + box.Width / 2, box.Y + box.Height / 2,
            new() { Button = MouseButton.Right });
        await page.WaitForSelectorAsync(".tm-spreadsheet-context-sheet",
            new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });

        await page.Locator(".tm-spreadsheet-context-menu__item", new() { HasTextString = "Set column width" })
            .First.DispatchEventAsync("click");
        await page.WaitForSelectorAsync(".tm-spreadsheet-resize-dialog",
            new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });

        var overlay = await page.Locator(".tm-spreadsheet-resize-dialog.tm-modal-overlay").First.BoundingBoxAsync();
        var viewport = page.ViewportSize!;
        Assert.IsNotNull(overlay);
        Assert.IsTrue(overlay!.Width >= viewport.Width - 1 && overlay.Height >= viewport.Height - 1,
            $"the dialog overlay covers the VIEWPORT, not the card (overlay {overlay.Width:0}x{overlay.Height:0}, "
            + $"viewport {viewport.Width}x{viewport.Height})");

        // Enter applies and closes; Escape must keep working too.
        await page.Locator(".tm-spreadsheet-resize-dialog__input").FillAsync("90");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForSelectorAsync(".tm-spreadsheet-resize-dialog",
            new() { State = WaitForSelectorState.Detached, Timeout = 60_000 });

        await ShotPhase6Async(page, "narrow-host-resize-dialog");
    }

    [TestMethod]
    public async Task Phase6_Desktop_ContextMenu_Keyboard_RovesAndEscapeCloses()
    {
        var page = await OpenPhase6DemoAsync(Phase6DesktopWidth, 1000, hasTouch: false);
        var grid = Phase6DemoSpreadsheet(page).Locator(".tm-spreadsheet-canvas-grid");
        var box = await grid.BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.ClickAsync(box!.X + 140, box.Y + 90, new() { Button = MouseButton.Right });
        await page.WaitForSelectorAsync(".tm-spreadsheet-context-menu",
            new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });

        // The first entry takes the focus on open, the arrows rove, Home/End jump.
        var first = await page.EvaluateAsync<string>(
            "() => document.activeElement?.getAttribute('data-context-menu-id') || 'none'");
        Assert.AreEqual("format-cells", first, "the first menu entry is focused on open");

        await page.Keyboard.PressAsync("ArrowDown");
        var second = await page.EvaluateAsync<string>(
            "() => document.activeElement?.getAttribute('data-context-menu-id') || 'none'");
        Assert.AreEqual("copy", second, "ArrowDown moves to the next entry");

        await page.Keyboard.PressAsync("End");
        var last = await page.EvaluateAsync<string>(
            "() => document.activeElement?.getAttribute('data-context-menu-id') || 'none'");
        Assert.AreEqual("clear-all", last, "End jumps to the last entry");

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync(".tm-spreadsheet-context-menu",
            new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });
    }

    // ── Review round: the fullscreen Notion editor is a container, not the viewport ──

    [TestMethod]
    public async Task Phase6_NotionEditModal_At390_IsMobile_AndKeepsTheFooterInView()
    {
        var page = await OpenPhase6DemoAsync(Phase6DesktopWidth, 1000, hasTouch: false);
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForTimeoutAsync(2500);

        var opened = await page.EvaluateAsync<bool>(@"() => {
            const btn = document.querySelector(""[data-testid='spreadsheet-demo']"");
            return !!btn;
        }");
        _ = opened;

        // The demo's notion page hosts a spreadsheet block on the seeded page; open its editor.
        var edit = page.Locator(".tm-notion-spreadsheet-block .tm-notion-spreadsheet-block__edit-btn").First;
        if (await edit.CountAsync() == 0)
        {
            Assert.Inconclusive("the seeded Notion page has no spreadsheet block to edit on this run");
            return;
        }

        await page.SetViewportSizeAsync(390, 844);
        await edit.ClickAsync();
        await page.WaitForSelectorAsync(".tm-notion-spreadsheet-edit-modal .tm-spreadsheet",
            new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        await page.WaitForTimeoutAsync(800);

        Assert.AreEqual("mobile", await page.Locator(".tm-notion-spreadsheet-edit-modal .tm-spreadsheet")
            .First.GetAttributeAsync("data-layout"),
            "the fullscreen modal is a 390px container, so the editor resolves the phone chrome");

        var footer = await page.Locator(".tm-notion-spreadsheet-edit-modal__footer").BoundingBoxAsync();
        var viewport = page.ViewportSize!;
        Assert.IsNotNull(footer);
        Assert.IsTrue(footer!.Y + footer.Height <= viewport.Height + 1,
            $"the Save / Discard footer stays inside the viewport (footer bottom {footer.Y + footer.Height:0} vs {viewport.Height})");

        // And at 568x320 (landscape phone) the footer must still be visible.
        await page.SetViewportSizeAsync(568, 320);
        await page.WaitForTimeoutAsync(800);
        var smallFooter = await page.Locator(".tm-notion-spreadsheet-edit-modal__footer").BoundingBoxAsync();
        Assert.IsNotNull(smallFooter);
        Assert.IsTrue(smallFooter!.Y + smallFooter.Height <= 321,
            $"the footer stays in view at 568x320 (bottom {smallFooter.Y + smallFooter.Height:0})");

        await ShotPhase6Async(page, "notion-edit-modal-568x320");
    }

    // ── Review round: the tablet chrome in a reference-width (~1000px) container ──

    [TestMethod]
    public async Task Phase6_Tablet_ReferenceWidth_ShowsTheTwoRowsWithLabels()
    {
        // The demo column is viewport - ~330px, so 1330 gives a ~1000px container: still < 1024, i.e. the
        // TABLET chrome in the width the reference mockup was drawn at.
        var page = await OpenPhase6DemoAsync(1330, 830, hasTouch: true);
        var spreadsheet = Phase6DemoSpreadsheet(page);
        Assert.AreEqual("tablet", await DataLayoutAsync(spreadsheet));
        Assert.AreEqual(2, await spreadsheet.Locator("[data-toolbar-row]").CountAsync());
        Assert.IsTrue(await spreadsheet.Locator(".tm-spreadsheet-compact-toolbar__row [data-tm-group]").CountAsync() > 0,
            "the compact rows keep the frozen data-tm-group contract");
        await ShotPhase6ElementAsync(page, "tablet-reference-width-chrome");
    }

    // ── 320px: no horizontal overflow ───────────────────────────────────────

    [TestMethod]
    public async Task Phase6_Narrow_320_HasNoHorizontalOverflow()
    {
        var page = await OpenPhase6DemoAsync(Phase6NarrowWidth, 780, hasTouch: true);

        var spreadsheet = Phase6DemoSpreadsheet(page);
        Assert.AreEqual("mobile", await DataLayoutAsync(spreadsheet));

        var overflow = await page.EvaluateAsync<int[]>(@"() => {
            const root = document.documentElement;
            const sheet = document.querySelector(""[data-testid='spreadsheet-demo'] .tm-spreadsheet"");
            const bar = sheet ? sheet.querySelector("".tm-spreadsheet-toolbar"") : null;
            return [
                root.scrollWidth - root.clientWidth,
                sheet ? sheet.scrollWidth - sheet.clientWidth : 0,
                bar ? bar.scrollWidth - bar.clientWidth : 0
            ];
        }");
        Assert.AreEqual(0, overflow[0], "the page must not scroll horizontally at 320");
        Assert.AreEqual(0, overflow[1], "the spreadsheet must not scroll horizontally at 320");
        Assert.AreEqual(0, overflow[2], "the compact ribbon must not overflow at 320");

        await ShotPhase6Async(page, "mobile-320-main");
    }

    // ── UX artifacts: one element shot per layout ───────────────────────────

    [TestMethod]
    public async Task Phase6_Screenshots_OfEachLayout()
    {
        // Element shots, not page shots: the demo page is a long document, so a viewport capture of a
        // 512px-tall phone chrome lands in the middle of the grid and shows nothing of the chrome. The
        // element shot is the artifact the UX review reads against spreadsheet--{tablet,mobile}.png.
        var tablet = await OpenPhase6DemoAsync(Phase6TabletWidth, 820, hasTouch: true);
        await ShotPhase6ElementAsync(tablet, "tablet-1024-chrome");

        var mobile = await OpenPhase6DemoAsync(Phase6MobileWidth, 844, hasTouch: true);
        await ShotPhase6ElementAsync(mobile, "mobile-390-chrome");

        var narrow = await OpenPhase6DemoAsync(Phase6NarrowWidth, 780, hasTouch: true);
        await ShotPhase6ElementAsync(narrow, "mobile-320-chrome");

        var desktop = await OpenPhase6DemoAsync(Phase6DesktopWidth, 1000, hasTouch: false);
        await ShotPhase6ElementAsync(desktop, "desktop-1440-chrome");

        // The filter dropdown on a phone: a bottom sheet with 44px rows and a sticky Cancel / OK bar.
        var filter = await OpenPhase6DemoAsync(Phase6MobileWidth, 844, hasTouch: true);
        if (await TryOpenMobileFilterSheetAsync(filter))
        {
            await ShotPhase6ElementAsync(filter, "mobile-390-filter-sheet");
        }

        // The Notion page. NOTE the finding: TmNotionSpreadsheetBlock does NOT embed TmSpreadsheet (the block
        // is a preview plus a document-open dialog, the editor lives in the host), so there is no narrow
        // CONTAINER-embedded spreadsheet to shoot here — the artifact documents the page and the block's
        // absence of an embedded editor. The container-not-viewport contract is proven by
        // Phase6_DesktopPage_NarrowHosts_UseTheContainerMode (420px/760px hosts inside a 1440 page).
        var notion = await OpenPhase6DemoAsync(Phase6DesktopWidth, 1000, hasTouch: false);
        await notion.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(notion);
        await notion.WaitForTimeoutAsync(4500);
        Assert.AreEqual(0, await notion.Locator(".tm-notion-spreadsheet-block .tm-spreadsheet").CountAsync(),
            "the Notion block hosts a preview, never the editor component; a width-based mode therefore "
            + "cannot be asserted on the block itself");
        var notionShots = ShotPath("phase6-notion-1440-blocks");
        await notion.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = notionShots,
            Type = ScreenshotType.Png,
            FullPage = false,
            Timeout = 60_000,
        });
        TestContext.AddResultFile(notionShots);
    }

    /// <summary>
    /// Opens the header filter dropdown on the PHONE chrome: select A1:A2, enable the auto-filter from the
    /// action bar's More menu, then click the canvas' own filter button (the JS exposes its rects through
    /// <c>__tmSpreadsheetCanvas.filterButtons</c>, so the spec never guesses a coordinate).
    /// </summary>
    private async Task<bool> TryOpenMobileFilterSheetAsync(IPage page)
    {
        var spreadsheet = Phase6DemoSpreadsheet(page);
        var grid = spreadsheet.Locator(".tm-spreadsheet-canvas-grid");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = 120, Y = 60 } });
        await page.Keyboard.PressAsync("Shift+ArrowDown");

        await spreadsheet.Locator(".tm-mobile-action-bar__more").DispatchEventAsync("click");
        await page.WaitForTimeoutAsync(300);
        var filterEntry = page.Locator(".tm-mobile-action-bar__menu [data-action-id='filter']");
        if (await filterEntry.CountAsync() == 0)
        {
            return false;
        }

        await filterEntry.DispatchEventAsync("click");
        await page.WaitForTimeoutAsync(400);

        var buttons = await grid.EvaluateHandleAsync("(el) => (el.__tmSpreadsheetCanvas?.filterButtons || [])");
        var count = await buttons.EvaluateAsync<int>("(list) => list.length");
        if (count == 0)
        {
            return false;
        }

        var pos = await grid.EvaluateAsync<Phase6Point>(
            @"(el) => {
                const b = (el.__tmSpreadsheetCanvas?.filterButtons || [])[0];
                return { x: Math.round(b.x + b.w / 2), y: Math.round(b.y + b.h / 2) };
            }");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = pos.X, Y = pos.Y } });

        // This box renders the demo at ~3 fps, so Playwright's stability checks alone can eat 15-30s per
        // action; the waits here are generous on purpose (the artifact is a screenshot, not a latency test).
        await page.WaitForSelectorAsync(".tm-spreadsheet-filter-dropdown--sheet",
            new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        Assert.AreEqual(1, await page.Locator(".tm-spreadsheet-filter-host").CountAsync(),
            "the phone filter dropdown is the core TmDrawer sheet");
        Assert.IsTrue(await page.Locator(".tm-spreadsheet-filter-dropdown__actions--sticky").CountAsync() > 0,
            "the sheet keeps Cancel / OK pinned to its bottom edge");
        return true;
    }

    /// <summary>
    /// An element shot taken from the FULL-PAGE bitmap at the element's page coordinates. Playwright's own
    /// element screenshot scrolls the element to the viewport top, where the demo's sticky navbar paints
    /// over its first ~46px — at a phone width that is exactly the compact header, so the artifact would
    /// hide the very chrome it documents. Capturing the page bitmap with a clip leaves the page alone.
    /// </summary>
    private async Task ShotPhase6ElementAsync(IPage page, string name)
    {
        var path = ShotPath("phase6-" + name);
        var spreadsheet = Phase6DemoSpreadsheet(page);

        // The demo shell's top bar is sticky: Chromium's full-page capture paints it at the top of the tile
        // that holds the clip, which would cover the phone chrome's compact header. Hide the SHELL's sticky
        // and fixed surfaces only — never one inside the spreadsheet (the mobile action bar is itself
        // position: sticky and must stay visible).
        await page.EvaluateAsync(@"() => {
            for (const el of document.querySelectorAll('body *')) {
                if (el.closest('.tm-spreadsheet')) continue;
                const pos = getComputedStyle(el).position;
                if (pos === 'sticky' || pos === 'fixed') el.style.visibility = 'hidden';
            }
        }");
        var clip = await spreadsheet.EvaluateAsync<Phase6Clip>(@"(el) => {
            const r = el.getBoundingClientRect();
            return {
                X = 0, Y = 0, Width = 0, Height = 0
            };
        }".Replace("X = 0, Y = 0, Width = 0, Height = 0",
                   "X: Math.max(0, r.left + window.scrollX), Y: Math.max(0, r.top + window.scrollY), Width: Math.ceil(r.width), Height: Math.ceil(r.height)"));
        await page.WaitForTimeoutAsync(600);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png,
            FullPage = true,
            Clip = new Clip { X = clip.X, Y = clip.Y, Width = clip.Width, Height = clip.Height },
            Timeout = 120_000,
            Animations = ScreenshotAnimations.Disabled,
        });
        TestContext.AddResultFile(path);
    }

    private sealed class Phase6Clip
    {
        public float X { get; set; }

        public float Y { get; set; }

        public float Width { get; set; }

        public float Height { get; set; }
    }

    // ── Coarse pointer: 44px targets and the 2-tap budget ───────────────────

    [TestMethod]
    public async Task Phase6_Tablet_CoarsePointer_ToolbarTargetsAre44()
    {
        var page = await OpenPhase6DemoAsync(Phase6TabletWidth, 820, hasTouch: true);

        var tooSmall = await Phase6DemoSpreadsheet(page)
            .Locator(".tm-spreadsheet-toolbar button")
            .EvaluateAllAsync<string[]>(@"(buttons) => buttons.filter((b) => {
                const r = b.getBoundingClientRect();
                if (r.width === 0 || r.height === 0) return false;
                if (b.closest('[inert]') || b.getAttribute('aria-hidden') === 'true') return false;
                return r.width < 44 - 0.5 || r.height < 44 - 0.5;
            }).map((b) => {
                const r = b.getBoundingClientRect();
                return (b.getAttribute('data-command-id') || b.textContent.trim().slice(0, 20)) + ' ' +
                    Math.round(r.width) + 'x' + Math.round(r.height);
            })");

        Assert.AreEqual(0, tooSmall.Length,
            "every visible compact-ribbon button is a 44x44 target on a coarse pointer: " + string.Join(", ", tooSmall));
    }

    [TestMethod]
    public async Task Phase6_Mobile_BasicCommands_ReachableInTwoTaps()
    {
        var page = await OpenPhase6DemoAsync(Phase6MobileWidth, 844, hasTouch: true);

        var spreadsheet = Phase6DemoSpreadsheet(page);
        var toolbar = spreadsheet.Locator(".tm-spreadsheet-toolbar");

        foreach (var command in new[] { "bold", "italic", "underline", "number-format" })
        {
            var onBar = toolbar.Locator($"[data-command-id='{command}']");
            var visible = 0;
            for (var i = 0; i < await onBar.CountAsync(); i++)
            {
                if (await onBar.Nth(i).IsVisibleAsync())
                {
                    visible++;
                }
            }

            if (visible > 0)
            {
                continue; // one tap
            }

            // Two taps: open the row's "More" and pick the command there.
            await toolbar.Locator(".tm-toolbar-more").First.DispatchEventAsync("click");
            await page.WaitForSelectorAsync(".tm-toolbar-more__menu",
                new() { State = WaitForSelectorState.Visible, Timeout = 60_000 });
            var menu = page.Locator(".tm-toolbar-more__menu").First;
            var expected = command == "number-format" ? "Number" : char.ToUpperInvariant(command[0]) + command[1..];
            var entry = menu.Locator("button").Filter(new() { HasTextString = expected }).First;
            await entry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForSelectorAsync(".tm-toolbar-more__menu",
                new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });
        }
    }

    // ── Container, not viewport: the demo's fixed-width boxes on a 1440 page ─

    [TestMethod]
    public async Task Phase6_DesktopPage_NarrowHosts_UseTheContainerMode()
    {
        var page = await OpenPhase6DemoAsync(Phase6DesktopWidth, 1000, hasTouch: false);

        var wide = page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet").First;
        Assert.AreEqual("desktop", await DataLayoutAsync(wide),
            "the wide instance inside a 1440 page is the desktop chrome");

        var narrow = page.Locator("[data-testid='spreadsheet-responsive-mobile'] .tm-spreadsheet");
        Assert.AreEqual("mobile", await DataLayoutAsync(narrow),
            "a 420px box inside the SAME 1440 page is a phone — the container decides, not the viewport");

        var tablet = page.Locator("[data-testid='spreadsheet-responsive-tablet'] .tm-spreadsheet");
        Assert.AreEqual("tablet", await DataLayoutAsync(tablet), "a 760px box is the tablet chrome");

        var pinned = page.Locator("[data-testid='spreadsheet-responsive-pinned'] .tm-spreadsheet");
        Assert.AreEqual("desktop", await DataLayoutAsync(pinned),
            "LayoutMode=Desktop pins the desktop chrome inside a 420px box");

        await page.Locator("[data-testid='spreadsheet-responsive-mobile']").ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);
        await ShotPhase6Async(page, "desktop-1440-responsive-hosts");

        await ShotPhase6Async(page, "desktop-1440-regression");
    }
}
