using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F5 lane: the mobile action bar on the real components — the /mobile-action-bar demo page
/// and the TmDashboard edit mode — at 390 and 320 with touch emulation, plus 1440 to prove
/// the hidden/desktop behaviour. Screenshots are viewport-only (FullPage resets touch
/// emulation); the touch capture helper resizes the viewport to the scroll height, while the
/// sticky checks MUST keep the real 844-high viewport — a stretched screenshot hides sticky
/// failures (F5 review round 1, X2c).
/// </summary>
[TestClass]
public class MobileActionBarE2ETests : WasmTestBase
{
    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "mobile-action-bar");

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private static async Task<IBrowserContext> CreateTouchContextAsync(int width, int height, string locale = "en-US")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            IsMobile = true,
            Locale = locale,
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

    /// <summary>
    /// Viewport-only capture WITHOUT touching the viewport size — the only honest way to
    /// screenshot a sticky bar (a viewport stretched to the page height pins every sticky
    /// element to the page bottom and hides the failure).
    /// </summary>
    private async Task<string> CaptureViewportOnlyAsync(IPage page, string name)
    {
        await page.WaitForTimeoutAsync(200);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");

        var path = Path.Combine(ShotDir, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        return path;
    }

    /// <summary>Asserts the bar is pinned to the viewport bottom edge (sticky, ±1 px).</summary>
    private static async Task AssertBarPinnedToViewportBottomAsync(IPage page, ILocator bar)
    {
        var box = await bar.BoundingBoxAsync();
        Assert.IsNotNull(box, "the bar must be measurable");
        var viewport = page.ViewportSize!;
        Assert.IsTrue(Math.Abs(box!.Y + box.Height - viewport.Height) <= 1,
            $"the sticky bar must sit on the viewport bottom (bottom {box.Y + box.Height}, viewport {viewport.Height})");
    }

    [TestMethod]
    public async Task Demo_390_Bar_Sticky_MoreSheet_ConfirmAboveSheet()
    {
        var context = await CreateTouchContextAsync(390, 844);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/mobile-action-bar");
        await WaitForAppReadyAsync(page);

        var root = page.Locator("[data-testid='mab-auto-bar']");
        var bar = root.Locator(".tm-mobile-action-bar__bar");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Placement: sticky inside the container, never fixed; 44px-class touch targets.
        var position = await bar.EvaluateAsync<string>("el => getComputedStyle(el).position");
        Assert.AreEqual("sticky", position, "the default placement is position: sticky");
        var barBox = await bar.BoundingBoxAsync();
        Assert.IsNotNull(barBox);
        Assert.IsTrue(barBox!.Height >= 44, $"bar must be at least the 44px touch target, was {barBox.Height}");

        // Three tile actions + the More trigger.
        var buttons = bar.Locator(".tm-mobile-action-bar__action");
        Assert.AreEqual(3, await buttons.CountAsync(), "3 actions stay on the bar");
        await Assertions.Expect(bar.Locator(".tm-mobile-action-bar__more")).ToBeVisibleAsync();

        // X2 (b): the content region reserves NOTHING for a sticky bar (the bar sits in flow at
        // the end of the content; a padding reserve would double-book an empty band at scroll
        // end). Only the FixedViewport variant reserves (see the 1440 test).
        var padding = await root.Locator(".tm-mobile-action-bar__body")
            .EvaluateAsync<string>("el => getComputedStyle(el).paddingBottom");
        Assert.AreEqual("0px", padding, "a sticky bar reserves no body padding");

        // The demo's disabled action renders disabled on the bar (X18).
        var disabledAction = bar.Locator(".tm-mobile-action-bar__action[disabled]");
        Assert.AreEqual(1, await disabledAction.CountAsync(), "one disabled action on the bar");
        await Assertions.Expect(disabledAction).ToContainTextAsync("Print");

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
        Assert.AreEqual(3, await menuItems.CountAsync(), "Duplicate, Archive and Delete overflow");

        // X18: the disabled demo item shows up disabled inside the menu too.
        Assert.AreEqual(1, await menu.Locator("[role='menuitem'][disabled]").CountAsync(),
            "one disabled overflow item");

        // Menu semantics: initial focus lands on the first ENABLED menuitem.
        await page.WaitForTimeoutAsync(300);
        var focusedRole = await page.EvaluateAsync<string>(
            "() => document.activeElement?.getAttribute('role') ?? document.activeElement?.tagName ?? 'none'");
        Assert.AreEqual("menuitem", focusedRole, "initial focus must be the first enabled menuitem");
        var focusedLabel = await page.EvaluateAsync<string>(
            "() => document.activeElement?.textContent?.trim() ?? ''");
        Assert.AreEqual("Duplicate", focusedLabel, "the disabled item is skipped by the initial focus");

        await CaptureTouchViewportAsync(page, 390, "390-more-sheet");

        // The destructive item opens a confirm dialog: it must paint ABOVE the sheet.
        // probe4: elementFromPoint skips inert elements, so strip inert (and restore it
        // synchronously) to make the paint-order assertion honest.
        await menuItems.Nth(2).ClickAsync(); // Delete (index 2: Duplicate, Archive, Delete)
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

        // X13: confirming Delete closes the sheet (the host calls CloseMoreAsync) and focus
        // returns to the More trigger.
        await page.Locator(".tm-dialog .tm-dialog-btn-ok").ClickAsync();
        await Assertions.Expect(dialogPanel).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".tm-overlay-panel-sheet")).ToBeHiddenAsync();
        // The demo closes the More sheet from the confirmed Delete result.
        Assert.AreEqual(0, await page.Locator(".tm-overlay-panel-sheet:visible").CountAsync(),
            "the demo closes the More sheet from the confirmed Delete result");
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.classList?.contains('tm-mobile-action-bar__more') ?? false",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });

        await CaptureTouchViewportAsync(page, 390, "390-after-confirm-ok");
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

        // X3: the full label text stays in the DOM (clamped to two lines, never clipped into
        // an ellipsis of a single line).
        var labels = bar.Locator(".tm-mobile-action-bar__label-text");
        var texts = await labels.AllTextContentsAsync();
        CollectionAssert.Contains(texts.ToList(), "Sdílet");
        CollectionAssert.Contains(texts.ToList(), "Tisk");
        CollectionAssert.Contains(texts.ToList(), "Upravit");
        CollectionAssert.Contains(
            (await bar.Locator(".tm-mobile-action-bar__more .tm-mobile-action-bar__label-text").AllTextContentsAsync()).ToList(),
            "Více");

        await AssertLabelsFitTwoLinesAsync(bar);

        // Touch targets stay at the 44px class even in the narrowest layout.
        var firstButton = await bar.Locator(".tm-mobile-action-bar__action").First.BoundingBoxAsync();
        Assert.IsTrue(firstButton!.Height >= 44, $"button height {firstButton.Height}");

        await CaptureTouchViewportAsync(page, 320, "320-bar-cs");
    }

    [DataTestMethod]
    [DataRow("cs")]
    [DataRow("fr")]
    public async Task Demo_320_LocalizedLabels_FitTwoLines_NoClipping(string culture)
    {
        // X3: at 320 no label is clipped to a single-line ellipsis in cs or fr — the tile
        // buttons wrap the full label onto at most two lines.
        var context = await CreateTouchContextAsync(320, 740);
        await context.AddInitScriptAsync(
            $$"""
            localStorage.setItem('tm-demo-culture', '{{culture}}');
            document.cookie = 'tm-demo-culture={{culture}}; path=/';
            """);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/mobile-action-bar");
        await WaitForAppReadyAsync(page);

        var bar = page.Locator("[data-testid='mab-auto-bar'] .tm-mobile-action-bar__bar");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await AssertLabelsFitTwoLinesAsync(bar);

        var actions = bar.Locator(".tm-mobile-action-bar__action");
        var count = await actions.CountAsync();
        Assert.IsTrue(count >= 3, "the bar renders its tile actions");
        for (var i = 0; i < count; i++)
        {
            var box = await actions.Nth(i).BoundingBoxAsync();
            Assert.IsTrue(box!.Height >= 44, $"action {i} must keep the 44px touch target, was {box.Height}");
        }

        await CaptureTouchViewportAsync(page, 320, $"320-bar-{culture}");
    }

    /// <summary>
    /// X3: every bar label renders its full text within the two-line clamp — the computed clamp
    /// is two lines, the box never shows a single-line ellipsis cut, and nothing exceeds two
    /// lines of height.
    /// </summary>
    private static async Task AssertLabelsFitTwoLinesAsync(ILocator bar)
    {
        var labels = bar.Locator(".tm-mobile-action-bar__label-text");
        var count = await labels.CountAsync();
        Assert.IsTrue(count >= 3, "the bar renders at least three labels");
        for (var i = 0; i < count; i++)
        {
            var label = labels.Nth(i);
            var fits = await label.EvaluateAsync<bool>(
                """
                el => {
                    const style = getComputedStyle(el);
                    if (style.webkitLineClamp !== '2' && style.lineClamp !== '2') return false;
                    // No clipping beyond the two-line clamp: the intrinsic content height fits.
                    return el.scrollHeight - el.clientHeight <= 1;
                }
                """);
            Assert.IsTrue(fits, $"label {i} must wrap its full text within the two-line clamp");
            var text = (await label.TextContentAsync())?.Trim();
            Assert.IsFalse(string.IsNullOrEmpty(text), $"label {i} carries its full text");
        }
    }

    [TestMethod]
    public async Task Demo_1440_AutoHidden_BodyContentSurvives_ForcedVariantsPaint()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/mobile-action-bar");
        await WaitForAppReadyAsync(page);

        // Auto visibility: above the mobile breakpoint the bar does not exist...
        Assert.AreEqual(0, await page.Locator("[data-testid='mab-auto-bar'] .tm-mobile-action-bar__bar").CountAsync(),
            "the Auto bar must be hidden at 1440");

        // X1 (BLOCKER): ...but the ChildContent belongs to the host at every layout — the demo
        // card keeps its document list above the breakpoint.
        await Assertions.Expect(page.Locator("[data-testid='mab-auto-bar'] .mab-docs")).ToBeVisibleAsync();

        // Forced mobile layout: the sticky and inline variants render; the viewport-anchored
        // variant is a deliberate opt-in the demo reveals on demand (two bars must not fight
        // for the bottom edge at once).
        var stickyRoot = page.Locator("[data-testid='mab-variant-sticky'] .tm-mobile-action-bar");
        var stickyBar = stickyRoot.Locator(".tm-mobile-action-bar__bar");
        var inlineRoot = page.Locator("[data-testid='mab-variant-inline'] .tm-mobile-action-bar");
        var inlineBar = inlineRoot.Locator(".tm-mobile-action-bar__bar");
        Assert.AreEqual(1, await stickyBar.CountAsync());
        Assert.AreEqual(1, await inlineBar.CountAsync());

        // X2 (a): with no ChildContent the ROOT is the sticky box (the bar is static inside),
        // so the bar sticks even in a bare container like the dashboard's.
        Assert.AreEqual("sticky", await stickyRoot.EvaluateAsync<string>("el => getComputedStyle(el).position"));
        Assert.AreEqual("static", await stickyBar.EvaluateAsync<string>("el => getComputedStyle(el).position"));
        Assert.AreEqual("static", await inlineBar.EvaluateAsync<string>("el => getComputedStyle(el).position"));

        await page.Locator("[data-testid='mab-toggle-fixed']").ClickAsync();
        var fixedRoot = page.Locator("[data-testid='mab-variant-fixed'] .tm-mobile-action-bar");
        var fixedBar = fixedRoot.Locator(".tm-mobile-action-bar__bar");
        await fixedBar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual("fixed", await fixedBar.EvaluateAsync<string>("el => getComputedStyle(el).position"));

        // The fixed bar pins to the viewport bottom.
        var fixedBox = await fixedBar.BoundingBoxAsync();
        var viewport = page.ViewportSize!;
        Assert.IsTrue(Math.Abs(fixedBox!.Y + fixedBox.Height - viewport.Height) < 2,
            "the FixedViewport bar sits on the viewport bottom edge");

        // X2 (b): only the FixedViewport variant reserves body height, equal to the bar's real
        // box (token = touch target + bar padding + border; the tile's min-height floors the
        // content at the touch target, so the equality is deterministic for one-line labels).
        var fixedPadding = await fixedRoot.Locator(".tm-mobile-action-bar__body")
            .EvaluateAsync<string>("el => getComputedStyle(el).paddingBottom");
        var fixedPaddingPx = double.Parse(fixedPadding.Replace("px", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        Assert.AreEqual(fixedBox.Height, fixedPaddingPx, delta: 1.5,
            "the FixedViewport body reserve equals the bar height");
    }

    [TestMethod]
    public async Task Demo_1440_ForcedVariant_MorePopover_PaintsSurface_AndReturnsFocus()
    {
        // X4/X5/X10: the bar resolves Mobile inside its container on a desktop viewport, so the
        // More menu opens as the ANCHORED POPOVER — it must paint a real surface and return
        // focus to the More trigger after an item runs.
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/mobile-action-bar");
        await WaitForAppReadyAsync(page);

        var more = page.Locator("[data-testid='mab-variant-sticky'] .tm-mobile-action-bar__more");
        await more.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // X10: the trigger advertises the menu like every other popup trigger.
        Assert.AreEqual("menu", await more.GetAttributeAsync("aria-haspopup"));
        Assert.AreEqual("false", await more.GetAttributeAsync("aria-expanded"));
        var controls = await more.GetAttributeAsync("aria-controls");
        Assert.IsFalse(string.IsNullOrEmpty(controls), "aria-controls points at the panel Id");

        // Keyboard: Enter opens the popover (native button activation — Enter fires on keydown).
        await more.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var panel = page.Locator(".tm-mobile-action-bar__menu-panel");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual("true", await more.GetAttributeAsync("aria-expanded"));
        Assert.AreEqual(controls, await panel.GetAttributeAsync("id"));

        // X4: a real surface — background, border and shadow, not the transparent reset.
        var background = await panel.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        var borderTopWidth = await panel.EvaluateAsync<string>("el => getComputedStyle(el).borderTopWidth");
        var boxShadow = await panel.EvaluateAsync<string>("el => getComputedStyle(el).boxShadow");
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", background, "the popover must not be transparent");
        Assert.AreEqual("1px", borderTopWidth, "the popover carries a 1px border");
        Assert.AreNotEqual("none", boxShadow, "the popover carries the popover shadow");

        // X5: choosing an item (KeepMenuOpen=false) closes the popover and restores focus to
        // the More trigger — never <body>.
        await page.Keyboard.PressAsync("Enter"); // activates the first menuitem (Duplicate)
        await Assertions.Expect(panel).ToBeHiddenAsync();
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.classList?.contains('tm-mobile-action-bar__more') ?? false",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task Dashboard_390_EditMode_BarSticksAtViewportBottom()
    {
        // X2 (c): the real sticky proof at 390x844 — the viewport keeps its real height while
        // the page scrolls. Never resize the viewport to the page height here.
        var context = await CreateTouchContextAsync(390, 844);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/dashboard");
        await WaitForAppReadyAsync(page);

        // Enter edit mode on the phone.
        var edit = page.Locator("[data-testid='dashboard-edit']");
        await edit.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await edit.ClickAsync();

        var root = page.Locator(".tm-dashboard .tm-mobile-action-bar");
        var bar = root.Locator(".tm-mobile-action-bar__bar");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual("sticky", await root.EvaluateAsync<string>("el => getComputedStyle(el).position"),
            "the bare dashboard root is the sticky box");

        // Mid-scroll: the container spans the viewport, so the bar is pinned to its bottom.
        await page.EvaluateAsync(
            """
            () => {
                const dash = document.querySelector('.tm-dashboard');
                const top = dash.getBoundingClientRect().top + window.scrollY;
                window.scrollTo(0, top + dash.offsetHeight / 2 - window.innerHeight / 2);
            }
            """);
        await page.WaitForTimeoutAsync(200);
        await AssertBarPinnedToViewportBottomAsync(page, bar);

        await CaptureViewportOnlyAsync(page, "390-dashboard-edit-mid-sticky");

        // Scroll to the very end: the bar settles at the container's bottom edge with no
        // double-reserved empty band below the content.
        await page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
        await page.WaitForTimeoutAsync(200);
        var settled = await page.EvaluateAsync<(double BarBottom, double DashBottom)>(
            """
            () => {
                const bar = document.querySelector('.tm-dashboard .tm-mobile-action-bar__bar');
                const dash = document.querySelector('.tm-dashboard');
                return {
                    BarBottom: bar.getBoundingClientRect().bottom,
                    DashBottom: dash.getBoundingClientRect().bottom,
                };
            }
            """);
        Assert.IsTrue(Math.Abs(settled.BarBottom - settled.DashBottom) <= 1.5,
            $"at scroll end the bar sits at the container bottom (bar {settled.BarBottom}, dashboard {settled.DashBottom})");
    }

    [TestMethod]
    public async Task Demo_390_AutoCard_BarSticksAtViewportBottom_WhileTheCardStraddlesTheFold()
    {
        // X2 (c): sticky proof for the demo's Auto card at a real (not page-height) viewport.
        // The card is shorter than the page: at 844px the fold cuts only ~1px of it, so use a
        // 700px-high viewport to give the pin a real holding range (SetViewportSize keeps the
        // touch emulation; only FullPage screenshots reset it).
        var context = await CreateTouchContextAsync(390, 844);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/mobile-action-bar");
        await WaitForAppReadyAsync(page);
        await page.SetViewportSizeAsync(390, 700);

        var bar = page.Locator("[data-testid='mab-auto-bar'] .tm-mobile-action-bar__bar");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Scroll so the fold cuts 40px into the card (card bottom 40px below the viewport
        // bottom — solidly inside the sticky range): the bar is pinned to the viewport bottom.
        var pinCut = await page.EvaluateAsync<double>(
            """
            () => {
                const card = document.querySelector("[data-testid='mab-auto-bar']");
                const top = card.getBoundingClientRect().top + window.scrollY;
                window.scrollTo(0, Math.max(0, top + card.offsetHeight - window.innerHeight - 40));
                return card.getBoundingClientRect().bottom - window.innerHeight;
            }
            """);
        Assert.IsTrue(pinCut > 5, $"the fold must cut the card (bottom offset {pinCut}px) for the pin to engage");
        await AssertBarPinnedToViewportBottomAsync(page, bar);

        await CaptureViewportOnlyAsync(page, "390-auto-card-mid-sticky");

        // Scroll until the whole card is in view: the sticky range is exhausted, the bar sits
        // in flow at the card's bottom edge (no pinning, no floating above the content).
        await page.EvaluateAsync(
            """
            () => {
                const card = document.querySelector("[data-testid='mab-auto-bar']");
                const top = card.getBoundingClientRect().top + window.scrollY;
                window.scrollTo(0, top - 60);
            }
            """);
        await page.WaitForTimeoutAsync(200);
        var settled = await page.EvaluateAsync<(double BarBottom, double CardBottom)>(
            """
            () => {
                const card = document.querySelector("[data-testid='mab-auto-bar']");
                const bar = document.querySelector("[data-testid='mab-auto-bar'] .tm-mobile-action-bar__bar");
                return {
                    BarBottom: bar.getBoundingClientRect().bottom,
                    CardBottom: card.getBoundingClientRect().bottom,
                };
            }
            """);
        Assert.IsTrue(Math.Abs(settled.BarBottom - settled.CardBottom) <= 1.5,
            $"with the card fully in view the bar sits at the card's bottom edge (bar {settled.BarBottom}, card {settled.CardBottom})");

        // Back into the pin range: the pin engages again.
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await page.WaitForTimeoutAsync(200);
        await AssertBarPinnedToViewportBottomAsync(page, bar);
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

        // The three edit actions live on the bar as tile buttons, and the wrapping toolbar
        // actions are gone.
        var labels = await bar.Locator(".tm-mobile-action-bar__label-text").AllTextContentsAsync();
        CollectionAssert.AreEqual(new List<string> { "Add Widget", "Save Changes", "Cancel" }, labels.ToList(),
            "dashboard edit actions render through the mobile action bar");
        Assert.AreEqual(0, await page.Locator(".tm-dashboard-toolbar-right .tm-btn:visible").CountAsync(),
            "the desktop toolbar actions must not render twice on mobile");

        await CaptureTouchViewportAsync(page, 390, "390-dashboard-edit");

        // The bar's actions are wired: Add Widget opens the widget selector.
        await bar.Locator(".tm-mobile-action-bar__action", new() { HasText = "Add Widget" }).ClickAsync();
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
