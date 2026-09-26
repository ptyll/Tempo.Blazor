using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E coverage for the Phase 18.2 TmOverlayPanel primitive on the /overlay demo page:
/// top-layer popover semantics, flip/shift inside the viewport, scroll tracking, escape from an
/// overflow container, dismissal gestures, and one Firefox leg (the Popover API ships in
/// Firefox ≥125, so the same top-layer behavior must hold there).
/// </summary>
[TestClass]
public class OverlayPanelE2ETests : WasmTestBase
{
    private const string PageUrl = "https://localhost:7106/overlay";

    private async Task<IPage> OpenOverlayPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(PageUrl);
        await WaitForAppReadyAsync(page);
        return page;
    }

    [TestMethod]
    [TestCategory("Smoke")]
    public async Task Overlay_OpensAsPopover_InTopLayer_BelowTrigger()
    {
        var page = await OpenOverlayPageAsync();

        await page.GetByTestId("overlay-open-bottom").ClickAsync();
        var panel = page.GetByTestId("overlay-panel-bottom");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // The panel is a live top-layer popover and reports its resolved side.
        Assert.IsTrue(await panel.EvaluateAsync<bool>("el => el.matches(':popover-open')"));
        Assert.AreEqual("bottom", await panel.GetAttributeAsync("data-tm-placement"));

        // Anchored directly under the trigger.
        var anchorBox = await page.GetByTestId("overlay-open-bottom").BoundingBoxAsync();
        var panelBox = await panel.BoundingBoxAsync();
        Assert.IsNotNull(anchorBox);
        Assert.IsNotNull(panelBox);
        Assert.IsTrue(panelBox!.Y >= anchorBox!.Y + anchorBox.Height,
            $"panel top {panelBox.Y} should sit at/below anchor bottom {anchorBox.Y + anchorBox.Height}");
    }

    [TestMethod]
    public async Task Overlay_EscapesOverflowContainer()
    {
        var page = await OpenOverlayPageAsync();

        await page.GetByTestId("overlay-open-scroll").ClickAsync();
        var panel = page.GetByTestId("overlay-panel-scroll");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var boxBounds = await page.GetByTestId("overlay-scroll-box").BoundingBoxAsync();
        var panelBox = await panel.BoundingBoxAsync();
        Assert.IsNotNull(boxBounds);
        Assert.IsNotNull(panelBox);

        // The panel is placed to the RIGHT of the trigger — i.e. outside the scroll box's right
        // edge. A clipped descendant could never paint there.
        Assert.IsTrue(panelBox!.X + panelBox.Width > boxBounds!.X + boxBounds.Width - 1,
            $"panel {panelBox.X}..{panelBox.X + panelBox.Width} should escape the scroll box right edge {boxBounds.X + boxBounds.Width}");
    }

    [TestMethod]
    public async Task Overlay_TracksAnchorOnInnerScroll()
    {
        var page = await OpenOverlayPageAsync();

        await page.GetByTestId("overlay-open-scroll").ClickAsync();
        var panel = page.GetByTestId("overlay-panel-scroll");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var before = await panel.BoundingBoxAsync();

        await page.GetByTestId("overlay-scroll-box").EvaluateAsync("el => el.scrollTop += 60");
        // Placement runs on requestAnimationFrame — give it two frames.
        await page.WaitForTimeoutAsync(150);

        var after = await panel.BoundingBoxAsync();
        Assert.IsNotNull(before);
        Assert.IsNotNull(after);
        Assert.IsTrue(after!.Y < before!.Y - 30,
            $"panel should track the anchor upward on inner scroll (before {before.Y}, after {after.Y})");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    public async Task Overlay_FlipsAbove_WhenNoRoomBelow()
    {
        var page = await OpenOverlayPageAsync();

        var trigger = page.GetByTestId("overlay-open-edge");
        // Pin the trigger's bottom edge to the viewport bottom: zero room below, so a bottom
        // placement must flip to top. scroll-behavior:smooth animates the jump, so wait for the
        // scroll to actually finish instead of a fixed timeout.
        await trigger.EvaluateAsync("el => el.scrollIntoView({ block: 'end' })");
        // scroll-behavior:smooth animates the jump; wait until the trigger's bottom edge actually
        // lands on the viewport bottom (±2px) before clicking — otherwise the click races the
        // animation and the panel is placed against a mid-scroll rect.
        await page.WaitForFunctionAsync(
            """
            () => {
                const el = document.querySelector('[data-testid="overlay-open-edge"]');
                return el && Math.abs(el.getBoundingClientRect().bottom - window.innerHeight) <= 2;
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 10_000 });

        await trigger.ClickAsync();
        var panel = page.GetByTestId("overlay-panel-edge");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual("top", await panel.GetAttributeAsync("data-tm-placement"));

        var anchorBox = await trigger.BoundingBoxAsync();
        var panelBox = await panel.BoundingBoxAsync();
        Assert.IsNotNull(anchorBox);
        Assert.IsNotNull(panelBox);
        Assert.IsTrue(panelBox!.Y + panelBox.Height <= anchorBox!.Y + 1,
            $"flipped panel should end at/above anchor top (panel bottom {panelBox.Y + panelBox.Height}, anchor top {anchorBox.Y})");
        Assert.IsTrue(panelBox.X >= 0 && panelBox.X + panelBox.Width <= 1281,
            "panel should stay inside the viewport horizontally");
    }

    [TestMethod]
    public async Task Overlay_Escape_Closes()
    {
        var page = await OpenOverlayPageAsync();

        await page.GetByTestId("overlay-open-bottom").ClickAsync();
        var panel = page.GetByTestId("overlay-panel-bottom");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await page.Keyboard.PressAsync("Escape");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    [TestMethod]
    public async Task Overlay_OutsidePointerDown_Closes()
    {
        var page = await OpenOverlayPageAsync();

        await page.GetByTestId("overlay-open-bottom").ClickAsync();
        var panel = page.GetByTestId("overlay-panel-bottom");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Click the page heading — outside panel and trigger.
        await page.Locator("h1").First.ClickAsync();
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    [TestMethod]
    public async Task Overlay_InsideModal_PaintsAboveModal()
    {
        var page = await OpenOverlayPageAsync();

        await page.GetByTestId("overlay-open-modal").ClickAsync();
        var modalTrigger = page.GetByTestId("overlay-open-in-modal");
        await modalTrigger.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await modalTrigger.ClickAsync();

        var panel = page.GetByTestId("overlay-panel-modal");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // The pixel at the panel's centre must belong to the panel — not to the modal overlay or
        // the page behind it. elementFromPoint answers exactly that. The rect is measured inside
        // the page so a non-finite box (a panel that never got placed) reports what it saw instead
        // of surfacing as a marshalling error.
        var hit = await page.EvaluateAsync<string>(
            """
            () => {
                const panel = document.querySelector('[data-testid="overlay-panel-modal"]');
                if (!panel) return 'no-panel';
                const r = panel.getBoundingClientRect();
                if (!Number.isFinite(r.left) || !Number.isFinite(r.top) || r.width <= 0 || r.height <= 0)
                    return `bad-rect:${r.left},${r.top} ${r.width}x${r.height}`;
                const el = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                return el && panel.contains(el)
                    ? 'hit'
                    : `miss:${el ? el.tagName + '.' + String(el.className) : 'null'}`;
            }
            """);
        Assert.AreEqual("hit", hit,
            "elementFromPoint over the panel should hit the panel itself (top layer above modal)");
    }

    [TestMethod]
    public async Task Overlay_DatePicker_InModal_FlipsAbove_AndStaysInsideViewport()
    {
        // The original defect (18.2): a date field at the end of a scrolling modal body opened its
        // calendar below the fold — clipped by the modal's overflow and mispositioned against the
        // transformed .tm-modal box.
        var page = await OpenOverlayPageAsync();

        await page.GetByTestId("overlay-open-modal").ClickAsync();
        var modalBody = page.Locator(".tm-modal-body");
        await modalBody.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Scroll the field to the bottom of the modal body, then open the calendar.
        var trigger = page.Locator("[data-testid='overlay-datepicker'] .tm-date-picker-trigger");
        await trigger.EvaluateAsync("el => el.scrollIntoView({ block: 'end' })");
        await page.WaitForTimeoutAsync(150);
        await trigger.ClickAsync();

        var panel = page.Locator(".tm-date-picker-popup");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual("top", await panel.GetAttributeAsync("data-tm-placement"));

        var triggerBox = await trigger.BoundingBoxAsync();
        var panelBox = await panel.BoundingBoxAsync();
        Assert.IsNotNull(triggerBox);
        Assert.IsNotNull(panelBox);
        // Entirely inside the viewport.
        Assert.IsTrue(panelBox!.X >= 0 && panelBox.Y >= 0
            && panelBox.X + panelBox.Width <= 1281 && panelBox.Y + panelBox.Height <= 721,
            $"flipped calendar must paint fully inside the viewport, got {panelBox.X},{panelBox.Y} {panelBox.Width}x{panelBox.Height}");
        // Above the trigger.
        Assert.IsTrue(panelBox.Y + panelBox.Height <= triggerBox!.Y + 1,
            $"flipped calendar bottom {panelBox.Y + panelBox.Height} should sit at/above trigger top {triggerBox.Y}");
    }

    [TestMethod]
    public async Task Overlay_DatePicker_InModal_TracksTriggerOnModalScroll()
    {
        var page = await OpenOverlayPageAsync();

        await page.GetByTestId("overlay-open-modal").ClickAsync();
        var modalBody = page.Locator(".tm-modal-body");
        await modalBody.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var trigger = page.Locator("[data-testid='overlay-datepicker'] .tm-date-picker-trigger");
        await trigger.EvaluateAsync("el => el.scrollIntoView({ block: 'end' })");
        await page.WaitForTimeoutAsync(150);
        await trigger.ClickAsync();

        var panel = page.Locator(".tm-date-picker-popup");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Scroll the modal body back up a bit — the trigger moves with it and the panel must
        // follow on the next animation frame (the defect let the panel float off the anchor).
        await modalBody.EvaluateAsync("el => el.scrollTop -= 80");
        await page.WaitForTimeoutAsync(150);

        var triggerBox = await trigger.BoundingBoxAsync();
        var panelBox = await panel.BoundingBoxAsync();
        Assert.IsNotNull(triggerBox);
        Assert.IsNotNull(panelBox);
        Assert.IsTrue(Math.Abs(panelBox!.Y + panelBox.Height - triggerBox!.Y) <= 8,
            $"panel should keep hugging the trigger after modal scroll (panel bottom {panelBox.Y + panelBox.Height}, trigger top {triggerBox.Y})");
    }

    /// <summary>
    /// B1: an outside click that lands on a focusable field must keep the focus it just earned —
    /// the closing panel only reclaims focus for clicks on dead space. The regression pulled
    /// focus back to the dropdown's trigger on every dismissal.
    /// </summary>
    [TestMethod]
    public async Task Overlay_OutsideClick_IntoFocusableField_KeepsFocusOnField()
    {
        var page = await OpenOverlayPageAsync();

        var trigger = page.Locator("[data-testid='overlay-focus-multiselect'] .tm-multiselect");
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.ClickAsync();

        var panel = page.Locator("[data-testid='overlay-focus-multiselect'] .tm-overlay-panel");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Click into the plain input next to the dropdown — outside both panel and anchor.
        var field = page.GetByTestId("overlay-outside-input");
        await field.ClickAsync();

        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        // Focus must sit on the field, not back on the multiselect trigger.
        var activeTestId = await page.EvaluateAsync<string>(
            "() => document.activeElement ? document.activeElement.getAttribute('data-testid') : 'none'");
        Assert.AreEqual("overlay-outside-input", activeTestId,
            "the clicked field must own focus after the outside dismissal");
    }

    /// <summary>
    /// N1: one Escape gesture closes exactly one layer. The datepicker panel shuts on keydown;
    /// the host TmModal closes on keyup — overlay.js must swallow that keyup, or the same key
    /// would close the modal a beat later. A second Escape still closes the modal.
    /// </summary>
    [TestMethod]
    [TestCategory("Smoke")]
    public async Task Overlay_Escape_InModal_ClosesPanelButNotModal()
    {
        var page = await OpenOverlayPageAsync();

        await page.GetByTestId("overlay-open-modal").ClickAsync();
        var modalBody = page.Locator(".tm-modal-body");
        await modalBody.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var trigger = page.Locator("[data-testid='overlay-datepicker'] .tm-date-picker-trigger");
        await trigger.ClickAsync();

        var panel = page.Locator(".tm-date-picker-popup");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // One gesture: the panel goes, the modal stays.
        await page.Keyboard.PressAsync("Escape");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        Assert.IsTrue(await page.Locator(".tm-modal").CountAsync() == 1,
            "the modal must survive the Escape that closed its datepicker panel");

        // The suppressor is one-shot: the next Escape is a fresh gesture and still closes the modal.
        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    /// <summary>
    /// N3: once the anchor leaves the viewport entirely, the panel parks hidden instead of
    /// clamping to an edge; it reappears when the anchor is scrolled back in.
    /// </summary>
    [TestMethod]
    public async Task Overlay_Hides_WhenAnchorScrollsFullyOutOfViewport()
    {
        var page = await OpenOverlayPageAsync();

        var trigger = page.GetByTestId("overlay-open-edge");
        await trigger.ScrollIntoViewIfNeededAsync();
        var initialScrollY = await page.EvaluateAsync<double>("() => window.scrollY");

        await trigger.ClickAsync();
        var panel = page.GetByTestId("overlay-panel-edge");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // The page carries 120vh of scroll room below the trigger — scroll to the bottom so the
        // anchor leaves the viewport through the top edge.
        await page.EvaluateAsync("() => window.scrollTo({ top: document.documentElement.scrollHeight, behavior: 'instant' })");
        await page.WaitForTimeoutAsync(200); // placement runs on requestAnimationFrame

        var visibility = await panel.EvaluateAsync<string>(
            "el => getComputedStyle(el).visibility");
        Assert.AreEqual("hidden", visibility,
            "panel must hide while its anchor is fully outside the viewport");

        // And it must come back when the anchor returns.
        await page.EvaluateAsync($"() => window.scrollTo({{ top: {initialScrollY}, behavior: 'instant' }})");
        await page.WaitForTimeoutAsync(200);
        visibility = await panel.EvaluateAsync<string>(
            "el => getComputedStyle(el).visibility");
        Assert.AreEqual("visible", visibility,
            "panel must reappear once its anchor is back inside the viewport");
    }

    /// <summary>
    /// N163: the Escape-keyup suppressor exists so the keyup of a panel-closing Escape cannot
    /// reach keyup-driven hosts (TmModal). It disarms on that keyup or on window blur — but a
    /// keyup the browser swallows natively must not leave it armed forever, or it would eat the
    /// NEXT, unrelated Escape. After the 1s timeout a fresh Escape still closes the modal on the
    /// first press.
    /// </summary>
    [TestMethod]
    public async Task Overlay_EscapeKeyupSuppressor_TimesOut_AndNextEscapeReachesModal()
    {
        var page = await OpenOverlayPageAsync();

        await page.GetByTestId("overlay-open-modal").ClickAsync();
        var modal = page.Locator(".tm-modal");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var trigger = page.Locator("[data-testid='overlay-datepicker'] .tm-date-picker-trigger");
        await trigger.ClickAsync();
        var panel = page.Locator(".tm-date-picker-popup");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Swallowed keyup: DownAsync dispatches ONLY the keydown — the panel closes and the
        // suppressor arms, but no keyup follows to disarm it (the fullscreen/PiP scenario).
        await page.Keyboard.DownAsync("Escape");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        Assert.AreEqual(1, await modal.CountAsync(),
            "the modal must survive the Escape keydown that closed its panel");

        // Past the 1000ms suppressor timeout the armed listener is gone, so the NEXT Escape's
        // keyup reaches the modal on the first real press — a still-armed suppressor would eat it
        // and leave the modal open.
        await page.WaitForTimeoutAsync(1300);
        await page.Keyboard.PressAsync("Escape");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    /// <summary>
    /// N165: with the Popover API removed before any page script runs, overlay.js takes the
    /// fallback path — position:fixed + containing-block math instead of the browser top layer.
    /// The panel must still carry the same anchor geometry, be marked with
    /// data-tm-overlay-fallback, and dismiss on Escape.
    /// </summary>
    [TestMethod]
    public async Task Overlay_WithoutPopoverApi_FallsBackToFixed_AndDismissesOnEscape()
    {
        var page = await OpenOverlayPageWithoutPopoverApiAsync();

        await page.GetByTestId("overlay-open-bottom").ClickAsync();
        var panel = page.GetByTestId("overlay-panel-bottom");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Fallback markers: no top layer, fixed positioning instead.
        Assert.AreEqual("true", await panel.GetAttributeAsync("data-tm-overlay-fallback"));
        Assert.IsFalse(await panel.EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "without showPopover the panel must not be a live popover");
        Assert.AreEqual("bottom", await panel.GetAttributeAsync("data-tm-placement"));

        var anchorBox = await page.GetByTestId("overlay-open-bottom").BoundingBoxAsync();
        var panelBox = await panel.BoundingBoxAsync();
        Assert.IsNotNull(anchorBox);
        Assert.IsNotNull(panelBox);
        Assert.IsTrue(panelBox!.Y >= anchorBox!.Y + anchorBox.Height,
            $"fallback panel top {panelBox.Y} should sit at/below anchor bottom {anchorBox.Y + anchorBox.Height}");

        await page.Keyboard.PressAsync("Escape");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    /// <summary>
    /// N165: the fallback's containing-block walk. An open .tm-modal carries transform: scale(1),
    /// which makes it the containing block for fixed descendants — overlay.js must measure
    /// against its padding box instead of the viewport. The panel still has to land under its
    /// trigger (in viewport coordinates) and paint above the modal chrome.
    /// </summary>
    [TestMethod]
    public async Task Overlay_WithoutPopoverApi_InsideModal_PositionsAgainstContainingBlock()
    {
        var page = await OpenOverlayPageWithoutPopoverApiAsync();

        await page.GetByTestId("overlay-open-modal").ClickAsync();
        var modalTrigger = page.GetByTestId("overlay-open-in-modal");
        await modalTrigger.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await modalTrigger.ClickAsync();

        var panel = page.GetByTestId("overlay-panel-modal");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual("true", await panel.GetAttributeAsync("data-tm-overlay-fallback"));
        Assert.AreEqual("bottom", await panel.GetAttributeAsync("data-tm-placement"));

        // Geometry is viewport-space truth: the containing-block math must land the panel
        // directly under the trigger even though its inline left/top are relative to the
        // transformed .tm-modal box.
        var triggerBox = await modalTrigger.BoundingBoxAsync();
        var panelBox = await panel.BoundingBoxAsync();
        Assert.IsNotNull(triggerBox);
        Assert.IsNotNull(panelBox);
        Assert.IsTrue(panelBox!.Y >= triggerBox!.Y + triggerBox.Height,
            $"fallback panel top {panelBox.Y} should sit at/below trigger bottom {triggerBox.Y + triggerBox.Height}");

        // No top layer here: the panel still has to paint above the modal chrome it overlaps.
        var hit = await page.EvaluateAsync<string>(
            """
            () => {
                const panel = document.querySelector('[data-testid="overlay-panel-modal"]');
                if (!panel) return 'no-panel';
                const r = panel.getBoundingClientRect();
                if (!Number.isFinite(r.left) || !Number.isFinite(r.top) || r.width <= 0 || r.height <= 0)
                    return `bad-rect:${r.left},${r.top} ${r.width}x${r.height}`;
                const el = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                return el && panel.contains(el)
                    ? 'hit'
                    : `miss:${el ? el.tagName + '.' + String(el.className) : 'null'}`;
            }
            """);
        Assert.AreEqual("hit", hit,
            "fallback panel must paint above the modal chrome it overlaps");

        // Escape dismissal is identical in fallback mode — and its keyup suppressor still shields
        // the host modal.
        await page.Keyboard.PressAsync("Escape");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        Assert.AreEqual(1, await page.Locator(".tm-modal").CountAsync(),
            "the modal must survive the Escape that closed its fallback panel");
    }

    private async Task<IPage> OpenOverlayPageWithoutPopoverApiAsync()
    {
        var context = await CreateContextAsync();
        // Before ANY page script: overlay.js reads HTMLElement.prototype.showPopover once, at
        // module evaluation, to choose between the top layer and the fixed+fallback path (N165).
        await context.AddInitScriptAsync("delete HTMLElement.prototype.showPopover;");
        var page = await context.NewPageAsync();
        await page.GotoAsync(PageUrl);
        await WaitForAppReadyAsync(page);
        return page;
    }

    /// <summary>
    /// Firefox leg: the Popover API is supported since Firefox 125, so the identical top-layer
    /// contract must hold there. PlaywrightTestBase runs Chromium only, so this test owns a
    /// dedicated Playwright + Firefox pair.
    /// </summary>
    [TestMethod]
    public async Task Overlay_Firefox_OpensAsPopover_AndDismisses()
    {
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !TestContext.Properties.Contains("Headless") || TestContext.Properties["Headless"]?.ToString() != "false",
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
            IgnoreHTTPSErrors = true,
        });
        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(PageUrl);
            await WaitForAppReadyAsync(page);

            await page.GetByTestId("overlay-open-bottom").ClickAsync();
            var panel = page.GetByTestId("overlay-panel-bottom");
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            Assert.IsTrue(await panel.EvaluateAsync<bool>("el => el.matches(':popover-open')"));
            Assert.AreEqual("bottom", await panel.GetAttributeAsync("data-tm-placement"));

            await page.Keyboard.PressAsync("Escape");
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        }
        finally
        {
            await context.CloseAsync();
        }
    }
}
