using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// FZE-4 A10 ([3f1747c0]): the top-layer promotion contract on the other two render modes.
/// <see cref="OverlayRound3FixE2ETests"/> proves it on WASM only; the same scenarios run against
/// the Blazor Server demo (7107) and the InteractiveAuto demo (7108): a palette opened from a
/// promoted drawer paints above it, a toast already on screen re-raises above a drawer opened
/// after it, and (Server) the restored popover roots after a circuit reconnect stay promoted —
/// (IA) both auto legs record their render mode and promote overlays. No trusted dev certificate
/// is needed: every context sets IgnoreHTTPSErrors.
/// </summary>
internal static class OverlayTopLayerScenarios
{
    internal const int ViewportWidth = 1440;
    internal const int ViewportHeight = 900;
    /// <summary>
    /// The probe4 paint assertion: what PAINTS at the hit element's point is the top surface. Inert
    /// is stripped synchronously around the hit-test and restored after (the focus trap inerts
    /// everything behind the top overlay, and elementFromPoint would skip it).
    /// </summary>
    internal static async Task<bool> PaintsAtAsync(IPage page, string hitSelector, string topSelector)
    {
        return await page.EvaluateAsync<bool>(
            """
            ({ hit, top }) => {
                const hitEl = document.querySelector(hit);
                const topEl = document.querySelector(top);
                if (!hitEl || !topEl) return false;
                const wasInert = [...document.querySelectorAll('[inert]')];
                wasInert.forEach(e => e.removeAttribute('inert'));
                const r = hitEl.getBoundingClientRect();
                const el = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                wasInert.forEach(e => e.setAttribute('inert', ''));
                return !!el && topEl.contains(el);
            }
            """,
            new { hit = hitSelector, top = topSelector });
    }

    /// <summary>
    /// Deterministic replacement for the fixed promotion sleeps: the root carries
    /// <c>popover="manual"</c> the moment Blazor renders it, and overlay promotion turns on
    /// <c>:popover-open</c> — so this wait is exactly "the surface is in the top layer".
    /// </summary>
    private static Task WaitForPromotedAsync(IPage page, string selector) =>
        page.WaitForFunctionAsync(
            "selector => { const el = document.querySelector(selector); return !!el && el.matches(':popover-open'); }",
            selector,
            new PageWaitForFunctionOptions { Timeout = 15000 });

    internal static async Task CommandPaletteOpenedFromDrawerPaintsAboveTheDrawer(IPage page)
    {
        await page.Locator("[data-testid='open-right-drawer']").ClickAsync();
        var drawer = page.Locator(".tm-drawer--right");
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await WaitForPromotedAsync(page, ".tm-drawer--right");

        await page.Locator("[data-testid='drawer-open-palette']").ClickAsync();
        var palette = page.Locator(".tm-command-palette-backdrop");
        await palette.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await WaitForPromotedAsync(page, ".tm-command-palette-backdrop");

        Assert.IsTrue(await palette.EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "the palette backdrop must be a live top-layer popover (V2)");
        Assert.IsTrue(await PaintsAtAsync(page, ".tm-command-palette", ".tm-command-palette-backdrop"),
            "the palette opened from inside the drawer must paint above it");

        // Escape peels the palette first; the drawer survives, and focus returns to the palette's
        // trigger inside the drawer.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(palette).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 5000 });
        Assert.AreEqual(1, await page.Locator(".tm-drawer--right").CountAsync(),
            "the drawer survives the palette's Escape");
        await Assertions.Expect(page.Locator("[data-testid='drawer-open-palette']")).ToBeFocusedAsync();
    }

    internal static async Task ToastAlreadyVisibleThenDrawerOpensToastPaintsOnTop(IPage page)
    {
        // Push a long toast, THEN open the modal surface — the order that regressed in R3-M1.
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Long (10s)" }).ClickAsync();
        var toast = page.Locator(".tm-toast-container:popover-open .tm-toast").First;
        await toast.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await page.Locator("[data-testid='open-right-drawer']").ClickAsync();
        var drawer = page.Locator(".tm-drawer--right");
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await WaitForPromotedAsync(page, ".tm-drawer--right");

        // The drawer's promotion must have re-raised the pinned toast container above itself.
        Assert.IsTrue(await PaintsAtAsync(page, ".tm-toast-container:popover-open .tm-toast", ".tm-toast-container:popover-open"),
            "a toast already on screen must paint above the drawer opened after it (R3-M1)");
    }

