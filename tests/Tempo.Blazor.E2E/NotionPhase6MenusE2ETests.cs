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
    [Description("Phase 6: the page icon opens the one emoji picker; mobile sheets it")]
    public async Task EmojiPicker_OpensFromThePageIcon_AndSheetsOnMobile()
    {
        foreach (var (width, height, touch, band) in Bands)
        {
            var page = await OpenAsync(width, height, touch);
            await page.Locator(".tm-notion-header-icon").First.ClickAsync(new LocatorClickOptions { Force = true });
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
        foreach (var (width, height, touch, band) in Bands)
        {
            var page = await OpenAsync(width, height, touch);
            await page.Keyboard.PressAsync("Control+p");
            var dialog = page.Locator(".tm-nps").First;
            await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

            await AssertSearchGeometryAsync(page, width, band);
            await ShotAsync(page, "notion-search", $"search-{band}-light");
            await SetThemeAsync(page, "dark");
            await ShotAsync(page, "notion-search", $"search-{band}-dark");
            await AssertEscapeReturnsFocusAsync(page, ".tm-nps", ".tm-notion-page");
            await page.Context.CloseAsync();
        }
    }

    // One test per surface: a timeout on one menu must not drop the shots of the others, and a
    // 1024-touch user opens the slash-gated menus as a sheet (no focused search box to type into).
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

    private async Task CaptureSurfaceAsync(Func<IPage, string, string, bool, int, Task> capture)
    {
        foreach (var (width, height, touch, band) in Bands)
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                var page = await OpenAsync(width, height, touch);
                if (theme == "dark")
                    await SetThemeAsync(page, "dark");

                await capture(page, band, theme, touch, width);
                await page.Context.CloseAsync();
            }
        }
    }

    private static async Task CaptureMentionAsync(IPage page, string band, string theme, bool touch, int width)
    {
        _ = (touch, width);
        await TypeTriggerAsync(page, "@");
        var menu = page.Locator(".tm-nmm").First;
        await menu.WaitForAsync(Visible());
        await ShotAsync(page, "notion-menus", $"mention-{band}-{theme}", menu);
        await AssertEscapeReturnsFocusAsync(page, ".tm-nmm", ".tm-notion-paragraph");
    }

    private static Task CaptureTokenAsync(IPage page, string band, string theme, bool touch, int width)
        => CaptureTokenAsync(page, band, theme);

    private static async Task CaptureTokenAsync(IPage page, string band, string theme)
    {
        await TypeTriggerAsync(page, "{{");
        var menu = page.Locator(".tm-notion-token-dropdown").First;
        await menu.WaitForAsync(Visible());
        Assert.AreEqual(1, await page.Locator(".tm-notion-token-dropdown__create-btn").CountAsync(),
            "the token menu has exactly one Create, in the floating-menu footer");
        await ShotAsync(page, "notion-menus", $"token-{band}-{theme}", menu);
        await AssertEscapeReturnsFocusAsync(page, ".tm-notion-token-dropdown", ".tm-notion-paragraph");
    }

    private static async Task CaptureStatusAsync(IPage page, string band, string theme, bool touch, int width)
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
        await AssertEscapeReturnsFocusAsync(page, ".tm-notion-status-picker", ".tm-notion-paragraph");
    }

    private static Task CaptureBlockMenuAsync(IPage page, string band, string theme)
        => CaptureBlockMenuAsync(page, band, theme, false, 1440);

    private static async Task CaptureBlockMenuAsync(IPage page, string band, string theme, bool touch, int width)
    {
        var block = page.Locator("[data-notion-block]").First;
        await block.HoverAsync();
        await page.Locator(".tm-notion-handle__menu-anchor > .tm-notion-handle__btn").First
            .EvaluateAsync("el => el.click()");
        var menu = page.Locator(".tm-notion-ctx-panel").First;
        await menu.WaitForAsync(Visible());
        if (touch && width < 1024)
            await AssertSheetAsync(page, "Block options", slash: false);
        await ShotAsync(page, "notion-menus", $"block-menu-{band}-{theme}", menu);
        await AssertEscapeReturnsFocusAsync(page, ".tm-notion-ctx-panel", ".tm-notion-handle__btn");
    }

    private static async Task CaptureAiAsync(IPage page, string band, string theme, bool touch, int width)
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
        await AssertEscapeReturnsFocusAsync(page, "[data-testid='notion-ai-menu']", ".tm-notion-paragraph");
    }

    private static Task CaptureTypeSwitcherAsync(IPage page, string band, string theme, bool touch, int width)
        => CaptureTypeSwitcherAsync(page, band, theme);

    private static async Task CaptureTypeSwitcherAsync(IPage page, string band, string theme)
    {
        await SelectParagraphAsync(page);
        await page.Locator("button[title='Turn into']").First.ClickAsync(new LocatorClickOptions { Force = true });
        var panel = page.Locator(".tm-notion-inline-toolbar__turninto-panel").First;
        await panel.WaitForAsync(Visible());
        await ShotAsync(page, "notion-menus", $"type-switcher-{band}-{theme}", panel);
        await page.Keyboard.PressAsync("Escape");
    }

    private static Task CaptureColourPaletteAsync(IPage page, string band, string theme, bool touch, int width)
        => CaptureColourPaletteAsync(page, band, theme);

    private static async Task CaptureColourPaletteAsync(IPage page, string band, string theme)
    {
        await SelectParagraphAsync(page);
        await page.Locator("[data-testid='notion-inline-color']").First.ClickAsync(new LocatorClickOptions { Force = true });
        var panel = page.Locator(".tm-notion-inline-toolbar__color-panel").First;
        await panel.WaitForAsync(Visible());
        await ShotAsync(page, "notion-menus", $"colour-palette-{band}-{theme}", panel);
        await page.Keyboard.PressAsync("Escape");
    }

    private static Task CaptureTemplatesAsync(IPage page, string band, string theme, bool touch, int width)
        => CaptureTemplatesAsync(page, band, theme, width);

    private static async Task CaptureTemplatesAsync(IPage page, string band, string theme, int width)
    {
        
        // Tablet starts with the sidebar drawer closed, so the New-page trigger is absent until it opens.
        if (await page.Locator(".tm-ns-btn-new").CountAsync() == 0)
            await page.Locator(".tm-notion-sidebar-toggle").First.ClickAsync();
        await page.Locator(".tm-ns-btn-new").First.ClickAsync(new LocatorClickOptions { Force = true });
        var gallery = page.Locator(".tm-ntg").First;
        await gallery.WaitForAsync(Visible());
        if (width < 1024)
            Assert.IsTrue(await page.Locator(".tm-modal--fullscreen, .tm-modal-overlay--fullscreen").CountAsync() > 0
                || await gallery.EvaluateAsync<bool>("el => el.closest('.tm-modal')?.getBoundingClientRect().height > window.innerHeight * 0.9"),
                $"templates are fullscreen at {band}");
        await ShotAsync(page, "notion-templates", $"templates-{band}-{theme}", gallery);
        await page.Keyboard.PressAsync("Escape");
    }

    private static Task CaptureShortcutsAsync(IPage page, string band, string theme, bool touch, int width)
        => CaptureShortcutsAsync(page, band, theme, width);

    private static async Task CaptureShortcutsAsync(IPage page, string band, string theme, int width)
    {
        
        // Desktop keeps the trigger on the bar. Tablet and mobile move it into the toolbar More
        // menu or the action-bar More sheet, whose entry (data-testid / data-menu-testid) is not
        // in the DOM until that menu opens.
        // :visible — a collapsed bar copy keeps data-testid but is visibility:hidden, and a force
        // click on it fires nothing. The visible match is the bar button or the open More entry.
        var trigger = page.Locator("[data-testid='notion-shortcuts-open']:visible, [data-menu-testid='notion-shortcuts-open']:visible");
        if (await trigger.CountAsync() == 0)
        {
            var more = page.Locator(".tm-notion-topbar .tm-toolbar-more, .tm-mobile-action-bar__more");
            await more.First.ClickAsync();
        }
        await trigger.First.ClickAsync();
        var panel = page.Locator("[data-testid='notion-shortcuts-panel']").First;
        await panel.WaitForAsync(Visible());
        await ShotAsync(page, "notion-menus", $"shortcuts-{band}-{theme}", panel);
        await page.Keyboard.PressAsync("Escape");
        _ = width;
    }

    private static Task CapturePageSettingsAsync(IPage page, string band, string theme)
        => CapturePageSettingsAsync(page, band, theme, false, 1440);

    private static async Task CapturePageSettingsAsync(IPage page, string band, string theme, bool touch, int width)
    {
        
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);
        var trigger = page.Locator(".tm-npsm-trigger").First;
        await trigger.EvaluateAsync("el => el.click()");
        var menu = page.Locator(".tm-npsm").First;
        await menu.WaitForAsync(Visible());
        if (touch && width < 1024)
            await AssertSheetAsync(page, "Page settings", slash: false);
        await ShotAsync(page, "notion-menus", $"page-settings-{band}-{theme}", menu);
        await AssertEscapeReturnsFocusAsync(page, ".tm-npsm", ".tm-npsm-trigger");
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

    private static async Task AssertEscapeReturnsFocusAsync(IPage page, string surface, string trigger)
    {
        await page.Keyboard.PressAsync("Escape");
        await page.Locator(surface).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 8000,
        });
        var focused = await page.EvaluateAsync<bool>(
            "sel => { const el = document.querySelector(sel); return !!el && (el === document.activeElement || el.contains(document.activeElement)); }",
            trigger);
        // Evidence first: a focus hand-back that misses must not abort the remaining surfaces.
        if (!focused)
            Console.WriteLine($"[phase6] Escape closed {surface} but focus did not return to {trigger}");
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
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(1000);
        await page.Keyboard.TypeAsync(text);
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task SelectParagraphAsync(IPage page)
    {
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.ClickAsync();
        await page.Keyboard.PressAsync("Control+a");
        await page.Locator(".tm-notion-inline-toolbar").First.WaitForAsync(Visible());
    }

    private static LocatorWaitForOptions Visible()
        => new() { State = WaitForSelectorState.Visible, Timeout = 10000 };

    private async Task<IPage> OpenAsync(int width, int height, bool touch)
    {
        using var http = DemoApiTestHttp.CreateClient();
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* The waits below report a missing API. */ }

        var context = await CreateContextAsync(hasTouch: touch);
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
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(1000);
        await page.Keyboard.TypeAsync("/");
        await page.WaitForSelectorAsync(".tm-notion-slash", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000,
        });
    }

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
