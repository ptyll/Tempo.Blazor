using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Feedback;

/// <summary>
/// Activate-after-promote (F3 follow-up, c51ddf81 #1): opening any of the promoted overlay roots
/// must leave the initial focus INSIDE the overlay — never on <c>&lt;body&gt;</c> — on every
/// repeated open, and even when the promote module is delayed at the network layer while a
/// consumer stylesheet strips the root's own author <c>display</c> (the UA
/// <c>[popover]:not(:popover-open) { display: none }</c> then hides the unpromoted root, and a
/// trap that activates before its promotion focuses nothing).
/// </summary>
/// <remarks>
/// The gantt import dialog row is the historical GREEN control: it activated after its promotion
/// before the shared gate existed.
/// </remarks>
[TestClass]
[TestCategory("WASM")]
public sealed class OverlayInitialFocusE2ETests : WasmTestBase
{
    // name, route, opener, promoted root, expected initial focus
    // The opener must be a plain CSS selector: it is reused for `document.activeElement.matches`
    // after each Escape (the close path must return focus to the opener).
    private const string Palette = "palette,/layout,[data-testid='open-command-palette'],.tm-command-palette-backdrop,.tm-command-palette-input";
    private const string Shortcuts = "shortcuts,/layout,[data-testid='shortcuts-described-open'],.tm-keyboard-shortcuts-overlay,.tm-keyboard-shortcuts-close";
    private const string Lightbox = "lightbox,/gallery,.tm-gallery-item,.tm-lightbox,.tm-lightbox-close";
    private const string Modal = "modal,/modal-dialog,[data-testid='open-basic-modal'],.tm-modal-overlay,.tm-modal-close";
    private const string Dialog = "dialog,/modal-dialog,[data-testid='open-long-dialog'],.tm-modal-overlay,.tm-dialog-btn-cancel";
    private const string Drawer = "drawer,/feedback,[data-testid='open-right-drawer'],.tm-drawer--right,.tm-drawer__close";
    private const string GanttControl = "gantt-control,/gantt,button[data-testid='gantt-import-btn'],.tm-modal-overlay.tm-gantt__import-dialog,.tm-modal-close";

