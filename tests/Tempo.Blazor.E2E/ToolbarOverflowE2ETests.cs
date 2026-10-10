using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F4 lane: the overflow toolbar on the REAL components (the /toolbar-forms demo drives
/// <c>TmToolbar</c>/<c>TmToolbarButton</c> as shipped) at 1440, 1024, 390 and 320 — phone sizes with
/// touch emulation. The checks are computed values and real key presses, never markup alone: how many
/// buttons really fit, that a collapsed copy is invisible and unreachable, the roving tabindex
/// (one tab stop, arrows skip hidden/disabled), the More menu keyboard (ArrowUp/Down, Home/End,
/// typeahead, Escape returning focus), the bottom sheet on a phone with 44px items, Labels=Auto
/// measured on the toolbar and the label-below ribbon. Every screenshot is viewport-only at the real
/// viewport height (FullPage resets touch emulation).
/// </summary>
[TestClass]
public class ToolbarOverflowE2ETests : WasmTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "toolbar-overflow");

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private async Task<IPage> OpenAsync(int width, int height, bool touch, string locale = "en-US", bool reducedMotion = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = locale,
            IgnoreHTTPSErrors = true,
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/toolbar-forms");
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='toolbar-overflow-demo']").WaitForAsync();
        return page;
    }

    private static ILocator Bar(IPage page, string testId) => page.Locator($"[data-testid='{testId}']");

    /// <summary>Waits until the toolbar's fit measurement has settled (the trigger renders or nothing collapses).</summary>
    private static async Task SettleAsync(IPage page, string testId)
    {
        await page.WaitForTimeoutAsync(600);
        var bar = Bar(page, testId);
        await bar.WaitForAsync();
        // Two frames: measurement is rAF-coalesced, the re-render follows the interop call.
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
    }

    private async Task<string> ShootAsync(IPage page, ILocator subject, string name)
    {
        // Frame the subject near the top of the viewport; the capture itself is viewport-only.
        await subject.EvaluateAsync("el => { el.scrollIntoView({ block: 'start', behavior: 'instant' }); window.scrollBy({ top: -120, behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(500);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        var path = Path.Combine(ShotDir, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        return path;
    }

    private static async Task<string[]> OnBarTextsAsync(ILocator bar)
        => await bar.Locator("button.tm-toolbar-btn:not(.tm-toolbar-item--collapsed)")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");

    private static async Task<string[]> CollapsedTextsAsync(ILocator bar)
        => await bar.Locator(".tm-toolbar-item--collapsed")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");

    private static async Task<string[]> MenuTextsAsync(IPage page)
        => await page.Locator("[role='menu'] [role='menuitem']")
            .EvaluateAllAsync<string[]>("els => els.map(e => e.textContent.trim())");

    private static async Task AssertNoPageOverflowAsync(IPage page, string where)
    {
        var overflow = await page.EvaluateAsync<double>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.IsTrue(overflow <= 1, $"{where}: the page must not scroll horizontally (overflow {overflow}px)");
    }

    // ── 1440: everything fits; only the overflow-only buttons live in the menu ─────────────────────

    [TestMethod]
    public async Task Desktop_1440_WideToolbar_KeepsAutoButtons_AndOverflowOnlyLiveInTheMenu()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        await SettleAsync(page, "toolbar-overflow-1100");
        var bar = Bar(page, "toolbar-overflow-1100");

        CollectionAssert.AreEqual(
            new[] { "New", "Open", "Save", "Copy", "Paste" },
            await OnBarTextsAsync(bar),
            "all five Auto buttons fit in 1100px, in the written order, with visible labels");
        Assert.AreEqual(0, await bar.Locator(".tm-toolbar-item--collapsed").CountAsync());

        var trigger = bar.Locator("button.tm-toolbar-more");
        await Assertions.Expect(trigger).ToBeVisibleAsync();
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-expanded"));
        await trigger.ClickAsync();
        await Assertions.Expect(page.Locator("[role='menu']")).ToBeVisibleAsync();
        CollectionAssert.AreEqual(new[] { "Export", "Delete" }, await MenuTextsAsync(page), "OverflowOnly buttons are ONLY in the menu");
        Assert.AreEqual(0, await page.Locator(".tm-overlay-panel-sheet").CountAsync(), "a desktop viewport shows the anchored popover");

        var shot = await ShootAsync(page, bar, "toolbar-1440-menu-open");
        Assert.IsTrue(File.Exists(shot));
        await AssertNoPageOverflowAsync(page, "1440");
    }

    // ── 1024 (tablet): the 380px toolbar collapses Secondary buttons, Primary stay ─────────────────

    [TestMethod]
    public async Task Tablet_1024_MidToolbar_CollapsesSecondaryFirst_AndTheCollapsedCopyIsInvisibleAndInert()
    {
        var page = await OpenAsync(1024, 768, touch: false);
        await SettleAsync(page, "toolbar-overflow-380");
        var bar = Bar(page, "toolbar-overflow-380");

        var onBar = await OnBarTextsAsync(bar);
        var collapsed = await CollapsedTextsAsync(bar);
        Assert.IsTrue(collapsed.Length > 0, "380px cannot hold five labelled buttons - something must collapse");
        CollectionAssert.IsSubsetOf(new[] { "New", "Save" }, onBar, "Primary buttons are the last to leave");
        foreach (var name in collapsed)
        {
            CollectionAssert.Contains(new[] { "Open", "Copy", "Paste" }, name, $"\"{name}\" collapsed but is not Secondary");
        }

        // The collapsed copy is real but unseen and unreachable.
        var first = bar.Locator(".tm-toolbar-item--collapsed").First;
        Assert.AreEqual("hidden", await first.EvaluateAsync<string>("el => getComputedStyle(el).visibility"));
        Assert.AreEqual("absolute", await first.EvaluateAsync<string>("el => getComputedStyle(el).position"));
        Assert.IsTrue(await first.EvaluateAsync<bool>("el => el.inert"), "inert: not focusable, not clickable");
        Assert.AreEqual("true", await first.GetAttributeAsync("aria-hidden"));

        // Both places: every collapsed button is offered in the menu, in written order, then the pinned ones.
        await bar.Locator("button.tm-toolbar-more").ClickAsync();
        var menu = await MenuTextsAsync(page);
        CollectionAssert.AreEqual(
            new[] { "Open", "Copy", "Paste", "Export", "Delete" }.Where(t => menu.Contains(t)).ToArray(), menu);
        foreach (var name in collapsed) CollectionAssert.Contains(menu, name);
        CollectionAssert.Contains(menu, "Export");
        CollectionAssert.Contains(menu, "Delete");

        // No stray divider: a divider with no visible button after it inside its group is hidden.\n        var dangling = await bar.Locator(".tm-toolbar-divider").EvaluateAllAsync<bool[]>("""
            els => els.map(d => { let n = d.nextElementSibling; while (n) { if (n.matches('button.tm-toolbar-btn:not(.tm-toolbar-item--collapsed)')) return false; n = n.nextElementSibling; } return getComputedStyle(d).visibility !== 'hidden'; })
            """);
        Assert.IsFalse(dangling.Any(x => x), "a divider followed only by collapsed buttons must not be painted");

        // The bar never scrolls or wraps: its row stays inside its own box.
        var fits = await bar.EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1");
        Assert.IsTrue(fits, "the toolbar must not overflow horizontally");

        await ShootAsync(page, bar, "toolbar-1024-collapsed-menu-open");
        await AssertNoPageOverflowAsync(page, "1024");
    }

    [TestMethod]
    public async Task Tablet_1024_ResizingTheWindow_CollapsesAndExpandsWithoutStickyState()
    {
        var page = await OpenAsync(1024, 768, touch: false);
        await SettleAsync(page, "toolbar-overflow-380");
        var bar = Bar(page, "toolbar-overflow-380");
        var before = await CollapsedTextsAsync(bar);
        Assert.IsTrue(before.Length > 0);

        // A phone-width window narrows the demo wrapper below 380px: strictly more buttons collapse.
        await page.SetViewportSizeAsync(340, 800);
        await SettleAsync(page, "toolbar-overflow-380");
        var narrow = await CollapsedTextsAsync(bar);
        Assert.IsTrue(narrow.Length > before.Length, $"a narrower toolbar collapses more buttons (before {before.Length}, narrow {narrow.Length})");
        foreach (var name in before) CollectionAssert.Contains(narrow, name, "what was collapsed stays collapsed when there is less room");

        await page.SetViewportSizeAsync(1024, 768);
        await SettleAsync(page, "toolbar-overflow-380");
        CollectionAssert.AreEqual(before, await CollapsedTextsAsync(bar), "back at 1024 the same buttons collapse (the partition is a pure function of the room)");
    }
    // ── keyboard: roving tabindex ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Keyboard_RovingTabindex_OneTabStop_ArrowsSkipCollapsedAndDisabled_RtlIndependent()
    {
        var page = await OpenAsync(1024, 768, touch: false);
        await SettleAsync(page, "toolbar-overflow-380");
        var bar = Bar(page, "toolbar-overflow-380");
        Assert.AreEqual("toolbar", await bar.GetAttributeAsync("role"));
        Assert.AreEqual("Document tools", await bar.GetAttributeAsync("aria-label"));

        var stops = await bar.Locator("[tabindex='0']").CountAsync();
        Assert.AreEqual(1, stops, "exactly one tab stop in the toolbar");

        var focusable = await bar.EvaluateAsync<string[]>(
            """
            el => [...el.querySelectorAll('button.tm-toolbar-btn, button.tm-toolbar-more')]
                  .filter(b => !b.disabled && !b.closest('.tm-toolbar-item--collapsed'))
                  .map(b => b.getAttribute('aria-label') || b.textContent.trim())
            """);
        Assert.IsTrue(focusable.Length >= 3, "New, Save and the More trigger at least");

        // Tab enters on the tab stop, ArrowRight walks the visible buttons and wraps, never into a collapsed copy.
        await page.Locator("[data-testid='toolbar-overflow-380'] [tabindex='0']").FocusAsync();
        var visited = new List<string>();
        for (var i = 0; i < focusable.Length + 1; i++)
        {
            visited.Add(await page.EvaluateAsync<string>("() => document.activeElement.getAttribute('aria-label') || document.activeElement.textContent.trim()"));
            await page.Keyboard.PressAsync("ArrowRight");
        }

        CollectionAssert.AreEqual(focusable, visited.Take(focusable.Length).ToArray(), "ArrowRight visits exactly the enabled on-bar controls, in order");
        Assert.AreEqual(visited[0], visited[^1], "and wraps around");

        await page.Keyboard.PressAsync("End");
        Assert.AreEqual(focusable[^1], await page.EvaluateAsync<string>("() => document.activeElement.getAttribute('aria-label') || document.activeElement.textContent.trim()"));
        await page.Keyboard.PressAsync("Home");
        Assert.AreEqual(focusable[0], await page.EvaluateAsync<string>("() => document.activeElement.getAttribute('aria-label') || document.activeElement.textContent.trim()"));
        Assert.AreEqual(1, await bar.Locator("[tabindex='0']").CountAsync(), "the tab stop follows focus");

        // Tab leaves the toolbar (it does not walk its buttons).
        await page.Keyboard.PressAsync("Tab");
        var stillInside = await page.EvaluateAsync<bool>("() => !!document.activeElement.closest('[data-testid=\"toolbar-overflow-380\"]')");
        Assert.IsFalse(stillInside, "Tab moves on to the next control, outside the toolbar");

        // Vertical arrows are left alone: the page may scroll, focus stays.
        await page.Locator("[data-testid='toolbar-overflow-380'] [tabindex='0']").FocusAsync();
        var label = await page.EvaluateAsync<string>("() => document.activeElement.getAttribute('aria-label') || document.activeElement.textContent.trim()");
        await page.Keyboard.PressAsync("ArrowDown");
        Assert.AreEqual(label, await page.EvaluateAsync<string>("() => document.activeElement.getAttribute('aria-label') || document.activeElement.textContent.trim()"));
    }

    // ── More menu keyboard ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Keyboard_MoreMenu_ArrowsHomeEndTypeahead_EscapeReturnsFocus_ActivationInvokesOnce()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        await SettleAsync(page, "toolbar-overflow-300");
        var bar = Bar(page, "toolbar-overflow-300");
        var trigger = bar.Locator("button.tm-toolbar-more");
        await Assertions.Expect(trigger).ToBeVisibleAsync();

        // Focus scrolls the trigger into view smoothly; opening while it still moves would park the panel
        // (anchor not settled) - a user presses Enter after the page stopped, so the test does too.
        await trigger.FocusAsync();
        await page.WaitForTimeoutAsync(1500);
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[role='menu']")).ToBeVisibleAsync();
        Assert.AreEqual("true", await trigger.GetAttributeAsync("aria-expanded"));
        Assert.AreEqual(await page.Locator("[role='menu']").First.EvaluateAsync<string>("el => el.closest('.tm-overlay-panel, [id]')?.id ?? ''"),
            await trigger.GetAttributeAsync("aria-controls"), "aria-controls points at the rendered panel");

        var items = await MenuTextsAsync(page);
        Assert.IsTrue(items.Length >= 3, "Open/Copy/Paste collapse into the menu next to Export/Delete");
        await page.WaitForFunctionAsync("() => !!document.activeElement?.closest(\"[role=menuitem]\")", null, new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual(items[0], await ActiveMenuItemAsync(page), "the first enabled item has initial focus");

        await page.Keyboard.PressAsync("ArrowDown");
        Assert.AreEqual(items[1], await ActiveMenuItemAsync(page));
        await page.Keyboard.PressAsync("ArrowUp");
        await page.Keyboard.PressAsync("ArrowUp");
        Assert.AreEqual(items[^1], await ActiveMenuItemAsync(page), "ArrowUp wraps to the last item");
        await page.Keyboard.PressAsync("Home");
        Assert.AreEqual(items[0], await ActiveMenuItemAsync(page));
        await page.Keyboard.PressAsync("End");
        Assert.AreEqual(items[^1], await ActiveMenuItemAsync(page));

        await page.Keyboard.PressAsync("Home");
        await page.WaitForTimeoutAsync(800);
        await page.Keyboard.PressAsync("e");
        Assert.AreEqual("Export", await ActiveMenuItemAsync(page), "typeahead: E focuses Export");
        await page.WaitForTimeoutAsync(800);
        await page.Keyboard.PressAsync("d");
        Assert.AreEqual("Delete", await ActiveMenuItemAsync(page), "typeahead: D focuses Delete");

        // Only one item is a tab stop inside the menu (roving).
        var menuStops = await page.Locator("[role='menu'] [role='menuitem'][tabindex='0']").CountAsync();
        Assert.AreEqual(1, menuStops);

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("[role='menu']")).ToHaveCountAsync(0);
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-expanded"), "aria-expanded is accurate after Escape");
        Assert.IsTrue(await trigger.EvaluateAsync<bool>("el => el === document.activeElement"), "focus returns to the trigger");

        // Activation: Enter on a menu item invokes the button's OnClick exactly once and closes the menu.
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[role='menu']")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("[role='menu']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-testid='toolbar-overflow-log']")).ToContainTextAsync("Delete");
        Assert.AreEqual(1, await page.Locator("[data-testid='toolbar-overflow-log']").CountAsync());
    }

    private static Task<string> ActiveMenuItemAsync(IPage page)
        => page.EvaluateAsync<string>("() => document.activeElement?.closest('[role=\"menuitem\"]')?.textContent.trim() ?? '(none)'");

    // ── 390 / 320 phones, touch ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Phone_390_Touch_MoreOpensTheBottomSheet_With44pxItems_AndNoPageOverflow()
    {
        var page = await OpenAsync(390, 844, touch: true);
        await SettleAsync(page, "toolbar-overflow-300");
        var bar = Bar(page, "toolbar-overflow-300");
        var trigger = bar.Locator("button.tm-toolbar-more");
        await Assertions.Expect(trigger).ToBeVisibleAsync();

        var box = await trigger.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.Width >= 43.5 && box.Height >= 43.5, $"the More trigger is a 44px target on touch ({box.Width}x{box.Height})");
        foreach (var button in await bar.Locator("button.tm-toolbar-btn:not(.tm-toolbar-item--collapsed)").AllAsync())
        {
            var b = await button.BoundingBoxAsync();
            Assert.IsTrue(b!.Height >= 43.5, $"toolbar buttons are touch targets (height {b.Height})");
        }

        await ShootAsync(page, bar, "toolbar-390-closed");
        await trigger.TapAsync();
        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[role='menu']")).ToBeVisibleAsync();
        foreach (var item in await page.Locator("[role='menu'] [role='menuitem']").AllAsync())
        {
            var b = await item.BoundingBoxAsync();
            Assert.IsTrue(b!.Height >= 43.5, $"menu items are 44px on a phone (height {b.Height})");
        }

        var sheetBox = await sheet.BoundingBoxAsync();
        Assert.IsTrue(Math.Abs(sheetBox!.Y + sheetBox.Height - 844) <= 2, "the sheet is anchored to the viewport bottom");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"), "touch emulation still on");
        var path = Path.Combine(ShotDir, "toolbar-390-sheet-open.png");
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        Assert.IsTrue(File.Exists(path));

        // Choosing an item closes the sheet and runs the action once.
        await page.Locator("[role='menu'] [role='menuitem']").Last.TapAsync();
        await Assertions.Expect(page.Locator(".tm-overlay-panel-sheet")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-testid='toolbar-overflow-log']")).ToContainTextAsync("Delete");
        await AssertNoPageOverflowAsync(page, "390");
    }

    [TestMethod]
    public async Task Phone_320_Touch_Czech_AllToolbars_StayInsideTheViewport()
    {
        var page = await OpenAsync(320, 640, touch: true, locale: "cs-CZ");
        foreach (var id in new[] { "toolbar-overflow-1100", "toolbar-overflow-380", "toolbar-overflow-300", "toolbar-ribbon" })
        {
            await SettleAsync(page, id);
            var inside = await Bar(page, id).EvaluateAsync<bool>("el => { const r = el.getBoundingClientRect(); return r.left >= -1 && r.right <= innerWidth + 1 && el.scrollWidth <= el.clientWidth + 1; }");
            Assert.IsTrue(inside, $"{id}: the toolbar must stay inside the 320px viewport without scrolling");
            Assert.IsTrue(await Bar(page, id).Locator("button.tm-toolbar-more").CountAsync() == 1, $"{id}: a 320px toolbar overflows into More");
        }

        var czTitle = await page.Locator("[data-testid='toolbar-overflow-demo'] h2").InnerTextAsync();
        Assert.AreEqual("Toolbar s přetečením", czTitle, "the demo is localized (cs)");

        var bar = Bar(page, "toolbar-overflow-300");
        await bar.Locator("button.tm-toolbar-more").TapAsync();
        await Assertions.Expect(page.Locator(".tm-overlay-panel-sheet")).ToBeVisibleAsync();
        var texts = await MenuTextsAsync(page);
        CollectionAssert.Contains(texts, "Exportovat");
        CollectionAssert.Contains(texts, "Smazat");
        foreach (var item in await page.Locator("[role='menu'] [role='menuitem']").AllAsync())
        {
            var clipped = await item.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1");
            Assert.IsFalse(clipped, "a menu entry must not clip at 320 (the label truncates inside its own span)");
        }

        var path = Path.Combine(ShotDir, "toolbar-320-cs-sheet-open.png");
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        Assert.IsTrue(File.Exists(path));
        await page.Keyboard.PressAsync("Escape");
        await AssertNoPageOverflowAsync(page, "320 cs");
    }

    [TestMethod]
    public async Task Phone_320_French_MoreTrigger_IsNamedInFrench()
    {
        var page = await OpenAsync(320, 640, touch: true, locale: "fr-FR");
        await SettleAsync(page, "toolbar-overflow-300");
        var trigger = Bar(page, "toolbar-overflow-300").Locator("button.tm-toolbar-more");
        Assert.AreEqual("Plus", await trigger.GetAttributeAsync("aria-label"));
        await trigger.TapAsync();
        var texts = await MenuTextsAsync(page);
        CollectionAssert.Contains(texts, "Exporter");
        CollectionAssert.Contains(texts, "Supprimer");
        await ShootAsync(page, Bar(page, "toolbar-overflow-300"), "toolbar-320-fr-sheet-open");
    }

    // ── labels ───────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Labels_Auto_DropsTheLabelByTheToolbarWidth_NotTheViewport()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var narrow = Bar(page, "toolbar-labels-auto");
        var wide = Bar(page, "toolbar-labels-auto-wide");
        await narrow.WaitForAsync();

        // The SAME 1440 viewport: the 360px toolbar is below the mobile breakpoint, the 720px one is not.
        Assert.AreEqual("none", await narrow.Locator(".tm-toolbar-btn-text").First.EvaluateAsync<string>("el => getComputedStyle(el).display"),
            "a toolbar narrower than 640px drops its labels (container query on the toolbar)");
        Assert.AreNotEqual("none", await wide.Locator(".tm-toolbar-btn-text").First.EvaluateAsync<string>("el => getComputedStyle(el).display"),
            "a 720px toolbar keeps its labels");
        Assert.AreEqual("New", await narrow.Locator("button.tm-toolbar-btn").First.GetAttributeAsync("aria-label"),
            "a CSS-hidden label stays the accessible name");

        var narrowButton = await narrow.Locator("button.tm-toolbar-btn").First.BoundingBoxAsync();
        var wideButton = await wide.Locator("button.tm-toolbar-btn").First.BoundingBoxAsync();
        Assert.IsTrue(wideButton!.Width > narrowButton!.Width, "the labelled button is wider than its icon-only twin");
        await ShootAsync(page, narrow, "toolbar-1440-labels-auto");
    }

    [TestMethod]
    public async Task Ribbon_LabelBelow_StacksTheLabelUnderTheIcon_AndKeepsDividers()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        await SettleAsync(page, "toolbar-ribbon");
        var bar = Bar(page, "toolbar-ribbon");

        var first = bar.Locator("button.tm-toolbar-btn--label-below").First;
        await Assertions.Expect(first).ToBeVisibleAsync();
        var stacked = await first.EvaluateAsync<bool>(
            """
            el => {
                const icon = el.querySelector('.tm-icon').getBoundingClientRect();
                const text = el.querySelector('.tm-toolbar-btn-text').getBoundingClientRect();
                return text.top >= icon.bottom - 1 && Math.abs((text.left + text.width / 2) - (icon.left + icon.width / 2)) <= 2;
            }
            """);
        Assert.IsTrue(stacked, "the label sits centred under the icon");
        Assert.IsTrue(await bar.Locator(".tm-toolbar-divider").CountAsync() >= 2, "groups are separated by dividers");
        await ShootAsync(page, bar, "toolbar-1440-ribbon");

        await page.SetViewportSizeAsync(1024, 768);
        await SettleAsync(page, "toolbar-ribbon");
        await ShootAsync(page, bar, "toolbar-1024-ribbon");
    }

    // ── themes and motion ────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Dark_MoreMenuAndButtons_UseThemeSurfaces()
    {
        var page = await OpenAsync(1024, 768, touch: false);
        await page.EvaluateAsync("() => { document.documentElement.setAttribute('data-theme', 'dark'); document.documentElement.classList.add('tm-dark'); }");
        await SettleAsync(page, "toolbar-overflow-380");
        var bar = Bar(page, "toolbar-overflow-380");
        await bar.Locator("button.tm-toolbar-more").ClickAsync();
        var panel = page.Locator(".tm-toolbar-more__panel");
        await Assertions.Expect(panel).ToBeVisibleAsync();

        var panelBg = await panel.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        var pageBg = await page.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", panelBg, "the popover paints a surface");
        Assert.AreNotEqual("rgb(255, 255, 255)", panelBg, "the dark surface is not the light one");
        var itemColor = await panel.Locator("[role='menuitem']").First.EvaluateAsync<string>("el => getComputedStyle(el).color");
        Assert.AreNotEqual(panelBg, itemColor, $"item text must contrast with the surface (page {pageBg})");
        await ShootAsync(page, bar, "toolbar-1024-dark-menu-open");
    }

    [TestMethod]
    public async Task ReducedMotion_ToolbarButtonsDoNotTransition()
    {
        var page = await OpenAsync(1440, 900, touch: false, reducedMotion: true);
        await SettleAsync(page, "toolbar-overflow-1100");
        var bar = Bar(page, "toolbar-overflow-1100");
        var duration = await bar.Locator("button.tm-toolbar-btn").First.EvaluateAsync<string>("el => getComputedStyle(el).transitionDuration");
        var triggerDuration = await bar.Locator("button.tm-toolbar-more").EvaluateAsync<string>("el => getComputedStyle(el).transitionDuration");
        Assert.IsTrue(duration.Split(',').All(d => double.Parse(d.Trim().TrimEnd('s'), CultureInfo.InvariantCulture) <= 0.001),
            $"reduced motion zeroes the button transition (was {duration})");
        Assert.IsTrue(triggerDuration.Split(',').All(d => double.Parse(d.Trim().TrimEnd('s'), CultureInfo.InvariantCulture) <= 0.001),
            $"and the More trigger's (was {triggerDuration})");
    }
}
