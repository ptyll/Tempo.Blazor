using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Decision EDITOR-SHELL-TABLET-PRESENTATION on the REAL core shell: the <c>/editor-shell</c> demo's overlay section
/// (<c>TabletPanelPresentation.Overlay</c>, thresholds 768 / 1200). At 1024 the shell is a tablet: the toolbar buttons drive an overlay
/// drawer (Left) and a non-modal side sheet (Right) over a full-width canvas, at most one open, closed by the x or Escape (the
/// focus returns to the toggle); at 1440 the same shell docks its panels. Screenshots land in the git-ignored
/// <c>TestResults/editor-shell-overlay</c> folder.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EditorShellTabletOverlayE2ETests : WasmTestBase
{
    private const string Host = "[data-testid='editor-shell-overlay-host']";

    private static string ShotDir => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "editor-shell-overlay");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (TempoBlazor.slnx).");
    }

    private async Task<IPage> OpenAsync(int width, int height, bool touch, bool dark = false)
    {
        Directory.CreateDirectory(ShotDir);
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = false,
            Locale = "en-US",
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/editor-shell", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.AddStyleTagAsync(new()
        {
            Content = "aside.fixed { display: none !important; } main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }",
        });
        if (dark) await page.EvaluateAsync("() => document.documentElement.setAttribute('data-theme', 'dark')");
        await page.Locator(Host).ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private static ILocator Overlay(IPage page, string side) => page.Locator($"{Host} .tm-editor-shell__overlay--{side}");

    private async Task ShotAsync(IPage page, string name)
    {
        await page.WaitForTimeoutAsync(300);
        await page.Locator(Host).ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(ShotDir, $"{name}.png") });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(1024, 800)]
    [DataRow(1100, 900)]
    [Description("Tablet: the toolbar buttons open the overlay drawer / side sheet over a full-width canvas, at most one open, no rails, no separators")]
    public async Task Tablet_OverlayPanels_AtMostOne_CanvasKeepsItsWidth(int width, int height)
    {
        var page = await OpenAsync(width, height, touch: true);
        var layout = page.Locator($"{Host} .tm-editor-shell");
        await Assertions.Expect(layout).ToHaveAttributeAsync("data-layout", "tablet");
        var canvas = page.Locator($"{Host} [data-region='canvas']");
        var before = (await canvas.BoundingBoxAsync())!.Width;
        Assert.AreEqual(0, await page.Locator($"{Host} .tm-editor-shell__rail, {Host} [role='separator'], {Host} .tm-editor-shell__panel-toggle").CountAsync(), "no rails, separators or strip toggles");

        await page.Locator("[data-testid='es-overlay-toggle-left']").TapAsync();
        await Overlay(page, "left").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await ShotAsync(page, $"tablet-{width}--drawer");
        Assert.AreEqual(before, (await canvas.BoundingBoxAsync())!.Width, 1, "the drawer overlays: the canvas keeps its width");
        var drawer = (await Overlay(page, "left").BoundingBoxAsync())!;
        var host = (await page.Locator(Host).BoundingBoxAsync())!;
        Assert.IsTrue(drawer.Width <= host.Width * 0.6 + 1, $"the drawer covers at most 60 % ({drawer.Width:0} of {host.Width:0})");
        Assert.AreEqual("false", await Overlay(page, "left").GetAttributeAsync("aria-modal"));

        await page.Locator("[data-testid='es-overlay-toggle-right']").TapAsync();
        await Overlay(page, "right").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await Overlay(page, "left").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
        Assert.AreEqual(1, await page.Locator($"{Host} .tm-editor-shell__overlay").CountAsync(), "exactly one overlay");
        Assert.AreEqual("true", await page.Locator("[data-testid='es-overlay-toggle-right']").GetAttributeAsync("aria-pressed"));
        Assert.AreEqual("false", await page.Locator("[data-testid='es-overlay-toggle-left']").GetAttributeAsync("aria-pressed"));
        Assert.AreEqual("true", await page.Locator("[data-testid='es-overlay-toggle-right']").GetAttributeAsync("aria-expanded"));
        Assert.AreEqual("false", await page.Locator("[data-testid='es-overlay-toggle-left']").GetAttributeAsync("aria-expanded"));
        var controls = await page.Locator("[data-testid='es-overlay-toggle-right']").GetAttributeAsync("aria-controls");
        Assert.IsFalse(string.IsNullOrEmpty(controls), "an open panel's toggle names it through aria-controls");
        Assert.AreEqual(1, await page.Locator($"[id='{controls}']").CountAsync(), "aria-controls resolves to the open panel content");
        await ShotAsync(page, $"tablet-{width}--sheet");

        await Overlay(page, "right").Locator(".tm-editor-shell__overlay-close").TapAsync();
        await Overlay(page, "right").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: Escape closes the panel that holds the focus and the focus returns to the toolbar button that opened it")]
    public async Task Tablet_Escape_ClosesPanel_AndReturnsFocusToTheToggle()
    {
        var page = await OpenAsync(1024, 800, touch: false);
        await page.Locator("[data-testid='es-overlay-toggle-left']").ClickAsync();
        await Overlay(page, "left").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.WaitForFunctionAsync("() => !!document.activeElement?.closest('.tm-editor-shell__overlay')", null, new PageWaitForFunctionOptions { Timeout = 5_000 });

        await page.Keyboard.PressAsync("Escape");

        await Overlay(page, "left").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
        await page.WaitForFunctionAsync("() => document.activeElement?.getAttribute('data-testid') === 'es-overlay-toggle-left'", null, new PageWaitForFunctionOptions { Timeout = 5_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Coarse pointer: the close button reaches the 44 px touch target")]
    public async Task Tablet_CloseButton_Is44px_OnATouchContext()
    {
        var page = await OpenAsync(1024, 800, touch: true);
        await page.Locator("[data-testid='es-overlay-toggle-right']").TapAsync();
        var close = Overlay(page, "right").Locator(".tm-editor-shell__overlay-close");
        await close.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        var coarse = await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches");
        if (!coarse) Assert.Inconclusive("Chromium did not report (pointer: coarse) for the touch context.");
        var box = (await close.BoundingBoxAsync())!;
        Assert.IsTrue(box.Width >= 43.5 && box.Height >= 43.5, $"the close button is {box.Width:0}x{box.Height:0}");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Dark mode: the overlay rides the theme tokens (surface, text) - screenshots for the UX review")]
    public async Task Tablet_Overlay_Dark_RidesTheTokens()
    {
        var page = await OpenAsync(1024, 800, touch: true, dark: true);
        await page.Locator("[data-testid='es-overlay-toggle-right']").TapAsync();
        await Overlay(page, "right").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await ShotAsync(page, "tablet-1024-dark--sheet");

        var colors = await Overlay(page, "right").EvaluateAsync<string[]>("e => { const s = getComputedStyle(e); return [s.backgroundColor, s.color]; }");
        Assert.AreNotEqual("rgb(255, 255, 255)", colors[0], "the dark surface is not the light white");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Desktop 1440: the same shell docks both panels - no overlay, resizers present")]
    public async Task Desktop_1440_DocksThePanels_NoOverlay()
    {
        var page = await OpenAsync(1440, 900, touch: false);
        await Assertions.Expect(page.Locator($"{Host} .tm-editor-shell")).ToHaveAttributeAsync("data-layout", "desktop");

        Assert.AreEqual(0, await page.Locator($"{Host} .tm-editor-shell__overlay").CountAsync());
        Assert.AreEqual(0, await page.Locator($"{Host} aside.tm-editor-shell__panel").CountAsync(), "both demo panels start closed");
        await page.Locator("[data-testid='es-overlay-toggle-left']").ClickAsync();
        await page.Locator("[data-testid='es-overlay-toggle-right']").ClickAsync();

        await page.Locator($"{Host} aside.tm-editor-shell__panel--right").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.AreEqual(2, await page.Locator($"{Host} aside.tm-editor-shell__panel").CountAsync(), "desktop docks BOTH panels (the overlay's one-at-a-time rule is tablet only)");
        Assert.AreEqual(0, await page.Locator($"{Host} .tm-editor-shell__overlay").CountAsync(), "a desktop toggle never produces an overlay");
        Assert.AreEqual(2, await page.Locator($"{Host} .tm-editor-shell__resizer").CountAsync(), "the docked panels keep their resize separators");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: a tablet -> desktop resize docks the open panel; the overlay is gone")]
    public async Task TabletToDesktop_Resize_DocksTheOpenPanel()
    {
        var page = await OpenAsync(1024, 800, touch: false);
        await page.Locator("[data-testid='es-overlay-toggle-right']").ClickAsync();
        await Overlay(page, "right").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await page.SetViewportSizeAsync(1440, 900);
        await Assertions.Expect(page.Locator($"{Host} .tm-editor-shell")).ToHaveAttributeAsync("data-layout", "desktop", new() { Timeout = 10_000 });

        Assert.AreEqual(0, await page.Locator($"{Host} .tm-editor-shell__overlay").CountAsync(), "the overlay is a tablet presentation only");
        Assert.AreEqual(1, await page.Locator($"{Host} aside.tm-editor-shell__panel--right").CountAsync(), "the open panel docks");
    }
}