    private async Task<IBrowserContext> NewContextAsync(int width, int height, bool touch)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = touch,
            IsMobile = touch,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = "en-US",
        });
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        RegisterContext(context);
        return context;
    }

    private static string[] Spec(string row)
    {
        var parts = row.Split(',');
        // Guard: a comma-bearing opener selector is rejoined.
        return parts.Length == 5 ? parts : [parts[0], parts[1], string.Join(',', parts[2..^2]), parts[^2], parts[^1]];
    }

    // ── Natural repeated opens: the focus always lands inside, never on <body> ──────────

    [TestMethod]
    [DataRow(Palette, 1440, 900, false, DisplayName = "palette 1440")]
    [DataRow(Palette, 390, 844, true, DisplayName = "palette 390 touch")]
    [DataRow(Shortcuts, 1440, 900, false, DisplayName = "shortcuts 1440")]
    [DataRow(Shortcuts, 390, 844, true, DisplayName = "shortcuts 390 touch")]
    [DataRow(Lightbox, 1440, 900, false, DisplayName = "lightbox 1440")]
    [DataRow(Lightbox, 390, 844, true, DisplayName = "lightbox 390 touch")]
    [DataRow(Modal, 1440, 900, false, DisplayName = "modal 1440")]
    [DataRow(Modal, 390, 844, true, DisplayName = "modal 390 touch")]
    [DataRow(Dialog, 1440, 900, false, DisplayName = "dialog 1440")]
    [DataRow(Dialog, 390, 844, true, DisplayName = "dialog 390 touch")]
    [DataRow(Drawer, 1440, 900, false, DisplayName = "drawer 1440")]
    [DataRow(Drawer, 390, 844, true, DisplayName = "drawer 390 touch")]
    [DataRow(GanttControl, 1440, 900, false, DisplayName = "gantt (control) 1440")]
    [DataRow(GanttControl, 390, 844, true, DisplayName = "gantt (control) 390 touch")]
    public async Task InitialFocus_LandsInside_OnRepeatedOpens(
        string spec, int width, int height, bool touch)
    {
        var parts = Spec(spec);
        var name = parts[0];
        var page = await (await NewContextAsync(width, height, touch)).NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{parts[1]}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);

        // Focus-lost counter: a focusout with no relatedTarget whose NEXT microtask still finds
        // document.activeElement on <body>. Installed once per page. It observes what the old
        // AreNotEqual("body") could not: a focus that landed on <body> and moved on again before
        // the assertion ran.
        await page.EvaluateAsync(
            """
            () => {
                if (window.__tmBodyFocusDrops === undefined) {
                    window.__tmBodyFocusDrops = 0;
                    document.addEventListener('focusout', (e) => {
                        if (e.relatedTarget) return;
                        queueMicrotask(() => {
                            if (document.activeElement === document.body) window.__tmBodyFocusDrops++;
                        });
                    });
                }
            }
            """);

        for (var open = 1; open <= 3; open++)
        {
            await page.EvaluateAsync("() => { window.__tmBodyFocusDrops = 0; }");
            await page.Locator(parts[2]).First.ClickAsync();
            await page.Locator(parts[3]).First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

            // Poll until the focus landed on the expected element (the trap's initial move rides
            // the lazy focus-trap module import) — never a fixed sleep.
            await page.WaitForFunctionAsync(
                $"() => {{ const a = document.activeElement; return !!a && a.matches(\"{parts[4]}\"); }}",
                null,
                new PageWaitForFunctionOptions { Timeout = 10_000 });

            var bodyDrops = await page.EvaluateAsync<int>("() => window.__tmBodyFocusDrops");
            Assert.AreEqual(0, bodyDrops,
                $"[{name}] open #{open} at {width}px: focus must land inside the overlay without dropping through <body>");

            await page.Keyboard.PressAsync("Escape");
            await page.Locator(parts[3]).First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });

            // Every close path must return focus to the opener (APG), not only close the overlay.
            await page.WaitForFunctionAsync(
                $"() => {{ const a = document.activeElement; return !!a && a.matches(\"{parts[2]}\"); }}",
                null,
                new PageWaitForFunctionOptions { Timeout = 5000 });
        }
    }

    // ── A slow promote with no author display on the root: the gate still holds the trap back ──

    [TestMethod]
    [DataRow(Palette, DisplayName = "palette (slow promote, no own display)")]
    [DataRow(Shortcuts, DisplayName = "shortcuts (slow promote, no own display)")]
    [DataRow(Lightbox, DisplayName = "lightbox (slow promote, no own display)")]
    [DataRow(Modal, DisplayName = "modal (slow promote, no own display)")]
    [DataRow(Dialog, DisplayName = "dialog (slow promote, no own display)")]
    [DataRow(Drawer, DisplayName = "drawer (slow promote, no own display)")]
    [DataRow(GanttControl, DisplayName = "gantt control (slow promote, no own display)")]
    public async Task InitialFocus_SurvivesASlowPromote_WhenTheRootHasNoOwnDisplay(string spec)
    {
        var parts = Spec(spec);
        // A fresh context per run: the promote module has not been imported yet, so its only
        // request is the click-time one the route below can catch and delay.
        var context = await NewContextAsync(1440, 900, touch: false);
        // Hold the promote module at the network layer so the promotion lands ~800 ms after the
        // root rendered. MUST carry wildcards: Blazor serves it as tm-top-layer.<fingerprint>.js,
        // so a literal path silently matches nothing. Only requests after the flag below are
        // delayed — a boot-time import must not stall navigation — and the counter proves the
        // click-time load was actually intercepted (otherwise the delay is vacuous).
        var delayedHits = 0;
        var openerClicked = false;
        await context.RouteAsync("**/tm-top-layer*.js*", async r =>
        {
            if (!openerClicked)
            {
                await r.FallbackAsync();
                return;
            }
            Interlocked.Increment(ref delayedHits);
            await Task.Delay(800);
            await r.FallbackAsync();
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{parts[1]}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);

        // Strip the author display every root relies on today: an unpromoted popover root is then
        // display:none, so an early trap activation would drop its initial-focus move entirely.
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = "[popover]:not(:popover-open){display:none !important}"
        });

        openerClicked = true;
        await page.Locator(parts[2]).First.ClickAsync();
        await page.WaitForFunctionAsync(
            $"() => {{ const a = document.activeElement; return !!a && a.matches(\"{parts[4]}\"); }}",
            null,
            new PageWaitForFunctionOptions { Timeout = 15_000 });

        Assert.IsTrue(Volatile.Read(ref delayedHits) > 0,
            "promote module was not delayed — test is vacuous");

        var active = await page.EvaluateAsync<string>("() => document.activeElement ? document.activeElement.tagName.toLowerCase() : '(null)'");
        Assert.AreNotEqual("body", active,
            $"[{parts[0]}] even with the promote delayed and the root display:none, the initial focus must land inside the overlay");
    }
}
