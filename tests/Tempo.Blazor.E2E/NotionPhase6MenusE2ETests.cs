using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase-6 functional coverage for the anchored menus, pickers, search, templates and shortcuts
/// (plan c8d4bcd2, N6 + N7 + N10, NO4). Screenshots land in
/// tests/Tempo.Blazor.E2E/__screenshots__/notion-*/phase-6/.
/// </summary>
[TestClass]
public sealed class NotionPhase6MenusE2ETests : WasmTestBase
{
    private static readonly (int Width, int Height, bool Touch, string Band)[] Bands =
    [
        (1440, 900, false, "1440"),
        (1024, 768, true, "1024"),
        (390, 844, true, "390"),
        (320, 700, true, "320"),
    ];

    [TestMethod]
    [Description("Phase 6: typing / opens the slash menu at every band; a mobile band sheets it and a pick inserts a block")]
    public async Task SlashMenu_OpensAtEveryBand_AndAMobilePickInsertsABlock()
    {
        foreach (var (width, height, touch, band) in Bands)
        {
            var page = await OpenAsync(width, height, touch);
            await OpenSlashAsync(page);

            var menu = page.Locator(".tm-notion-slash").First;
            Assert.IsTrue(await menu.IsVisibleAsync(), $"the slash menu opens at {band}");

            if (touch && width < 1024)
            {
                await AssertSheetAsync(page, "Insert block", slash: true);
                await ShotAsync(page, "notion-menus", $"slash-sheet-{band}-light",
                    page.Locator(".tm-overlay-panel-sheet").First);
            }

            await ShotAsync(page, "notion-menus", $"slash-{band}-light", menu);
            await SetThemeAsync(page, "dark");
            await ShotAsync(page, "notion-menus", $"slash-{band}-dark", menu);
            if (touch && width < 1024)
            {
                await ShotAsync(page, "notion-menus", $"slash-sheet-{band}-dark",
                    page.Locator(".tm-overlay-panel-sheet").First);
            }
            await page.Context.CloseAsync();
        }

        var mobile = await OpenAsync(390, 844, true);
        await OpenSlashAsync(mobile);
        await mobile.Locator(".tm-notion-slash__item").First.ClickAsync();
        await mobile.WaitForTimeoutAsync(400);
        Assert.IsTrue(await mobile.Locator("[data-notion-block]").CountAsync() > 1,
            "picking a slash item on a mobile sheet inserts a block");
        await mobile.Context.CloseAsync();
    }

    [TestMethod]
    [Description("CF3 (plan 98c80bc3): Enter on the last row leaves the caret block flush with the editor's fold; '/' must still show the menu — with NO test-driven centering (the editor keeps the reserve band, overlay.js is untouched).")]
    public async Task SlashMenu_AfterEnterOnLastRow_ShowsWithoutCentering()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").Last;
        await para.ClickAsync();
        await page.Keyboard.PressAsync("Control+End");
        // m1: park the last paragraph deterministically flush with the fold — the exact
        // pre-Enter state that used to park the slash panel invisibly.
        var innerHeight = await ParkLastParagraphAtTheFoldAsync(page);

        var beforeBlockId = await page.EvaluateAsync<string>(
            "() => document.activeElement?.closest('[data-block-id]')?.getAttribute('data-block-id') ?? ''");
        await page.Keyboard.PressAsync("Enter");
        // The split landing focuses the freshly created block (a DIFFERENT data-block-id) —
        // waited for deterministically, no sleeps.
        await page.WaitForFunctionAsync(
            @"
before => {
    const active = document.activeElement;
    return !!active?.classList?.contains('tm-notion-paragraph')
        && active.isContentEditable === true
        && (active.closest('[data-block-id]')?.getAttribute('data-block-id') ?? '') !== before;
}",
            beforeBlockId, new PageWaitForFunctionOptions { Timeout = 10000 });

