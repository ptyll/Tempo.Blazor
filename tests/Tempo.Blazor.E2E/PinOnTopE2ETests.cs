using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 4ce1b994 phase 2 (decision TOP-LAYER-PIN, docs/top-layer-pin.md): browser coverage of the
/// /pin-on-top demo. The pinned connection banner (TmPinnedOnTop, Priority=100) must paint above a
/// TmModal and a modal TmDrawer opened AFTER it (the 2.11 gap) and above a modal it was shown FROM.
/// Its Retry button must stay clickable under the modal's inert background, and it must sit above
/// a toast (priority 0). Under a native showModal() dialog it must NOT be promoted, and it must land
/// on top once the dialog closes. The geometry assertion is elementFromPoint at the banner's
/// centre: the top-layer paint order is exactly what hit-testing returns.
/// Screenshots go to __screenshots__/pin-on-top/ at 1440 and 390, in light and dark.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class PinOnTopE2ETests : WasmTestBase
{
    private const int LongTimeout = 20000;

    private static readonly string ShotDir = Path.Combine(
        TestContextHelper.RepositoryRoot, "tests", "Tempo.Blazor.E2E", "__screenshots__", "pin-on-top");

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, Timeout = 60_000 });
        var info = new FileInfo(path);
        Assert.IsTrue(info.Length > 2 * 1024, $"screenshot {name}.png must be a real capture, got {info.Length} bytes");
    }

    private async Task<IPage> OpenPageAsync(int width = 1440, int height = 900, bool dark = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/pin-on-top");
        await page.Locator("[data-testid='pin-toggle-banner']").WaitForAsync(new LocatorWaitForOptions { Timeout = LongTimeout });
        if (dark)
            await ToggleDarkModeAsync(page);
        return page;
    }

    /// <summary>
    /// The hit-test at the banner's centre as ONE JS function source: "banner", or the topmost other
    /// surface. <see cref="TopAtBannerAsync"/> evaluates it once; <see cref="WaitTopAsync"/> re-runs
    /// the very same source every frame as its predicate, so the diagnostic and the wait can never drift.
    /// </summary>
    private const string TopAtBannerScript =
        """
        () => {
            const banner = document.querySelector('[data-testid="pin-banner"]');
            if (!banner) return 'missing';
            const r = banner.getBoundingClientRect();
            if (r.width === 0 || r.height === 0) return 'hidden';
            const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
            if (!hit) return 'none';
            if (banner.contains(hit)) return 'banner';
            return hit.closest('dialog') ? 'native-dialog'
                 : hit.closest('.tm-modal-overlay, .tm-drawer') ? 'modal'
                 : hit.closest('.tm-toast-container') ? 'toast'
                 : 'other:' + (hit.className || hit.tagName);
        }
        """;

    private static Task<string> TopAtBannerAsync(IPage page) => page.EvaluateAsync<string>(TopAtBannerScript);

    private static async Task WaitTopAsync(IPage page, string expected)
    {
        // T2: one state wait instead of a sampled 100 ms polling loop — the predicate (the same
        // JS as TopAtBannerAsync) re-runs in the page on every frame until it returns `expected`,
        // or the 4 s budget expires. A timeout fails with the expectation AND what the hit-test
        // last returned, instead of a bare TimeoutException.
        try
        {
            await page.WaitForFunctionAsync(
                "expected => (" + TopAtBannerScript + ")() === expected",
                expected,
                new PageWaitForFunctionOptions { Timeout = 4000 });
        }
        catch (TimeoutException)
        {
            Assert.Fail($"expected '{expected}', observed '{await TopAtBannerAsync(page)}'");
        }
    }

    private static Task<bool> IsPopoverOpenAsync(IPage page) => page.EvaluateAsync<bool>(
        "() => { const b = document.querySelector('[data-testid=\"pin-banner\"]'); return !!b && b.matches(':popover-open'); }");

    private static async Task ShowBannerAsync(IPage page)
    {
        await page.Locator("[data-testid='pin-toggle-banner']").ClickAsync();
        await page.Locator("[data-testid='pin-banner']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
    }

    [TestMethod]
    [Description("PIN-E2E.1: the banner stays above a TmModal opened AFTER it, and its Retry stays clickable (not inert)")]
    public async Task Banner_StaysAboveAModalOpenedAfterIt_AndIsNotInert()
    {
        var page = await OpenPageAsync();
        await ShowBannerAsync(page);
        await WaitTopAsync(page, "banner");
        Assert.IsTrue(await IsPopoverOpenAsync(page), "a visible pin rides the top layer");

        await page.Locator("[data-testid='pin-open-modal']").ClickAsync();
        await page.Locator(".tm-modal-overlay").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
        await WaitTopAsync(page, "banner");

        var inert = await page.EvaluateAsync<bool>("() => !!document.querySelector('[data-testid=\"pin-banner\"]').closest('[inert]')");
        Assert.IsFalse(inert, "a pinned element is exempt from the modal trap's inert background");
        await page.Locator("[data-testid='pin-banner-retry']").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='pin-retry-count']")).ToContainTextAsync("1×");
        await Assertions.Expect(page.Locator(".tm-modal-overlay")).ToBeVisibleAsync(new() { Timeout = 5000 });

        await ShotAsync(page, "banner-over-modal-1440-light");
    }

    [TestMethod]
    [Description("PIN-E2E.2: the banner shown FROM inside an open modal lands above it")]
    public async Task Banner_ShownFromInsideAnOpenModal_LandsAboveIt()
    {
        var page = await OpenPageAsync();
        await page.Locator("[data-testid='pin-open-modal']").ClickAsync();
        await page.Locator("[data-testid='pin-modal-toggle-banner']").ClickAsync();
        await WaitTopAsync(page, "banner");
        await ShotAsync(page, "banner-from-modal-1440-light");
    }

    [TestMethod]
    [Description("PIN-E2E.3: the banner stays above a modal drawer opened after it")]
    public async Task Banner_StaysAboveADrawerOpenedAfterIt()
    {
        var page = await OpenPageAsync();
        await ShowBannerAsync(page);
        await page.Locator("[data-testid='pin-open-drawer']").ClickAsync();
        await page.Locator(".tm-drawer").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
        await WaitTopAsync(page, "banner");
        await ShotAsync(page, "banner-over-drawer-1440-light");
    }

    [TestMethod]
    [Description("PIN-E2E.4: priority 100 paints above a toast (priority 0) pushed after the banner, and both sit above the modal")]
    public async Task Banner_PaintsAboveAToastPushedAfterIt()
    {
        var page = await OpenPageAsync();
        await ShowBannerAsync(page);
        await page.Locator("[data-testid='pin-open-modal']").ClickAsync();
        await page.Locator("[data-testid='pin-modal-push-toast']").ClickAsync();
        await page.Locator(".tm-toast").First.WaitForAsync(new LocatorWaitForOptions { Timeout = LongTimeout });

        // The banner and the toast sit at different edges, so their mutual order is pinned by the
        // node tests (tm-top-layer.test.mjs, raise within its band). Here: all three ride the top layer.
        var open = await page.EvaluateAsync<string>(
            """
            () => [document.querySelector('[data-testid="pin-banner"]'),
                   document.querySelector('.tm-toast-container'),
                   document.querySelector('.tm-modal-overlay')]
                  .map(e => e && e.matches(':popover-open') ? '1' : '0').join('')
            """);
        Assert.AreEqual("111", open, "banner, toast container and modal are all in the top layer");
        await WaitTopAsync(page, "banner");
        // The toast stays reachable above the modal too. The hit-test reads PAINT order, which
        // settles a beat AFTER the toast element mounts (the registry promotes the new toast over
        // the already-promoted modal on its next raise), so it is ONE state wait — the toast-centre
        // hit-test re-evaluated in the page every frame — instead of a sampled polling loop.
        try
        {
            await page.WaitForFunctionAsync(
                """
                () => {
                    const card = document.querySelector('.tm-toast');
                    if (!card) return false;
                    const r = card.getBoundingClientRect();
                    const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                    return !!hit && card.contains(hit);
                }
                """,
                null,
                new PageWaitForFunctionOptions { Timeout = 4000 });
        }
        catch (TimeoutException)
        {
            Assert.Fail("the toast pushed from inside the modal paints above the modal");
        }
        await ShotAsync(page, "banner-and-toast-over-modal-1440-light");
    }

    [TestMethod]
    [Description("PIN-E2E.9: a toast pushed BEFORE a modal opens stays above the modal (the shared registry re-raises it)")]
    public async Task ToastPushedBeforeTheModal_StaysAboveIt()
    {
        var page = await OpenPageAsync();
        await page.Locator("[data-testid='pin-push-toast']").ClickAsync();
        var card = page.Locator(".tm-toast").First;
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = LongTimeout });
        await page.Locator("[data-testid='pin-open-modal']").ClickAsync();
        await page.Locator(".tm-modal-overlay").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });

        var toastTop = false;
        for (var i = 0; i < 40 && !toastTop; i++)
        {
            toastTop = await page.EvaluateAsync<bool>(
                """
                () => {
                    const card = document.querySelector('.tm-toast');
                    if (!card) return false;
                    const r = card.getBoundingClientRect();
                    const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                    return !!hit && card.contains(hit);
                }
                """);
            if (!toastTop) await page.WaitForTimeoutAsync(100);
        }
        Assert.IsTrue(toastTop, "the toast already on screen must be re-raised above the modal promoted after it");
        await ShotAsync(page, "toast-before-modal-1440-light");
    }

    [TestMethod]
    [Description("PIN-E2E.5: under a native showModal() dialog the banner is NOT promoted; it lands on top after close")]
    public async Task NativeModalDialog_DefersThePin_UntilItCloses()
    {
        var page = await OpenPageAsync();
        await page.Locator("[data-testid='pin-open-native-dialog']").ClickAsync();
        await page.Locator("dialog[data-testid='pin-native-dialog-element']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
        await page.Locator("[data-testid='pin-banner']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = LongTimeout });
        await page.WaitForFunctionAsync("() => !document.querySelector('[data-testid=\"pin-banner\"]').hasAttribute('hidden')");
        // Negative assertion: give the observer its frame, then check nothing was promoted.
        await page.WaitForTimeoutAsync(300);
        Assert.IsFalse(await IsPopoverOpenAsync(page), "no pin is promoted above an open native modal dialog");
        await ShotAsync(page, "banner-under-native-dialog-1440-light");

        await page.Locator("[data-testid='pin-native-dialog-close']").ClickAsync();
        await WaitTopAsync(page, "banner");
        Assert.IsTrue(await IsPopoverOpenAsync(page), "the close re-sync promotes the pin");
        await ShotAsync(page, "banner-after-native-dialog-1440-light");
    }

    [TestMethod]
    [Description("PIN-E2E.6: hiding the banner demotes it and hands back the popover attribute (edge case)")]
    public async Task HidingTheBanner_DemotesIt_AndRemovesThePopoverAttribute()
    {
        var page = await OpenPageAsync();
        await ShowBannerAsync(page);
        await WaitTopAsync(page, "banner");
        await page.Locator("[data-testid='pin-banner-dismiss']").ClickAsync();
        await page.WaitForFunctionAsync("() => { const b = document.querySelector('[data-testid=\"pin-banner\"]'); return b.hasAttribute('hidden') && !b.hasAttribute('popover'); }");
        Assert.IsFalse(await IsPopoverOpenAsync(page));
        // Show again from inside a modal: the same element is promoted again.
        await page.Locator("[data-testid='pin-open-modal']").ClickAsync();
        await page.Locator("[data-testid='pin-modal-toggle-banner']").ClickAsync();
        await WaitTopAsync(page, "banner");
    }

    [TestMethod]
    [Description("PIN-E2E.7: the bar keeps its bottom-bar geometry (the popover UA defaults are reset)")]
    public async Task Banner_KeepsBottomBarGeometry()
    {
        var page = await OpenPageAsync();
        await ShowBannerAsync(page);
        await WaitTopAsync(page, "banner");
        var box = await page.EvaluateAsync<double[]>(
            "() => { const r = document.querySelector('[data-testid=\"pin-banner\"]').getBoundingClientRect(); return [r.left, r.right, r.bottom, innerWidth, innerHeight]; }");
        Assert.AreEqual(0, box[0], 1, "left edge flush with the viewport (UA margin:auto would centre it)");
        Assert.AreEqual(box[3], box[1], 1, "spans the viewport width (UA width:fit-content would shrink it)");
        Assert.AreEqual(box[4], box[2], 1, "sits on the bottom edge (UA inset:0 would pin top too)");
    }

    [TestMethod]
    [Description("PIN-E2E.10: bare [data-tm-pin-on-top] markup (the attribute front door, no host CSS) is pinned and keeps a bottom-bar box because the UA popover defaults are reset")]
    public async Task AttributeFrontDoor_BareMarkup_IsPinned_AndTheUaPopoverDefaultsAreReset()
    {
        // The demo banner's own scoped CSS and Tailwind's preflight override most UA [popover]
        // defaults, so they would hide a missing reset (mutation B stayed green against them). This
        // probe has ONLY inline position/offsets: whatever else it looks like is the reset's doing.
        var page = await OpenPageAsync();
        await page.EvaluateAsync(
            """
            () => {
                const bar = document.createElement('div');
                bar.id = 'pin-probe';
                bar.setAttribute('data-tm-pin-on-top', '');
                bar.style.cssText = 'position:fixed;left:0;right:0;bottom:0;font:16px sans-serif';
                bar.textContent = 'probe';
                document.body.appendChild(bar);
            }
            """);
        await page.WaitForFunctionAsync("() => document.getElementById('pin-probe').matches(':popover-open')",
            null, new PageWaitForFunctionOptions { Timeout = LongTimeout });

        var box = await page.EvaluateAsync<string>(
            """
            () => {
                const el = document.getElementById('pin-probe');
                const r = el.getBoundingClientRect();
                const cs = getComputedStyle(el);
                return [Math.round(r.left), Math.round(r.right - innerWidth), Math.round(r.bottom - innerHeight),
                        r.height < innerHeight / 2, cs.backgroundColor, cs.marginTop].join('|');
            }
            """);
        // left 0 | right flush | bottom flush | not stretched to full height | transparent (not Canvas) | no auto margin
        Assert.AreEqual("0|0|0|true|rgba(0, 0, 0, 0)|0px", box,
            "without the reset the UA rules (inset:0, margin:auto, fit-content, Canvas background) turn the bar into a centred white box");
    }

    [TestMethod]
    [Description("PIN-E2E.8: screenshots — banner over modal at 390 light/dark and 1440 dark")]
    public async Task Screenshots_PhoneAndDark()
    {
        foreach (var (w, h, dark, name) in new[] { (390, 844, false, "390-light"), (390, 844, true, "390-dark"), (1440, 900, true, "1440-dark") })
        {
            var page = await OpenPageAsync(w, h, dark);
            await ShowBannerAsync(page);
            await page.Locator("[data-testid='pin-open-modal']").ClickAsync();
            await page.Locator(".tm-modal-overlay").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = LongTimeout });
            await WaitTopAsync(page, "banner");
            await ShotAsync(page, $"banner-over-modal-{name}");
        }
    }
}
