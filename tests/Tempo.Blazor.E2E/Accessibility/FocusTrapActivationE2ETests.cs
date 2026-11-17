using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Accessibility;

/// <summary>
/// Focus-trap ACTIVATION semantics (WASM @ 7106), against the /modal-dialog demo's custom TmModal.
/// </summary>
/// <remarks>
/// <para>
/// <c>FocusTrap.ActivateAsync</c> is gated on a LAZY ES-module import, so <c>activate()</c> can land an
/// arbitrary amount of time after the overlay rendered — long enough for the user to have clicked into a
/// control inside it. Both tests here make that timing DETERMINISTIC instead of racing it: the module
/// request is held at the network layer (Playwright route) and released only once the test has put focus
/// exactly where it wants it. That reproduces at 100% what a cold start reproduced at roughly 1 in 8.
/// </para>
/// <para>
/// Two branches, because the correct behaviour is a conjunction and either half alone is satisfiable by a
/// broken implementation:
/// </para>
/// <list type="bullet">
///   <item><description>focus already INSIDE ⇒ the trap must leave it alone (the bug: it was stolen to
///   <c>button.tm-modal-close</c>);</description></item>
///   <item><description>focus OUTSIDE ⇒ the trap MUST pull it in (ARIA). Without this branch, simply
///   deleting the initial-focus call would pass.</description></item>
/// </list>
/// <para>
/// FLOOR (both tests): the glob carries a wildcard because Blazor fingerprints static web assets
/// (<c>tm-focus-trap.&lt;hash&gt;.js</c>) — a literal <c>**/tm-focus-trap.js</c> silently matches NOTHING,
/// the module then loads before the test places focus, and "focus stayed put" becomes trivially true.
/// <see cref="AssertGateHeldEveryModuleRequest"/> therefore asserts BOTH that the gate caught a request
/// and that it caught EVERY one — the second half matters because a glob that holds 1 of 2 requests
/// leaves the other free to load the module early, and a mere "at least one" check cannot see that.
/// The inside-branch additionally proves the trap was LIVE in this run (Tab from the last focusable wraps
/// to the first — only the trap's keydown handler does that) before it believes "nothing moved".
/// </para>
/// </remarks>
[TestClass]
[TestCategory("WASM")]
public sealed class FocusTrapActivationE2ETests : WasmTestBase
{
    private const string Route = "/modal-dialog";

    // MUST carry wildcards: Blazor serves this as tm-focus-trap.<fingerprint>.js.
    private const string FocusTrapModuleGlob = "**/tm-focus-trap*.js*";

    private const string ModuleUrlNeedle = "tm-focus-trap";

    /// <summary>Stable, readable identity for a DOM element: testid when present, else tag + classes.</summary>
    private const string DescribeFunctionJs =
        """
        window.tmDescribe = el => {
            if (!el) return '(null)';
            if (el === document.body) return 'body';
            const tag = (el.tagName || '?').toLowerCase();
            const tid = el.getAttribute && el.getAttribute('data-testid');
            if (tid) return tag + '[data-testid=' + tid + ']';
            const raw = typeof el.className === 'string' ? el.className.trim() : '';
            return tag + (raw ? '.' + raw.split(/\s+/).join('.') : '');
        };
        """;

    private const string ActiveElementJs = "() => { " + DescribeFunctionJs + " return window.tmDescribe(document.activeElement); }";

    /// <summary>
    /// Records EVERY focus change from the moment it is installed, so the assertion is over a window of
    /// time rather than a single sample — a point-in-time read could be taken before or after the steal.
    /// </summary>
    private const string InstallFocusRecorderJs =
        "() => { " + DescribeFunctionJs + """
        window.__tmFocusLog = [];
        document.addEventListener('focusin', e => window.__tmFocusLog.push(window.tmDescribe(e.target)), true);
        }
        """;

    private const string FloorProbeMark = "### floor-probe";

    private sealed record GatedModule(TaskCompletionSource Gate, List<string> Gated, List<string> Seen);

