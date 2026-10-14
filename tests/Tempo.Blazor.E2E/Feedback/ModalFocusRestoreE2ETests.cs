using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Feedback;

/// <summary>
/// Focus-restore contract of a modal that AUTOFOCUSES its first field from the content's own
/// lifecycle (the host-app pattern behind P04-CF F7). The gated owner's scope notes the opener at
/// its MOUNT parameter-set phase — before the content child's autofocus can run — the owners
/// pre-import the focus-trap module on their first render (so even the FIRST open of a session has
/// the opener records), and the module falls back through the focus history and the last
/// pointerdown when a capture was poisoned anyway. The opener must therefore be restored on BOTH
/// render modes, for pointer AND keyboard opens, cold and warm; the close must neither strand the
/// focus inside the removed overlay nor fight the focus the app settles on its post-close action
/// after the close render.
/// </summary>
/// <remarks>
/// <para>
/// The app's own post-close focus is issued AFTER the close render (the page's OnAfterRenderAsync):
/// a focus issued EARLIER is dropped by the browser, because the open modal holds the page inert
/// and focus() on an inert element does nothing — exactly why hosts re-focused in a
/// requestAnimationFrame after close.
/// </para>
/// <para>
/// Screenshots for the UX review land in <c>tmp/f6-focus-restore/</c> (repo root) and their
/// existence is asserted, per the MSTest AddResultFile scratch-directory lesson.
/// </para>
/// </remarks>
[TestClass]
[TestCategory("WASM")]
public sealed class ModalFocusRestoreWasmE2ETests : WasmTestBase
{
    private const string Route = "/modal-dialog";

    /// <summary>Emits a stable, readable identity for an element (class prefix + short text).</summary>
    private const string DescribeFunctionJs =
        """
        const tmDescribe = el => {
            if (!el) return '(null)';
            if (el === document.body) return 'body';
            const tag = el.tagName.toLowerCase();
            const tid = el.getAttribute && el.getAttribute('data-testid');
            return tag + (tid ? '[data-testid=' + tid + ']' : '#' + (el.id || ''));
        };
        """;

    private const string ActiveElementJs = "() => { " + DescribeFunctionJs + " return tmDescribe(document.activeElement); }";

    /// <summary>
    /// Records every <c>HTMLElement.focus()</c> call (the CALL, not its effect — a focus on an inert
    /// element is dropped by the browser and the log is what shows the trap tried). The defect this
    /// pins: the deactivate focused the DYING opener capture (the field the app focused during the
    /// open) before the render removed it.
    /// </summary>
    private const string InstallFocusCallRecorderJs =
        "() => { " + DescribeFunctionJs + """
        window.__focusCalls = [];
        const origFocus = HTMLElement.prototype.focus;
        HTMLElement.prototype.focus = function (...a) {
            window.__focusCalls.push(tmDescribe(this));
            return origFocus.apply(this, a);
        };
        }
        """;

    private static string ShotDir => Path.Combine(FindRepoRoot(), "tmp", "f6-focus-restore");

