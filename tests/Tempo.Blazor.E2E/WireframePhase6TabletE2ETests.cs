using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Wireframe redesign plan ce19aa8c, phase 6 — the tablet layout. The editor resolves its layout from its OWN container
/// (mobile &lt; 768, tablet 768-1199, desktop ≥ 1200): on a tablet the command row is two toolbars, the left panel
/// (Components / Pages / Layers) is an overlay drawer, the inspector a side sheet with a close ×, at most ONE sheet is open,
/// the minimap is hidden and every control reaches 44 px on a coarse pointer. Runs with <c>HasTouch=true</c> at 1024×768
/// (demo sidebar hidden, container ≈ 960 px) and 1366×1024 (demo sidebar kept, container ≈ 1006 px: still tablet), plus the 1440 desktop
/// regression and the tablet → desktop flip. Screenshots land in <c>__screenshots__/wireframe/phase6/</c> only with
/// <c>TM_E2E_UPDATE_BASELINE=1</c> (otherwise in the git-ignored TestResults folder); review them against
/// <c>output/imagegen/tempo-reimagined/Tempo.Blazor.Wireframe/wireframe-editor--tablet.png</c> and
/// <c>wireframe-layers-panel--tablet.png</c>.
/// </summary>
[TestClass]
public sealed class WireframePhase6TabletE2ETests : WasmTestBase
{
    private const string EditorUrl = "/wireframe-editor";

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ShotDirectory => UpdateBaseline
        ? Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "wireframe", "phase6")
        : Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "wireframe-phase6");

    // ── Toolbar ───────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1024, 768, false)]
    [DataRow(1366, 1024, true)]
    [Description("Tablet toolbar: two rows, neither clipped (scrollWidth <= clientWidth), Export and Import inside the editor, every command reachable, targets >= 44px on a coarse pointer")]
    public async Task Tablet_Toolbar_TwoRows_NoClipping_44pxTargets(int width, int height, bool keepSidebar)
    {
        var (page, editor) = await OpenAsync(width, height, keepSidebar, dark: false);

        Assert.AreEqual("tablet", await editor.GetAttributeAsync("data-editor-layout"), "the editor container resolves to tablet");
        Assert.AreEqual(2, await editor.Locator("[data-testid^='wd-toolbar-row-']").CountAsync(), "two toolbar rows");

        var metrics = await editor.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            e => {
                const box = e.getBoundingClientRect();
                const rows = Array.from(e.querySelectorAll('[data-testid^="wd-toolbar-row-"]'));
                const bars = Array.from(e.querySelectorAll('.tm-toolbar'));
                const inside = el => { const r = el.getBoundingClientRect(); return r.width > 0 && r.left >= box.left - 1 && r.right <= box.right + 1; };
                const visibleButtons = Array.from(e.querySelectorAll('.tm-toolbar-btn:not(.tm-toolbar-item--collapsed)')).filter(b => b.getBoundingClientRect().width > 0);
                return {
                    rowOverflow: rows.map(r => r.scrollWidth - r.clientWidth),
                    barOverflow: bars.map(b => b.scrollWidth - b.clientWidth),
                    exportInside: inside(e.querySelector('[data-testid="wd-export"]')),
                    importInside: inside(e.querySelector('[data-testid="wd-import"]')),
                    editorOverflow: e.scrollWidth - e.clientWidth,
                    smallest: Math.min(...visibleButtons.map(b => Math.round(b.getBoundingClientRect().height))),
                    count: visibleButtons.length,
                    coarse: matchMedia('(pointer: coarse)').matches
                };
            }
            """);
        foreach (var overflow in metrics.GetProperty("rowOverflow").EnumerateArray())
            Assert.IsTrue(overflow.GetInt32() <= 0, $"a toolbar row is clipped by {overflow.GetInt32()}px");
        foreach (var overflow in metrics.GetProperty("barOverflow").EnumerateArray())
            Assert.IsTrue(overflow.GetInt32() <= 0, $"a toolbar is clipped by {overflow.GetInt32()}px");
        Assert.IsTrue(metrics.GetProperty("exportInside").GetBoolean(), "Export is inside the editor");
        Assert.IsTrue(metrics.GetProperty("importInside").GetBoolean(), "Import is inside the editor");
        Assert.IsTrue(metrics.GetProperty("editorOverflow").GetInt32() <= 0, "the editor does not scroll horizontally");

        // Every command stays reachable: on the bar or in a More menu.
        foreach (var (tooltip, menuText) in new[] { ("Undo", "Undo"), ("Redo", "Redo"), ("Fit to view", "Fit to view"), ("Select", "Select"), ("Zoom in", "Zoom in") })
        {
            var command = await CommandAsync(page, editor, tooltip, menuText);
            Assert.IsTrue(await command.CountAsync() > 0, $"'{tooltip}' is reachable");
            await page.Keyboard.PressAsync("Escape");
        }

        if (!metrics.GetProperty("coarse").GetBoolean())
            Assert.Inconclusive("Chromium did not report (pointer: coarse) for the touch context; the 44px rule cannot be asserted here.");
        Assert.IsTrue(metrics.GetProperty("smallest").GetInt32() >= 44, $"toolbar buttons reach 44px, the smallest is {metrics.GetProperty("smallest").GetInt32()}px");

        await ShotAsync(editor, $"tablet-{width}--toolbar-light");
    }

    // ── One sheet at a time, the full touch flow ──────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1024, 768, false)]
    [DataRow(1366, 1024, true)]
    [Description("Touch flow: open the left drawer, tap a component (it is inserted and the drawer yields to the inspector sheet), edit a property in the sheet, close it, switch page, toggle a layer's visibility")]
    public async Task Tablet_TouchFlow_Drawer_Insert_Inspector_Pages_Layers(int width, int height, bool keepSidebar)
    {
        var (page, editor) = await OpenAsync(width, height, keepSidebar, dark: false);
        Assert.AreEqual(0, await editor.Locator(".tm-editor-shell__overlay").CountAsync(), "no sheet at load");
        Assert.AreEqual(0, await editor.Locator(".tm-editor-shell__panel").CountAsync(), "no docked panel on a tablet");
        Assert.AreEqual(0, await editor.Locator(".tm-wd-editor__minimap-wrap").CountAsync(), "the minimap is hidden on a tablet");

        // 1. Open the left drawer and tap a component.
        var countBefore = await editor.Locator("g[data-el-id]").CountAsync();
        await editor.Locator("[data-testid='wd-toggle-left']").TapAsync();
        var drawer = editor.Locator("[data-testid='wd-left-drawer']");
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await ShotAsync(editor, $"tablet-{width}--drawer-components");
        Assert.IsTrue(await drawer.Locator(".tm-wd-toolbox--single-column").CountAsync() == 1, "single-column toolbox with names");
        Assert.IsTrue((await drawer.Locator(".tm-wd-toolbox__item-name").First.InnerTextAsync()).Trim().Length > 0, "the rows carry names");
        await drawer.Locator(".tm-wd-toolbox__item[data-component-type='TmButton']").First.TapAsync();

        // 2. The element is on the canvas, the drawer closed and the inspector sheet took over.
        await page.WaitForFunctionAsync("n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length > n",
            countBefore, new PageWaitForFunctionOptions { Timeout = 10_000 });
        var inspector = editor.Locator("[data-testid='wd-inspector-sheet']");
        await inspector.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual(0, await editor.Locator("[data-testid='wd-left-drawer']").CountAsync(), "the drawer closed by the insertion");
        Assert.AreEqual(1, await editor.Locator(".tm-editor-shell__overlay").CountAsync(), "exactly one sheet is open");

        // 3. Edit a property in the sheet.
        var name = inspector.Locator("[data-testid='wd-element-name']");
        await name.FillAsync("Book now");
        await name.PressAsync("Tab");
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id]')?.getAttribute('aria-label') === 'Book now' || !!document.querySelectorAll('.tm-wd-editor')[0].querySelector('g[data-el-id] title')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await ShotAsync(editor, $"tablet-{width}--inspector-sheet");

        // 4. Close the sheet with the ×: the canvas gets its width back.
        await inspector.Locator("[data-testid='wd-sheet-close']").TapAsync();
        await editor.Locator(".tm-editor-shell__overlay").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });

        // 5. Switch page: the Pages tab of the drawer.
        await editor.Locator("[data-testid='wd-toggle-left']").TapAsync();
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await drawer.Locator("[role='tab']", new() { HasTextString = "Pages" }).TapAsync();
        await editor.Locator("[data-testid='wd-page-add']").TapAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('.tm-wd-pages__item').length >= 2",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        var firstPage = editor.Locator(".tm-wd-pages__select").First;
        await firstPage.TapAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-pages__select[aria-current=\"page\"]') === document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-pages__select')",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        // 6. Toggle a layer's visibility from the Layers tab.
        await drawer.Locator("[role='tab']", new() { HasTextString = "Layers" }).TapAsync();
        var visibility = editor.Locator(".tm-wd-layers__visibility").First;
        await visibility.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        var pressedBefore = await visibility.GetAttributeAsync("aria-pressed");
        await visibility.TapAsync();
        await page.WaitForFunctionAsync("v => document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-layers__visibility').getAttribute('aria-pressed') !== v",
            pressedBefore, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await ShotAsync(editor, $"tablet-{width}--drawer-layers");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: opening the inspector while the drawer is open closes the drawer (and the reverse): never two sheets; the toggles carry aria-expanded (and no aria-pressed)")]
    public async Task Tablet_AtMostOneSheet_TogglesReflectState()
    {
        var (_, editor) = await OpenAsync(1024, 768, keepSidebar: false, dark: false);
        var left = editor.Locator("[data-testid='wd-toggle-left']");
        var right = editor.Locator("[data-testid='wd-toggle-inspector']");

        await left.TapAsync();
        await editor.Locator("[data-testid='wd-left-drawer']").WaitForAsync();
        Assert.AreEqual("true", await left.GetAttributeAsync("aria-expanded"));

        await right.TapAsync();
        await editor.Locator("[data-testid='wd-inspector-sheet']").WaitForAsync();
        await editor.Locator("[data-testid='wd-left-drawer']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        Assert.AreEqual(1, await editor.Locator(".tm-editor-shell__overlay").CountAsync());
        Assert.AreEqual("false", await left.GetAttributeAsync("aria-expanded"));
        Assert.AreEqual("true", await right.GetAttributeAsync("aria-expanded"));

        await left.TapAsync();
        await editor.Locator("[data-testid='wd-left-drawer']").WaitForAsync();
        Assert.AreEqual(1, await editor.Locator(".tm-editor-shell__overlay").CountAsync(), "still exactly one sheet");

        await left.TapAsync();
        await editor.Locator(".tm-editor-shell__overlay").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: Escape closes the sheet that holds the focus and returns the focus to the control that opened it (the toolbar toggle) - the core shell's overlay contract")]
    public async Task Tablet_Escape_ClosesSheet_AndRestoresFocusToTheToggle()
    {
        var (page, editor) = await OpenAsync(1024, 768, keepSidebar: false, dark: false);
        await editor.Locator("[data-testid='wd-toggle-inspector']").TapAsync();
        var sheet = editor.Locator("[data-testid='wd-inspector-sheet']");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForFunctionAsync("() => !!document.activeElement?.closest('[data-testid=\"wd-inspector-sheet\"]')",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        await page.Keyboard.PressAsync("Escape");

        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
        await page.WaitForFunctionAsync("() => document.activeElement?.getAttribute('data-testid') === 'wd-toggle-inspector'",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: the sheet is a core shell overlay - the left drawer paints over the canvas without narrowing it, and the toolbar toggle that is pressed is the only way back besides × / Escape")]
    public async Task Tablet_LeftDrawer_IsACoreOverlay_AndDoesNotNarrowTheCanvas()
    {
        var (_, editor) = await OpenAsync(1024, 768, keepSidebar: false, dark: false);
        var svg = editor.Locator(".tm-wd-canvas__svg");
        var before = (await svg.BoundingBoxAsync())!.Width;

        await editor.Locator("[data-testid='wd-toggle-left']").TapAsync();
        var drawer = editor.Locator("[data-testid='wd-left-drawer']");
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        Assert.IsTrue(await drawer.EvaluateAsync<bool>("e => e.classList.contains('tm-editor-shell__overlay') && e.classList.contains('tm-editor-shell__overlay--left')"), "the drawer is the core overlay");
        Assert.AreEqual("false", await drawer.GetAttributeAsync("aria-modal"), "non-modal");
        Assert.AreEqual(0, await editor.Locator(".tm-drawer, [popover]").CountAsync(), "no modal drawer, nothing in the top layer");
        Assert.AreEqual(before, (await svg.BoundingBoxAsync())!.Width, 2, "the overlay drawer never narrows the canvas");
        var box = (await drawer.BoundingBoxAsync())!;
        var editorBox = (await editor.BoundingBoxAsync())!;
        Assert.IsTrue(box.Width <= editorBox.Width * 0.6 + 1, $"the drawer covers at most 60 % of the editor ({box.Width:0} of {editorBox.Width:0})");
    }
    // ── Canvas viewport ───────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Opening the inspector sheet gives the canvas less room and the page stays fully inside it (re-fitted by script); closing gives the room back")]
    public async Task Tablet_InspectorSheet_RelayoutsCanvas_PageStaysVisible()
    {
        var (page, editor) = await OpenAsync(1024, 768, keepSidebar: false, dark: false);
        var svg = editor.Locator(".tm-wd-canvas__svg");
        var wide = (await svg.BoundingBoxAsync())!.Width;
        await page.EvaluateAsync("() => { window.__wdSvg = document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__svg'); }");

        await editor.Locator("[data-testid='wd-toggle-inspector']").TapAsync();
        await editor.Locator("[data-testid='wd-inspector-sheet']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForTimeoutAsync(500);
        var narrow = (await svg.BoundingBoxAsync())!.Width;
        Assert.IsTrue(narrow < wide - 100, $"the canvas narrows beside the sheet ({wide:0} -> {narrow:0})");

        var fits = await page.EvaluateAsync<bool>(
            """
            () => {
                const e = document.querySelectorAll('.tm-wd-editor')[0];
                const s = e.querySelector('.tm-wd-canvas__svg').getBoundingClientRect();
                const p = e.querySelector('.tm-wd-canvas__page').getBoundingClientRect();
                return p.left >= s.left - 1 && p.right <= s.right + 1 && p.width > 0;
            }
            """);
        Assert.IsTrue(fits, "the page stays inside the narrower canvas");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => window.__wdSvg === document.querySelectorAll('.tm-wd-editor')[0].querySelector('.tm-wd-canvas__svg') && window.__wdSvg.isConnected"),
            "the canvas svg is the same node: the sheet did not rebuild it");

        await editor.Locator("[data-testid='wd-sheet-close']").TapAsync();
        await editor.Locator(".tm-editor-shell__overlay").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual(wide, (await svg.BoundingBoxAsync())!.Width, 2, "the canvas gets its width back");
    }

    // ── Layout flips ──────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: tablet -> desktop (viewport grows to 1440) drops the sheets, docks the panels and returns to ONE toolbar")]
    public async Task Tablet_ToDesktop_Flip_DocksPanels()
    {
        var (page, editor) = await OpenAsync(1024, 768, keepSidebar: false, dark: false);
        await editor.Locator("[data-testid='wd-toggle-inspector']").TapAsync();
        await editor.Locator("[data-testid='wd-inspector-sheet']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await page.SetViewportSizeAsync(1440, 900);
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-wd-editor')[0].getAttribute('data-editor-layout') === 'desktop'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(800);

        Assert.AreEqual(0, await editor.Locator(".tm-editor-shell__overlay").CountAsync(), "the sheets are gone");
        Assert.IsTrue(await editor.Locator("[data-region='left'] .tm-wd-toolbox").CountAsync() > 0, "the left panel is docked again");
        Assert.IsTrue(await editor.Locator("[data-region='right'] .tm-wd-props").CountAsync() > 0, "the inspector is docked again");
        Assert.AreEqual(0, await editor.Locator("[data-testid^='wd-toolbar-row-']").CountAsync(), "one toolbar on desktop");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Desktop regression at 1440: one toolbar row, docked toolbox and inspector, the minimap, no sheets, no tablet toggles")]
    public async Task Desktop_1440_Unchanged()
    {
        var (_, editor) = await OpenAsync(1440, 900, keepSidebar: false, dark: false, touch: false);

        Assert.AreEqual("desktop", await editor.GetAttributeAsync("data-editor-layout"));
        Assert.AreEqual(1, await editor.Locator(".tm-toolbar").CountAsync(), "one TmToolbar");
        Assert.AreEqual(0, await editor.Locator("[data-testid^='wd-toolbar-row-']").CountAsync());
        Assert.IsTrue(await editor.Locator("[data-region='left'] .tm-wd-toolbox").IsVisibleAsync(), "docked toolbox");
        Assert.AreEqual(0, await editor.Locator(".tm-wd-toolbox--single-column").CountAsync(), "3-column tiles on desktop");
        Assert.IsTrue(await editor.Locator("[data-region='right'] .tm-wd-props").IsVisibleAsync(), "docked inspector");
        Assert.AreEqual(1, await editor.Locator(".tm-wd-editor__minimap-wrap").CountAsync(), "the minimap card");
        Assert.AreEqual(0, await editor.Locator(".tm-editor-shell__overlay, [data-testid='wd-toggle-left']").CountAsync(), "no tablet sheets or toggles");
        var overflow = await editor.Locator(".tm-toolbar").EvaluateAsync<int>("b => b.scrollWidth - b.clientWidth");
        Assert.IsTrue(overflow <= 0, $"the desktop toolbar is not clipped ({overflow}px)");
    }

    // ── Screenshots (light / dark) ────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("1024×768 touch, light and dark: the empty tablet editor, the components drawer, the inspector sheet with a selection, the layers drawer")]
    public async Task Screenshots_Tablet_1024_LightAndDark()
    {
        Directory.CreateDirectory(ShotDirectory);
        foreach (var dark in new[] { false, true })
        {
            var tag = dark ? "dark" : "light";
            var (page, editor) = await OpenAsync(1024, 768, keepSidebar: false, dark);
            await ShotAsync(editor, $"tablet-1024-{tag}--empty");

            await editor.Locator("[data-testid='wd-toggle-left']").TapAsync();
            await editor.Locator("[data-testid='wd-left-drawer']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await page.WaitForTimeoutAsync(400);
            await ShotAsync(editor, $"tablet-1024-{tag}--drawer-components");

            await editor.Locator("[data-testid='wd-left-drawer'] .tm-wd-toolbox__item[data-component-type='TmButton']").First.TapAsync();
            await editor.Locator("[data-testid='wd-inspector-sheet'] [data-testid='wd-element-name']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await page.WaitForTimeoutAsync(600);
            await ShotAsync(editor, $"tablet-1024-{tag}--inspector-selected");

            await editor.Locator("[data-testid='wd-toggle-left']").TapAsync();
            await editor.Locator("[data-testid='wd-left-drawer'] [role='tab']", new() { HasTextString = "Layers" }).TapAsync();
            await editor.Locator(".tm-wd-layers__item").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await page.WaitForTimeoutAsync(400);
            await ShotAsync(editor, $"tablet-1024-{tag}--drawer-layers");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(IPage Page, ILocator Editor)> OpenAsync(int width, int height, bool keepSidebar, bool dark, bool touch = true)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = touch,
            IsMobile = false,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        page.Console += (_, message) => { if (message.Type is "error") TestContext.WriteLine($"[console.{message.Type}] {message.Text}"); };
        page.PageError += (_, error) => TestContext.WriteLine($"[pageerror] {error}");
        // The layout resolves from the editor's CONTAINER and the first measurement decides which panels dock, so the demo sidebar
        // is removed BEFORE the app boots when the test wants the viewport's width for the editor (not after).
        await page.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en');"
            + (keepSidebar ? string.Empty :
            """
            document.addEventListener('DOMContentLoaded', () => {
                const style = document.createElement('style');
                style.textContent = 'aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }';
                document.head.appendChild(style);
            });
            """));
        await GotoAppReadyAsync(page, $"{BaseUrl}{EditorUrl}");

        await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-canvas__svg", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });
        if (dark)
        {
            // The demo toggle lives in the sidebar the test hides: switch the theme the way the toggle does.
            await page.EvaluateAsync("() => { document.documentElement.setAttribute('data-theme', 'dark'); document.documentElement.classList.add('dark'); }");
        }

        var editor = page.Locator(".tm-wd-editor").First;
        await editor.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(1500);   // layout resolution + toolbar overflow measurement settle
        return (page, editor);
    }

    /// <summary>A toolbar command: the visible bar button (tooltip prefix) or, when it collapsed, its entry in a "More" menu.</summary>
    private static async Task<ILocator> CommandAsync(IPage page, ILocator editor, string tooltipPrefix, string menuText)
    {
        var bar = editor.Locator($".tm-toolbar button.tm-toolbar-btn:not(.tm-toolbar-item--collapsed)[aria-label^='{tooltipPrefix}']");
        if (await bar.CountAsync() > 0) return bar.First;
        foreach (var more in await editor.Locator("button.tm-toolbar-more").AllAsync())
        {
            await more.ClickAsync();
            var entry = page.Locator("[role='menu'] [role='menuitem'], [role='menu'] [role='menuitemcheckbox']").Filter(new() { HasTextString = menuText });
            if (await entry.CountAsync() > 0) return entry.First;
            await page.Keyboard.PressAsync("Escape");
        }

        return page.Locator("[data-never]");
    }

    private async Task ShotAsync(ILocator locator, string name)
    {
        await locator.Page.Mouse.MoveAsync(0, 0);
        var path = Path.Combine(ShotDirectory, name + ".png");
        Directory.CreateDirectory(ShotDirectory);
        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png, Timeout = 60_000 });
        TestContext.AddResultFile(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
