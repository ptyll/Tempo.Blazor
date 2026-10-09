using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F5 lane: the mobile action bar on the real components — the /mobile-action-bar demo page
/// and the TmDashboard edit mode — at 390 and 320 with touch emulation, plus 1440 to prove
/// the hidden/desktop behaviour. Screenshots are viewport-only (FullPage resets touch
/// emulation), with the viewport height set to the scroll height.
/// </summary>
[TestClass]
public class MobileActionBarE2ETests : WasmTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "mobile-action-bar");

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private static async Task<IBrowserContext> CreateTouchContextAsync(int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            IsMobile = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        return context;
    }

    /// <summary>
    /// Viewport-only screenshot that keeps touch emulation intact: resize the viewport to the
    /// scroll height instead of using FullPage.
    /// </summary>
    private async Task<string> CaptureTouchViewportAsync(IPage page, int width, string name)
    {
        var height = await page.EvaluateAsync<int>(
            "() => Math.min(Math.ceil(document.documentElement.scrollHeight), 4000)");
        await page.SetViewportSizeAsync(width, height);
        await page.WaitForTimeoutAsync(200); // let the sticky bar and transitions settle
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");

        var path = Path.Combine(ShotDir, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });

        var coarse = await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches");
        Assert.IsTrue(coarse, "(pointer: coarse) must still match after the touch screenshot");
        return path;
    }

    [TestMethod]
    public async Task Demo_390_Bar_Sticky_MoreSheet_ConfirmAboveSheet()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/mobile-action-bar");
        await WaitForAppReadyAsync(page);

        var bar = page.Locator("[data-testid='mab-auto-bar'] .tm-mobile-action-bar__bar");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Placement: sticky inside the container, never fixed; 44px-class touch targets.
        var position = await bar.EvaluateAsync<string>("el => getComputedStyle(el).position");
        Assert.AreEqual("sticky", position, "the default placement is position: sticky");
        var barBox = await bar.BoundingBoxAsync();
        Assert.IsNotNull(barBox);
        Assert.IsTrue(barBox!.Height >= 44, $"bar must be at least the 44px touch target, was {barBox.Height}");

        var buttons = bar.Locator(".tm-btn");
        Assert.AreEqual(4, await buttons.CountAsync(), "3 actions + the More trigger");

        // The content region above keeps a clearance equal to the bar height (no overlap).
        var padding = await page.Locator("[data-testid='mab-auto-bar'] .tm-mobile-action-bar__body")
            .EvaluateAsync<string>("el => getComputedStyle(el).paddingBottom");
        var paddingPx = double.Parse(padding.Replace("px", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        Assert.AreEqual(barBox.Height, paddingPx, delta: 1.5,
            "padding-bottom of the region above must equal the bar height");

        // An action on the bar runs.
        await bar.Locator(".tm-mobile-action-bar__action", new() { HasText = "Share" }).ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='mab-last-action']")).ToContainTextAsync("Share");

        await CaptureTouchViewportAsync(page, 390, "390-bar");

        // The More menu presents as the F3 bottom sheet on a phone. Focus the trigger first:
        // a touch tap does not move focus, and the focus scope restores to whatever was
        // focused when the sheet opened — this models a keyboard/AT user deterministically.
        var moreButton = bar.Locator(".tm-mobile-action-bar__more");
        await moreButton.FocusAsync();
        await moreButton.ClickAsync();
        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var menu = sheet.Locator("[role='menu']");
        await Assertions.Expect(menu).ToBeVisibleAsync();
        var menuItems = menu.Locator("[role='menuitem']");
        Assert.AreEqual(2, await menuItems.CountAsync(), "Archive and Delete overflow");

        // Menu semantics: initial focus lands on the first menuitem.
        await page.WaitForTimeoutAsync(300);
        var focusedRole = await page.EvaluateAsync<string>(
            "() => document.activeElement?.getAttribute('role') ?? document.activeElement?.tagName ?? 'none'");
        Assert.AreEqual("menuitem", focusedRole, "initial focus must be the first menuitem");

        await CaptureTouchViewportAsync(page, 390, "390-more-sheet");

        // The destructive item opens a confirm dialog: it must paint ABOVE the sheet.
        // probe4: elementFromPoint skips inert elements, so strip inert (and restore it
        // synchronously) to make the paint-order assertion honest.
        await menuItems.Nth(1).ClickAsync(); // Delete
        var dialogPanel = page.Locator(".tm-modal-container");
        await dialogPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // The dialog promotes to the top layer after its opening render — the paint assertion
        // is meaningless before showPopover() ran. The promoted root is the modal overlay.
        await page.WaitForFunctionAsync(
            "() => { const overlay = document.querySelector('.tm-modal-overlay[popover]'); return !!overlay && overlay.matches(':popover-open'); }",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });

        var dialogPaintsAbove = await page.EvaluateAsync<bool>(
            """
            () => {
                const inerted = [];
                document.querySelectorAll('[inert]').forEach(el => { el.removeAttribute('inert'); inerted.push(el); });
                try {
                    const panel = document.querySelector('.tm-dialog');
                    if (!panel) return false;
                    const rect = panel.getBoundingClientRect();
                    const x = rect.left + rect.width / 2;
                    const y = rect.top + rect.height / 2;
                    // Topmost first; pointer-events:none regions are skipped by the hit test,
                    // so judge by the paint-order position of the first real element of each
                    // surface (elementsFromPoint, plural).
                    const stack = document.elementsFromPoint(x, y);
                    const dialogHit = stack.findIndex(el => panel.contains(el));
                    const sheetHit = stack.findIndex(el => !!el.closest('.tm-overlay-panel-sheet'));
                    return dialogHit !== -1 && (sheetHit === -1 || dialogHit < sheetHit);
                } finally {
                    inerted.forEach(el => el.setAttribute('inert', ''));
                }
            }
            """);
        Assert.IsTrue(dialogPaintsAbove, "the confirm dialog opened from the More sheet must paint above the sheet (top-layer order = open order)");

        // The item opted into KeepMenuOpen: the sheet stays open behind the dialog.
        await Assertions.Expect(page.Locator(".tm-overlay-panel-sheet")).ToBeVisibleAsync();

        await CaptureTouchViewportAsync(page, 390, "390-confirm-above-sheet");

        // Escape order: the dialog (innermost scope) first, then the sheet; focus returns to
        // the More trigger at the end.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialogPanel).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".tm-overlay-panel-sheet")).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-overlay-panel-sheet")).ToBeHiddenAsync();

        // The restore lands a frame after the trap teardown — poll rather than read once.
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.classList?.contains('tm-mobile-action-bar__more') ?? false",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task Demo_320_CzechLabels_NoOverflow_NoHorizontalScroll()
    {
        var context = await CreateTouchContextAsync(320, 740);
        // The demo culture follows the browser: seed localStorage + cookie (the language
        // switcher itself is unreachable inside the collapsed mobile nav).
        await context.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'cs');
            document.cookie = 'tm-demo-culture=cs; path=/';
            """);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/mobile-action-bar");
        await WaitForAppReadyAsync(page);

        var bar = page.Locator("[data-testid='mab-auto-bar'] .tm-mobile-action-bar__bar");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Localized labels (cs) fit without horizontal overflow.
        var scrollWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(scrollWidth <= 320, $"no horizontal overflow at 320 (scrollWidth {scrollWidth})");

        var labels = await bar.Locator(".tm-btn .tm-btn-label").AllInnerTextsAsync();
        CollectionAssert.Contains(labels.ToList(), "Sdílet");
        CollectionAssert.Contains(labels.ToList(), "Více");

        // Touch targets stay at the 44px class even in the narrowest layout.
        var firstButton = await bar.Locator(".tm-btn").First.BoundingBoxAsync();
        Assert.IsTrue(firstButton!.Height >= 44, $"button height {firstButton.Height}");

        await CaptureTouchViewportAsync(page, 320, "320-bar-cs");
    }

    [TestMethod]
    public async Task Demo_1440_AutoHidden_ForcedVariantsPaint()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/mobile-action-bar");
        await WaitForAppReadyAsync(page);

        // Auto visibility: above the mobile breakpoint the bar does not exist.
        Assert.AreEqual(0, await page.Locator("[data-testid='mab-auto-bar'] .tm-mobile-action-bar__bar").CountAsync(),
            "the Auto bar must be hidden at 1440");

        // Forced mobile layout: the sticky and inline variants render; the viewport-anchored
        // variant is a deliberate opt-in the demo reveals on demand (two bars must not fight
        // for the bottom edge at once).
        var sticky = page.Locator("[data-testid='mab-variant-sticky'] .tm-mobile-action-bar__bar");
        var inline = page.Locator("[data-testid='mab-variant-inline'] .tm-mobile-action-bar__bar");
        Assert.AreEqual(1, await sticky.CountAsync());
        Assert.AreEqual(1, await inline.CountAsync());

        Assert.AreEqual("sticky", await sticky.EvaluateAsync<string>("el => getComputedStyle(el).position"));
        Assert.AreEqual("static", await inline.EvaluateAsync<string>("el => getComputedStyle(el).position"));

        await page.Locator("[data-testid='mab-toggle-fixed']").ClickAsync();
        var fixedBar = page.Locator("[data-testid='mab-variant-fixed'] .tm-mobile-action-bar__bar");
        await fixedBar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual("fixed", await fixedBar.EvaluateAsync<string>("el => getComputedStyle(el).position"));

        // The fixed bar pins to the viewport bottom.
        var fixedBox = await fixedBar.BoundingBoxAsync();
        var viewport = page.ViewportSize!;
        Assert.IsTrue(Math.Abs(fixedBox!.Y + fixedBox.Height - viewport.Height) < 2,
            "the FixedViewport bar sits on the viewport bottom edge");
    }

    [TestMethod]
    public async Task Dashboard_390_EditMode_UsesActionBar()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/dashboard");
        await WaitForAppReadyAsync(page);

        // Enter edit mode on the phone.
        var edit = page.Locator("[data-testid='dashboard-edit']");
        await edit.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await edit.ClickAsync();

        var bar = page.Locator(".tm-dashboard .tm-mobile-action-bar__bar");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // The three edit actions live on the bar, and the wrapping toolbar actions are gone.
        var labels = await bar.Locator(".tm-btn .tm-btn-label").AllInnerTextsAsync();
        CollectionAssert.AreEqual(new List<string> { "Add Widget", "Save Changes", "Cancel" }, labels.ToList(),
            "dashboard edit actions render through the mobile action bar");
        Assert.AreEqual(0, await page.Locator(".tm-dashboard-toolbar-right .tm-btn:visible").CountAsync(),
            "the desktop toolbar actions must not render twice on mobile");

        await CaptureTouchViewportAsync(page, 390, "390-dashboard-edit");

        // The bar's actions are wired: Add Widget opens the widget selector.
        await bar.Locator(".tm-btn", new() { HasText = "Add Widget" }).ClickAsync();
        await page.Locator(".tm-widget-selector-overlay").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    [TestMethod]
    public async Task Dashboard_1440_EditMode_KeepsDesktopToolbar()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/dashboard");
        await WaitForAppReadyAsync(page);

        await page.Locator("[data-testid='dashboard-edit']").ClickAsync();

        Assert.AreEqual(0, await page.Locator(".tm-dashboard .tm-mobile-action-bar__bar").CountAsync(),
            "no mobile action bar at 1440");
        Assert.AreEqual(3, await page.Locator(".tm-dashboard-toolbar-right .tm-btn:visible").CountAsync(),
            "the desktop edit toolbar keeps Add/Save/Cancel");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
