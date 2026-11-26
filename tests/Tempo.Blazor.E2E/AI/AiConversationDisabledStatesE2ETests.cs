using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace Tempo.Blazor.E2E.AI;

/// <summary>
/// E2E of the unified disabled idiom of the AI package (plan b75365fb fáze 4b, EmusCz 2.13.0 remaining
/// 463b8c0b) on the <c>/ai-conversation</c> demo page's disabled-states card (WASM @ 7106): the retry of
/// the failed sample call stays in flight for a few seconds, so the spec reads its computed look enabled
/// and then disabled — opacity 1 → the shared <c>--tm-opacity-disabled</c> (0.5), cursor pointer →
/// not-allowed, and the hover fill belongs to the enabled control only. The card's two-version answer
/// shows version 1 of 2, so its Previous button is the static disabled version button of the same idiom
/// (Next stays enabled, proving the disabled state is the edge, not the whole switcher). The forced-colors
/// pass compares both disabled controls against an injected <c>span{color:GrayText}</c> probe — system
/// colours are not remapped by the mode, so the probe reads the real system value — and pins the full
/// opacity the forced-colors rules keep. Screenshots land in <c>__screenshots__/ai-conversation/</c> for
/// the light/dark/forced-colors UX review.
/// </summary>
/// <remarks>
/// Split out of <c>AiConversationE2ETests.cs</c> so neither file breaches the 1200-line source ceiling
/// (N270/F13); the small helpers are duplicated per class, the repo's E2E pattern. Mutant guards: the
/// literal <c>opacity: 0.6</c> / <c>cursor: default</c> restored on either control, a plain
/// <c>:hover</c> without <c>:not(:disabled)</c>, or a dropped forced-colors GrayText/opacity rule each
/// turn one of these specs red.
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
        // the pointer. The click starts the few-seconds in-flight window the assertions read.
        await retry.ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(retry).ToBeDisabledAsync();

        var disabled = await ReadPaintAsync(page, RetrySelector);
        Assert.AreEqual("0.5", disabled.Opacity,
            "the disabled retry dims through --tm-opacity-disabled (0.5), never a literal alpha");
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
        await Microsoft.Playwright.Assertions.Expect(retry).ToBeDisabledAsync();
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
        await Microsoft.Playwright.Assertions.Expect(retry).ToBeDisabledAsync();

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
        // the system itself guarantees (the state is GrayText alone).
        var retryOpacity = await retry.EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.AreEqual("1", retryOpacity,
            "forced colors drop the resting dim — opacity over GrayText would only lower its contrast");
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

    /// <summary>The computed paint of one control of the disabled-states card.</summary>
    private static async Task<ControlPaint> ReadPaintAsync(IPage page, string selector)
    {
        var json = await page.EvaluateAsync<string>(
            """
            (selector) => {
                const el = document.querySelector(selector);
                const style = getComputedStyle(el);
                return JSON.stringify({
                    opacity: style.opacity,
                    cursor: style.cursor,
                    background: style.backgroundColor,
                });
            }
            """, selector);
        using var document = JsonDocument.Parse(json);
        return new ControlPaint(
            document.RootElement.GetProperty("opacity").GetString() ?? "",
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

    private sealed record ControlPaint(string Opacity, string Cursor, string Background);

    private sealed record SystemColorProbe(string Element, string System);
}