    private async Task<(IPage Page, GatedModule Module)> OpenWithGatedFocusTrapModuleAsync()
    {
        var module = new GatedModule(new TaskCompletionSource(), [], []);

        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        await context.RouteAsync(FocusTrapModuleGlob, async route =>
        {
            lock (module.Gated) module.Gated.Add(route.Request.Url);
            try { await module.Gate.Task.WaitAsync(TimeSpan.FromSeconds(90)); }
            catch (TimeoutException) { /* never released — let the request through so the run fails on the floor, not on a hang */ }
            await route.ContinueAsync();
        });

        var page = await context.NewPageAsync();
        page.Request += (_, request) =>
        {
            if (request.Url.Contains(ModuleUrlNeedle, StringComparison.Ordinal))
            {
                lock (module.Seen) module.Seen.Add(request.Url);
            }
        };

        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}{Route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return (page, module);
    }

    /// <summary>
    /// FLOOR: the gate caught EVERY focus-trap module request this run made — not merely at least one.
    /// <para>
    /// The two counts come from two independent mechanisms, which is the only reason comparing them
    /// says anything: <c>Gated</c> is appended by the <see cref="IBrowserContext.RouteAsync(string, Action{IRoute})"/>
    /// handler, so it counts Playwright GLOB matches, while <c>Seen</c> is appended by the page's
    /// <c>Request</c> event using a plain <c>Contains</c> substring test that never consults the glob.
    /// Comparing one filter with itself would be vacuous; these disagree whenever the glob is wrong.
    /// </para>
    /// <para>
    /// Every URL the glob can match necessarily contains the needle, so Gated ⊆ Seen and the failure
    /// this catches is one-directional and concrete: a SECOND, ungated route to the same module, which
    /// would load it before the test places focus and turn "focus did not move" trivially true. The
    /// weaker <c>Gated &gt; 0</c> check alone stays green in exactly that case.
    /// </para>
    /// </summary>
    private static void AssertGateHeldEveryModuleRequest(GatedModule module)
    {
        string[] gated, seen;
        lock (module.Gated) gated = [.. module.Gated];
        lock (module.Seen) seen = [.. module.Seen];

        Assert.IsTrue(
            gated.Length > 0,
            $"FLOOR: the route glob '{FocusTrapModuleGlob}' held NO request, so the focus trap module was "
          + "never gated and this test measured nothing. Requests whose URL contained "
          + $"'{ModuleUrlNeedle}': [{string.Join(", ", seen)}].");

        Assert.AreEqual(
            seen.Length,
            gated.Length,
            $"FLOOR: the route glob '{FocusTrapModuleGlob}' held {gated.Length} of {seen.Length} focus-trap "
          + $"module request(s) — {seen.Length - gated.Length} slipped past it UNGATED and could have loaded "
          + "the module before this test placed focus, which would make the result trivially true. "
          + $"Gated (glob): [{string.Join(", ", gated)}]. Seen (substring '{ModuleUrlNeedle}'): "
          + $"[{string.Join(", ", seen)}].");
    }

    /// <summary>Emits the run's floor values into the .trx so the numbers come from an artifact, not from prose.</summary>
    private static string FloorReport(GatedModule module, string extra, string[] focusLog)
    {
        string[] gated, seen;
        lock (module.Gated) gated = [.. module.Gated];
        lock (module.Seen) seen = [.. module.Seen];
        return $"FLOOR gated={gated.Length} seen={seen.Length} gatedUrls=[{string.Join(", ", gated)}] "
             + $"{extra} focusLog=[{string.Join(" -> ", focusLog)}]";
    }