    internal static async Task CircuitReconnectRestoredPopoverRootsStayPromotedAndANewSurfacePaintsOnTop(IPage page)
    {
        // The demo _Host.cshtml defines no custom reconnect UI, so blazor.web.js drives its default.
        // .NET 9/10 default UI: a host <div id="components-reconnect-modal"> carrying a SHADOW ROOT
        // whose .components-reconnect-overlay gets .components-reconnect-visible while rejoining and
        // loses it again after a successful rejoin (host.style.display flips block/none). The legacy
        // .components-reconnect-show/-hide classes on the modal belong to the page-defined UI path —
        // both shapes are handled below so the scenario survives a framework upgrade either way.
        await page.Locator("[data-testid='open-right-drawer']").ClickAsync();
        var drawer = page.Locator(".tm-drawer--right");
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await WaitForPromotedAsync(page, ".tm-drawer--right");

        // Cut the network: the circuit drops and the reconnect UI shows. 60s budget: the browser
        // learns about the severed WebSocket through the SignalR keepalive/timeout machinery, which
        // on a loaded machine (full verification shard) can take well over the default 30s action
        // budget — the wait itself stays a state poll, only its ceiling grows.
        await page.Context.SetOfflineAsync(true);
        await page.WaitForFunctionAsync(ReconnectUiStateJs("shown"), null, new PageWaitForFunctionOptions { Timeout = 60000 });

        // Restore the network and wait for the circuit to come back (fast rejoin attempts).
        await page.Context.SetOfflineAsync(false);
        await page.WaitForFunctionAsync(ReconnectUiStateJs("hidden"), null, new PageWaitForFunctionOptions { Timeout = 60000 });

        // The resumed circuit kept the DOM — and with it the browser's top-layer slot.
        Assert.IsTrue(await drawer.EvaluateAsync<bool>("el => el.matches(':popover-open')"),
            "after a circuit reconnect the drawer root must still be a live top-layer popover");

        // A surface opened after the reconnect promotes above the restored root.
        await page.Locator("[data-testid='drawer-open-palette']").ClickAsync();
        var palette = page.Locator(".tm-command-palette-backdrop");
        await palette.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await WaitForPromotedAsync(page, ".tm-command-palette-backdrop");
        Assert.IsTrue(await PaintsAtAsync(page, ".tm-command-palette", ".tm-command-palette-backdrop"),
            "a palette opened after the reconnect must paint above the restored drawer");
    }

    /// <summary>
    /// The reconnect-UI state probe: 'shown' resolves once the default UI is displaying, 'hidden'
    /// once it went back to its hidden state after a successful rejoin. A <c>failed</c> state
    /// resolves neither and both waits time out — which is the honest failure.
    /// </summary>
    private static string ReconnectUiStateJs(string expectedState) =>
        $$"""
        () => {
            const modal = document.getElementById('components-reconnect-modal');
            if (!modal) return '{{expectedState}}' === 'hidden';   // default UI removed entirely = hidden
            if (modal.classList.contains('components-reconnect-failed')) return false;
            // Legacy page-defined UI shape: show/hide classes on the modal itself.
            if (modal.classList.contains('components-reconnect-show')) return 'shown' === '{{expectedState}}';
            if (modal.classList.contains('components-reconnect-hide')) return 'hidden' === '{{expectedState}}';
            // .NET 9/10 default UI shape: a shadow root with the overlay that toggles -visible.
            const overlay = modal.shadowRoot && modal.shadowRoot.querySelector('.components-reconnect-overlay');
            if (overlay) {
                return ('hidden' === '{{expectedState}}') !== overlay.classList.contains('components-reconnect-visible');
            }
            return ('hidden' === '{{expectedState}}') === (modal.style.display === 'none');
        }
        """;
}

/// <summary>
/// The top-layer scenarios on the Blazor Server demo — the promotion work must not depend on the
/// WASM host: the same Blazor JS runs over a circuit, and a reconnect must not lose the promoted
/// roots.
/// </summary>
[TestClass]
public sealed class ServerOverlayTopLayerE2ETests : ServerTestBase
{
    private async Task<IPage> OpenFeedbackAsync()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = OverlayTopLayerScenarios.ViewportWidth, Height = OverlayTopLayerScenarios.ViewportHeight },
            IgnoreHTTPSErrors = true,
            Locale = "en-US",
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/feedback", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    [TestMethod]
    public async Task CommandPalette_OpenedFromDrawer_PaintsAboveTheDrawer()
    {
        var page = await OpenFeedbackAsync();
        await OverlayTopLayerScenarios.CommandPaletteOpenedFromDrawerPaintsAboveTheDrawer(page);
    }

    [TestMethod]
    public async Task ToastAlreadyVisible_ThenDrawerOpens_ToastPaintsOnTop()
    {
        var page = await OpenFeedbackAsync();
        await OverlayTopLayerScenarios.ToastAlreadyVisibleThenDrawerOpensToastPaintsOnTop(page);
    }

    [TestMethod]
    public async Task CircuitReconnect_RestoredPopoverRoots_StayPromoted_AndANewSurfacePaintsOnTop()
    {
        var page = await OpenFeedbackAsync();
        await OverlayTopLayerScenarios.CircuitReconnectRestoredPopoverRootsStayPromotedAndANewSurfacePaintsOnTop(page);
    }
}

