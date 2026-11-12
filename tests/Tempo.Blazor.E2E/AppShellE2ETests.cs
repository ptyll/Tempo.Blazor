using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 5d396592 phase 6 — the <c>/app-shell</c> demo page in a real browser. The page embeds the live
/// <c>TmAppShell</c> in a width-controlled stage, so this lane drives the pure container-owned frame
/// behaviour: the presentation follows the STAGE width (phone / tablet / desktop with no viewport
/// resize), the work area resizes by pointer and by keyboard and survives a reload, the phone drawer
/// hands the focus in and back, the account menu follows the APG menu-button keyboard contract, a
/// checklist toggle announces exactly once, and a scoped axe scan of the frame finds no critical or
/// serious violation. Assertions read computed values, ARIA state and real geometry — never markup
/// alone — and the reference screenshots (light + dark at 375 / 900 / 1440) land in
/// <c>TestResults/app-shell</c>.
/// </summary>
public abstract class AppShellE2EBase : PlaywrightTestBase
{
    private const string StorageKey = "tempo.tm-app-shell.demo-app-shell";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "app-shell");

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

    private ILocator Frame(IPage page) => page.GetByTestId("shell-app-shell");

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1000, bool touch = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/app-shell");
        await page.WaitForSelectorAsync("[data-testid='app-shell-stage']", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    /// <summary>
    /// Picks one of the demo's width presets and waits until the frame actually resolved that layout.
    /// The frame classifies its own container, so the preset — not the browser viewport — is what moves.
    /// </summary>
    private async Task SelectWidthAsync(IPage page, string preset, string layout)
    {
        var button = page.GetByTestId($"app-shell-width-{preset}");
        await button.EvaluateAsync("el => { el.scrollIntoView({ block: 'center', behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(150);
        await button.ClickAsync();
        await page.WaitForFunctionAsync(
            "expected => document.querySelector(\"[data-testid='shell-app-shell']\")?.getAttribute('data-layout') === expected",
            layout, new PageWaitForFunctionOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(350);
    }

    private async Task OpenWorkAreaAsync(IPage page)
    {
        var toggle = page.GetByTestId("app-shell-work-area-toggle");
        if (await toggle.GetAttributeAsync("aria-pressed") == "false")
        {
            await toggle.ClickAsync();
        }

        await page.WaitForSelectorAsync("[data-testid='app-shell-sources']", new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(250);
    }

    private async Task<int> WorkAreaWidthAsync(IPage page)
    {
        var value = await page.GetByTestId("shell-app-shell-work-area-resizer").GetAttributeAsync("aria-valuenow");
        return int.Parse(value!, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ActiveTestIdAsync(IPage page)
        => await page.EvaluateAsync<string?>(
            "() => document.activeElement ? document.activeElement.getAttribute('data-testid') : null");

    private async Task<string> ShootStageAsync(IPage page, string name)
    {
        // The FRAME, not the stage: a preset wider than the demo column overflows the scrolling stage,
        // and an element screenshot is what the visual review needs to see in full.
        var stage = page.GetByTestId("shell-app-shell");
        // Park the frame just below the demo's own fixed mobile header (64px): an element screenshot
        // of a frame scrolled under that header would show the header painted over its top bar.
        await stage.EvaluateAsync(
            """
            el => {
                const r = el.getBoundingClientRect();
                window.scrollTo({ top: Math.max(0, r.top + window.scrollY - 96), behavior: 'instant' });
                // The demo page gives this frame the whole content width, so the presets normally fit;
                // unclip the stage anyway so a narrower browser window still yields a whole-frame
                // artifact instead of a scroll-container crop (the next page load restores it).
                const host = el.closest("[data-testid='app-shell-stage']");
                if (host) host.style.overflow = 'visible';
            }
            """);
        await page.WaitForTimeoutAsync(300);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await stage.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        return path;
    }

    // ── Container-owned presentations ──────────────────────────────────────────

    [TestMethod]
    public async Task Frame_Follows_The_Stage_Width_Without_Touching_The_Viewport()
    {
        var page = await OpenAsync();
        var frame = Frame(page);

        // The demo's default is the column's own width; the presets below are what changes the layout.
        Assert.AreNotEqual("Auto", await frame.GetAttributeAsync("data-layout"));

        await SelectWidthAsync(page, "phone", "mobile");
        await Assertions.Expect(page.GetByTestId("app-shell-resolved-layout")).ToHaveAttributeAsync("data-resolved", "mobile");
        await Assertions.Expect(page.GetByTestId("shell-sidebar")).Not.ToBeVisibleAsync();
        var hamburger = page.GetByTestId("topbar-menu-button");
        await Assertions.Expect(hamburger).ToBeVisibleAsync();
        Assert.AreEqual("false", await hamburger.GetAttributeAsync("aria-expanded"));
        Assert.IsNull(await hamburger.GetAttributeAsync("aria-controls"),
            "aria-controls is only emitted while the drawer it points at exists");

        await SelectWidthAsync(page, "tablet", "tablet");
        await Assertions.Expect(page.GetByTestId("shell-sidebar")).ToBeVisibleAsync();
        await Assertions.Expect(frame.Locator("[data-region='sidebar']")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("tm-app-shell__sidebar--rail"));
        // The tablet opener is the rail's own toggle, not a top-bar hamburger.
        Assert.AreEqual(0, await page.GetByTestId("topbar-menu-button").CountAsync());

        await SelectWidthAsync(page, "desktop", "desktop");
        Assert.AreEqual(0, await frame.Locator("[data-region='sidebar'].tm-app-shell__sidebar--rail").CountAsync(),
            "the desktop presentation docks the full column, not the rail");
        var navWidth = (await page.GetByTestId("shell-app-shell-sidebar").BoundingBoxAsync())!.Width;
        Assert.IsTrue(navWidth > 100, $"the docked column shows labels, so it is wider than a rail ({navWidth}px)");

        // The viewport itself never changed: the stage scrolled instead.
        Assert.AreEqual(1440, await page.EvaluateAsync<int>("() => window.innerWidth"));

        await ShootStageAsync(page, "frame-desktop-1440");
    }

    [TestMethod]
    public async Task Phone_Drawer_Takes_The_Focus_Closes_On_Escape_And_Hands_It_Back()
    {
        var page = await OpenAsync(width: 390, height: 900, touch: true);
        await SelectWidthAsync(page, "phone", "mobile");

        var hamburger = page.GetByTestId("topbar-menu-button");
        await hamburger.ScrollIntoViewIfNeededAsync();
        await hamburger.ClickAsync();
        await Assertions.Expect(hamburger).ToHaveAttributeAsync("aria-expanded", "true");

        var drawer = page.GetByTestId("shell-app-shell-nav-overlay");
        await Assertions.Expect(drawer).ToBeVisibleAsync();
        Assert.AreEqual(await hamburger.GetAttributeAsync("aria-controls"), await drawer.GetAttributeAsync("id"),
            "the opener names the drawer it opened");

        // The focus moved INTO the drawer, and the account footer is mounted there too.
        await page.WaitForFunctionAsync(
            "() => { const d = document.querySelector(\"[data-testid='shell-app-shell-nav-overlay']\"); return !!d && d.contains(document.activeElement); }",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });
        await Assertions.Expect(drawer.GetByTestId("frame-account-user-menu-trigger")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, "phone-drawer-390.png"),
            FullPage = false,
        });

        // Escape closes the modal drawer and hands the focus back to the hamburger.
        await page.Keyboard.PressAsync("Escape");
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        await Assertions.Expect(hamburger).ToHaveAttributeAsync("aria-expanded", "false");
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.getAttribute('data-testid') === 'topbar-menu-button'",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });

        // Choosing a destination closes it too (and never leaves the focus on the body).
        await hamburger.ClickAsync();
        await Assertions.Expect(drawer).ToBeVisibleAsync();
        await drawer.GetByTestId("shell-sidebar-item-orders").ClickAsync();
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        Assert.IsTrue(await page.Locator("[data-testid='shell-app-shell']").CountAsync() > 0,
            "a chosen destination must not tear the frame down (the entry anchors this page, not the app root)");
    }

    // ── Work area: resize, clamp, persistence ──────────────────────────────────

    [TestMethod]
    public async Task Work_Area_Resize_By_Keyboard_Clamps_And_Persists_A_Reload()
    {
        var page = await OpenAsync();
        await SelectWidthAsync(page, "desktop", "desktop");
        await OpenWorkAreaAsync(page);

        var separator = page.GetByTestId("shell-app-shell-work-area-resizer");
        await Assertions.Expect(separator).ToBeVisibleAsync();
        Assert.AreEqual("separator", await separator.GetAttributeAsync("role"));
        Assert.AreEqual("vertical", await separator.GetAttributeAsync("aria-orientation"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(await separator.GetAttributeAsync("aria-label")));
        Assert.IsNull(await page.EvaluateAsync<string?>($"() => localStorage.getItem('{StorageKey}')"),
            "nothing is stored before the user resizes");

        var initial = await WorkAreaWidthAsync(page);
        Assert.AreEqual("420", await separator.GetAttributeAsync("aria-valuenow"));
        var min = int.Parse((await separator.GetAttributeAsync("aria-valuemin"))!, System.Globalization.CultureInfo.InvariantCulture);
        var max = int.Parse((await separator.GetAttributeAsync("aria-valuemax"))!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.AreEqual(280, min);
        Assert.IsTrue(max >= initial && max <= 640, $"aria-valuemax is the reachable maximum ({max})");

        await separator.FocusAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "436");

        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "420");

        await page.Keyboard.PressAsync("Shift+ArrowLeft");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "484");

        await page.Keyboard.PressAsync("End");
        await page.WaitForTimeoutAsync(300);
        var reachedMax = await WorkAreaWidthAsync(page);
        Assert.IsTrue(reachedMax is > 484 and <= 640, $"End goes to the maximum the content allows ({reachedMax}px)");
        var contentWidth = (await page.GetByTestId("shell-app-shell-main").BoundingBoxAsync())!.Width;
        Assert.IsTrue(contentWidth >= 479, $"MinContentWidth keeps the content readable ({contentWidth}px)");

        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "280");

        // Settle on a user width and reload: the value is restored from localStorage.
        await page.Keyboard.PressAsync("Shift+ArrowLeft");
        await page.Keyboard.PressAsync("ArrowLeft");
        var settled = await WorkAreaWidthAsync(page);
        Assert.AreEqual(360, settled);
        var stored = await page.EvaluateAsync<string?>($"() => localStorage.getItem('{StorageKey}')");
        Assert.IsNotNull(stored, "a user resize persists under the frame's key");
        StringAssert.Contains(stored, "360");

        await page.ReloadAsync();
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync("[data-testid='app-shell-stage']", new PageWaitForSelectorOptions { Timeout = 30000 });
        await SelectWidthAsync(page, "desktop", "desktop");
        await OpenWorkAreaAsync(page);
        await Assertions.Expect(page.GetByTestId("shell-app-shell-work-area-resizer"))
            .ToHaveAttributeAsync("aria-valuenow", "360", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });

        await ShootStageAsync(page, "work-area-restored-1440");
    }

    [TestMethod]
    public async Task Work_Area_Resize_By_Pointer_Drag_Changes_The_Column_And_Stores_The_Width()
    {
        var page = await OpenAsync();
        await SelectWidthAsync(page, "desktop", "desktop");
        await OpenWorkAreaAsync(page);

        var separator = page.GetByTestId("shell-app-shell-work-area-resizer");
        var workArea = page.GetByTestId("shell-app-shell-work-area");
        var before = (await workArea.BoundingBoxAsync())!.Width;

        var box = (await separator.BoundingBoxAsync())!;
        var y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 - 40, y, new MouseMoveOptions { Steps = 4 });
        await page.Mouse.MoveAsync(box.X + box.Width / 2 - 80, y, new MouseMoveOptions { Steps = 4 });
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);

        var after = (await workArea.BoundingBoxAsync())!.Width;
        Assert.IsTrue(after > before + 40, $"dragging the separator left grows the work area ({before}px -> {after}px)");
        Assert.AreEqual((int)Math.Round(after), await WorkAreaWidthAsync(page),
            "the announced value matches the painted column");

        var stored = await page.EvaluateAsync<string?>($"() => localStorage.getItem('{StorageKey}')");
        Assert.IsNotNull(stored, "a pointer resize persists too");
        StringAssert.Contains(stored, ((int)Math.Round(after)).ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Double click resets to the host width (420). The separator MOVED with the resize, so the
        // pointer has to follow the element, not the coordinates of the drag start.
        var moved = (await separator.BoundingBoxAsync())!;
        await page.Mouse.DblClickAsync(moved.X + moved.Width / 2, moved.Y + moved.Height / 2);
        await page.WaitForTimeoutAsync(400);
        await Assertions.Expect(separator).ToHaveAttributeAsync("aria-valuenow", "420");
        Assert.AreEqual(420, (int)Math.Round((await workArea.BoundingBoxAsync())!.Width));

        await ShootStageAsync(page, "work-area-pointer-1440");
    }

    // ── Account menu and checklist ─────────────────────────────────────────────

    [TestMethod]
    public async Task Account_Menu_Keyboard_Follows_The_Apg_Menu_Button_Pattern_And_Switches_The_Theme()
    {
        var page = await OpenAsync();
        await SelectWidthAsync(page, "desktop", "desktop");

        var trigger = page.GetByTestId("frame-account-user-menu-trigger");
        Assert.AreEqual("menu", await trigger.GetAttributeAsync("aria-haspopup"));
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-expanded"));
        var label = await trigger.GetAttributeAsync("aria-label");
        StringAssert.Contains(label, "Jane Doe", "the accessible name contains the visible user name");

        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.getAttribute('data-testid') === 'frame-account-user-menu-item-orders'",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");

        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(page.GetByTestId("frame-account-user-menu-sign-out")).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(page.GetByTestId("frame-account-user-menu-item-orders")).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.getAttribute('data-testid') === 'frame-account-user-menu-trigger'",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-expanded"));

        // The profile completion block and the theme switch are the reasons this menu is more than a list.
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.ClickAsync();
        var menu = page.GetByTestId("frame-account-user-menu").Locator("[role='menu']");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        StringAssert.Contains(await menu.TextContentAsync() ?? string.Empty, "Profile 60% complete");
        var theme = page.GetByTestId("frame-account-user-menu-theme");
        Assert.AreEqual("menuitemcheckbox", await theme.GetAttributeAsync("role"));
        var wasDark = await page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('dark') || !!document.querySelector(\"[data-theme='dark']\")");
        Assert.AreEqual("false", await theme.GetAttributeAsync("aria-checked"));

        await theme.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => !!document.querySelector(\"[data-theme='dark']\")", null, new PageWaitForFunctionOptions { Timeout = 15000 });
        Assert.IsFalse(wasDark, "precondition: the demo started in the light theme");

        await page.Locator("button[title*='light' i]:visible").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => !document.querySelector(\"[data-theme='dark']\")", null, new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    [TestMethod]
    public async Task Checklist_Toggle_Updates_The_Model_Announces_Once_And_Reports_To_The_Page()
    {
        var page = await OpenAsync();
        await SelectWidthAsync(page, "desktop", "desktop");

        var checklist = page.GetByTestId("live-onboarding-checklist");
        await Assertions.Expect(checklist).ToBeVisibleAsync();
        var first = checklist.Locator("input[type='checkbox']").First;
        Assert.IsFalse(await first.IsCheckedAsync());
        await Assertions.Expect(page.GetByTestId("live-onboarding-checklist-progress-text")).ToContainTextAsync("0 of 4");

        await first.CheckAsync();

        await Assertions.Expect(page.GetByTestId("live-onboarding-checklist-progress-text")).ToContainTextAsync("1 of 4");
        await Assertions.Expect(page.GetByTestId("app-shell-frame-last-step")).ToContainTextAsync("Connect a data source");

        // ONE polite live region announces the change; a second read must not duplicate the state.
        var announcer = page.GetByTestId("live-onboarding-checklist-announcer");
        Assert.AreEqual("status", await announcer.GetAttributeAsync("role"));
        StringAssert.Contains(await announcer.TextContentAsync() ?? string.Empty, "Connect a data source");
        StringAssert.Contains(await announcer.TextContentAsync() ?? string.Empty, "done");
        Assert.AreEqual(1, await checklist.Locator("[role='status']").CountAsync(),
            "the checklist keeps exactly one live region");

        await ShootStageAsync(page, "checklist-checked-1440");
    }

    // ── Variants: the Workspace treatment drops the content panel (TG-008) ─────

    private async Task<string> ComputedAsync(IPage page, string selector, string property)
        => await page.EvaluateAsync<string>(
            $"() => getComputedStyle(document.querySelector(\"{selector}\")).{property}");

    /// <summary>The frame div paints the workspace background (the root itself is transparent).</summary>
    private static string FrameSelector => "[data-testid='shell-app-shell'] [data-region='frame']";

    private async Task SelectVariantAsync(IPage page, string variant)
    {
        var button = page.GetByTestId($"app-shell-variant-{variant}");
        await button.EvaluateAsync("el => { el.scrollIntoView({ block: 'center', behavior: 'instant' }); }");
        await page.WaitForTimeoutAsync(150);
        await button.ClickAsync();
        await page.WaitForFunctionAsync(
            "expected => document.querySelector(\"[data-testid='shell-app-shell']\")?.getAttribute('data-variant') === expected",
            variant, new PageWaitForFunctionOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(250);
    }

    [TestMethod]
    public async Task Variant_Workspace_Drops_The_Content_Panel_In_Light_And_Dark()
    {
        var page = await OpenAsync();
        await SelectWidthAsync(page, "desktop", "desktop");

        const string transparent = "rgba(0, 0, 0, 0)";

        // Inset baseline: the content IS a surface card with a border (today's look, unchanged).
        Assert.AreEqual("inset", await Frame(page).GetAttributeAsync("data-variant"));
        var insetMainBg = await ComputedAsync(page, "[data-testid='shell-app-shell-main']", "backgroundColor");
        Assert.AreNotEqual(transparent, insetMainBg, "the Inset content panel is an opaque surface card");
        Assert.AreEqual("1px", await ComputedAsync(page, "[data-testid='shell-app-shell-main']", "borderTopWidth"));
        var insetFrameBg = await ComputedAsync(page, FrameSelector, "backgroundColor");

        // Flat: still the opaque surface, but no card border.
        await SelectVariantAsync(page, "flat");
        Assert.AreNotEqual(transparent, await ComputedAsync(page, "[data-testid='shell-app-shell-main']", "backgroundColor"));
        Assert.AreEqual("0px", await ComputedAsync(page, "[data-testid='shell-app-shell-main']", "borderTopWidth"));

        // Workspace: main is transparent — the workspace tone behind it paints through, no border.
        await SelectVariantAsync(page, "workspace");
        Assert.AreEqual(transparent, await ComputedAsync(page, "[data-testid='shell-app-shell-main']", "backgroundColor"),
            "the Workspace variant has no content panel: main is transparent over the workspace");
        Assert.AreEqual("0px", await ComputedAsync(page, "[data-testid='shell-app-shell-main']", "borderTopWidth"));
        Assert.AreEqual("0px", await ComputedAsync(page, "[data-testid='shell-app-shell-main']", "borderTopLeftRadius"));
        Assert.AreEqual(insetFrameBg, await ComputedAsync(page, FrameSelector, "backgroundColor"),
            "the frame itself keeps painting the workspace background");

        // The work area keeps its own panel in the workspace variant (chrome, not content).
        await OpenWorkAreaAsync(page);
        Assert.AreNotEqual(transparent, await ComputedAsync(page, "[data-testid='shell-app-shell-work-area']", "backgroundColor"),
            "a tool column floating on the transparent workspace would read as page content");
        await page.GetByTestId("app-shell-work-area-toggle").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='app-shell-sources']",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });

        // Dark theme: the transparency holds and the workspace tone flips with the token layer.
        await page.Locator("button[title*='dark' i]:visible").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);
        Assert.AreEqual(transparent, await ComputedAsync(page, "[data-testid='shell-app-shell-main']", "backgroundColor"),
            "the Workspace variant stays panel-less in the dark theme");
        Assert.AreNotEqual(insetFrameBg, await ComputedAsync(page, FrameSelector, "backgroundColor"),
            "the workspace background follows the dark token layer");

        await ShootStageAsync(page, "frame-workspace-dark-1440");
        await page.Locator("button[title*='light' i]:visible").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => !document.querySelector(\"[data-theme='dark']\")", null, new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    [TestMethod]
    public async Task Variant_Screenshots_Light_And_Dark_At_1440()
    {
        var page = await OpenAsync();
        await SelectWidthAsync(page, "desktop", "desktop");

        foreach (var variant in new[] { "inset", "flat", "workspace" })
        {
            await SelectVariantAsync(page, variant);
            await ShootStageAsync(page, $"frame-variant-{variant}-light-1440");

            await page.Locator("button[title*='dark' i]:visible").First.ClickAsync();
            await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15000 });
            await page.WaitForTimeoutAsync(400);
            await ShootStageAsync(page, $"frame-variant-{variant}-dark-1440");

            await page.Locator("button[title*='light' i]:visible").First.ClickAsync();
            await page.WaitForFunctionAsync(
                "() => !document.querySelector(\"[data-theme='dark']\")", null, new PageWaitForFunctionOptions { Timeout = 15000 });
            await page.WaitForTimeoutAsync(300);
        }

        // Edge case: the workspace variant WITH the work area open — the panel must read as chrome.
        await SelectVariantAsync(page, "workspace");
        await OpenWorkAreaAsync(page);
        await ShootStageAsync(page, "frame-variant-workspace-workarea-light-1440");
    }

    [TestMethod]
    public async Task Variant_Workspace_On_A_Phone_Stage_Screenshot()
    {
        var page = await OpenAsync(width: 390, height: 900, touch: true);
        await SelectWidthAsync(page, "phone", "mobile");
        await SelectVariantAsync(page, "workspace");

        const string transparent = "rgba(0, 0, 0, 0)";
        Assert.AreEqual(transparent, await ComputedAsync(page, "[data-testid='shell-app-shell-main']", "backgroundColor"),
            "a phone container gets the same panel-less main as a desktop one");

        await ShootStageAsync(page, "frame-variant-workspace-phone-390");
    }

    // ── axe and the visual reference ───────────────────────────────────────────

    private static async Task AssertNoAxeViolationsAsync(IPage page, string state)
    {
        var violations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelector("[data-testid='app-shell-stage']");
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).slice(0, 4).join('; ')})`);
            }
            """);
        Assert.AreEqual(0, violations.Length, $"axe [{state}]: " + string.Join(" | ", violations));
    }

    [TestMethod]
    public async Task Axe_Scoped_To_The_Frame_Finds_No_Critical_Or_Serious_Violation()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js" });

        await SelectWidthAsync(page, "desktop", "desktop");
        await OpenWorkAreaAsync(page);
        await page.WaitForTimeoutAsync(500);
        await AssertNoAxeViolationsAsync(page, "desktop with the work area open");

        // Leaving the work area open would leave a modal sheet over the phone frame (and its backdrop
        // would swallow the drawer click below). Close it with the same user control that opened it.
        await page.GetByTestId("app-shell-work-area-toggle").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='app-shell-sources']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });

        await SelectWidthAsync(page, "phone", "mobile");
        await page.WaitForTimeoutAsync(500);
        await AssertNoAxeViolationsAsync(page, "phone");

        await page.GetByTestId("topbar-menu-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("shell-app-shell-nav-overlay")).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(600);
        await AssertNoAxeViolationsAsync(page, "phone with the navigation drawer open");
    }

    [TestMethod]
    public async Task Visual_Reference_Light_And_Dark_At_375_900_And_1440()
    {
        foreach (var width in new[] { 375, 900, 1440 })
        {
            var page = await OpenAsync(width: width, height: 1000, touch: width < 640);
            await page.WaitForTimeoutAsync(400);
            if (width >= 1440)
            {
                // The widest reference is the DESKTOP presentation: the stage is narrower than the
                // demo column, so "fit" would only ever show the tablet rail at 1440.
                await SelectWidthAsync(page, "desktop", "desktop");
            }

            var lightText = await page.EvaluateAsync<string>(
                "() => getComputedStyle(document.querySelector(\"[data-testid='app-shell-stage']\")).backgroundColor");
            await ShootStageAsync(page, $"frame-light-{width}");

            await page.Locator("button[title*='dark' i]:visible").First.ClickAsync();
            await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15000 });
            await page.WaitForTimeoutAsync(400);

            var darkText = await page.EvaluateAsync<string>(
                "() => getComputedStyle(document.querySelector(\"[data-testid='app-shell-stage']\")).backgroundColor");
            Assert.AreNotEqual(lightText, darkText, $"the frame follows the token layer at {width}px");
            await ShootStageAsync(page, $"frame-dark-{width}");

            // The account menu is the frame's own theme switch: it is the entry the visual review opens.
            await page.Locator("button[title*='light' i]:visible").First.ClickAsync();
            await page.WaitForFunctionAsync(
                "() => !document.querySelector(\"[data-theme='dark']\")", null, new PageWaitForFunctionOptions { Timeout = 15000 });
        }
    }
}

/// <summary>Plan 5d396592 phase 6 — the frame E2E lane on the WASM demo (:7106).</summary>
[TestClass]
public sealed class AppShellE2ETests : AppShellE2EBase
{
    /// <inheritdoc />
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}

/// <summary>Plan 5d396592 phase 6 — the same frame contract on the Blazor Server demo (:7107).</summary>
[TestClass]
public sealed class AppShellServerE2ETests : AppShellE2EBase
{
    /// <inheritdoc />
    protected override string BaseUrl => "https://localhost:7107";
}
