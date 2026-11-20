using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Boot readiness, cold-boot recovery and layout/animation settling of <see cref="PlaywrightTestBase"/>.
/// </summary>
public abstract partial class PlaywrightTestBase
{
    /// <summary>
    /// Navigates to <paramref name="url"/> and waits for the app to be ready, with the ONE controlled recovery from
    /// a slow cold boot: when the readiness gate times out the SAME page is reloaded once (the app and its cached
    /// framework assets are up by then; only the first boot was slow) and gated again. This is the only place that
    /// recovery lives — specs and per-feature harnesses call this instead of keeping a local retry copy
    /// ("ReadinessInfrastructureTests.NoSpecKeepsALocalColdBootRecoveryCopy" enforces it). A second timeout is a real
    /// failure and propagates. The wait budgets come from <see cref="ReadinessTimeouts"/> (<c>TM_E2E_READY_TIMEOUT_MS</c>).
    /// </summary>
    protected internal async Task GotoAppReadyAsync(IPage page, string url, WaitUntilState waitUntil = WaitUntilState.Load)
    {
        var timeouts = ReadinessTimeouts.FromEnvironment();
        var navigated = false;
        // The first attempt INCLUDES the navigation: a GotoAsync that times out on a slow cold boot is recovered
        // by the same single controlled reload as a readiness timeout. When the navigation never committed (the page
        // is still about:blank) there is nothing to reload, so the recovery navigates again.
        await ColdBootRecovery.RunAsync(
            async () =>
            {
                if (!navigated)
                {
                    navigated = true;
                    await page.GotoAsync(url, new PageGotoOptions { WaitUntil = waitUntil, Timeout = timeouts.DomContentLoadedMs });
                }

                await WaitForBootGateAsync(page);
            },
            () => RecoverBootAsync(ReloadOrNavigateAsync));

        return;

        Task ReloadOrNavigateAsync() =>
            string.Equals(page.Url, "about:blank", StringComparison.OrdinalIgnoreCase)
                ? page.GotoAsync(url, new PageGotoOptions { WaitUntil = waitUntil, Timeout = timeouts.DomContentLoadedMs })
                : page.ReloadAsync(new PageReloadOptions { WaitUntil = waitUntil, Timeout = timeouts.DomContentLoadedMs });
    }

    /// <summary>
    /// Reloads the CURRENT page and waits for the app to be ready, with the same ONE controlled recovery as
    /// <see cref="GotoAppReadyAsync"/>. Specs that reload a page (to prove state survives a reload, to re-run a boot
    /// path) call this instead of <c>page.ReloadAsync()</c> + <c>WaitForAppReadyAsync</c> (<c>waitUntil</c> keeps the spec's own
    /// reload wait condition): a reload boots the WASM
    /// runtime again, and a boot whose one framework fetch dies stalls for good without the recovery.
    /// </summary>
    protected internal async Task ReloadAppReadyAsync(IPage page, WaitUntilState waitUntil = WaitUntilState.Load)
    {
        var timeouts = ReadinessTimeouts.FromEnvironment();
        var reloaded = false;
        await ColdBootRecovery.RunAsync(
            async () =>
            {
                if (!reloaded)
                {
                    reloaded = true;
                    await page.ReloadAsync(new PageReloadOptions { WaitUntil = waitUntil, Timeout = timeouts.DomContentLoadedMs });
                }

                await WaitForBootGateAsync(page);
            },
            () => RecoverBootAsync(() => page.ReloadAsync(new PageReloadOptions { WaitUntil = waitUntil, Timeout = timeouts.DomContentLoadedMs })));
    }

    /// <summary>
    /// Runs <paramref name="trigger"/> (a click, a language switch, a form submit ...) that makes the page load a NEW
    /// document, waits for the app of that document to be ready and recovers a stalled boot with the same ONE
    /// controlled reload as <see cref="GotoAppReadyAsync"/>. It replaces the hand-rolled
    /// <c>trigger + WaitForLoadState + WaitForAppReadyAsync</c> sequence, which could pass on the OLD document
    /// (its <c>data-blazor-ready</c> is still set while the navigation is in flight).
    /// </summary>
    /// <remarks>
    /// The old document is ALWAYS marked before the trigger and the gate waits until the mark is gone, so it can never
    /// pass on the old page. A trigger that does not replace the document (client-side routing, a click that does
    /// nothing) is therefore a failure by design: use <see cref="GotoAppReadyAsync"/> / <see cref="ReloadAppReadyAsync"/>
    /// or a plain locator wait for navigation inside the running app.
    /// </remarks>
    /// <param name="page">The page whose document is replaced by the trigger.</param>
    /// <param name="trigger">The action that starts the full document load (never awaited for the load itself).</param>
    protected internal async Task NavigateAppReadyAsync(IPage page, Func<Task> trigger)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(trigger);