/// <summary>
/// The top-layer scenarios on the InteractiveAuto demo. The first visit is claimed by the Server
/// renderer (no persisted blazor-resource-hash yet); a reload usually runs on WebAssembly — so the
/// promotion contract is exercised on both auto legs across the class.
/// </summary>
[TestClass]
public sealed class InteractiveAutoOverlayTopLayerE2ETests : InteractiveAutoTestBase
{
    private async Task<IPage> OpenFeedbackAsync()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = OverlayTopLayerScenarios.ViewportWidth, Height = OverlayTopLayerScenarios.ViewportHeight },
            IgnoreHTTPSErrors = true,
            Locale = "en-US",
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/feedback", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    /// <summary>The render mode the shared layout stamped from RendererInfo.Name — 'none' while only prerender markup exists.</summary>
    private static async Task<string> ReadRenderModeAsync(IPage page) =>
        await page.EvaluateAsync<string>(
            """
            () => new Promise(resolve => {
                const started = Date.now();
                const tick = () => {
                    const v = document.querySelector('[data-render-mode]')?.getAttribute('data-render-mode') ?? 'none';
                    if (v !== 'none' || Date.now() - started > 30000) { resolve(v); return; }
                    setTimeout(tick, 250);
                };
                tick();
            })
            """);

    [TestMethod]
    public async Task CommandPalette_OpenedFromDrawer_PaintsAboveTheDrawer()
    {
        var page = await OpenFeedbackAsync();
        await OverlayTopLayerScenarios.CommandPaletteOpenedFromDrawerPaintsAboveTheDrawer(page);
    }

    [TestMethod]
    public async Task ToastAlreadyVisible_ThenDrawerOpens_ToastPaintsOnTop()
    {
        var page = await OpenFeedbackAsync();
        await OverlayTopLayerScenarios.ToastAlreadyVisibleThenDrawerOpensToastPaintsOnTop(page);
    }

    [TestMethod]
    public async Task RenderModeIsRecorded_AndOverlayPromotes()
    {
        // Leg 1: the first load (usually Server-claimed through the fail-quickly latch).
        var page = await OpenFeedbackAsync();
        var firstLeg = await ReadRenderModeAsync(page);
        TestContext.WriteLine($"data-render-mode (first load): {firstLeg}");
        Assert.IsTrue(firstLeg is "Server" or "WebAssembly",
            $"the first leg must record a real interactive renderer, got '{firstLeg}'");

        // Leg 2 must be deterministic, not "usually": the same proof as
        // InteractiveAutoTests.WasmBoot_RendersOnWebAssemblyRenderer. blazor.web.js never migrates
        // a component once the Server renderer has claimed it — on the FIRST visit it latches
        // onWebAssemblyFailedToLoadQuickly (no persisted blazor-resource-hash), so Server claims
        // every auto component; only a fully completed WASM boot persists
        // `blazor-resource-hash:<assembly>` to localStorage. Wait for that persisted hash (5 min
        // budget) before reloading, then poll [data-render-mode] until WebAssembly (3 min budget).
        var booted = await page.EvaluateAsync<bool>("""
            () => new Promise(resolve => {
                const started = Date.now();
                const tick = () => {
                    const v = window.localStorage.getItem('blazor-resource-hash:Tempo.Blazor.Demo.InteractiveAuto.Client');
                    if (v || Date.now() - started > 300000) { resolve(!!v); return; }
                    setTimeout(tick, 500);
                };
                tick();
            })
            """);
        Assert.IsTrue(booted, "WASM boot never completed: blazor-resource-hash was not persisted within 5 min.");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        var mode = await page.EvaluateAsync<string>("""
            () => new Promise(resolve => {
                const started = Date.now();
                const seen = [];
                const tick = () => {
                    const v = document.querySelector('[data-render-mode]')?.getAttribute('data-render-mode') ?? 'none';
                    if (seen.length === 0 || seen[seen.length - 1] !== v) seen.push(v + '@' + (Date.now() - started) + 'ms');
                    if (v === 'WebAssembly' || Date.now() - started > 180000) {
                        resolve(seen.join(' -> '));
                        return;
                    }
                    setTimeout(tick, 250);
                };
                tick();
            })
            """);
        TestContext.WriteLine($"data-render-mode timeline (after reload): {mode}");
        Assert.AreEqual("WebAssembly", mode.Split(" -> ").Last().Split('@')[0]);

        await OverlayTopLayerScenarios.CommandPaletteOpenedFromDrawerPaintsAboveTheDrawer(page);
    }
}
