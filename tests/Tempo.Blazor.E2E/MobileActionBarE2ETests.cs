using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// F5 lane: the mobile action bar on the real components — the /mobile-action-bar demo page
/// and the TmDashboard edit mode — at 390 and 320 with touch emulation, plus 1440 to prove
/// the hidden/desktop behaviour. Every screenshot is viewport-only at the real viewport
/// height (FullPage resets touch emulation; a viewport stretched to the page height pins
/// every sticky element and hides sticky failures — F5 review round 2, Y13): the sticky-bar
/// helper scrolls the bar into its pinning range before the capture, the sheet/dialog shots
/// rely on the surfaces being viewport-anchored.
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

    /// <summary>
    /// Y13: label shots are VIEWPORT-CLIPPED at the real viewport height. The sticky bar is
    /// scrolled into its pinning range first (container bottom at the viewport bottom), so the
    /// capture shows the bar exactly where a user sees it — never a page-height stretch.
    /// </summary>
    private async Task<string> CaptureStickyBarViewportAsync(IPage page, ILocator bar, string name)
    {
        await bar.EvaluateAsync(
            """
            el => {
                const box = el.closest('.tm-mobile-action-bar');
                const top = box.getBoundingClientRect().top + window.scrollY;
                window.scrollTo(0, Math.max(0, top + box.offsetHeight - window.innerHeight));
            }
            """);
        return await CaptureViewportOnlyAsync(page, name);
    }

    /// <summary>
    /// Y6: no label line may be an orphan narrower than 12px (the fr-320 "Partage|r" defect) —
    /// measured per rendered line box through a Range, not from the token.
    /// </summary>
    private static async Task AssertNoOrphanLabelLinesAsync(ILocator bar)
    {
        var labels = bar.Locator(".tm-mobile-action-bar__label-text");
        var count = await labels.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var noOrphan = await labels.Nth(i).EvaluateAsync<bool>(
                """
                el => {
                    const range = document.createRange();
                    range.selectNodeContents(el);
                    const lines = [...range.getClientRects()]
                        .filter(r => r.width > 0 && r.height > 0);
                    return lines.length > 0 && lines.every(r => r.width >= 12);
                }
                """);
            var text = (await labels.Nth(i).TextContentAsync())?.Trim();
            Assert.IsTrue(noOrphan, $"label \"{text}\" must not leave a mid-word orphan line under 12px");
        }
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

        await CaptureStickyBarViewportAsync(page, bar, "390-bar");

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
        // Duplicate, Focus title, Archive and Delete overflow (the Y8 focus-move action included).
        Assert.AreEqual(4, await menuItems.CountAsync(), "Duplicate, Focus title, Archive and Delete overflow");

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

        await CaptureViewportOnlyAsync(page, "390-more-sheet");

        // The destructive item opens a confirm dialog: it must paint ABOVE the sheet.
        // probe4: elementFromPoint skips inert elements, so strip inert (and restore it
        // synchronously) to make the paint-order assertion honest.
        await menuItems.Nth(3).ClickAsync(); // Delete (index 3: Duplicate, Focus title, Archive, Delete)
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

        await CaptureViewportOnlyAsync(page, "390-confirm-above-sheet");

        // X13: confirming Delete closes the sheet (the host calls CloseMoreAsync) and focus
        // returns to the More trigger.
        await page.Locator(".tm-dialog .tm-dialog-btn-ok").ClickAsync();
        await Assertions.Expect(dialogPanel).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".tm-overlay-panel-sheet")).ToBeHiddenAsync();
        // Y1: the confirmed close flows through the controlled IsOpenChanged — the trigger must
        // not keep announcing an expanded menu.
        Assert.AreEqual("false", await moreButton.GetAttributeAsync("aria-expanded"),
            "aria-expanded resets after the confirmed CloseMoreAsync flow");
        // The demo closes the More sheet from the confirmed Delete result.
        Assert.AreEqual(0, await page.Locator(".tm-overlay-panel-sheet:visible").CountAsync(),
            "the demo closes the More sheet from the confirmed Delete result");
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.classList?.contains('tm-mobile-action-bar__more') ?? false",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });

        await CaptureStickyBarViewportAsync(page, bar, "390-after-confirm-ok");
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

        await CaptureStickyBarViewportAsync(page, bar, "320-bar-cs");
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
        // Y6: no mid-word orphan lines (the fr "Partage|r" defect) at the narrowest width.
        await AssertNoOrphanLabelLinesAsync(bar);

        var actions = bar.Locator(".tm-mobile-action-bar__action");
        var count = await actions.CountAsync();
        Assert.IsTrue(count >= 3, "the bar renders its tile actions");
        for (var i = 0; i < count; i++)
        {
            var box = await actions.Nth(i).BoundingBoxAsync();
            Assert.IsTrue(box!.Height >= 44, $"action {i} must keep the 44px touch target, was {box.Height}");
        }

        await CaptureStickyBarViewportAsync(page, bar, $"320-bar-{culture}");
    }

    [TestMethod]
    public async Task Demo_320_FrenchFixedViewport_ReserveEqualsBarHeight_WhenLabelsWrap()
    {
        // Y4: the reserve token derives from the two-line tile floor, so at 320 fr — where every
        // label wraps — the FixedViewport body reserve still equals the REAL bar box.
        var context = await CreateTouchContextAsync(320, 740);
        await context.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'fr');
            document.cookie = 'tm-demo-culture=fr; path=/';
            """);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/mobile-action-bar");
        await WaitForAppReadyAsync(page);

        await page.Locator("[data-testid='mab-toggle-fixed']").ClickAsync();
        var fixedRoot = page.Locator("[data-testid='mab-variant-fixed'] .tm-mobile-action-bar");
        var fixedBar = fixedRoot.Locator(".tm-mobile-action-bar__bar");
        await fixedBar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // fr 320 wraps the demo labels onto two lines — the reserve must still match.
        await AssertLabelsFitTwoLinesAsync(fixedBar);
        var barBox = await fixedBar.BoundingBoxAsync();
        var reserve = await fixedRoot.Locator(".tm-mobile-action-bar__body")
            .EvaluateAsync<string>("el => getComputedStyle(el).paddingBottom");
        var reservePx = double.Parse(reserve.Replace("px", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
        Assert.AreEqual(barBox!.Height, reservePx, delta: 1.5,
            $"the FixedViewport reserve ({reservePx}) must equal the real bar box ({barBox.Height}) even with wrapped labels");

        await CaptureViewportOnlyAsync(page, "320-fixed-fr-reserve");
    }

    [TestMethod]
    public async Task Demo_390_MoreSheet_AriaExpanded_FalseAfterEveryClosePath()
    {
        // Y1 (R2-M1): the More trigger's aria-expanded must return to "false" on EVERY sheet close
        // path — Escape, Done, backdrop and item select each leave it "true" before the fix (axe:
        // aria-valid-attr-value, because aria-controls then points at an absent id).
        var context = await CreateTouchContextAsync(390, 844);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/mobile-action-bar");
        await WaitForAppReadyAsync(page);

        var bar = page.Locator("[data-testid='mab-auto-bar'] .tm-mobile-action-bar__bar");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var more = bar.Locator(".tm-mobile-action-bar__more");
        var sheet = page.Locator(".tm-overlay-panel-sheet");

        async Task OpenAndAssertExpandedAsync()
        {
            await more.FocusAsync();
            await more.ClickAsync();
            await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            Assert.AreEqual("true", await more.GetAttributeAsync("aria-expanded"));
        }

        // Escape.
        await OpenAndAssertExpandedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(sheet).ToBeHiddenAsync();
        Assert.AreEqual("false", await more.GetAttributeAsync("aria-expanded"), "after sheet Escape");

        // Sheet Done.
        await OpenAndAssertExpandedAsync();
        await sheet.Locator(".tm-overlay-panel-sheet__done").ClickAsync();
        await Assertions.Expect(sheet).ToBeHiddenAsync();
        Assert.AreEqual("false", await more.GetAttributeAsync("aria-expanded"), "after sheet Done");

        // Backdrop.
        await OpenAndAssertExpandedAsync();
        await page.Locator("[data-tm-backdrop]").ClickAsync();
        await Assertions.Expect(sheet).ToBeHiddenAsync();
        Assert.AreEqual("false", await more.GetAttributeAsync("aria-expanded"), "after the backdrop click");

        // Item select.
        await OpenAndAssertExpandedAsync();
        await sheet.Locator("[role='menuitem']", new() { HasText = "Duplicate" }).ClickAsync();
        await Assertions.Expect(sheet).ToBeHiddenAsync();
        Assert.AreEqual("false", await more.GetAttributeAsync("aria-expanded"), "after an item select");
        await Assertions.Expect(page.Locator("[data-testid='mab-last-action']")).ToContainTextAsync("Duplicate");
    }

    [TestMethod]
    public async Task Demo_390_MoreSheet_DoesNotReopenAfterViewportFlip()
    {
        // Z1 (round 3, MAJOR): with the sheet open, widening past the breakpoint hides the Auto
        // bar — the controlled panel is disposed WITHOUT an IsOpenChanged, so a stale open
        // state made the sheet REOPEN by itself on the way back (phone rotation portrait →
        // landscape → portrait), and the best-effort focus move onto the disposed menu element
        // threw an unhandled JSException ("Unable to focus an invalid element") in about half
        // the runs — #blazor-error-ui, a dead Server circuit in Blazor Server.
        var context = await CreateTouchContextAsync(390, 844);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        var pageErrors = new List<string>();
        page.PageError += (_, e) => pageErrors.Add(e);
        await page.GotoAsync($"{BaseUrl}/mobile-action-bar");
        await WaitForAppReadyAsync(page);

        var bar = page.Locator("[data-testid='mab-auto-bar'] .tm-mobile-action-bar__bar");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var more = bar.Locator(".tm-mobile-action-bar__more");
        var sheet = page.Locator(".tm-overlay-panel-sheet");

        await more.FocusAsync();
        await more.ClickAsync();
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual("true", await more.GetAttributeAsync("aria-expanded"),
            "the sheet is open before the flip");

        // Landscape wider than the mobile breakpoint: the Auto bar does not render at all.
        await page.SetViewportSizeAsync(1100, 844);
        await Assertions.Expect(bar).ToHaveCountAsync(0);
        await Assertions.Expect(sheet).ToBeHiddenAsync();

        // ...and back to portrait: the bar renders again — the sheet must stay closed.
        await page.SetViewportSizeAsync(390, 844);
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForTimeoutAsync(1500); // settle: a stale reopen or the focus JSException lands here

        Assert.AreEqual("false", await more.GetAttributeAsync("aria-expanded"),
            "the sheet must not reopen by itself after the bar hid while it was open");
        await Assertions.Expect(sheet).ToBeHiddenAsync();
        Assert.AreEqual(0, await page.Locator(".tm-overlay-panel-sheet:visible").CountAsync(),
            "no sheet visible after the flip back");
        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        pageErrors.Should().BeEmpty();

        await CaptureViewportOnlyAsync(page, "390-after-flip-no-reopen");
    }

    [TestMethod]
    public async Task Demo_390_MenuActionThatMovesFocus_KeepsItThere()
    {
        // Y8: the menu closes and its focus restore lands BEFORE the action runs — an action that
        // moves focus elsewhere must win, not race a late restore back to the More trigger.
        var context = await CreateTouchContextAsync(390, 844);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/mobile-action-bar");
        await WaitForAppReadyAsync(page);

        var bar = page.Locator("[data-testid='mab-auto-bar'] .tm-mobile-action-bar__bar");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var more = bar.Locator(".tm-mobile-action-bar__more");
        await more.FocusAsync();
        await more.ClickAsync();

        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await sheet.Locator("[role='menuitem']", new() { HasText = "Focus title" }).ClickAsync();

        await Assertions.Expect(sheet).ToBeHiddenAsync();
        await page.WaitForFunctionAsync(
            "() => document.activeElement === document.querySelector('.mab-header h1')",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });
        Assert.AreEqual("false", await more.GetAttributeAsync("aria-expanded"),
            "the focus-moving action still closes the menu first");
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
        // the More trigger — never <body>. Wait for the initial-focus move to land first: a
        // premature Enter would re-activate the still-focused More trigger instead of the
        // menuitem and the popover would stay open.
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.getAttribute('role') === 'menuitem'",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });
        await page.Keyboard.PressAsync("Enter"); // activates the first menuitem (Duplicate)
        await Assertions.Expect(panel).ToBeHiddenAsync();
        Assert.AreEqual("false", await more.GetAttributeAsync("aria-expanded"),
            "aria-expanded resets after a popover item select (Y1)");
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.classList?.contains('tm-mobile-action-bar__more') ?? false",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });

        // Y1: the same for the keyboard dismissal — Escape closes the popover and the trigger
        // stops announcing an expanded menu.
        await page.Keyboard.PressAsync("Enter"); // reopens the popover
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual("true", await more.GetAttributeAsync("aria-expanded"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(panel).ToBeHiddenAsync();
        Assert.AreEqual("false", await more.GetAttributeAsync("aria-expanded"),
            "aria-expanded resets after popover Escape (Y1)");
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
        // The tiles inherit the host webfont (font: inherit) — a late font swap shifts the card
        // layout between the scroll and the pin measurement. Settle the fonts first so the
        // sticky assertion measures a stable box.
        await page.WaitForFunctionAsync(
            "() => document.fonts.status === 'loaded'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

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
        await page.WaitForTimeoutAsync(200); // settle the pin before measuring
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

        // The three edit actions live on the bar as tile buttons — the save action carries the
        // SHORT mobile label (Y11), the desktop toolbar keeps the full wording. The wrapping
        // toolbar actions are gone.
        var labels = await bar.Locator(".tm-mobile-action-bar__label-text").AllTextContentsAsync();
        CollectionAssert.AreEqual(new List<string> { "Add Widget", "Save", "Cancel" }, labels.ToList(),
            "dashboard edit actions render through the mobile action bar");
        Assert.AreEqual(0, await page.Locator(".tm-dashboard-toolbar-right .tm-btn:visible").CountAsync(),
            "the desktop toolbar actions must not render twice on mobile");

        await CaptureStickyBarViewportAsync(page, bar, "390-dashboard-edit");

        // The bar's actions are wired: Add Widget opens the widget selector.
        await bar.Locator(".tm-mobile-action-bar__action", new() { HasText = "Add Widget" }).ClickAsync();
        await page.Locator(".tm-widget-selector-overlay").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    [TestMethod]
    public async Task Dashboard_320_EditMode_FrenchLabels_NotClamped_NoOrphans()
    {
        // Y11 (review round 2, m2): fr 320 clamped "Enregistrer les…" to a single-line ellipsis.
        // The bar tile now carries the short label; the two-line floor and break-word
        // hyphenation must render every label whole — no clamp, no mid-word orphan line.
        var context = await CreateTouchContextAsync(320, 740);
        await context.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'fr');
            document.cookie = 'tm-demo-culture=fr; path=/';
            """);
        var page = await context.NewPageAsync();
        RegisterContext(context);
        await page.GotoAsync($"{BaseUrl}/dashboard");
        await WaitForAppReadyAsync(page);

        await page.Locator("[data-testid='dashboard-edit']").ClickAsync();
        var bar = page.Locator(".tm-dashboard .tm-mobile-action-bar__bar");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var labels = bar.Locator(".tm-mobile-action-bar__label-text");
        var texts = (await labels.AllTextContentsAsync()).Select(t => t.Trim()).ToList();
        CollectionAssert.AreEqual(new List<string> { "Ajouter un widget", "Enregistrer", "Annuler" }, texts,
            "the fr edit actions render whole through the bar");

        await AssertLabelsFitTwoLinesAsync(bar);
        await AssertNoOrphanLabelLinesAsync(bar);

        await CaptureStickyBarViewportAsync(page, bar, "320-dashboard-edit-fr");
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