    private async Task<IPage> OpenModalDialogPageAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}{Route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        await page.GetByTestId("focus-restore-section").ScrollIntoViewIfNeededAsync();
        return page;
    }

    /// <summary>Opens the form modal with a KEYBOARD gesture: the trigger takes focus, Enter opens.</summary>
    private static async Task OpenRestoreModalWithKeyboardAsync(IPage page)
    {
        await page.GetByTestId("open-restore-modal").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await WaitForFieldAutofocusAsync(page);
    }

    private static async Task OpenRestoreModalWithPointerAsync(IPage page)
    {
        await page.GetByTestId("open-restore-modal").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await WaitForFieldAutofocusAsync(page);
    }

    private static async Task WaitForFieldAutofocusAsync(IPage page)
    {
        // The content's own lifecycle autofocuses the field; wait for it so the precondition below
        // is a gate, not a race.
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'restore-field'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    private static async Task CloseWithEscapeAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });
    }

    /// <summary>
    /// Captures the focused trigger with its focus-visible ring for the UX review. A plain element
    /// screenshot clips to the border box, which cuts the outline off — the shot is a small page
    /// clip with a margin around the element instead.
    /// </summary>
    private async Task ShotFocusedTriggerAsync(IPage page, string testId, string fileName)
    {
        var box = await page.GetByTestId(testId).BoundingBoxAsync();
        Assert.IsNotNull(box, $"the shot target {testId} must have a box");
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, fileName);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Clip = new Clip
            {
                X = Math.Max(0, box.X - 24),
                Y = Math.Max(0, box.Y - 24),
                Width = box.Width + 48,
                Height = box.Height + 48,
            },
        });
        Assert.IsTrue(File.Exists(path), $"the screenshot must exist on disk: {path}");
        TestContext.AddResultFile(path);
        TestContext.WriteLine($"shot: {path}");
    }

    private async Task ShotPageAsync(IPage page, string fileName)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, fileName);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        Assert.IsTrue(File.Exists(path), $"the screenshot must exist on disk: {path}");
        TestContext.AddResultFile(path);
        TestContext.WriteLine($"shot: {path}");
    }

    /// <summary>
    /// COLD start + WARM cycle, keyboard-opened: the trigger is focused and Enter opens the modal —
    /// there is no pointerdown record for this open, so the opener comes from the mount note (and
    /// the focus history behind it). Escape closes and the focus returns to the trigger. This is
    /// the very FIRST open of the browser session: the eager ensureLoaded pre-import makes it behave
    /// like every later one (previously the first activation loaded the module and lost the opener,
    /// and this close landed on <c>body</c>).
    /// </summary>
    [TestMethod]
    [TestCategory("Smoke")]
    public async Task KeyboardOpen_ColdThenWarm_EscapeReturnsTheFocusToTheTrigger()
    {
        var page = await OpenModalDialogPageAsync();

        // COLD: the first open of the session.
        await OpenRestoreModalWithKeyboardAsync(page);
        await CloseWithEscapeAsync(page);
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'open-restore-modal'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        var coldLanded = await page.EvaluateAsync<string>(ActiveElementJs);
        Assert.AreEqual(
            "button[data-testid=open-restore-modal]",
            coldLanded,
            "a keyboard-opened modal must return the focus to its trigger on the FIRST open of a session too");

        await ShotFocusedTriggerAsync(page, "open-restore-modal", "wasm-keyboard-escape-trigger.png");

        // WARM: the second open restores the trigger the same way.
        await OpenRestoreModalWithKeyboardAsync(page);
        await CloseWithEscapeAsync(page);
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'open-restore-modal'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    /// <summary>
    /// Pointer open, X close: the focus returns to the trigger. The overlay held the focus (the
    /// close button was pressed inside it), so the restore runs — and the opener is the engaged
    /// trigger, never the field the app autofocus-focussed and never the body.
    /// </summary>
    [TestMethod]
    public async Task PointerOpen_CloseX_ReturnsTheFocusToTheTrigger()
    {
        var page = await OpenModalDialogPageAsync();
        await OpenRestoreModalWithPointerAsync(page);

        await page.Locator(".tm-modal-close").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });

        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'open-restore-modal'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        var landed = await page.EvaluateAsync<string>(ActiveElementJs);
        Assert.AreEqual(
            "button[data-testid=open-restore-modal]",
            landed,
            "a pointer-opened modal must return the focus to the trigger the user engaged — on WASM too");
    }

    /// <summary>
    /// The OK close runs exactly once through the deactivate: it must NEVER focus the dying opener
    /// capture (the field the app focused during the open — the defect had the close focus that
    /// dying element, and the focus fell to <c>body</c> when the overlay left the DOM). The app's
    /// own post-close focus (scheduled for after the close render) lands on the post-close action
    /// and stays — the close never fights it. Screenshot: the focus ring on the post-close action.
    /// </summary>
    [TestMethod]
    [TestCategory("Smoke")]
    public async Task OkClose_NeverFocusesTheDyingField_TheAppFocusLandsAfterClose()
    {
        var page = await OpenModalDialogPageAsync();
        await OpenRestoreModalWithPointerAsync(page);
        await page.EvaluateAsync(InstallFocusCallRecorderJs);

        await page.Locator(".tm-modal-btn-ok").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });

        var calls = await page.EvaluateAsync<string[]>("() => window.__focusCalls.slice()");
        TestContext.WriteLine("focus calls during close: [" + string.Join(" -> ", calls) + "]");

        Assert.IsFalse(
            calls.Any(c => c != null && c.Contains("restore-field")),
            $"the deactivate focused the DYING opener capture (the restore-field): [{string.Join(" -> ", calls)}] — " +
            "a poisoned capture must never be restored to; the close falls back to the body instead");

        // The app's post-close focus (scheduled after the close render) lands on the post-close action.
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'restore-post-action'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        await ShotFocusedTriggerAsync(page, "restore-post-action", "wasm-ok-post-action.png");
    }

    /// <summary>
    /// The phase's race made deterministic: the module request is held at the network layer, the app
    /// focuses its field DURING the import, and only then the trap activates with the poisoned
    /// capture. The OK close must never focus that dying field — and the final focus location is
    /// asserted: the app's post-close focus on the post-close action holds.
    /// </summary>
    [TestMethod]
    public async Task OkClose_AfterFieldFocusedDuringTheLazyImport_NeverFocusesTheDyingField()
    {
        var gate = new TaskCompletionSource();
        var gated = 0;
        var seen = 0;

        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        // MUST carry wildcards: Blazor serves this as tm-focus-trap.<fingerprint>.js.
        await context.RouteAsync("**/tm-focus-trap*.js*", async route =>
        {
            Interlocked.Increment(ref gated);
            try { await gate.Task.WaitAsync(TimeSpan.FromSeconds(60)); }
            catch (TimeoutException) { /* let it through — the run then fails on the floor below */ }
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("tm-focus-trap", StringComparison.Ordinal)) Interlocked.Increment(ref seen);
        };
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}{Route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.GetByTestId("focus-restore-section").ScrollIntoViewIfNeededAsync();

        await page.GetByTestId("open-restore-modal").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // The app focuses its field while the import is still in flight (the phase's scenario).
        await page.EvaluateAsync("() => document.querySelector(\"[data-testid='restore-field']\").focus()");

        TestContext.WriteLine($"FLOOR gated={gated} seen={seen}");
        Assert.IsTrue(
            Volatile.Read(ref gated) > 0,
            $"FLOOR: the route glob held NO focus-trap module request (seen: {seen}) — the module was " +
            "never gated and this test measured nothing.");

        // Register the response waiter BEFORE releasing: the served response fires the moment the
        // gate opens, and a waiter started afterwards would miss it.
        var moduleResponse = page.WaitForResponseAsync(
            r => r.Url.Contains("tm-focus-trap", StringComparison.Ordinal),
            new PageWaitForResponseOptions { Timeout = 30000 });
        gate.SetResult();
        await moduleResponse;
        // The response only proves delivery; activate() runs an interop hop later.
        await page.WaitForTimeoutAsync(2500);

        // PRECONDITION: the trap activated with the field already focused (it did not steal to the
        // close button), so the capture it took was poisoned — exactly the phase's scenario.
        var duringImport = await page.EvaluateAsync<string>(ActiveElementJs);
        Assert.AreEqual(
            "input[data-testid=restore-field]",
            duringImport,
            "PRECONDITION: the app focus placed during the lazy import did not survive the trap's " +
            $"activation (focus is on '{duringImport}') — this run did not model the race.");

        await page.EvaluateAsync(InstallFocusCallRecorderJs);
        await page.Locator(".tm-modal-btn-ok").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });

        var calls = await page.EvaluateAsync<string[]>("() => window.__focusCalls.slice()");
        TestContext.WriteLine("focus calls during close: [" + string.Join(" -> ", calls) + "]");
        Assert.IsFalse(
            calls.Any(c => c != null && c.Contains("restore-field")),
            $"the deactivate focused the DYING opener capture captured during the import race: " +
            $"[{string.Join(" -> ", calls)}] — a capture inside the closing surface is never restored to");

        // The FINAL focus location: the app's post-close focus (after the close render) holds.
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'restore-post-action'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    /// <summary>
    /// Dialog over modal (nested restore): the confirm dialog opened from INSIDE the modal returns
    /// the focus to the button that opened it, and only then does closing the modal return the
    /// focus to the page's trigger — each layer restores its own opener.
    /// </summary>
    [TestMethod]
    public async Task DialogOverModal_NestedConfirm_RestoresIntoTheModal_ThenToTheTrigger()
    {
        var page = await OpenModalDialogPageAsync();
        await OpenRestoreModalWithPointerAsync(page);

        // Open the confirm dialog from inside the modal.
        await page.GetByTestId("open-nested-dialog").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-dialog"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // Close the dialog: its opener (the button inside the modal) takes the focus back.
        await page.Locator(".tm-dialog-btn-cancel").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-dialog"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'open-nested-dialog'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        // Close the modal: the page's trigger takes the focus back.
        await CloseWithEscapeAsync(page);
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'open-restore-modal'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    /// <summary>
    /// The opener was removed from the DOM while the modal was open: no candidate outside the
    /// surface survives, and the close falls back to <c>body</c> deterministically — the documented
    /// fallback. It must never focus the dying field, and it must not throw. Screenshot: the closed
    /// state with the focus nowhere (the documented edge case).
    /// </summary>
    [TestMethod]
    public async Task OpenerRemovedWhileOpen_CloseFallsBackToTheBody()
    {
        var page = await OpenModalDialogPageAsync();
        await OpenRestoreModalWithPointerAsync(page);

        // A re-render replaced the opener while the modal was open.
        await page.EvaluateAsync("() => document.querySelector(\"[data-testid='open-restore-modal']\").remove()");

        await page.Locator(".tm-modal-close").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement === document.body",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        await ShotPageAsync(page, "wasm-opener-removed-body.png");
    }

    /// <summary>An explicit RestoreFocusTargetId still restores the named target (the demo scope).</summary>
    [TestMethod]
    public async Task RestoreFocusTargetId_StillRestoresTheNamedTarget()
    {
        var page = await OpenModalDialogPageAsync();
        await page.GetByTestId("scope-trigger").ScrollIntoViewIfNeededAsync();

        await page.GetByTestId("scope-trigger").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.id === 'scope-input'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        await page.GetByTestId("scope-done").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.id === 'scope-trigger'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    /// <summary>
    /// The inline (NON-modal) sheet on the same page: its scope is Active on the very FIRST
    /// parameter set, so the mount note must never suppress the activation (an earlier else-if
    /// between the two left inline sheets rendered but never trapped — no Escape, no restore, no
    /// Tab cycle). Opened from the keyboard, a non-modal sheet leaves the focus on the trigger; the
    /// user Tabs into it (native tabbing — a non-modal trap does not cycle), and Escape closes it:
    /// the focus returns to the trigger the mount note named. Screenshot: the focus ring on the
    /// trigger after the Escape close.
    /// </summary>
    [TestMethod]
    public async Task InlineSheet_EscapeClosesAndReturnsTheFocusToTheTrigger()
    {
        var page = await OpenModalDialogPageAsync();
        var trigger = page.GetByTestId("open-inline-drawer");
        await trigger.ScrollIntoViewIfNeededAsync();

        // Keyboard open: no pointerdown record exists, so the opener comes from the mount note alone.
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var sheet = page.Locator(".tm-drawer--inline");
        await Assertions.Expect(sheet)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        var onTrigger = await page.EvaluateAsync<string>(ActiveElementJs);
        Assert.AreEqual(
            "button[data-testid=open-inline-drawer]",
            onTrigger,
            "a non-modal sheet leaves the focus where it is — on the trigger that opened it");

        // The user Tabs into the sheet.
        var inside = false;
        for (var i = 0; i < 12 && !inside; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            inside = await page.EvaluateAsync<bool>(
                "() => { const a = document.activeElement; return !!a && !!a.closest && !!a.closest('.tm-drawer__panel'); }");
        }
        Assert.IsTrue(inside, "PRECONDITION: Tab never reached the inline sheet — there is nothing to Escape out of");

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(sheet)
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'open-inline-drawer'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        var landed = await page.EvaluateAsync<string>(ActiveElementJs);
        Assert.AreEqual(
            "button[data-testid=open-inline-drawer]",
            landed,
            "closing the inline sheet with Escape must return the focus to the trigger that opened it");

        await ShotFocusedTriggerAsync(page, "open-inline-drawer", "wasm-inline-sheet-escape-trigger.png");
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

/// <summary>
/// The same focus-restore contract on the <b>Server</b> demo (7107). On a circuit the mount note
/// rides ahead of the render batch, so closing a form modal that autofocuses its field returns the
/// focus to the trigger on every open — cold first open included (the eager pre-import loads the
/// module while the page is still closed) — the full SpravaFinanci scenario, without the rAF
/// self-heal.
/// </summary>
[TestClass]
[TestCategory("Smoke")]
public sealed class ModalFocusRestoreServerE2ETests : ServerTestBase
{
    private const string Route = "/modal-dialog";

    private const string ActiveElementJs =
        """
        () => {
            const a = document.activeElement;
            if (!a) return '(null)';
            if (a === document.body) return 'body';
            const tag = a.tagName.toLowerCase();
            const tid = a.getAttribute('data-testid');
            const id = a.id;
            return tag + (tid ? '[data-testid=' + tid + ']' : (id ? '#' + id : ''));
        }
        """;

    private static string ShotDir => Path.Combine(FindRepoRoot(), "tmp", "f6-focus-restore");

    private async Task<IPage> OpenModalDialogPageAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}{Route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        await page.GetByTestId("focus-restore-section").ScrollIntoViewIfNeededAsync();
        return page;
    }

    private async Task ShotFocusedTriggerAsync(IPage page, string testId, string fileName)
    {
        var box = await page.GetByTestId(testId).BoundingBoxAsync();
        Assert.IsNotNull(box, $"the shot target {testId} must have a box");
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, fileName);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Clip = new Clip
            {
                X = Math.Max(0, box.X - 24),
                Y = Math.Max(0, box.Y - 24),
                Width = box.Width + 48,
                Height = box.Height + 48,
            },
        });
        Assert.IsTrue(File.Exists(path), $"the screenshot must exist on disk: {path}");
        TestContext.AddResultFile(path);
        TestContext.WriteLine($"shot: {path}");
    }

    private async Task ShotPageAsync(IPage page, string fileName)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, fileName);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
        Assert.IsTrue(File.Exists(path), $"the screenshot must exist on disk: {path}");
        TestContext.AddResultFile(path);
        TestContext.WriteLine($"shot: {path}");
    }

    /// <summary>
    /// Keyboard open, COLD and WARM: focus the trigger, Enter, the field autofocuses, Escape — and
    /// the focus is back on the trigger. The first open of the session behaves like every later
    /// one: the owner pre-imported the module while the page was still closed, and the mount note
    /// (with the focus history behind it) names the trigger even though nothing was ever pressed.
    /// Screenshot: the focus ring on the trigger after the Escape close.
    /// </summary>
    [TestMethod]
    public async Task KeyboardOpen_ColdThenWarm_EscapeReturnsTheFocusToTheTrigger()
    {
        var page = await OpenModalDialogPageAsync();

        // COLD: the first open of the session.
        await page.GetByTestId("open-restore-modal").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'restore-field'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'open-restore-modal'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        var coldLanded = await page.EvaluateAsync<string>(ActiveElementJs);
        Assert.AreEqual(
            "button[data-testid=open-restore-modal]",
            coldLanded,
            "a keyboard-opened modal must return the focus to its trigger on the FIRST open of a session too");

        await ShotFocusedTriggerAsync(page, "open-restore-modal", "server-keyboard-escape-trigger.png");

        // WARM: the second open restores the trigger the same way.
        await page.GetByTestId("open-restore-modal").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'restore-field'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'open-restore-modal'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    /// <summary>
    /// Pointer open, X close — the very first open of the circuit: the mount note names the trigger
    /// before the overlay renders, so the close returns to it (never to the autofocus field, never
    /// to the body).
    /// </summary>
    [TestMethod]
    public async Task PlainClose_ReturnsTheFocusToTheTrigger()
    {
        var page = await OpenModalDialogPageAsync();

        await page.GetByTestId("open-restore-modal").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'restore-field'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        await page.Locator(".tm-modal-close").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'open-restore-modal'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        var landed = await page.EvaluateAsync<string>(ActiveElementJs);
        Assert.AreEqual(
            "button[data-testid=open-restore-modal]",
            landed,
            "the opener (noted before the overlay rendered) must return the focus to the trigger — " +
            "not to the field the app focused during the open and not to the body");
    }

    /// <summary>The OK close must not steal the post-close focus on the circuit either. Screenshot:
    /// the focus ring on the post-close action.</summary>
    [TestMethod]
    public async Task OkClose_KeepsTheFocusTheAppPlacedOnItsPostCloseAction_Server()
    {
        var page = await OpenModalDialogPageAsync();
        await page.GetByTestId("open-restore-modal").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'restore-field'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        await page.Locator(".tm-modal-btn-ok").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'restore-post-action'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        var landed = await page.EvaluateAsync<string>(ActiveElementJs);
        Assert.AreEqual(
            "button[data-testid=restore-post-action]",
            landed,
            "the app settled the focus on its post-close action before the close ran; the " +
            "deactivation must not steal it back on the circuit either");

        await ShotFocusedTriggerAsync(page, "restore-post-action", "server-ok-post-action.png");
    }

    /// <summary>
    /// The opener was removed from the DOM while the modal was open: no live candidate outside the
    /// surface survives and the close falls back to <c>body</c> — the documented edge case.
    /// Screenshot: the closed state with the focus nowhere.
    /// </summary>
    [TestMethod]
    public async Task OpenerRemovedWhileOpen_CloseFallsBackToTheBody_Server()
    {
        var page = await OpenModalDialogPageAsync();
        await page.GetByTestId("open-restore-modal").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'restore-field'",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        // A re-render replaced the opener while the modal was open.
        await page.EvaluateAsync("() => document.querySelector(\"[data-testid='open-restore-modal']\").remove()");

        await page.Locator(".tm-modal-close").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-modal-overlay"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement === document.body",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        await ShotPageAsync(page, "server-opener-removed-body.png");
    }

    /// <summary>
    /// Freeze: a Confirm <see cref="TmDialog"/>'s own initial-focus move keeps the trigger as the
    /// opener (the trap moved the focus itself, so the capture was never poisoned) — closing the
    /// dialog returns to the trigger on every cycle.
    /// </summary>
    [TestMethod]
    public async Task ConfirmDialog_CloseReturnsTheFocusToItsTrigger()
    {
        var page = await OpenModalDialogPageAsync();
        var trigger = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Show Confirm" });
        await trigger.ClickAsync();
        await Assertions.Expect(page.Locator(".tm-dialog"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        await page.Locator(".tm-dialog-btn-cancel").ClickAsync();
        await Assertions.Expect(page.Locator(".tm-dialog"))
            .ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 15000 });
        // The trigger is a TmButton without id/testid — identify it by its label, and never accept
        // the body (whose textContent contains the whole page and would match anything).
        await page.WaitForFunctionAsync(
            "() => { const a = document.activeElement; return !!a && a !== document.body && (a.textContent || '').indexOf('Show Confirm') >= 0; }",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        var landedText = await page.EvaluateAsync<string>("() => (document.activeElement?.textContent || '').trim()");
        TestContext.WriteLine($"confirm dialog close landed on '{landedText}'");
        Assert.AreEqual(
            "Show Confirm",
            landedText,
            "closing the confirm dialog must return the focus to its trigger");
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
