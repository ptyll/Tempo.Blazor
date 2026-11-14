using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 3e96f6d8 phase 5 — <c>TmContextUsage</c> in a real browser. The assertions read behaviour rather than
/// markup: the reading names the amount and the budget, the bar and the grid paint one segment / cell per section,
/// a section expands into its entries and a removed entry changes the page's own reading, a removed section with
/// entries hands the focus back to the panel root without raising the error UI, the compaction shrinks
/// the context and disables itself, the next turn crosses the budget and the panel says so, hovering a grid cell
/// and focusing a legend row highlight the same section, the keyboard reaches every action while the focus stays
/// inside the panel, the edge states say what they are, the phone never scrolls sideways and a scoped axe scan
/// finds no critical or serious violation. Reference screenshots (light, dark, phone) land in
/// <c>tests/Tempo.Blazor.E2E/TestResults/context-usage</c>.
/// </summary>
public abstract class ContextUsageE2EBase : PlaywrightTestBase
{
    private const string BasicSection = "[data-testid='context-usage-basic-section']";
    private const string VariantsSection = "[data-testid='context-usage-variants-section']";
    private const string EdgeSection = "[data-testid='context-usage-edge-section']";
    private const string Basic = "[data-testid='context-usage-basic']";
    private const string Grid = "[data-testid='context-usage-grid']";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "context-usage");

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static ILocator Row(IPage page, string key)
        => page.Locator($"{Basic} tr.tm-context-usage__row[data-section-key='{key}']");

    private static async Task<string> ReadoutAsync(IPage page)
        => ((await page.Locator("[data-testid='context-usage-readout']").TextContentAsync()) ?? string.Empty).Trim();

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1200, bool coarsePointer = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            HasTouch = coarsePointer,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/context-usage");
        await page.WaitForSelectorAsync(
            $"{Basic} [data-testid='context-usage-meter']",
            new PageWaitForSelectorOptions { Timeout = 30000 });
        // The panel's own data on screen, not a pause: every test in this base reads the meter's state, so the
        // first read must be of the finished render (and the layout settled for the geometry ones).
        await Assertions.Expect(page.Locator("[data-testid='context-usage-readout']")).ToContainTextAsync(
            "14,000 of 16,000 tokens", new LocatorAssertionsToContainTextOptions { Timeout = 30000 });
        // No page-wide settle right after a cold load: the phone test frames its own section and settles it.
        return page;
    }

    [TestMethod]
    public async Task TheReadingNamesTheAmountTheBudgetAndEverySection()
    {
        var page = await OpenAsync();

        StringAssert.Contains(await ReadoutAsync(page), "14,000 of 16,000 tokens");
        StringAssert.Contains(await ReadoutAsync(page), "88");

        var meter = page.Locator($"{Basic} [data-testid='context-usage-meter']");
        Assert.AreEqual("meter", await meter.GetAttributeAsync("role"));
        Assert.AreEqual("14000", await meter.GetAttributeAsync("aria-valuenow"));
        Assert.AreEqual("16000", await meter.GetAttributeAsync("aria-valuemax"));
        StringAssert.Contains(await meter.GetAttributeAsync("aria-valuetext"), "14,000 of 16,000 tokens used");
        Assert.AreEqual("false", await page.Locator(Basic).GetAttributeAsync("data-over-budget") ?? "false");

        Assert.AreEqual(6, await page.Locator($"{Basic} .tm-context-usage__bar .tm-context-usage__segment").CountAsync());
        Assert.AreEqual(6, await page.Locator($"{Basic} tr.tm-context-usage__row").CountAsync());
        StringAssert.Contains(await Row(page, "files").TextContentAsync(), "Files");
        StringAssert.Contains(await Row(page, "files").TextContentAsync(), "5,000");
        StringAssert.Contains(await Row(page, "system").TextContentAsync(), "Required");
        StringAssert.Contains(await Row(page, "memory").TextContentAsync(), "Summarized");
        StringAssert.Contains(await Row(page, "results").TextContentAsync(), "Omitted");
        StringAssert.Contains(await page.Locator($"{Basic} [data-testid='context-usage-cache']").TextContentAsync(), "62");
    }

    [TestMethod]
    public async Task ASectionExpandsIntoItsEntries_AndARemovedEntryChangesTheReading()
    {
        var page = await OpenAsync();

        await Row(page, "files").Locator(".tm-context-usage__toggle").ClickAsync();
        await page.WaitForSelectorAsync($"{Basic} tr.tm-context-usage__detail");
        Assert.AreEqual(2, await page.Locator($"{Basic} tr.tm-context-usage__detail .tm-context-usage__item").CountAsync());

        await page.Locator($"{Basic} tr.tm-context-usage__detail [data-item-id='contract'] .tm-context-usage__item-remove")
            .ClickAsync();

        await page.WaitForFunctionAsync(
            "document.querySelector(\"[data-testid='context-usage-readout']\").textContent.includes('10,000')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        Assert.AreEqual("1,000", await Row(page, "files").Locator(".tm-context-usage__tokens").TextContentAsync());
        StringAssert.Contains(
            await page.Locator("[data-testid='context-usage-log-entry']").First.TextContentAsync(),
            "Removed Contract.pdf from Files.");
    }

    [TestMethod]
    public async Task CompactingShrinksTheReading_AndDisablesItself()
    {
        var page = await OpenAsync();

        await page.Locator($"{Basic} [data-testid='context-usage-compact']").ClickAsync();

        await page.WaitForFunctionAsync(
            "document.querySelector(\"[data-testid='context-usage-readout']\").textContent.includes('4,560')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        StringAssert.Contains(
            await page.Locator("[data-testid='context-usage-log-entry']").First.TextContentAsync(),
            "Compacted the optional sections.");
        Assert.IsTrue(
            await page.Locator($"{Basic} [data-testid='context-usage-compact']").IsDisabledAsync(),
            "nothing optional is left to compact, so the action disables itself");
    }

    [TestMethod]
    public async Task TheNextTurnCrossesTheBudget_AndAResetRestoresIt()
    {
        var page = await OpenAsync();

        await page.Locator("[data-testid='context-usage-turn']").ClickAsync();

        await page.WaitForSelectorAsync($"{Basic} [data-testid='context-usage-over-budget']");
        Assert.AreEqual("true", await page.Locator(Basic).GetAttributeAsync("data-over-budget"));
        StringAssert.Contains(await ReadoutAsync(page), "16,500 of 16,000 tokens");
        StringAssert.Contains(await ReadoutAsync(page), "103");

        // ARIA forbids aria-valuenow above aria-valuemax, so the machine value stops at the budget while the
        // value text (what a screen reader reads) keeps the real total and the real percentage.
        var meter = page.Locator($"{Basic} [data-testid='context-usage-meter']");
        Assert.AreEqual("16000", await meter.GetAttributeAsync("aria-valuenow"));
        StringAssert.Contains(await meter.GetAttributeAsync("aria-valuetext"), "16,500");
        StringAssert.Contains(await meter.GetAttributeAsync("aria-valuetext"), "103");
        StringAssert.Contains(
            await page.Locator($"{Basic} [data-testid='context-usage-over-budget']").TextContentAsync(),
            "Over budget by 500");
        Assert.AreEqual(
            "The context window is over its budget.",
            (await page.Locator($"{Basic} [data-testid='context-usage-announcement']").TextContentAsync())?.Trim());

        await page.Locator("[data-testid='context-usage-reset']").ClickAsync();
        await page.WaitForFunctionAsync(
            "document.querySelector(\"[data-testid='context-usage-readout']\").textContent.includes('14,000')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        Assert.IsNull(await page.Locator(Basic).GetAttributeAsync("data-over-budget"));
    }

    [TestMethod]
    public async Task HoveringACellAndFocusingARowHighlightTheSameSection()
    {
        var page = await OpenAsync();

        // The grid cell at index 20 is inside the Files span of the budget.
        await page.Locator($"{Grid} [data-testid='context-usage-cell']").Nth(20).HoverAsync();
        await Assertions.Expect(page.Locator($"{Grid} tr.tm-context-usage__row[data-section-key='files']")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("tm-context-usage__row--highlight"),
            new LocatorAssertionsToHaveClassOptions { Timeout = 5000 });

        await page.Locator($"{Grid} tr.tm-context-usage__row[data-section-key='tools'] .tm-context-usage__toggle").FocusAsync();
        // The focus path paints the cells of the focused row, so the count of highlighted cells is the state.
        await Assertions.Expect(page.Locator($"{Grid} .tm-context-usage__cell--highlight[data-section-key='tools']"))
            .Not.ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
    }

    [TestMethod]
    public async Task TheKeyboardReachesEveryAction_AndTheFocusStaysInsideThePanel()
    {
        var page = await OpenAsync();

        await Row(page, "files").Locator(".tm-context-usage__toggle").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForSelectorAsync($"{Basic} tr.tm-context-usage__detail");

        var remove = page.Locator($"{Basic} tr.tm-context-usage__detail [data-item-id='contract'] .tm-context-usage__item-remove");
        await remove.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await page.WaitForFunctionAsync(
            "document.querySelector(\"[data-testid='context-usage-readout']\").textContent.includes('10,000')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        var focusInside = await page.EvaluateAsync<bool>(
            """
            () => {
                const root = document.querySelector("[data-testid='context-usage-basic']");
                return !!document.activeElement && root.contains(document.activeElement);
            }
            """);
        Assert.IsTrue(focusInside, "the pressed control disappeared with the removal, so the focus is handed back");
    }

    [TestMethod]
    public async Task RemovingASectionWithEntries_HandsTheFocusToThePanelRoot_WithoutAnError()
    {
        var page = await OpenAsync();

        // Files both HAS entries and is removable, so the pressed section-level Remove button leaves WITH the
        // section's own disclosure button. In a real browser a focus() on a target that has just left the page
        // is rejected ("Unable to focus an invalid element" → JSException); unhandled it raises #blazor-error-ui
        // on WASM and tears down a Server circuit, which is exactly what this walks.
        await page.Locator($"{Basic} tr.tm-context-usage__row[data-section-key='files'] .tm-context-usage__remove")
            .ClickAsync();

        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='context-usage-basic'] tr.tm-context-usage__row[data-section-key='files']\").length === 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        // The pressed button AND the section's disclosure left the DOM with the removal, so the hand-off to the
        // panel root is itself the state to wait for — a fixed pause read the focus before it moved.
        await page.WaitForFunctionAsync(
            "() => !!document.activeElement && document.activeElement.getAttribute('data-testid') === 'context-usage-basic'",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });

        var focusedTestId = await page.EvaluateAsync<string?>(
            "() => document.activeElement && document.activeElement.getAttribute('data-testid')");
        Assert.AreEqual("context-usage-basic", focusedTestId,
            "the pressed control and the section's disclosure button are gone, so the focus is handed to the panel root");

        var errorUiVisible = await page.EvaluateAsync<bool>(
            "() => { const e = document.getElementById('blazor-error-ui'); return !!e && getComputedStyle(e).display !== 'none'; }");
        Assert.IsFalse(errorUiVisible, "a rejected focus move must never reach #blazor-error-ui");

        StringAssert.Contains(
            await page.Locator("[data-testid='context-usage-log-entry']").First.TextContentAsync(),
            "Removed the Files section.");
    }

    [TestMethod]
    public async Task TheEdgeStatesSayWhatTheyAre()
    {
        var page = await OpenAsync();

        StringAssert.Contains(
            await page.Locator("[data-testid='context-usage-edge-empty'] [data-testid='context-usage-empty']").TextContentAsync(),
            "No context usage is reported.");
        Assert.AreEqual(
            "true",
            await page.Locator("[data-testid='context-usage-edge-over']").GetAttributeAsync("data-over-budget"));
        StringAssert.Contains(
            await page.Locator("[data-testid='context-usage-edge-over'] [data-testid='context-usage-over-budget']").TextContentAsync(),
            "600");
        Assert.AreEqual(
            "0",
            await page.Locator("[data-testid='context-usage-edge-unknown'] [data-testid='context-usage-meter']")
                .GetAttributeAsync("aria-valuemax"));
        Assert.AreEqual(
            0,
            await page.Locator("[data-testid='context-usage-edge-no-cache'] [data-testid='context-usage-cache']").CountAsync());
    }

    [TestMethod]
    public async Task ThePhoneKeepsThePanelInsideTheViewport_AndTheLegendReadable()
    {
        var page = await OpenAsync(375, 812, coarsePointer: true);

        await page.Locator(BasicSection).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, BasicSection);

        var overflow = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.IsFalse(overflow, "the panel must never make the page scroll sideways");

        // The label COLUMN (not the shrink-to-fit button) is what must stay readable: the numeric columns and
        // the 44 px Remove target take their own room and the label gets the rest.
        var column = await page.Locator($"{Basic} tr.tm-context-usage__row[data-section-key='files'] th.tm-context-usage__section")
            .BoundingBoxAsync();
        Assert.IsNotNull(column);
        Assert.IsTrue(column!.Width >= 140, $"the section label column keeps a readable width on a phone (was {column.Width})");

        var toggle = await page.Locator($"{Basic} tr.tm-context-usage__row[data-section-key='files'] .tm-context-usage__toggle")
            .BoundingBoxAsync();
        Assert.IsNotNull(toggle);
        Assert.IsTrue(toggle!.Height >= 44, $"a coarse pointer gets a 44 px target on the disclosure (was {toggle.Height})");

        // The entries of a section stay readable on a phone as well.
        await Row(page, "files").Locator(".tm-context-usage__toggle").TapAsync();
        await page.WaitForSelectorAsync($"{Basic} tr.tm-context-usage__detail");
    }

    [TestMethod]
    public async Task ScopedAxeScan_HasNoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        // Scan with a section expanded: the disclosure row and its Remove actions are the new surface.
        await Row(page, "files").Locator(".tm-context-usage__toggle").ClickAsync();
        await page.WaitForSelectorAsync($"{Basic} tr.tm-context-usage__detail");

        foreach (var testId in new[] { "context-usage-basic-section", "context-usage-variants-section", "context-usage-edge-section" })
        {
            var violations = await page.EvaluateAsync<string[]>(
                """
                async (selector) => {
                    const host = document.querySelector(selector);
                    // axe samples the RESOLVED colours: a finite CSS animation/transition still running (the demo's fade-in, a colour
                    // transition, the segmented indicator) paints a blended, low-contrast frame. Wait for them (looping ones,
                    // like the skeleton shimmer, never finish and are not waited for).
                    await Promise.all(document.getAnimations()
                        .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                        .map(a => a.finished.catch(() => { })));
                    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                    const result = await axe.run(host, {
                        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                        resultTypes: ['violations']
                    });
                    return result.violations
                        .filter(v => v.impact === 'critical' || v.impact === 'serious')
                        .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).slice(0, 4).join('; ')})`);
                }
                """,
                $"[data-testid='{testId}']");
            Assert.AreEqual(0, violations.Length, $"axe [{testId}]: " + string.Join(" | ", violations));
        }
    }

    [TestMethod]
    public async Task ReferenceScreenshots_LightDarkAndPhone()
    {
        var page = await OpenAsync();
        await page.Locator(BasicSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(page, "context-usage-basic-light-1440", BasicSection);

        await page.Locator(VariantsSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(page, "context-usage-variants-light-1440", VariantsSection);

        await page.Locator(EdgeSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(page, "context-usage-edge-light-1440", EdgeSection);

        await ToggleThemeAsync(page);
        await page.Locator(BasicSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(page, "context-usage-basic-dark-1440", BasicSection);
        await page.Locator(EdgeSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(page, "context-usage-edge-dark-1440", EdgeSection);

        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await phone.Locator(BasicSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(phone, "context-usage-basic-light-375", BasicSection);
        await Row(phone, "files").Locator(".tm-context-usage__toggle").TapAsync();
        await phone.WaitForSelectorAsync($"{Basic} tr.tm-context-usage__detail");
        await ShootAsync(phone, "context-usage-expanded-light-375", BasicSection);
        await ToggleThemeAsync(phone);
        await ShootAsync(phone, "context-usage-expanded-dark-375", BasicSection);
    }

    /// <summary>
    /// Switches the demo's own theme (the header toggle), so the whole frame repaints. The wait is on the
    /// shell's own theme signature plus the finished colour transition — a fixed pause read the card's
    /// background mid-fade, i.e. a colour no reader ever sees.
    /// </summary>
    private static async Task ToggleThemeAsync(IPage page)
    {
        var before = await page.EvaluateAsync<string?>(
            "() => { const shell = document.querySelector('.demo-shell'); return shell ? `${shell.getAttribute('data-theme')}|${shell.classList.contains('dark')}` : null; }");
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForFunctionAsync(
            """
            (before) => {
                const shell = document.querySelector('.demo-shell');
                return !!shell && `${shell.getAttribute('data-theme')}|${shell.classList.contains('dark')}` !== before;
            }
            """,
            before,
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await WaitForLayoutToSettleAsync(page, ".demo-shell");
    }

    /// <summary>Frames the section that is being reviewed, in the viewport, once its layout has settled.</summary>
    private async Task ShootAsync(IPage page, string name, string selector)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await WaitForLayoutToSettleAsync(page, selector);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{name}.png"),
            FullPage = false,
        });
    }
}

/// <summary>The WASM lane (the demo default).</summary>
[TestClass]
[TestCategory("WASM")]
public class ContextUsageE2ETests : ContextUsageE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}

/// <summary>
/// The Server lane (plan 7ba95ed6, Fáze 11): the same <c>TmContextUsage</c> panel over a real Blazor Server
/// circuit. The reading's ARIA contract, the segment/grid/cell highlight walk, the section removal that hands
/// the focus back instead of raising <c>#blazor-error-ui</c> (a rejected focus move tears a circuit down, so
/// this is the lane that can prove the guard), the compaction, the over-budget turn, the keyboard walk, the
/// edge states, the phone geometry and the scoped axe scan are all inherited.
/// </summary>
[TestClass]
[TestCategory("Server")]
public class ContextUsageServerE2ETests : ContextUsageE2EBase
{
    protected override string BaseUrl => "https://localhost:7107";
}

/// <summary>
/// The InteractiveAuto lane: the same panel on the host that starts on the Server circuit and finishes on
/// WebAssembly, so both runtimes and the handover between them see the same expectations.
/// </summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public class ContextUsageInteractiveAutoE2ETests : ContextUsageE2EBase
{
    protected override string BaseUrl => "https://localhost:7108";
}
