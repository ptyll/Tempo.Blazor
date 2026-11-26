using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.E2E.AI;

/// <summary>
/// E2E of the unified disabled idiom of the AI package (plan b75365fb fáze 4b, EmusCz 2.13.0 remaining
/// 463b8c0b) on the <c>/ai-conversation</c> demo page's disabled-states card (WASM @ 7106): the retry of
/// the failed sample call stays in flight for a few seconds, so the spec reads its computed look enabled
/// and then disabled — the CONTENT dims through the shared <c>--tm-opacity-disabled</c> (0.5) while the
/// button box keeps opacity 1 (an opacity on the box would dim the focus outline with it, WCAG 1.4.11 —
/// the fáze-4d review split the rule), cursor pointer → not-allowed, and the hover fill belongs to the
/// enabled control only. Fáze 4d moved the in-flight state
/// from the native <c>disabled</c> attribute to <c>aria-disabled="true"</c>: the button keeps its tab stop,
/// so a keyboard reader who activated it does not lose focus mid re-run (WCAG 2.4.3) — the focus-retention
/// spec below pins exactly that, the full-contrast focus ring of the focused in-flight retry
/// (<c>outlineColor</c> stays the theme token), the focus hand-off to the card's disclosure toggle once
/// the re-run succeeds and the Retry disappears, plus the double-activation refusal (the demo counter
/// stays at one). The
/// card's two-version answer shows version 1 of 2, so its Previous button is the static disabled version
/// button of the same idiom (Next stays enabled, proving the disabled state is the edge, not the whole
/// switcher). The forced-colors pass compares both disabled controls against an injected
/// <c>span{color:GrayText}</c> probe — system colours are not remapped by the mode, so the probe reads the
/// real system value — and pins the full opacity the forced-colors rules keep. Screenshots land in
/// <c>__screenshots__/ai-conversation/</c> for the light/dark/forced-colors UX review.
/// </summary>
/// <remarks>
/// Split out of <c>AiConversationE2ETests.cs</c> so neither file breaches the 1200-line source ceiling
/// (N270/F13); the small helpers are duplicated per class, the repo's E2E pattern. Mutant guards: the
/// literal <c>opacity: 0.6</c> / <c>cursor: default</c> restored on either control, a plain
/// <c>:hover</c> without the aria-disabled guard, a dropped forced-colors GrayText/opacity rule, the
/// native <c>disabled</c> attribute restored on the in-flight retry (the focus-retention assertions go
/// red — a natively disabled button cannot be document.activeElement) or the refusal guard removed from
/// <c>RetryAsync</c> (the counter assertion goes red) each turn one of these specs red.
/// </remarks>
[TestClass]
public sealed class AiConversationDisabledStatesE2ETests : WasmTestBase
{
    private const string CardSelector = "[data-testid='ai-disabled-states-demo']";
    private const string RetrySelector = CardSelector + " .tm-tool-call__retry";
    private const string PreviousSelector = CardSelector + " [data-tm-ai-action='version-prev']";
    private const string NextSelector = CardSelector + " [data-tm-ai-action='version-next']";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenPageAsync(int width = 1440, int height = 1000)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && message.Text.Contains("Unhandled exception", StringComparison.Ordinal))
            {
                lock (_clientErrors) _clientErrors.Add("CONSOLE: " + message.Text);
            }
        };

        await page.SetViewportSizeAsync(width, height);
        await GotoAppReadyAsync(page, $"{BaseUrl}/ai-conversation");
        await page.Locator(CardSelector).WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    /// <summary>
    /// The disabled-states card in light and dark: the enabled retry fills on hover, the disabled one
    /// (in flight) dims through the shared token, refuses the pointer and keeps its resting surface under
    /// the pointer; the version switcher's Previous is disabled (dimmed, not-allowed, no hover fill)
    /// while Next stays enabled and keeps its hover fill.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task DisabledStates_RetryAndVersionButtons_ReadDisabledInComputedStyles()
    {
        var page = await OpenPageAsync();
        var directory = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "ai-conversation");
        Directory.CreateDirectory(directory);

        var card = page.Locator(CardSelector);
        var retry = page.Locator(RetrySelector);
        await retry.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await Microsoft.Playwright.Assertions.Expect(retry).ToBeEnabledAsync();
        await WaitForLayoutToSettleAsync(page, CardSelector);

        // Enabled: full opacity, the pointer cursor, and the hover fill the disabled control must lose.
        var enabled = await ReadPaintAsync(page, RetrySelector);
        Assert.AreEqual("1", enabled.Opacity, "the enabled retry paints at full opacity");
        Assert.AreEqual("pointer", enabled.Cursor, "the enabled retry invites the click");

        await retry.HoverAsync();
        var enabledHovered = await ReadPaintAsync(page, RetrySelector);
        Assert.AreNotEqual(enabled.Background, enabledHovered.Background,
            "the enabled retry fills on hover — the affordance the disabled control must not fake");
        await MovePointerAwayAsync(page);

        // The version switcher shows 1/2: Previous is the disabled version button, Next stays enabled.
        var previous = page.Locator(PreviousSelector);
        await Microsoft.Playwright.Assertions.Expect(previous).ToBeDisabledAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator(NextSelector)).ToBeEnabledAsync();

        var disabledPrevious = await ReadPaintAsync(page, PreviousSelector);
        Assert.AreEqual("0.5", disabledPrevious.Opacity,
            "the disabled version button dims through --tm-opacity-disabled (0.5), never a literal alpha");
        Assert.AreEqual("not-allowed", disabledPrevious.Cursor,
            "a refused click says so under the pointer, not with the default arrow");

        await previous.HoverAsync();
        var disabledPreviousHovered = await ReadPaintAsync(page, PreviousSelector);
        Assert.AreEqual(disabledPrevious.Background, disabledPreviousHovered.Background,
            "a disabled version button under the pointer keeps its resting surface — no hover fill");

        await page.Locator(NextSelector).HoverAsync();
        var enabledNextHovered = await ReadPaintAsync(page, NextSelector);
        Assert.AreNotEqual(disabledPreviousHovered.Background, enabledNextHovered.Background,
            "the enabled Next button keeps its hover fill — the disabled state is the edge, not the switcher");
        await MovePointerAwayAsync(page);

        await SetStickyHeaderVisibleAsync(page, false);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-disabled-states-enabled-light-1440.png"),
        });

        // Disabled: the shared dimming token, the refused-pointer cursor, the resting surface even under
        // the pointer. The click starts the few-seconds in-flight window the assertions read. The state
        // is aria-disabled, never the native disabled attribute — the button keeps its tab stop.
        await retry.ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(retry).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.IsNull(await retry.GetAttributeAsync("disabled"),
            "native disabled would drop the focus the reader just placed on the button (WCAG 2.4.3)");

        var disabled = await ReadPaintAsync(page, RetrySelector);
        Assert.AreEqual("1", disabled.Opacity,
            "the button BOX keeps full opacity — an opacity here would dim the focus outline with it " +
            "(WCAG 1.4.11), and the in-flight retry is the control that keeps the keyboard focus");
        Assert.AreEqual("0.5", disabled.ContentOpacity,
            "the dim lives on the content through --tm-opacity-disabled (0.5), never a literal alpha");
        Assert.AreEqual("not-allowed", disabled.Cursor,
            "a refused click says so under the pointer, not with the default arrow");

        await retry.HoverAsync();
        var disabledHovered = await ReadPaintAsync(page, RetrySelector);
        Assert.AreEqual(disabled.Background, disabledHovered.Background,
            "a disabled retry under the pointer keeps its resting surface — no hover fill");
        await MovePointerAwayAsync(page);

        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-disabled-states-disabled-light-1440.png"),
        });

        // The re-run completes: the call turns succeeded and the card's reset restores the failed sample.
        await Microsoft.Playwright.Assertions.Expect(card.Locator(".tm-tool-call--succeeded"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await page.Locator("[data-testid='ai-demo-retry-reset']").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(retry).ToBeEnabledAsync();

        // The dark pair of the same two states.
        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, CardSelector);
        await retry.ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(retry).ToHaveAttributeAsync("aria-disabled", "true");
        await MovePointerAwayAsync(page);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-disabled-states-disabled-dark-1440.png"),
        });

        await Microsoft.Playwright.Assertions.Expect(card.Locator(".tm-tool-call--succeeded"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await page.Locator("[data-testid='ai-demo-retry-reset']").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(retry).ToBeEnabledAsync();
        await MovePointerAwayAsync(page);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-disabled-states-enabled-dark-1440.png"),
        });
        await SetStickyHeaderVisibleAsync(page, true);

        AssertNoClientErrors();
    }

    /// <summary>
    /// Fáze 4d (remaining eeddc661): the in-flight retry keeps the KEYBOARD focus. The reader focuses
    /// Retry, presses Enter, and while the host's slow re-run is pending the button — now
    /// <c>aria-disabled="true"</c>, never natively disabled — is still <c>document.activeElement</c>
    /// (WCAG 2.4.3: a native disabled attribute would move focus to body the moment the re-run starts).
    /// Re-activating during the window (Enter, Space, a forced click — Playwright's actionability check
    /// refuses an aria-disabled button, itself proof the state is announced, so the force-click exercises
    /// the component's own guard) fires <c>OnRetry</c> only once: the demo counter the card renders stays
    /// at one. The fáze-4d review added the contrast half (WCAG 1.4.11): the dim lives on the button's
    /// CONTENT, so the focused in-flight retry keeps its focus ring at the full-contrast theme token
    /// (<c>outlineColor</c> rgb(37, 99, 235) in the light theme). And when the re-run succeeds, the
    /// focused Retry disappears with the failure — the card hands the focus to its disclosure toggle
    /// instead of letting it fall to <c>body</c> (the WaitForFunction below is that hand-off). The
    /// focused in-flight retry is screenshotted in light, dark and forced colors for the UX review (the
    /// focus ring must stay visible on the dimmed button).
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task InFlightRetry_KeepsKeyboardFocus_AndRefusesReactivation()
    {
        var page = await OpenPageAsync();
        var directory = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "ai-conversation");
        Directory.CreateDirectory(directory);

        var card = page.Locator(CardSelector);
        var retry = page.Locator(RetrySelector);
        var counter = page.Locator("[data-testid='ai-demo-slow-retry-count-value']");
        await retry.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLayoutToSettleAsync(page, CardSelector);

        // Focus the retry and activate it from the keyboard — the reader's path the fix protects.
        await retry.FocusAsync();
        Assert.AreEqual("OK", await ReadRetryFocusStateAsync(page),
            "the retry takes focus before the activation");
        await page.Keyboard.PressAsync("Enter");

        // The in-flight window is open: aria-disabled announces the state, the native disabled attribute
        // is NOT rendered, and the focus stayed on the button that was activated.
        await Microsoft.Playwright.Assertions.Expect(retry).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.IsNull(await retry.GetAttributeAsync("disabled"),
            "a natively disabled button leaves the tab order — the browser would move focus to body");
        Assert.AreEqual("OK", await ReadRetryFocusStateAsync(page),
            "the in-flight retry must still be document.activeElement (WCAG 2.4.3)");

        // The dimmed look is unchanged by the attribute move: the shared token on the CONTENT and the
        // refused cursor — while the button box keeps full opacity, so the focus ring of the focused
        // in-flight retry keeps the full-contrast theme token (WCAG 1.4.11; rgb(37, 99, 235) is the
        // light-theme --tm-border-color-focus → --tm-color-primary-600).
        var inFlight = await ReadPaintAsync(page, RetrySelector);
        Assert.AreEqual("1", inFlight.Opacity,
            "the button box stays undimmed — an opacity here would take the focus outline down with it");
        Assert.AreEqual("0.5", inFlight.ContentOpacity,
            "the in-flight retry still dims its content through --tm-opacity-disabled (0.5)");
        Assert.AreEqual("not-allowed", inFlight.Cursor,
            "the refused-pointer cursor survives the attribute move");
        Assert.AreEqual("rgb(37, 99, 235)",
            await retry.EvaluateAsync<string>("el => getComputedStyle(el).outlineColor"),
            "the focused in-flight retry paints the full-contrast focus ring — the dim moved off the box");

        // Re-activation attempts during the window: Enter, Space and a forced click. None may fire
        // OnRetry again — the demo counter is the observable. ToHaveTextAsync waits for the render the
        // forced click queues, so the exact count is read after the component settled.
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.PressAsync("Space");
        await retry.ClickAsync(new LocatorClickOptions { Force = true });
        // One reader action means one retry — the refusal guard swallowed every re-activation.
        await Microsoft.Playwright.Assertions.Expect(counter)
            .ToHaveTextAsync(new Regex(@"^\s*1\s*$"));
        Assert.AreEqual("OK", await ReadRetryFocusStateAsync(page),
            "the refused re-activations did not move the focus either");

        await SetStickyHeaderVisibleAsync(page, false);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-in-flight-retry-focus-light-1440.png"),
        });

        // The re-run completes, the reset restores the failed sample and zeroes the counter. The focused
        // Retry DISAPPEARS with the failure it re-ran: the card hands the focus to its disclosure toggle
        // (WCAG 2.4.3) — without the hand-off document.activeElement would be body here.
        await Microsoft.Playwright.Assertions.Expect(card.Locator(".tm-tool-call--succeeded"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        string focusDump;
        try
        {
            await page.WaitForFunctionAsync(
                """
                () => {
                    const active = document.activeElement;
                    return !!active
                        && active.classList.contains('tm-tool-call__toggle')
                        && !!active.closest("[data-testid='ai-disabled-states-demo']");
                }
                """,
                null,
                new PageWaitForFunctionOptions { Timeout = 5000 });
            focusDump = "OK";
        }
        catch (TimeoutException)
        {
            focusDump = await page.EvaluateAsync<string>(
                """
                () => {
                    const active = document.activeElement;
                    const toggle = document.querySelector("[data-testid='ai-disabled-states-demo'] .tm-tool-call__toggle");
                    const retry = document.querySelector("[data-testid='ai-disabled-states-demo'] .tm-tool-call__retry");
                    return 'active=' + (active ? active.tagName + '.' + active.className : 'null')
                        + ' | toggleExists=' + !!toggle
                        + ' | retryExists=' + !!retry;
                }
                """);
        }
        Assert.AreEqual("OK", focusDump,
            "the completed retry hands the focus to the card's disclosure toggle, not to body (WCAG 2.4.3)");
        await page.Locator("[data-testid='ai-demo-retry-reset']").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(retry).ToBeEnabledAsync();
        // The reset replays the sample from a clean count.
        await Microsoft.Playwright.Assertions.Expect(counter)
            .ToHaveTextAsync(new Regex(@"^\s*0\s*$"));

        // The dark pass: the same focus retention, the same screenshot.
        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, CardSelector);
        await retry.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Microsoft.Playwright.Assertions.Expect(retry).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.AreEqual("OK", await ReadRetryFocusStateAsync(page),
            "dark mode changes nothing about the focus retention");
        await SetStickyHeaderVisibleAsync(page, false);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-in-flight-retry-focus-dark-1440.png"),
        });
        await Microsoft.Playwright.Assertions.Expect(card.Locator(".tm-tool-call--succeeded"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await page.Locator("[data-testid='ai-demo-retry-reset']").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(retry).ToBeEnabledAsync();
        await ToggleDarkModeAsync(page);

        // The forced-colors pass: the focus ring is the system Highlight there (the card opts out of the
        // automatic adjustment), so the ring on the GrayText button is the readable pair.
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ForcedColors = ForcedColors.Active });
        await WaitForLayoutToSettleAsync(page, CardSelector);
        await retry.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Microsoft.Playwright.Assertions.Expect(retry).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.AreEqual("OK", await ReadRetryFocusStateAsync(page),
            "forced colors change nothing about the focus retention");
        await SetStickyHeaderVisibleAsync(page, false);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-in-flight-retry-focus-forced-colors-1440.png"),
        });
        await SetStickyHeaderVisibleAsync(page, true);

        AssertNoClientErrors();
    }

    /// <summary>
    /// The forced-colors half of the contract: both disabled controls sit inside surfaces that opt out of
    /// the automatic adjustment (<c>forced-color-adjust: none</c>), so the disabled state asks for the
    /// system <c>GrayText</c> explicitly — without it a disabled control in Windows high contrast reads
    /// exactly like an enabled one. The probe span carries <c>color: GrayText</c> inline, so the assertion
    /// compares each control against the system colour itself, not a guessed palette value. The same pass
    /// pins the FULL opacity the forced-colors rules keep (a dim over a system colour would only lower its
    /// guaranteed contrast) and records the card as
    /// <c>ai-conversation-disabled-states-disabled-forced-colors-1440.png</c>.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task DisabledStates_InForcedColors_UseTheSystemDisabledColor()
    {
        var page = await OpenPageAsync();
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ForcedColors = ForcedColors.Active });

        var card = page.Locator(CardSelector);
        var retry = page.Locator(RetrySelector);
        await retry.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        var enabledRetryColor = await retry.EvaluateAsync<string>("el => getComputedStyle(el).color");
        var enabledNextColor = await page.Locator(NextSelector)
            .EvaluateAsync<string>("el => getComputedStyle(el).color");

        // The click starts the in-flight window; the probes and the screenshot read the disabled state.
        await retry.ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(retry).ToHaveAttributeAsync("aria-disabled", "true");

        var retryProbe = await ReadSystemDisabledColorAsync(page, RetrySelector);
        Assert.AreEqual(retryProbe.System, retryProbe.Element,
            "the disabled retry paints the system GrayText inside its forced-color-adjust:none surface");
        Assert.AreNotEqual(enabledRetryColor, retryProbe.Element,
            "high contrast must tell the disabled retry from an enabled one");

        var previousProbe = await ReadSystemDisabledColorAsync(page, PreviousSelector);
        Assert.AreEqual(previousProbe.System, previousProbe.Element,
            "the disabled version button paints the system GrayText inside the opted-out switcher surface");
        Assert.AreNotEqual(enabledNextColor, previousProbe.Element,
            "high contrast must tell the disabled Previous from the enabled Next");

        // Both disabled controls keep FULL opacity: a dim over a system colour would lower the contrast
        // the system itself guarantees (the state is GrayText alone). The retry's dim lives on its
        // content children, so the forced-colors override drops it there.
        var retryOpacity = await retry.EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.AreEqual("1", retryOpacity,
            "forced colors drop the resting dim — opacity over GrayText would only lower its contrast");
        var retryContentOpacity = await retry.EvaluateAsync<string>(
            "el => getComputedStyle(el.querySelector(':scope > span')).opacity");
        Assert.AreEqual("1", retryContentOpacity,
            "the content dim is dropped in forced colors as well — GrayText alone carries the state");
        var previousOpacity = await page.Locator(PreviousSelector)
            .EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.AreEqual("1", previousOpacity,
            "forced colors drop the resting dim — opacity over GrayText would only lower its contrast");

        var directory = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "ai-conversation");
        Directory.CreateDirectory(directory);
        await MovePointerAwayAsync(page);
        await SetStickyHeaderVisibleAsync(page, false);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "ai-conversation-disabled-states-disabled-forced-colors-1440.png"),
        });
        await SetStickyHeaderVisibleAsync(page, true);

        AssertNoClientErrors();
    }

    /// <summary>The computed paint of one control of the disabled-states card: the box itself plus the
    /// first content child (the retry dims its CONTENT since the fáze-4d review, so the box and the
    /// content are read separately — an empty <c>ContentOpacity</c> means the control has no
    /// <c>&gt; span</c> child, e.g. the icon-only version buttons).</summary>
    private static async Task<ControlPaint> ReadPaintAsync(IPage page, string selector)
    {
        var json = await page.EvaluateAsync<string>(
            """
            (selector) => {
                const el = document.querySelector(selector);
                const style = getComputedStyle(el);
                const content = el.querySelector(':scope > span');
                return JSON.stringify({
                    opacity: style.opacity,
                    contentOpacity: content ? getComputedStyle(content).opacity : '',
                    cursor: style.cursor,
                    background: style.backgroundColor,
                });
            }
            """, selector);
        using var document = JsonDocument.Parse(json);
        return new ControlPaint(
            document.RootElement.GetProperty("opacity").GetString() ?? "",
            document.RootElement.GetProperty("contentOpacity").GetString() ?? "",
            document.RootElement.GetProperty("cursor").GetString() ?? "",
            document.RootElement.GetProperty("background").GetString() ?? "");
    }

    /// <summary>
    /// The element's computed colour next to an injected <c>span{color:GrayText}</c> probe: the mode does
    /// not remap system colours, so the probe reads the real system value to compare against.
    /// </summary>
    private static async Task<SystemColorProbe> ReadSystemDisabledColorAsync(IPage page, string selector)
    {
        var json = await page.EvaluateAsync<string>(
            """
            (selector) => {
                const el = document.querySelector(selector);
                const probe = document.createElement('span');
                probe.style.color = 'GrayText';
                el.parentElement.appendChild(probe);
                const system = getComputedStyle(probe).color;
                probe.remove();
                return JSON.stringify({ element: getComputedStyle(el).color, system });
            }
            """, selector);
        using var document = JsonDocument.Parse(json);
        return new SystemColorProbe(
            document.RootElement.GetProperty("element").GetString() ?? "",
            document.RootElement.GetProperty("system").GetString() ?? "");
    }

    private static Task MovePointerAwayAsync(IPage page)
        => page.Mouse.MoveAsync(0, 0);

    /// <summary>"OK" while the retry button is <c>document.activeElement</c>, otherwise a description of
    /// what actually holds the focus (for the failure message).</summary>
    private static Task<string> ReadRetryFocusStateAsync(IPage page)
        => page.EvaluateAsync<string>(
            """
            () => {
                const active = document.activeElement;
                if (!active) {
                    return 'none';
                }

                return active.classList.contains('tm-tool-call__retry')
                    ? 'OK'
                    : (active.tagName + '.' + active.className);
            }
            """);

    private static Task SetStickyHeaderVisibleAsync(IPage page, bool visible)
        => page.EvaluateAsync(
            """
            (visible) => {
                const header = document.querySelector('[data-demo-shell="main"] > .sticky');
                if (header) {
                    header.style.visibility = visible ? '' : 'hidden';
                }
            }
            """, visible);

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("repo root not found");
    }

    private sealed record ControlPaint(string Opacity, string ContentOpacity, string Cursor, string Background);

    private sealed record SystemColorProbe(string Element, string System);
}