    private static async Task<IPage> OpenCustomModalAsync(IPage page)
    {
        await page.GetByTestId("open-custom-modal").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Assertions.Expect(page.Locator("[data-testid='custom-modal-gotit-btn']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        return page;
    }

    private static async Task ReleaseAndSettleAsync(IPage page, GatedModule module)
    {
        var moduleResponse = page.WaitForResponseAsync(r => r.Url.Contains(ModuleUrlNeedle, StringComparison.Ordinal),
            new PageWaitForResponseOptions { Timeout = 30000 });
        module.Gate.SetResult();
        await moduleResponse;

        // The response only proves DELIVERY; `activate()` runs one interop hop later. The floor probe below
        // is what proves it actually ran — this wait only makes the probe non-flaky.
        await page.WaitForTimeoutAsync(2500);
    }

    /// <summary>
    /// THE REGRESSION: the user focused a control inside an already-open modal, the lazily imported focus
    /// trap arrived afterwards, and its unconditional initial-focus call yanked focus to the close button.
    /// </summary>
    [TestMethod]
    public async Task Modal_FocusTrapActivation_LeavesFocusAloneWhenItIsAlreadyInsideTheOverlay()
    {
        var (page, module) = await OpenWithGatedFocusTrapModuleAsync();
        await OpenCustomModalAsync(page);

        // The user puts focus on the LAST focusable inside the overlay (the trap's initial focus target is
        // the FIRST one, so a steal is unambiguous).
        var gotIt = page.Locator("[data-testid='custom-modal-gotit-btn']");
        await gotIt.FocusAsync();

        // PRECONDITION as a GATE, not as an assumption: if focus is not where we think it is, everything
        // below is vacuous, so this must be RED rather than silently true.
        var placed = await page.EvaluateAsync<string>(ActiveElementJs);
        Assert.AreEqual(
            "button[data-testid=custom-modal-gotit-btn]",
            placed,
            "PRECONDITION: focus was not placed inside the overlay, so the steal could not have been observed either way.");

        await page.EvaluateAsync(InstallFocusRecorderJs);

        await ReleaseAndSettleAsync(page, module);

        // Mark, then probe. Everything the trap did on activation is BEFORE the mark; everything the probe
        // itself does is after it.
        await page.EvaluateAsync($"() => window.__tmFocusLog.push('{FloorProbeMark}')");

        // FLOOR: prove the trap is LIVE in this run. Re-place focus on the last focusable (a no-op when the
        // fix holds, a correction when it does not) and Tab: only the trap's keydown handler wraps focus
        // back to the first focusable instead of letting it escape the overlay.
        await gotIt.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        var afterTab = await page.EvaluateAsync<string>(ActiveElementJs);

        var log = await page.EvaluateAsync<string[]>("() => window.__tmFocusLog.slice()");
        var markIndex = Array.IndexOf(log, FloorProbeMark);
        var duringActivation = markIndex < 0 ? log : log[..markIndex];

        TestContext.WriteLine(FloorReport(module, $"tab-from-last='{afterTab}'", log));

        AssertGateHeldEveryModuleRequest(module);
        Assert.IsTrue(markIndex >= 0, "FLOOR: the focus recorder never saw the probe mark — the log is not the one this test wrote.");
        Assert.AreEqual(
            "button.tm-modal-close",
            afterTab,
            "FLOOR: Tab from the last focusable did not wrap to the first, so the focus trap was NOT active in this "
          + "run and 'focus did not move' would be trivially true. Full focus log: ["
          + string.Join(" -> ", log) + "].");

        Assert.AreEqual(
            0,
            duringActivation.Length,
            "The focus trap MOVED focus that the user had already placed inside the overlay. Focus changes recorded "
          + "between placing focus on custom-modal-gotit-btn and the floor probe: ["
          + string.Join(" -> ", duringActivation) + "].");
    }

    /// <summary>
    /// The other half of the conjunction: when the overlay opens with focus still outside it, the trap MUST
    /// pull focus in. Without this, deleting the initial-focus call altogether would look like a fix.
    /// </summary>
    [TestMethod]
    public async Task Modal_FocusTrapActivation_MovesFocusInsideWhenItStartsOutsideTheOverlay()
    {
        var (page, module) = await OpenWithGatedFocusTrapModuleAsync();
        await OpenCustomModalAsync(page);

        // PRECONDITION as a GATE: focus must genuinely start OUTSIDE the overlay, otherwise "focus ended up
        // inside" proves nothing.
        var outside = await page.EvaluateAsync<bool>(
            "() => !document.activeElement || !document.activeElement.closest('.tm-modal')");
        var placed = await page.EvaluateAsync<string>(ActiveElementJs);
        Assert.IsTrue(outside, $"PRECONDITION: focus already sat inside the overlay before the trap loaded (it was on '{placed}').");

        await page.EvaluateAsync(InstallFocusRecorderJs);
        await ReleaseAndSettleAsync(page, module);

        var log = await page.EvaluateAsync<string[]>("() => window.__tmFocusLog.slice()");
        var landed = await page.EvaluateAsync<string>(ActiveElementJs);

        TestContext.WriteLine(FloorReport(module, $"focus-before='{placed}' focus-after='{landed}'", log));

        AssertGateHeldEveryModuleRequest(module);

        Assert.AreEqual(
            "button.tm-modal-close",
            landed,
            $"ARIA: the focus trap must move focus INTO the overlay when it activates with focus outside (it was on "
          + $"'{placed}', it ended on '{landed}'). Focus changes recorded after release: [{string.Join(" -> ", log)}].");
    }

    // ── Blast radius ────────────────────────────────────────────────────────────────────────────────
    // FocusTrap is shared by TmModal, TmDialog, TmDrawer and TmCommandPalette. Their bUnit suites run
    // under loose JS interop, so they never execute tm-focus-trap.js at all and cannot see this change.
    // These are the browser checks that they still get initial focus moved INSIDE on open — the half of
    // the contract the guard could plausibly have broken. TmCommandPalette is asserted down to the exact
    // element because its "search input is autofocused" behaviour IS the trap's initial-focus move.

    private async Task<IPage> OpenAsync(string route)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private async Task AssertFocusLandsInsideAsync(IPage page, string containerSelector, string expected, string component)
    {
        await Assertions.Expect(page.Locator(containerSelector))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        try
        {
            await page.WaitForFunctionAsync(
                $"() => !!document.activeElement && !!document.activeElement.closest('{containerSelector}')",
                null,
                new PageWaitForFunctionOptions { Timeout = 20000 });
        }
        catch (TimeoutException)
        {
            // Fall through: the assert below reports where focus actually ended up, which is the useful signal.
        }

        var landed = await page.EvaluateAsync<string>(ActiveElementJs);

        TestContext.WriteLine($"BLAST-RADIUS {component}: container='{containerSelector}' focus='{landed}'");
        Assert.AreEqual(expected, landed, $"{component}: the focus trap must still move initial focus inside {containerSelector} on open.");
    }

    [TestMethod]
    public async Task TmDialog_StillReceivesInitialFocusInsideOnOpen()
    {
        var page = await OpenAsync("/modal-dialog");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Show Confirm" }).ClickAsync();
        // TmDialog has no close button; its first focusable is the footer Cancel button. The demo
        // confirm dialog names CancelButtonVariant=OutlineSecondary, so the variant classes ride along.
        await AssertFocusLandsInsideAsync(page, ".tm-dialog", "button.tm-btn.tm-btn-outline-secondary.tm-btn-md.tm-dialog-btn-cancel", "TmDialog");
    }

    [TestMethod]
    public async Task TmDrawer_StillReceivesInitialFocusInsideOnOpen()
    {
        var page = await OpenAsync("/feedback");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Open Right Drawer" }).ClickAsync();
        await AssertFocusLandsInsideAsync(page, ".tm-drawer", "button.tm-drawer__close", "TmDrawer");
    }

    [TestMethod]
    public async Task TmCommandPalette_StillAutofocusesItsSearchInputOnOpen()
    {
        var page = await OpenAsync("/layout");
        // Exact (case-sensitive) — the top bar also carries a "Open command palette" icon trigger.
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Open Command Palette", Exact = true }).ClickAsync();
        await AssertFocusLandsInsideAsync(page, ".tm-command-palette", "input.tm-command-palette-input", "TmCommandPalette");
    }
}