        // m1: the editor keeps a reserve band under the landed block (min(200, 25% of the
        // visible height)) — assert the landed bottom actually cleared the band.
        var reserve = Math.Min(200, 0.25 * innerHeight);
        await page.WaitForFunctionAsync(
            @"
() => {
    const active = document.activeElement;
    if (!active?.classList?.contains('tm-notion-paragraph')) return false;
    const reserve = Math.min(200, 0.25 * (window.visualViewport?.height ?? window.innerHeight));
    return active.getBoundingClientRect().bottom <= window.innerHeight - reserve + 0.5;
}",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });
        var landedBottom = await page.EvaluateAsync<double>(
            "() => document.activeElement?.getBoundingClientRect().bottom ?? -1");
        Assert.IsTrue(landedBottom <= innerHeight - reserve + 0.5,
            $"the landed caret block keeps the viewport reserve (bottom {landedBottom} <= {innerHeight - reserve})");

        await page.Keyboard.TypeAsync("/");

        var menu = page.Locator(".tm-notion-slash").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000, State = WaitForSelectorState.Visible });
        Assert.IsTrue(await menu.IsVisibleAsync(),
            "the slash menu must be visible right after Enter on the last row (no parking, no centering workaround)");
        var box = await menu.BoundingBoxAsync();
        Assert.IsNotNull(box, "the visible menu must have a bounding box");
        Assert.IsTrue(box.Y + box.Height > 0 && box.Y < 900 && box.X < 1440,
            $"the menu renders inside the viewport (box {box.X},{box.Y} {box.Width}x{box.Height})");
    }

    [TestMethod]
    [Description("Phase 6: the page icon opens the one emoji picker; mobile sheets it")]
    public async Task EmojiPicker_OpensFromThePageIcon_AndSheetsOnMobile()
    {
        foreach (var (width, height, touch, band) in Bands)
        {
            var page = await OpenAsync(width, height, touch);
            // A page with no icon has no .tm-notion-header-icon — the opener is the "Add icon"
            // control, which only appears while the header is hovered.
            var icon = page.Locator(".tm-notion-header-icon");
            if (await icon.CountAsync() == 0)
            {
                await page.Locator(".tm-notion-header-meta").First.HoverAsync();
                await page.Locator(".tm-notion-header-meta__add-btn").First.ClickAsync();
            }
            else
            {
                await icon.First.ClickAsync();
            }
            var picker = page.Locator(".tm-modal-overlay").First;
            await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

            await ShotAsync(page, "notion-pickers", $"emoji-{band}-light", picker);
            await SetThemeAsync(page, "dark");
            await ShotAsync(page, "notion-pickers", $"emoji-{band}-dark", picker);
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Phase 6: Ctrl/Cmd+P opens page search as a modal; mobile is fullscreen")]
    public async Task PageSearch_OpensAsAModal_AtEveryBand()
    {
        // F3: the focus-return misses of every band are collected and asserted once, at the end.
        var misses = new List<string>();
        foreach (var (width, height, touch, band) in Bands)
        {
            var page = await OpenAsync(width, height, touch);
            // The user presses Ctrl/Cmd+P from the page, i.e. with a block focused; with nothing
            // focused there is no place for the focus to come back to.
            await page.Locator(".tm-notion-paragraph[contenteditable='true']").First.FocusAsync();
            await page.Keyboard.PressAsync("Control+p");
            var dialog = page.Locator(".tm-nps").First;
            await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

            await AssertSearchGeometryAsync(page, width, band);
            await ShotAsync(page, "notion-search", $"search-{band}-light");
            await SetThemeAsync(page, "dark");
            await ShotAsync(page, "notion-search", $"search-{band}-dark");
            var miss = await AssertEscapeReturnsFocusAsync(page, ".tm-nps", ".tm-notion-page");
            if (miss is not null)
                misses.Add($"[{band}] {miss}");
            await page.Context.CloseAsync();
        }

        Assert.AreEqual(0, misses.Count,
            "page search must return focus to the page on Escape at every band; missed bands: " + string.Join("; ", misses));
    }

    // One test per surface: a timeout on one menu must not drop the shots of the others, and a
    // 1024-touch user opens the slash-gated menus as a sheet (no focused search box to type into).
    [TestMethod]
    [Description("B3: notion-editor.js prewarms the shared overlay modules at parse time — the overlay.js resource entry exists BEFORE any menu opens and loaded 200 (the literal relative './_content/…' specifier resolved against the NotionEditor's own static-asset base and 404'd)")]
    public async Task NotionEditor_PrewarmsOverlayModule_BeforeTheFirstMenuOpens()
    {
        // The resource-timing buffer holds 250 entries by default and a WASM boot fetches more than
        // that, so the overlay.js entry would be evicted before the probe reads it — raise the
        // ceiling before ANY page script runs.
        var page = await OpenAsync(1440, 900, touch: false, configureContext: context =>
            context.AddInitScriptAsync("performance.setResourceTimingBufferSize(10000);")); // NO menu is opened here

        var expectedUrl = await page.EvaluateAsync<string>(
            "() => new URL('_content/Tempo.Blazor/js/overlay.js', document.baseURI).href");

        // The prewarm fires at script parse time — long before this wait starts — so the entry is
        // already in the resource timeline; the bounded wait only absorbs load hiccups.
        try
        {
            await page.WaitForFunctionAsync(
                "url => performance.getEntriesByType('resource').some(entry => entry.name === url)",
                expectedUrl, new PageWaitForFunctionOptions { Timeout = 15000 });
        }
        catch (TimeoutException)
        {
            var seen = await page.EvaluateAsync<string[]>(
                "() => performance.getEntriesByType('resource').map(entry => entry.name).filter(name => name.includes('overlay'))");
            Assert.Fail(
                $"no performance resource entry for {expectedUrl} before any menu opened — the prewarm import regressed "
                + $"to a relative specifier (its 404 would live under the NotionEditor's own asset base). overlay-named entries: "
                + (seen.Length == 0 ? "none" : string.Join(", ", seen)));
        }

        // responseStatus is Chromium 109+; when the entry cannot tell (0/undefined), the HEAD
        // verdict decides: the entry must exist and must NOT be the relative-URL 404.
        var status = await page.EvaluateAsync<long>(
            "url => (performance.getEntriesByType('resource').find(entry => entry.name === url) || {}).responseStatus ?? 0",
            expectedUrl);
        if (status == 0)
        {
            var headStatus = await page.EvaluateAsync<long>(
                "async url => (await fetch(url, { method: 'HEAD' })).status", expectedUrl);
            Assert.IsFalse(headStatus == 404,
                $"overlay.js resolved to a 404 (HEAD status {headStatus}) — the prewarm import is not using the absolute document.baseURI URL");
            Assert.AreEqual(200, headStatus, "overlay.js must be served for the prewarm to actually warm the module");
        }
        else
        {
            Assert.AreEqual(200, status,
                $"overlay.js prewarmed with the wrong URL or failed to load (responseStatus {status})");
        }
    }

    [TestMethod]
    [Description("Phase 6: the mention menu at every band, opened the way that band's user opens it")]
    public Task Phase6Menus_Mention_AtEveryBand() => CaptureSurfaceAsync(CaptureMentionAsync);

    [TestMethod]
    [Description("Phase 6: the token menu (one Create in the footer) at every band")]
    public Task Phase6Menus_Token_AtEveryBand() => CaptureSurfaceAsync(CaptureTokenAsync);

    [TestMethod]
    [Description("Phase 6: the status picker at every band, picked from the slash sheet on touch")]
    public Task Phase6Menus_Status_AtEveryBand() => CaptureSurfaceAsync(CaptureStatusAsync);

    [TestMethod]
    [Description("Mobile status picker (390 px, coarse): inserting / replacing a status with Enter hands the focus to the block, not <body> — the sheet keeps the page inert while the picker closes (restoreBlockFocus on insertSlashHtml and replaceActiveStatusChip)")]
    public async Task Phase6Menus_Status_EnterInsertOnMobileSheet_ReturnsFocusToTheBlock()
    {
        var page = await OpenAsync(390, 844, true);
        try
        {
            // Insert path: /status → label → Enter (insertSlashHtml).
            await TypeTriggerAsync(page, "/");
            var slash = page.Locator(".tm-notion-slash").First;
            await slash.WaitForAsync(Visible());
            var item = slash.Locator(".tm-notion-slash__item", new LocatorLocatorOptions { HasTextString = "Status" }).First;
            await item.EvaluateAsync("el => el.scrollIntoView({ block: 'center' })");
            await item.EvaluateAsync("el => el.click()");
            var picker = page.Locator(".tm-notion-status-picker").First;
            await picker.WaitForAsync(Visible());
            await AssertSheetHostsAsync(page, ".tm-notion-status-picker");

            await picker.Locator(".tm-notion-status-picker__input").First.FillAsync("DONE");
            await picker.Locator(".tm-notion-status-picker__input").First.PressAsync("Enter");
            await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 8000 });
            await page.Locator(".tm-notion-status[data-status-label='DONE']").First.WaitForAsync(Visible());
            Assert.AreEqual("ok", await WaitForBlockFocusAsync(page), "Enter-insert (insertSlashHtml) must leave the focus on the block");
            Assert.IsTrue(await CaretStandsRightAfterChipAsync(page, "DONE"), "Enter-insert: the collapsed caret must stand right behind the inserted DONE chip");

            // Replace path: tap the chip → label → Enter (replaceActiveStatusChip).
            await page.Locator(".tm-notion-status[data-status-label='DONE']").First.TapAsync();
            await picker.WaitForAsync(Visible());
            await picker.Locator(".tm-notion-status-picker__input").First.FillAsync("DOING");
            await picker.Locator(".tm-notion-status-picker__input").First.PressAsync("Enter");
            await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 8000 });
            await page.Locator(".tm-notion-status[data-status-label='DOING']").First.WaitForAsync(Visible());
            Assert.AreEqual("ok", await WaitForBlockFocusAsync(page), "Enter-replace (replaceActiveStatusChip) must leave the focus on the block");
            Assert.IsTrue(await CaretStandsRightAfterChipAsync(page, "DOING"), "Enter-replace: the collapsed caret must stand right behind the replacing DOING chip");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    private static async Task AssertSheetHostsAsync(IPage page, string surface)
    {
        var inSheet = await page.EvaluateAsync<bool>(
            "sel => !!document.querySelector(sel)?.closest('.tm-overlay-panel-sheet')", surface);
        Assert.IsTrue(inSheet, $"{surface} is presented on the mobile sheet");
    }

    /// <summary>
    /// True when the selection is collapsed and stands right behind the status chip with the given label
    /// (the chip is the node immediately before the caret in the chip's parent).
    /// </summary>
    private static Task<bool> CaretStandsRightAfterChipAsync(IPage page, string label) => page.EvaluateAsync<bool>(
        "label => { const sel = getSelection(); if (!sel || sel.rangeCount === 0) return false; const r = sel.getRangeAt(0); const chip = document.querySelector('.tm-notion-status[data-status-label=\"' + label + '\"]'); return !!chip && r.collapsed && r.startContainer === chip.parentNode && r.startContainer.childNodes[r.startOffset - 1] === chip; }",
        label);
    /// <summary>
    /// Waits (bounded) for <c>document.activeElement</c> to be an editable block and returns "ok", or a
    /// "miss:&lt;what owns the focus&gt;" description — a stuck <c>&lt;body&gt;</c> is the symptom.
    /// </summary>
    private static async Task<string> WaitForBlockFocusAsync(IPage page)
    {
        const string probe = "() => { const a = document.activeElement; return !!a && a !== document.body && a.classList.contains('tm-notion-paragraph') && a.isContentEditable; }";
        try
        {
            await page.WaitForFunctionAsync(probe, null, new PageWaitForFunctionOptions { Timeout = 4000 });
            return "ok";
        }
        catch (TimeoutException)
        {
            var on = await page.EvaluateAsync<string>(
                "() => { const a = document.activeElement; return a ? (a.tagName + '.' + (a.className || '')) : 'none'; }");
            return "miss:" + on;
        }
    }

    [TestMethod]
    [Description("Phase 6: the block menu at every band")]
    public Task Phase6Menus_BlockMenu_AtEveryBand() => CaptureSurfaceAsync(CaptureBlockMenuAsync);

    [TestMethod]
    [Description("Phase 6: the AI menu at every band, picked from the slash sheet on touch")]
    public Task Phase6Menus_Ai_AtEveryBand() => CaptureSurfaceAsync(CaptureAiAsync);

    [TestMethod]
    [Description("Phase 6: the type switcher at every band")]
    public Task Phase6Menus_TypeSwitcher_AtEveryBand() => CaptureSurfaceAsync(CaptureTypeSwitcherAsync);

    [TestMethod]
    [Description("Phase 6: the colour palette at every band")]
    public Task Phase6Menus_ColourPalette_AtEveryBand() => CaptureSurfaceAsync(CaptureColourPaletteAsync);

    [TestMethod]
    [Description("Phase 6: the template gallery at every band")]
    public Task Phase6Menus_Templates_AtEveryBand() => CaptureSurfaceAsync(CaptureTemplatesAsync);

    [TestMethod]
    [Description("Phase 6: the shortcuts panel at every band")]
    public Task Phase6Menus_Shortcuts_AtEveryBand() => CaptureSurfaceAsync(CaptureShortcutsAsync);

    [TestMethod]
    [Description("Phase 6: page settings at every band, including the 1024 sheet after the paint fix")]
    public Task Phase6Menus_PageSettings_AtEveryBand() => CaptureSurfaceAsync(CapturePageSettingsAsync);

    private async Task CaptureSurfaceAsync(Func<IPage, string, string, bool, int, Task<string?>> capture)
    {
        // F3: a missing focus return is COLLECTED, never silently ignored and never thrown from
        // the middle of the loop — every band/theme still gets its screenshots, and the test
        // fails ONCE, with every miss named.
        var misses = new List<string>();
        foreach (var (width, height, touch, band) in Bands)
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                var page = await OpenAsync(width, height, touch);
                if (theme == "dark")
                    await SetThemeAsync(page, "dark");

                var miss = await capture(page, band, theme, touch, width);
                if (miss is not null)
                    misses.Add($"[{band}/{theme}] {miss}");
                await page.Context.CloseAsync();
            }
        }

        Assert.AreEqual(0, misses.Count,
            "every surface must return focus to its trigger on Escape; missed surfaces: " + string.Join("; ", misses));
    }

    private static async Task<string?> CaptureMentionAsync(IPage page, string band, string theme, bool touch, int width)
    {
        _ = (touch, width);
        await TypeTriggerAsync(page, "@");
        var menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(Visible());
        await ShotAsync(page, "notion-menus", $"mention-{band}-{theme}", menu);
        return await AssertEscapeReturnsFocusAsync(page, ".tm-nmm", ".tm-notion-paragraph");
    }

    private static Task<string?> CaptureTokenAsync(IPage page, string band, string theme, bool touch, int width)
        => CaptureTokenAsync(page, band, theme);

    private static async Task<string?> CaptureTokenAsync(IPage page, string band, string theme)
    {
        await TypeTriggerAsync(page, "{{");
        var menu = page.Locator(".tm-notion-token-dropdown").First;
        await menu.WaitForAsync(Visible());
        Assert.AreEqual(1, await page.Locator(".tm-notion-token-dropdown__create-btn").CountAsync(),
            "the token menu has exactly one Create, in the floating-menu footer");
        await ShotAsync(page, "notion-menus", $"token-{band}-{theme}", menu);
        return await AssertEscapeReturnsFocusAsync(page, ".tm-notion-token-dropdown", ".tm-notion-paragraph");
    }

    private static async Task<string?> CaptureStatusAsync(IPage page, string band, string theme, bool touch, int width)
    {
        await TypeTriggerAsync(page, "/");
        var slash = page.Locator(".tm-notion-slash").First;
        await slash.WaitForAsync(Visible());
        // A touch band sheets the slash menu, and a sheet does not focus its search box, so a
        // 1024-touch user picks the visible Status row instead of typing a filter.
        if (touch)
        {
            // The sheet lists every block, so the row may sit below the fold: a touch user
            // scrolls to it. el.click() still fires once it is in view.
            var status = slash.Locator(".tm-notion-slash__item", new LocatorLocatorOptions { HasTextString = "Status" }).First;
            await status.EvaluateAsync("el => el.scrollIntoView({ block: 'center' })");
            await status.EvaluateAsync("el => el.click()");
        }
        else
        {
            var slashInput = slash.Locator(".tm-notion-slash__input");
            await slashInput.WaitForAsync(Visible());
            await page.WaitForFunctionAsync(
                "() => document.activeElement?.classList.contains('tm-notion-slash__input')");
            await slashInput.FillAsync("status");
            await page.WaitForTimeoutAsync(300);
            await slash.Locator(".tm-notion-slash__item", new LocatorLocatorOptions { HasTextString = "Status" })
                .First.EvaluateAsync("el => el.click()");
        }
        _ = width;
        var picker = page.Locator(".tm-notion-status-picker").First;
        await picker.WaitForAsync(Visible());
        await ShotAsync(page, "notion-pickers", $"status-{band}-{theme}", picker);
        return await AssertEscapeReturnsFocusAsync(page, ".tm-notion-status-picker", ".tm-notion-paragraph");
    }

    private static Task<string?> CaptureBlockMenuAsync(IPage page, string band, string theme)
        => CaptureBlockMenuAsync(page, band, theme, false, 1440);

    private static async Task<string?> CaptureBlockMenuAsync(IPage page, string band, string theme, bool touch, int width)
    {
        var block = page.Locator("[data-notion-block]").First;
        await block.HoverAsync();
        await page.Locator(".tm-notion-handle__menu-anchor > .tm-notion-handle__btn").First
            .EvaluateAsync("el => el.click()");
        var menu = page.Locator(".tm-notion-ctx-panel").First;
        await menu.WaitForAsync(Visible());
        if (touch && width < 1024)
            await AssertSheetAsync(page, "Block options", slash: false);
        await WaitUntilEscapeArmedAsync(page);
        await ShotAsync(page, "notion-menus", $"block-menu-{band}-{theme}", menu);
        return await AssertEscapeReturnsFocusAsync(page, ".tm-notion-ctx-panel", ".tm-notion-handle__menu-anchor > .tm-notion-handle__btn");
    }

    /// <summary>
    /// Waits until the open block menu has an ACTIVE focus trap. The panel is "visible" the moment
    /// Blazor rendered it, but the trap's activation rides a LATER JS-interop call (the lazy
    /// tm-focus-trap import) — under parallel load that gap stretched long enough for the Escape
    /// below to land in it and leave the panel visibly open forever. F1/F2: the wait is scoped to
    /// THE MENU via <c>data-tm-focus-trap-active</c>, and the marker means "THIS trap is ACTIVE"
    /// (a diagnostic lifecycle marker, see docs/overlays.md) — NOT "Escape is armed/will close",
    /// so this wait is the precondition for a stable Escape press, nothing more.
    /// </summary>
    private static async Task WaitUntilEscapeArmedAsync(IPage page)
        => await page.WaitForFunctionAsync(
            "() => !!document.querySelector('.tm-notion-ctx-panel[data-tm-focus-trap-active], [data-tm-focus-trap-active] .tm-notion-ctx-panel')",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });

    private static async Task<string?> CaptureAiAsync(IPage page, string band, string theme, bool touch, int width)
    {
        await TypeTriggerAsync(page, "/");
        var slash = page.Locator(".tm-notion-slash").First;
        await slash.WaitForAsync(Visible());
        if (!touch)
        {
            var slashInput = slash.Locator(".tm-notion-slash__input");
            await slashInput.WaitForAsync(Visible());
            await page.WaitForFunctionAsync(
                "() => document.activeElement?.classList.contains('tm-notion-slash__input')");
            await slashInput.FillAsync("ai");
            await page.WaitForTimeoutAsync(300);
        }

        _ = width;
        await slash.Locator("[data-testid='notion-ai-slash-item']").First.EvaluateAsync("el => el.click()");
        var menu = page.Locator("[data-testid='notion-ai-menu']").First;
        await menu.WaitForAsync(Visible());
        await ShotAsync(page, "notion-menus", $"ai-{band}-{theme}", menu);
        return await AssertEscapeReturnsFocusAsync(page, "[data-testid='notion-ai-menu']", ".tm-notion-paragraph");
    }

    private static Task<string?> CaptureTypeSwitcherAsync(IPage page, string band, string theme, bool touch, int width)
        => CaptureTypeSwitcherAsync(page, band, theme);

    private static async Task<string?> CaptureTypeSwitcherAsync(IPage page, string band, string theme)
    {
        await SelectParagraphAsync(page);
        await page.Locator("button[title='Turn into']").First.ClickAsync(new LocatorClickOptions { Force = true });
        var panel = page.Locator(".tm-notion-inline-toolbar__turninto-panel").First;
        await panel.WaitForAsync(Visible());
        await ShotAsync(page, "notion-menus", $"type-switcher-{band}-{theme}", panel);
        await page.Keyboard.PressAsync("Escape");
        return null;
    }

    private static Task<string?> CaptureColourPaletteAsync(IPage page, string band, string theme, bool touch, int width)
        => CaptureColourPaletteAsync(page, band, theme);

    private static async Task<string?> CaptureColourPaletteAsync(IPage page, string band, string theme)
    {
        await SelectParagraphAsync(page);
        await page.Locator("[data-testid='notion-inline-color']").First.ClickAsync(new LocatorClickOptions { Force = true });
        var panel = page.Locator(".tm-notion-inline-toolbar__color-panel").First;
        await panel.WaitForAsync(Visible());
        await ShotAsync(page, "notion-menus", $"colour-palette-{band}-{theme}", panel);
        await page.Keyboard.PressAsync("Escape");
        return null;
    }

    private static Task<string?> CaptureTemplatesAsync(IPage page, string band, string theme, bool touch, int width)
        => CaptureTemplatesAsync(page, band, theme, width);

    private static async Task<string?> CaptureTemplatesAsync(IPage page, string band, string theme, int width)
    {
        var bandName = width >= 1200 ? "desktop" : width >= 768 ? "tablet" : "mobile";
        await page.Locator($".tm-notion-editor[data-editor-layout='{bandName}']").First.WaitForAsync(Visible());
        // Tablet starts with the sidebar drawer closed, so the New-page trigger is absent until it opens.
        var newPage = page.Locator(".tm-ns-btn-new:visible");
        if (await newPage.CountAsync() == 0)
        {
            await page.Locator(".tm-notion-sidebar-toggle:visible").First.ClickAsync();
            await newPage.First.WaitForAsync(Visible());
        }
        await newPage.First.ClickAsync();
        var gallery = page.Locator(".tm-ntg").First;
        await gallery.WaitForAsync(Visible());
        if (width < 1024)
            Assert.IsTrue(await page.Locator(".tm-modal--fullscreen, .tm-modal-overlay--fullscreen").CountAsync() > 0
                || await gallery.EvaluateAsync<bool>("el => el.closest('.tm-modal')?.getBoundingClientRect().height > window.innerHeight * 0.9"),
                $"templates are fullscreen at {band}");
        // F-23: on a phone the gallery is a fullscreen sheet — a viewport shot (FullPage=false)
        // shows the sheet surface alone; a region capture used to include the page below it.
        await ShotAsync(page, "notion-templates", $"templates-{band}-{theme}", width < 640 ? null : gallery);
        await page.Keyboard.PressAsync("Escape");
        return null;
    }

    private static Task<string?> CaptureShortcutsAsync(IPage page, string band, string theme, bool touch, int width)
        => CaptureShortcutsAsync(page, band, theme, width);

    private static async Task<string?> CaptureShortcutsAsync(IPage page, string band, string theme, int width)
    {
        
        // Desktop and tablet keep the trigger on the bar. The mobile band has no bar copy — the
        // action-bar More sheet owns the only entry, and it is not in the DOM until that sheet
        // opens. :visible — a collapsed bar copy keeps data-testid but is visibility:hidden, and a
        // force click on it fires nothing.
        var trigger = page.Locator("[data-testid='notion-shortcuts-open']:visible, [data-menu-testid='notion-shortcuts-open']:visible");
        if (width < 640 || await trigger.CountAsync() == 0)
        {
            // The demo host pins the editor to 900px, so the action bar sits below a short phone
            // viewport. The toolbar More is the one on screen and it does not carry this entry.
            var more = page.Locator(".tm-mobile-action-bar__more").First;
            await more.ScrollIntoViewIfNeededAsync();
            await more.ClickAsync();
            await page.Locator(".tm-overlay-panel-sheet:visible, .tm-toolbar-more__menu:visible").First.WaitForAsync(Visible());
        }
        if (width <= 320)
        {
            var hit = await trigger.First.EvaluateAsync<bool>(
                """
                el => {
                    const box = el.getBoundingClientRect();
                    const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
                    return hit === el || el.contains(hit);
                }
                """);
            Assert.IsTrue(hit, $"shortcuts trigger at {width} is covered — elementFromPoint of its center is not the trigger");
        }
        await trigger.First.ClickAsync();
        var panel = page.Locator("[data-testid='notion-shortcuts-panel']").First;
        await panel.WaitForAsync(Visible());
        // F-23: on a phone the panel is a sheet — capture the viewport, not the element region.
        await ShotAsync(page, "notion-menus", $"shortcuts-{band}-{theme}", width < 640 ? null : panel);
        await page.Keyboard.PressAsync("Escape");
        _ = width;
        return null;
    }

    private static Task<string?> CapturePageSettingsAsync(IPage page, string band, string theme)
        => CapturePageSettingsAsync(page, band, theme, false, 1440);

    private static async Task<string?> CapturePageSettingsAsync(IPage page, string band, string theme, bool touch, int width)
    {
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);
        var trigger = page.Locator(".tm-npsm-trigger").First;
        // Keyboard activation: the trigger owns the focus when the menu opens (a bare el.click()
        // leaves it on whatever had it, and Escape then correctly returns it THERE).
        await trigger.FocusAsync();
        await trigger.EvaluateAsync("el => el.click()");
        var menu = page.Locator(".tm-npsm").First;
        await menu.WaitForAsync(Visible());
        if (touch && width < 1024)
            await AssertSheetAsync(page, "Page settings", slash: false);
        await ShotAsync(page, "notion-menus", $"page-settings-{band}-{theme}", menu);
        return await AssertEscapeReturnsFocusAsync(page, ".tm-npsm", ".tm-npsm-trigger");
    }

    private static async Task AssertSearchGeometryAsync(IPage page, int width, string band)
    {
        var dialog = page.Locator(".tm-modal").Filter(new LocatorFilterOptions { Has = page.Locator(".tm-nps") }).First;
        var box = await dialog.BoundingBoxAsync();
        Assert.IsNotNull(box, $"page search has a box at {band}");
        if (width >= 1024)
        {
            var centre = box!.X + box.Width / 2;
            Assert.IsTrue(Math.Abs(centre - width / 2.0) < width * 0.15, $"page search is centred at {band}");
        }
        else
        {
            Assert.IsTrue(box!.Width > width * 0.9 && box.Height > 600, $"page search is fullscreen at {band}");
        }
    }

    private static async Task AssertSheetAsync(IPage page, string title, bool slash)
    {
        var sheet = page.Locator(".tm-overlay-panel-sheet").First;
        Assert.IsTrue(await sheet.IsVisibleAsync(), "the surface sheets");
        var heading = sheet.Locator(".tm-overlay-panel-sheet__title").First;
        Assert.AreEqual(title, (await heading.InnerTextAsync()).Trim(), "the sheet titles the surface");
        Assert.IsTrue(await sheet.Locator(".tm-overlay-panel-sheet__done, .tm-notion-floating-menu__close").CountAsync() > 0,
            "the sheet has a close button");
        var min = slash ? 56 : 44;
        var shortRow = await page.EvaluateAsync<int>(
            """
            min => {
                const rows = document.querySelectorAll('.tm-overlay-panel-sheet [role="menuitem"], .tm-overlay-panel-sheet [role="option"]');
                return [...rows].filter(el => el.getBoundingClientRect().height < min - 0.5).length;
            }
            """,
            min);
        Assert.AreEqual(0, shortRow, $"every sheet row is at least {min}px");
    }

    /// <summary>
    /// Presses Escape against the open <paramref name="surface"/> and checks the focus hand-back.
    /// F3: instead of throwing (which would abort the remaining surfaces mid-loop) or only logging
    /// (which let the misses pass green), the helper RETURNS the miss and the caller COLLECTS the
    /// misses of its whole run, asserting <c>Assert.AreEqual(0, misses.Count, …)</c> at the END of
    /// the test — one failure listing every surface whose Escape left the focus behind.
    /// </summary>
    private static async Task<string?> AssertEscapeReturnsFocusAsync(IPage page, string surface, string trigger)
    {
        await page.Keyboard.PressAsync("Escape");
        await page.Locator(surface).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 8000,
        });
        var focused = await page.EvaluateAsync<string>(
            "sel => { const active = document.activeElement; const on = active ? (active.className || active.tagName) : 'none'; return (active && active !== document.body && (active.matches(sel) || !!active.closest(sel))) ? 'ok' : 'miss:' + on; }",
            trigger);
        return focused == "ok"
            ? null
            : $"Escape closed {surface} but focus did not return to {trigger} (active: {focused["miss:".Length..]})";
    }

    private static async Task TypeTriggerAsync(IPage page, string text)
    {
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await para.ScrollIntoViewIfNeededAsync();
        // A touch user taps. A mouse click on a touch context leaves no caret, so the trigger
        // never reaches the editor's input listener. End+Enter lands in a fresh empty block,
        // which is where a touch user types the trigger.
        var touch = await page.EvaluateAsync<bool>("() => navigator.maxTouchPoints > 0");
        if (touch)
            await para.TapAsync();
        else
            await para.ClickAsync();
        await page.Keyboard.PressAsync("End");
        var sourceBlockId = await page.EvaluateAsync<string>(
            "() => document.activeElement?.closest('[data-block-id]')?.getAttribute('data-block-id') ?? ''");
        await page.Keyboard.PressAsync("Enter");
        // State waits instead of fixed sleeps: typing before the fresh block owns the focus would
        // land in the SOURCE block (which the Enter pipeline then rewrites), and the menu is only
        // there to assert on once the trigger text has reached the editor's input listener.
        await NotionE2ETestBase.WaitForBlockFocusToMoveAsync(page, sourceBlockId);
        await page.Keyboard.TypeAsync(text);
        await page.Locator(TriggerMenuSelector(text)).First.WaitForAsync(Visible());
    }

    /// <summary>The menu a trigger text opens — the state <see cref="TypeTriggerAsync"/> waits for.</summary>
    private static string TriggerMenuSelector(string trigger) => trigger switch
    {
        "@" => ".tm-nmm",
        "{{" => ".tm-notion-token-dropdown",
        "/" => ".tm-notion-slash",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "no menu is mapped to this trigger"),
    };

    private static async Task SelectParagraphAsync(IPage page)
    {
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.ClickAsync();
        await page.Keyboard.PressAsync("Control+a");
        await page.Locator(".tm-notion-inline-toolbar").First.WaitForAsync(Visible());
    }

    private static LocatorWaitForOptions Visible()
        => new() { State = WaitForSelectorState.Visible, Timeout = 10000 };

    private async Task<IPage> OpenAsync(int width, int height, bool touch, Func<IBrowserContext, Task>? configureContext = null)
    {
        using var http = DemoApiTestHttp.CreateClient();
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* The waits below report a missing API. */ }

        var context = await CreateContextAsync(hasTouch: touch);
        if (configureContext is not null)
        {
            await configureContext(context);
        }
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}/notion-editor", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000,
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000,
        });
        return page;
    }

    private static async Task OpenSlashAsync(IPage page)
    {
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await para.ClickAsync();
        await page.Keyboard.PressAsync("End");
        // CF3 (m1): ONE Enter splits the paragraph; the caret lands in the freshly created
        // block (a different data-block-id) — waited for deterministically, no sleeps. The
        // editor keeps its viewport reserve under the landed block, so a following '/'
        // shows the menu.
        var beforeBlockId = await page.EvaluateAsync<string>(
            "() => document.activeElement?.closest('[data-block-id]')?.getAttribute('data-block-id') ?? ''");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            @"
before => {
    const active = document.activeElement;
    return !!active?.classList?.contains('tm-notion-paragraph')
        && active.isContentEditable === true
        && (active.closest('[data-block-id]')?.getAttribute('data-block-id') ?? '') !== before;
}",
            beforeBlockId, new PageWaitForFunctionOptions { Timeout = 10000 });
        await page.Keyboard.TypeAsync("/");
        await page.WaitForSelectorAsync(".tm-notion-slash", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000,
        });
    }

    /// <summary>
    /// m1: the shared deterministic parker (see <see cref="NotionEditorTestHelper"/>).
    /// </summary>
    private static Task<int> ParkLastParagraphAtTheFoldAsync(IPage page)
        => NotionEditorTestHelper.ParkLastParagraphAtTheFoldAsync(page);

    private static async Task SetThemeAsync(IPage page, string theme)
    {
        await page.EvaluateAsync(
            """
            theme => {
                document.documentElement.setAttribute('data-theme', theme);
                const shell = document.querySelector('.demo-shell');
                if (shell) shell.setAttribute('data-theme', theme);
            }
            """,
            theme);
        await page.WaitForTimeoutAsync(150);
    }

    private static string ShotDir(string area)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            "__screenshots__", area, "phase-6");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return dir ?? throw new DirectoryNotFoundException("TempoBlazor.slnx");
    }

    private static async Task ShotAsync(IPage page, string area, string name, ILocator? region = null)
    {
        await page.WaitForTimeoutAsync(200);
        var path = Path.Combine(ShotDir(area), name + ".png");
        if (region is not null && await region.CountAsync() > 0 && await region.IsVisibleAsync())
            await region.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Timeout = 8000 });
        else
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path });
    }
}
