using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 10c (HeatControl mock gaps) in a real browser: <c>TmTimePicker.HideClear</c>,
/// <c>ButtonVariant.OutlineDanger</c>, <c>TmAppShell.TopBarVisibility</c> + <c>BrandContent</c> and the
/// <c>FullWidth</c> <c>TmSegmentedControl</c> whose seven short labels must not clip. Assertions read
/// computed colours, real geometry and the DOM — and reference screenshots (light + dark) land in
/// <c>__screenshots__/small-gaps-10c</c> for the UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class SmallGaps10cE2ETests : WasmTestBase
{
    private static readonly string[] Themes = ["light", "dark"];

    private async Task<IPage> OpenAsync(string path, string readySelector, int width = 1440, int height = 1000)
    {
        var context = await CreateContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{path}");
        await page.WaitForSelectorAsync(readySelector, new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000,
        });
        return page;
    }

    private static async Task SetThemeAsync(IPage page, string theme)
    {
        // The CURRENT theme is read from the shell's own attribute: not every demo page's toggle carries
        // aria-pressed, so a pressed-state check would never switch back to light.
        var current = await page.Locator(".demo-shell").GetAttributeAsync("data-theme");
        if (!string.Equals(current, theme, StringComparison.Ordinal))
        {
            var toggle = page.Locator("[data-testid='theme-toggle']:visible").First;
            await toggle.ClickAsync();
        }

        await Assertions.Expect(page.Locator(".demo-shell"))
            .ToHaveAttributeAsync("data-theme", theme, new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.getAnimations().every(a => a.playState !== 'running' || a.effect?.getComputedTiming().iterations === Infinity)",
            null, new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    private string ShotPath(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
        var dir = Path.Combine(root, "tests", "Tempo.Blazor.E2E", "__screenshots__", "small-gaps-10c");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{name}.png");
    }

    private async Task ShootAsync(ILocator target, string name)
    {
        await target.ScrollIntoViewIfNeededAsync();
        await target.Page.Mouse.MoveAsync(0, 0);
        await target.Page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await target.Page.WaitForTimeoutAsync(250);
        var path = ShotPath(name);
        await target.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        TestContext.AddResultFile(path);
    }

    // ── TmTimePicker.HideClear ───────────────────────────────────────────────

    [TestMethod]
    public async Task TimePicker_HideClear_RemovesTheClearButton_WhileTheDefaultKeepsIt()
    {
        var page = await OpenAsync("/pickers", "[data-testid='time-picker-hide-clear']");

        var hidden = page.Locator("[data-testid='time-picker-hide-clear']");
        Assert.AreEqual(0, await hidden.Locator(".tm-picker-clear").CountAsync(),
            "HideClear must remove the clear button even though the field holds a value");
        Assert.AreEqual("09", await hidden.Locator(".tm-time-seg--hours").InputValueAsync(), "the seeded value is present");

        // The default picker (Basic Time Picker) shows the clear button once it has a value.
        var basic = page.Locator(".showcase-card", new PageLocatorOptions { HasText = "Basic Time Picker" })
            .Locator(".tm-time-picker");
        await basic.Locator(".tm-time-seg--hours").FillAsync("10");
        await basic.Locator(".tm-time-seg--hours").PressAsync("Tab");
        await basic.Locator(".tm-time-seg--minutes").FillAsync("15");
        await basic.Locator(".tm-time-seg--minutes").PressAsync("Tab");
        await Assertions.Expect(basic.Locator(".tm-picker-clear")).ToHaveCountAsync(1);
    }

    [TestMethod]
    public async Task TimePicker_HideClear_ValueStaysEditable()
    {
        var page = await OpenAsync("/pickers", "[data-testid='time-picker-hide-clear']");
        var card = page.Locator("[data-testid='time-picker-hide-clear-card']");

        await card.Locator(".tm-time-seg--hours").FillAsync("11");
        await card.Locator(".tm-time-seg--hours").PressAsync("Tab");

        await Assertions.Expect(card.Locator("p span.font-mono").First).ToHaveTextAsync("11:00");
        Assert.AreEqual(0, await card.Locator(".tm-picker-clear").CountAsync());
    }

    [TestMethod]
    public async Task TimePicker_HideClear_Screenshots_LightAndDark()
    {
        var page = await OpenAsync("/pickers", "[data-testid='time-picker-hide-clear']");
        foreach (var theme in Themes)
        {
            await SetThemeAsync(page, theme);
            await ShootAsync(page.Locator("[data-testid='time-picker-hide-clear-card']"), $"time-picker-hide-clear-{theme}");
        }
    }

    // ── ButtonVariant.OutlineDanger ──────────────────────────────────────────

    [TestMethod]
    public async Task OutlineDanger_PaintsTheQuietDestructiveLook_AndTheDangerHover()
    {
        var page = await OpenAsync("/buttons", "[data-testid='button-variant-outline-danger']");
        var button = page.Locator("[data-testid='button-variant-outline-danger']");
        await button.ScrollIntoViewIfNeededAsync();

        await Assertions.Expect(button).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"(^|\s)tm-btn-outline-danger(\s|$)"));

        var rest = await button.EvaluateAsync<string[]>(
            "el => { const s = getComputedStyle(el); return [s.backgroundColor, s.color, s.borderTopColor, s.borderTopWidth]; }");
        Assert.AreEqual("rgba(0, 0, 0, 0)", rest[0], "rest is transparent: the quiet look");
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", rest[2], "the soft border frames the target");
        Assert.AreEqual("1px", rest[3]);
        Assert.AreNotEqual(rest[1], rest[2], "ink and border are different tokens (text vs soft border)");

        await button.HoverAsync();
        await page.WaitForTimeoutAsync(300);
        var hovered = await button.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", hovered, "hover paints the danger-subtle wash");
    }

    [TestMethod]
    public async Task OutlineDanger_StaysReadable_InDarkTheme()
    {
        var page = await OpenAsync("/buttons", "[data-testid='button-variant-outline-danger']");
        var button = page.Locator("[data-testid='button-variant-outline-danger']");

        var light = await button.EvaluateAsync<string>("el => getComputedStyle(el).color");
        await SetThemeAsync(page, "dark");
        var dark = await button.EvaluateAsync<string>("el => getComputedStyle(el).color");

        Assert.AreNotEqual(light, dark, "the ink flips with the theme (#991b1b → #fca5a5), no dark block needed");
        Assert.AreEqual("rgb(252, 165, 165)", dark, "dark ink is the light danger tone --tm-color-danger-text (#fca5a5)");
    }

    [TestMethod]
    public async Task OutlineDanger_Screenshots_LightAndDark()
    {
        var page = await OpenAsync("/buttons", "[data-testid='button-variant-outline-danger']");
        foreach (var theme in Themes)
        {
            await SetThemeAsync(page, theme);
            await ShootAsync(page.Locator("[data-testid='buttons-variants-card']"), $"outline-danger-{theme}");
        }
    }

    // ── TmAppShell.TopBarVisibility + BrandContent ───────────────────────────

    private static ILocator Frame(IPage page) => page.GetByTestId("shell-app-shell");

    private async Task SelectWidthAsync(IPage page, string preset, string layout)
    {
        var button = page.GetByTestId($"app-shell-width-{preset}");
        await button.EvaluateAsync("el => el.scrollIntoView({ block: 'center', behavior: 'instant' })");
        await button.ClickAsync();
        await page.WaitForFunctionAsync(
            "expected => document.querySelector(\"[data-testid='shell-app-shell']\")?.getAttribute('data-layout') === expected",
            layout, new PageWaitForFunctionOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(300);
    }

    [TestMethod]
    public async Task AppShell_CompactOnly_DropsTheBarOnDesktop_AndKeepsItOnTabletAndPhone()
    {
        var page = await OpenAsync("/app-shell", "[data-testid='app-shell-stage']");
        await SelectWidthAsync(page, "desktop", "desktop");

        Assert.AreEqual(1, await Frame(page).Locator("header").CountAsync(), "Always (default): the desktop frame carries the bar");

        await page.GetByTestId("app-shell-topbar-compact").ClickAsync();
        await Assertions.Expect(page.GetByTestId("app-shell-topbar-compact")).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.AreEqual(0, await Frame(page).Locator("header").CountAsync(),
            "CompactOnly on a desktop: the bar is not in the DOM — not merely hidden by CSS");
        Assert.AreEqual(0, await Frame(page).Locator("[data-region='topbar']").CountAsync());
        Assert.AreEqual(1, await Frame(page).Locator("[data-region='sidebar']").CountAsync(), "the docked navigation stays");
        Assert.AreEqual(1, await Frame(page).Locator("main").CountAsync());

        await SelectWidthAsync(page, "tablet", "tablet");
        Assert.AreEqual(1, await Frame(page).Locator("header").CountAsync(), "a tablet is compact: the bar returns");

        await SelectWidthAsync(page, "phone", "mobile");
        Assert.AreEqual(1, await Frame(page).Locator("header").CountAsync(), "a phone is compact too");
        await Assertions.Expect(Frame(page).GetByTestId("topbar-menu-button")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task AppShell_BrandContent_ReplacesTheTitleInTheBuiltInBar()
    {
        var page = await OpenAsync("/app-shell", "[data-testid='app-shell-stage']");
        await SelectWidthAsync(page, "desktop", "desktop");

        Assert.AreEqual(0, await Frame(page).Locator("[data-testid='app-shell-brand-logo']").CountAsync());
        Assert.AreEqual(1, await Frame(page).Locator(".tm-topbar-brand-title").CountAsync(), "without the slot the plain title renders");

        await page.GetByTestId("app-shell-brand-logo-toggle").ClickAsync();

        var brand = Frame(page).Locator("header .tm-topbar-brand");
        await Assertions.Expect(brand.GetByTestId("app-shell-brand-logo")).ToBeVisibleAsync();
        Assert.AreEqual(0, await Frame(page).Locator("header .tm-topbar-brand-title").CountAsync(),
            "the brand slot takes the place of the plain title text");
    }

    [TestMethod]
    public async Task AppShell_TopBarSwitch_Screenshots_LightAndDark()
    {
        var page = await OpenAsync("/app-shell", "[data-testid='app-shell-stage']");
        await SelectWidthAsync(page, "desktop", "desktop");
        await page.GetByTestId("app-shell-topbar-compact").ClickAsync();
        foreach (var theme in Themes)
        {
            await SetThemeAsync(page, theme);
            await ShootAsync(Frame(page), $"app-shell-compact-only-desktop-{theme}");
        }

        await page.GetByTestId("app-shell-topbar-always").ClickAsync();
        await page.GetByTestId("app-shell-brand-logo-toggle").ClickAsync();
        foreach (var theme in Themes)
        {
            await SetThemeAsync(page, theme);
            await ShootAsync(Frame(page), $"app-shell-brand-logo-{theme}");
        }
    }

    // ── TmSegmentedControl FullWidth ─────────────────────────────────────────

    [TestMethod]
    [DataRow(390)]
    [DataRow(360)]
    public async Task FullWidthSegmented_SevenWeekdayLabels_AreNotClipped(int width)
    {
        var page = await OpenAsync("/data-display", "[data-testid='segmented-weekdays']", width: width, height: 900);
        var tray = page.Locator("[data-testid='segmented-weekdays']");
        await tray.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(400);

        var fits = await tray.Locator(".tm-segmented__option-label").EvaluateAllAsync<double[][]>(
            "els => els.map(el => [el.scrollWidth, el.clientWidth])");
        Assert.AreEqual(7, fits.Length, "seven weekday segments");
        for (var i = 0; i < fits.Length; i++)
        {
            Assert.IsTrue(fits[i][0] <= fits[i][1] + 0.5,
                $"label {i} is clipped at {width}px: scrollWidth {fits[i][0]} > clientWidth {fits[i][1]}");
        }

        var text = await tray.Locator(".tm-segmented__option-label").AllInnerTextsAsync();
        Assert.IsTrue(text.All(label => !label.Contains('…') && label.Length >= 1), "no label was truncated to an ellipsis");

        var padding = await tray.Locator(".tm-segmented__option").First.EvaluateAsync<string>(
            "el => getComputedStyle(el).paddingLeft");
        Assert.AreEqual("4px", padding, "FullWidth segments use the thin --tm-space-1 gutter");

        var trayBox = await tray.Locator(".tm-segmented__track").BoundingBoxAsync();
        var pageWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.IsNotNull(trayBox);
        Assert.IsTrue(trayBox!.X + trayBox.Width <= width + 0.5, "the tray stays inside the viewport");
        Assert.IsTrue(pageWidth <= width + 1, $"the page does not scroll sideways (scrollWidth {pageWidth})");
    }

    [TestMethod]
    public async Task FullWidthSegmented_ContentSizedTrays_KeepTheirSizePadding()
    {
        var page = await OpenAsync("/data-display", "[data-testid='segmented-sizes']");
        var padding = await page.Locator("[data-testid='segmented-sizes'] .tm-segmented--md .tm-segmented__option")
            .First.EvaluateAsync<string>("el => getComputedStyle(el).paddingLeft");

        Assert.AreEqual("12px", padding, "only FullWidth tightens the padding; the content-sized Md tray keeps --tm-space-3");
    }

    [TestMethod]
    public async Task FullWidthSegmented_KeyboardStillWorks_WithTheTightPadding()
    {
        var page = await OpenAsync("/data-display", "[data-testid='segmented-weekdays']", width: 390, height: 900);
        var options = page.Locator("[data-testid='segmented-weekdays'] [role='radio']");
        await options.Nth(0).FocusAsync();

        await page.Keyboard.PressAsync("ArrowRight");

        await Assertions.Expect(options.Nth(1)).ToHaveAttributeAsync("aria-checked", "true");
    }

    [TestMethod]
    public async Task FullWidthSegmented_Screenshots_PhoneAndDesktop_LightAndDark()
    {
        var phone = await OpenAsync("/data-display", "[data-testid='segmented-weekdays']", width: 390, height: 900);
        foreach (var theme in Themes)
        {
            await SetThemeAsync(phone, theme);
            await ShootAsync(phone.Locator("[data-testid='segmented-weekdays-card']"), $"segmented-weekdays-390-{theme}");
        }

        var desktop = await OpenAsync("/data-display", "[data-testid='segmented-weekdays']", width: 1440, height: 1000);
        foreach (var theme in Themes)
        {
            await SetThemeAsync(desktop, theme);
            await ShootAsync(desktop.Locator("[data-testid='segmented-weekdays-card']"), $"segmented-weekdays-1440-{theme}");
        }
    }
}