        var timeouts = ReadinessTimeouts.FromEnvironment();
        await page.EvaluateAsync("() => { window.__tmE2EOldDocument = true; }");

        await trigger();

        try
        {
            // WaitForFunction survives the navigation (it re-runs in the new document, where the mark is absent).
            await page.WaitForFunctionAsync("() => window.__tmE2EOldDocument !== true", null,
                new PageWaitForFunctionOptions { Timeout = timeouts.DomContentLoadedMs });
        }
        catch (Exception ex) when (ColdBootRecovery.IsTimeout(ex))
        {
            throw new InvalidOperationException(
                "NavigateAppReadyAsync: the trigger did not load a new document (the old page is still there). "
                + "It is for a trigger that replaces the document (forceLoad link, culture switch, form submit); "
                + "for client-side routing wait on the destination instead.", ex);
        }

        await ColdBootRecovery.RunAsync(
            () => WaitForBootGateAsync(page),
            () => RecoverBootAsync(() => page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = timeouts.DomContentLoadedMs })));
    }

    /// <summary>
    /// Creates a context (through <see cref="CreateContextAsync(BrowserNewContextOptions, bool)"/>: suite defaults, failure
    /// trace, bounded relaunching creation, cleanup registration), lets the spec prepare it
    /// (<paramref name="beforeNavigate"/>: init scripts, routes, cookies, permissions ...), opens one page and boots it on
    /// <paramref name="url"/> with <see cref="GotoAppReadyAsync"/> (the ONE controlled cold-boot reload). The context is
    /// <c>page.Context</c>.
    /// </summary>
    /// <param name="url">Absolute URL to open.</param>
    /// <param name="options">Context options (viewport, locale, touch, color scheme ...); <c>null</c> = suite defaults.</param>
    /// <param name="beforeNavigate">Runs after the context exists and before the first navigation.</param>
    /// <param name="waitUntil">The navigation's wait condition (kept per spec; the readiness gate follows regardless).</param>
    /// <param name="trace">Collect a failure trace; <c>false</c> for boot/perf specs whose timing the tracing would skew.</param>
    protected internal async Task<IPage> OpenAppPageAsync(
        string url,
        BrowserNewContextOptions? options = null,
        Func<IBrowserContext, Task>? beforeNavigate = null,
        WaitUntilState waitUntil = WaitUntilState.Load,
        bool trace = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var context = await CreateContextAsync(options ?? new BrowserNewContextOptions(), trace);
        if (beforeNavigate is not null)
        {
            await beforeNavigate(context);
        }

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, url, waitUntil);
        return page;
    }

    /// <summary>
    /// Navigates WITHOUT the readiness gate and WITHOUT the cold-boot reload. Only for specs whose subject is the boot
    /// itself (a boot-time measurement, a stalled/blocked boot on purpose, a page that never becomes ready); every other
    /// spec must use <see cref="GotoAppReadyAsync"/>. Such a spec creates its context with
    /// <c>CreateContextAsync(options, trace: false)</c>; never <see cref="OpenAppPageAsync"/>, which always recovers.
    /// </summary>
    /// <param name="page">The page to navigate.</param>
    /// <param name="url">Absolute URL.</param>
    /// <param name="reason">Why this navigation must not be recovered (required, written to the test output).</param>
    /// <param name="waitUntil">The navigation's wait condition.</param>
    protected internal async Task<IResponse?> GotoWithoutBootRecoveryAsync(
        IPage page,
        string url,
        string reason,
        WaitUntilState waitUntil = WaitUntilState.Load)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        TestContext.WriteLine($"[e2e] navigating without boot recovery: {reason}");
        return await page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = waitUntil,
            Timeout = ReadinessTimeouts.FromEnvironment().DomContentLoadedMs
        });
    }

    /// <summary>
    /// The recovery step shared by <see cref="GotoAppReadyAsync"/> and <see cref="ReloadAppReadyAsync"/>. The readiness
    /// gate timed out. Besides a slow first boot, the other cause is a host that DIED while the app was booting
    /// (observed 2026-11-18: Demo WASM died mid-run and the recovery reload landed on net::ERR_CONNECTION_REFUSED).
    /// Resurrect the hosts the same way a ClassInitialize would BEFORE the reload, and once more if the reload itself
    /// still meets a refused connection (the host died between the probe and the reload). A second refusal still
    /// propagates - the recovery stays bounded.
    /// </summary>
    private async Task RecoverBootAsync(Func<Task> reload)
    {
        await RecoverDemoHostsAsync();
        try
        {
            await reload();
        }
        catch (PlaywrightException ex) when (IsConnectionRefused(ex))
        {
            await RecoverDemoHostsAsync();
            await reload();
        }
    }

    /// <summary>
    /// Whether <paramref name="ex"/> is the browser-level "nothing is listening" error — the
    /// signature of a self-hosted demo host that died under the in-flight test.
    /// </summary>
    internal static bool IsConnectionRefused(PlaywrightException ex) =>
        ex.Message.Contains("ERR_CONNECTION_REFUSED", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("ERR_CONNECTION_RESET", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("ERR_EMPTY_RESPONSE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Waits for the Blazor application to be ready. NO recovery: a cold boot that stalls fails the spec.
    /// </summary>
    [Obsolete("A bare readiness wait has no cold-boot recovery: use GotoAppReadyAsync / ReloadAppReadyAsync / NavigateAppReadyAsync (or OpenAppPageAsync). Becomes private once every spec is migrated (E2E harness phase 1a).")]
    protected Task WaitForAppReadyAsync(IPage page) => WaitForBootGateAsync(page);

    /// <summary>
    /// The readiness gate itself (one deadline per attempt); every central helper ends in it.
    /// </summary>
    private async Task WaitForBootGateAsync(IPage page)
    {
        // Every budget derives from TM_E2E_READY_TIMEOUT_MS (default 120 s; the former hard-coded 60 s / 15 s / 45 s
        // windows were too tight for a cold WASM boot under full-lane CPU load). All waits are on a STATE, so a
        // longer cap only extends the wait of a slow boot, it never sleeps. Boots past 15 s are logged so a
        // creeping regression is visible in the test output instead of only in the duration. A cold-boot TIMEOUT is
        // recovered by GotoAppReadyAsync (one controlled reload), never by a per-spec copy.
        // ONE deadline per attempt (ReadyMs): each sub-wait gets only the time that is left, so the pre-gates can
        // never add up to more than the authoritative cap.
        var timeouts = ReadinessTimeouts.FromEnvironment();
        var attemptWatch = System.Diagnostics.Stopwatch.StartNew();
        float Remaining(int capMs) =>
            Math.Max(1f, Math.Min(capMs, timeouts.ReadyMs - (float)attemptWatch.Elapsed.TotalMilliseconds));

        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions
        {
            Timeout = Remaining(timeouts.DomContentLoadedMs)
        });

        try
        {
            await page.WaitForSelectorAsync(".tm-app-loaded, main, [data-testid='app-ready']", new PageWaitForSelectorOptions
            {
                Timeout = Remaining(timeouts.SelectorMs)
            });
        }
        catch (TimeoutException)
        {
            await page.WaitForFunctionAsync(
                """
                () => {
                    const body = document.body;
                    const app = document.querySelector('#app, [data-testid="app"], main') || body;
                    const text = (app?.textContent || body?.textContent || '').trim();
                    return (document.readyState === 'interactive' || document.readyState === 'complete')
                        && !!body
                        && body.children.length > 0
                        && text.length > 0
                        && !/^loading[.\s]*$/i.test(text);
                }
                """,
                null,
                new PageWaitForFunctionOptions { Timeout = Remaining(timeouts.FallbackMs) });
        }

        // Stateful readiness instead of the former fixed 1000 ms sleep (N193): the shared demo
        // MainLayout sets [data-blazor-ready] on <body> from OnAfterRenderAsync(firstRender) —
        // which prerendering never calls — so the attribute lands exactly when an interactive
        // runtime has rendered (WASM boot finished / server circuit up). A host whose runtime
        // never comes up fails this wait instead of silently passing under a sleep.
        var bootWatch = System.Diagnostics.Stopwatch.StartNew();
        await page.WaitForFunctionAsync(
            "() => document.body !== null && document.body.hasAttribute('data-blazor-ready')",
            null,
            new PageWaitForFunctionOptions { Timeout = Remaining(timeouts.ReadyMs) });
        bootWatch.Stop();
        if (bootWatch.Elapsed > TimeSpan.FromSeconds(15))
        {
            TestContext.WriteLine($"[e2e] slow boot: data-blazor-ready after {bootWatch.Elapsed.TotalSeconds:F1}s (timeout {timeouts.ReadyMs}ms)");
        }
    }

    /// <summary>
    /// Waits until fonts are loaded, no CSS transition runs, no smooth scroll is in flight and the boxes of
    /// <paramref name="selector"/> are identical across 3 consecutive frames (15 s budget).
    /// </summary>
    /// <remarks>
    /// The page is still MOVING when the app reports ready: the demo's Tailwind Play CDN generates CSS for
    /// freshly rendered classes after the interactive render, and <c>html { scroll-behavior: smooth }</c> turns
    /// every programmatic/focus scroll into an animation (docs/e2e-test-lanes.md "Timing-masked races").
    /// Geometry assertions must wait for this settled state, not for a fixed delay. <paramref name="selector"/>
    /// should list every element whose box the following assertions read; the window scroll position is part of
    /// the signature, so an in-flight smooth scroll counts as "moving" even when the elements' boxes are stable.
    /// A selector that matches no element never settles — the wait times out instead of passing on an empty page.
    /// </remarks>
    protected static async Task WaitForLayoutToSettleAsync(IPage page, string selector)
    {
        var settled = await page.EvaluateAsync<bool>(
            """
            async (selector) => {
                const frame = () => new Promise(resolve => requestAnimationFrame(() => resolve()));
                const transitionRunning = () => document.getAnimations()
                    .some(animation => animation instanceof CSSTransition && animation.playState === 'running');
                const deadline = performance.now() + 15000;
                let previous = null;
                let stableFrames = 0;
                while (performance.now() < deadline) {
                    await document.fonts.ready;
                    await frame();
                    const nodes = document.querySelectorAll(selector);
                    if (nodes.length === 0) {
                        // A selector that matches nothing must NOT settle on the empty signature:
                        // reset and keep polling so the wait times out instead of passing.
                        previous = null;
                        stableFrames = 0;
                        continue;
                    }
                    const current = [...nodes]
                        .map(node => {
                            const r = node.getBoundingClientRect();
                            return `${r.left},${r.top + window.scrollY},${r.width},${r.height}`;
                        })
                        .concat([`${window.scrollX},${window.scrollY}`])
                        .join('|');
                    const quiet = document.fonts.status === 'loaded' && !transitionRunning();
                    stableFrames = quiet && current === previous ? stableFrames + 1 : 0;
                    if (stableFrames >= 3) {
                        return true;
                    }

                    previous = current;
                }

                return false;
            }
            """, selector);
        Assert.IsTrue(settled,
            $"the layout never settled within 15s (fonts loading, a CSS transition running, the '{selector}' boxes/scroll still moving, or the selector matched no element) — geometry read now would be mid-reflow");
    }

    /// <summary>
    /// Waits until every FINITE animation inside <paramref name="scopeSelector"/> (the element and its subtree) has
    /// finished, and asserts it did (10 s budget). Infinite animations (spinners, pulses) are ignored.
    /// </summary>
    /// <remarks>
    /// <see cref="WaitForLayoutToSettleAsync"/> watches CSS TRANSITIONS only, and an entrance animation that holds its
    /// start state through a delay (<c>animation-delay</c>) looks perfectly stable until it plays. A spec that hovers
    /// or clicks a moving target then fights Playwright's actionability retries. This helper reads
    /// <c>getAnimations({ subtree: true })</c> (<c>running</c> and <c>pending</c> play states), loops until a pass
    /// finds no finite animation still running (an animation that starts while waiting is caught by the next pass)
    /// and races the whole loop against a 10 s timeout so a never-ending finite animation fails loudly instead of
    /// hanging.
    /// </remarks>
    /// <returns><c>true</c> when the scope was animation-quiet within the budget (the call has already asserted it).</returns>
    protected static async Task<bool> WaitForAnimationsToFinishAsync(IPage page, string scopeSelector)
    {
        var finished = await page.EvaluateAsync<bool>(
            """
            async (scopeSelector) => {
                const scope = document.querySelector(scopeSelector);
                if (!scope) {
                    return false;
                }

                const isFiniteRunning = animation =>
                    (animation.playState === 'running' || animation.playState === 'pending')
                    && animation.effect
                    && Number.isFinite(animation.effect.getComputedTiming().endTime);
                let cancelled = false;
                const drain = async () => {
                    while (!cancelled) {
                        const running = scope.getAnimations({ subtree: true }).filter(isFiniteRunning);
                        if (running.length === 0) {
                            return true;
                        }

                        await Promise.all(running.map(animation => animation.finished.catch(() => { })));
                    }

                    return false;
                };
                const timeout = new Promise(resolve => setTimeout(() => resolve(false), 10000));
                const result = await Promise.race([drain(), timeout]);
                cancelled = true;
                return result;
            }
            """, scopeSelector);
        Assert.IsTrue(finished,
            $"the finite animations inside '{scopeSelector}' did not finish within 10s (or the scope matched no element) - a hover/click now would hit a moving target");
        return finished;
    }
}
