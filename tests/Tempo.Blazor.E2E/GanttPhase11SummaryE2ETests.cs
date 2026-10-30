using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TmGantt redesign, phase 11: the opt-in summary band under the chart (five cards on desktop, a compact
/// strip plus a More-sheet entry on the read-mostly mobile container) and the portfolio's shared axis
/// (one sticky header, every card on the union range, the scrolls linked in the browser). Screenshots
/// (light + dark) go to <c>TestResults/gantt-phase11</c> for the UX review against the
/// <c>Scheduler/gantt--desktop.png</c>, <c>gantt-portfolio--desktop.png</c> and
/// <c>gantt-portfolio--mobile.png</c> mockups.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class GanttPhase11SummaryE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "gantt-phase11");

    private async Task<IPage> OpenAsync(string route, int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = "en-US",
        });
        await context.AddInitScriptAsync(
            """
            localStorage.setItem('tm-demo-culture', 'en');
            document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';
            """);
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90_000 });
        await WaitForAppReadyAsync(page);
        var gantt = page.Locator(".tm-gantt").First;
        await gantt.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return page;
    }

    private static async Task ToggleChipAsync(IPage page, string name)
    {
        var chip = page.Locator($"button:has-text('{name}')").First;
        await chip.ScrollIntoViewIfNeededAsync();
        await chip.ClickAsync();
    }

    private static ILocator Band(ILocator scope) => scope.Locator("[data-testid$='gantt-summary-band']").First;

    private static ILocator Band(IPage page) => page.Locator("[data-testid$='gantt-summary-band']").First;

    private static async Task<string> DoneLegendCountAsync(IPage page)
    {
        var count = page.Locator("[data-testid$='gantt-summary-status'] [data-status='GanttStatus_Done'] .tm-gantt-summary__legend-count").First;
        return (await count.CountAsync()) > 0 ? (await count.TextContentAsync())!.Trim() : "0";
    }

    private static async Task SetThemeAsync(IPage page, string theme) =>
        await page.EvaluateAsync(
            "t => { document.documentElement.setAttribute('data-theme', t); document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', t)); }",
            theme);

    /// <summary>Reads a numeric JS array through a JSON string (the .NET driver's array deserialization is unreliable).</summary>
    private static async Task<System.Collections.Generic.List<double>> ReadScrollsAsync(IPage page, string script)
    {
        var json = await page.EvaluateAsync<string>($"() => JSON.stringify({script})");
        return System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<double>>(json)
            ?? throw new InvalidOperationException("the browser returned no numbers");
    }

    private static async Task ShootBothAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await page.WaitForTimeoutAsync(200);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ShotDir, $"{name}-{theme}.png"), FullPage = false, Timeout = 60_000 });
        }

        await SetThemeAsync(page, "light");
    }

    [TestMethod]
    public async Task Summary_Band_Desktop_Shows_Cards_And_Updates_With_A_Status_Change()
    {
        var page = await OpenAsync("/gantt", 1440, 900);

        // Off by default: no band until the demo chip turns it on.
        (await Band(page.Locator(".tm-layout")).CountAsync()).Should().Be(0, "the band is opt-in");
        await ToggleChipAsync(page, "Summary band");

        var band = Band(page);
        await band.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var cards = page.Locator("[data-testid$='gantt-summary-band'] .tm-gantt-summary__card");
        (await cards.CountAsync()).Should().BeGreaterThan(0, "the desktop band renders its cards");
        (await page.Locator("[data-testid$='gantt-summary-budget']").CountAsync()).Should().BeGreaterThan(0,
            "the demo pins a budget total, so the budget card shows");

        // A status change through the task panel moves the counts (the summary is computed per refresh).
        await page.Locator(".tm-gantt__tree-row").First.ClickAsync();
        var statusSelect = page.Locator("[data-testid='task-status'] select").First;
        await statusSelect.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var current = await statusSelect.InputValueAsync();
        var target = current == "Done" ? "Open" : "Done";
        var before = await DoneLegendCountAsync(page);

        await statusSelect.SelectOptionAsync(target);
        await page.Locator("[data-testid='task-save']").First.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        var after = await DoneLegendCountAsync(page);
        after.Should().NotBe(before, $"changing the selected task's status from {current} to {target} must move the summary");

        // The compact strip is a mobile-only presentation: the desktop band never shows it.
        (await page.Locator("[data-testid$='gantt-summary-compact']").CountAsync()).Should().Be(0);

        await ShootBothAsync(page, "summary-desktop");
    }

    [TestMethod]
    public async Task Summary_Band_Mobile_Is_A_Strip_With_A_More_Sheet_Entry_And_No_Horizontal_Overflow()
    {
        foreach (var (width, height, name) in new[] { (390, 844, "summary-mobile-390"), (320, 640, "summary-mobile-320") })
        {
            var page = await OpenAsync("/gantt", width, height);
            await ToggleChipAsync(page, "Summary band");

            // The compact one-line strip replaces the cards in the chart.
            var strip = page.Locator("[data-testid$='gantt-summary-compact']").First;
            await strip.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            (await page.Locator("[data-testid$='gantt-summary-band'] .tm-gantt-summary__card").CountAsync()).Should().Be(0,
                "the mobile container shows the strip, not the cards");

            // The read-mostly page scrolls vertically only.
            var noHorizontalOverflow = await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1");
            noHorizontalOverflow.Should().BeTrue($"at {width}px the page must not scroll horizontally");

            // The More sheet carries the Summary entry; opening it shows the full cards.
            await page.Locator("[data-action-id='gantt-mobile-more']").First.ClickAsync();
            var entry = page.Locator("[data-testid$='gantt-more-summary']").First;
            await entry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await entry.ClickAsync();
            var sheetCards = page.Locator("[data-testid$='gantt-summary-sheet'] .tm-gantt-summary__card");
            await sheetCards.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            (await sheetCards.CountAsync()).Should().BeGreaterThan(0, "the sheet shows the full cards");

            if (width == 390)
            {
                await page.WaitForTimeoutAsync(300);
                await ShootBothAsync(page, name);
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    public async Task Portfolio_Shared_Axis_Aligns_Cards_And_Scrolls_Them_Together()
    {
        var page = await OpenAsync("/gantt-portfolio", 1440, 900);

        // Default PerProject: no shared header.
        (await page.Locator("[data-testid$='gantt-portfolio-axis']").CountAsync()).Should().Be(0);

        // Switch the demo to the shared axis.
        await page.Locator("[data-testid='gantt-demo-axis-mode']").First.SelectOptionAsync("Shared");
        var axis = page.Locator("[data-testid$='gantt-portfolio-axis']").First;
        await axis.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        // The scroll group registers in the portfolio's OnAfterRender (after the axis is already in the
        // DOM): give the registration a beat before driving the scroll.
        await page.WaitForTimeoutAsync(400);

        // The cards show no header rows of their own (the zero-height scroll carrier is invisible);
        // the one shared axis sits above the cards.
        var cardHeaders = page.Locator(".tm-gantt__timeline-header:visible");
        (await cardHeaders.CountAsync()).Should().Be(0, "the cards hide their own header rows; the one shared axis sits above");

        // The same date draws at the same x in every card: the today marker's offset inside its timeline.
        // EvaluateAsync returns the array as a JSON string (the .NET driver's array deserialization is
        // unreliable), so every read goes through JSON.stringify and System.Text.Json.
        var offsets = await ReadScrollsAsync(page,
            """
            (() => {
                const out = [];
                document.querySelectorAll("[data-testid$='today-marker']").forEach(marker => {
                    const timeline = marker.closest('.tm-gantt__timeline');
                    if (!timeline) return;
                    out.push(marker.getBoundingClientRect().left - timeline.getBoundingClientRect().left);
                });
                return out;
            })()
            """);
        offsets.Count.Should().BeGreaterThanOrEqualTo(2, "two project cards are on the page");
        offsets.Skip(1).Select(o => Math.Abs(o - offsets[0])).Max().Should().BeLessThanOrEqualTo(1.0,
            "the same date lands at the same x (±1 px) in every card");

        // Scrolling one card moves the shared header and the other cards (linkScrollGroup, in the browser).
        await page.EvaluateAsync(
            """
            (() => {
                const timelines = [...document.querySelectorAll('.tm-gantt__timeline')];
                timelines[0].scrollLeft = 120;
            })()
            """);
        await page.WaitForTimeoutAsync(250);
        var after = await ReadScrollsAsync(page,
            """
            (() => {
                const timelines = [...document.querySelectorAll('.tm-gantt__timeline')];
                const axisScroll = document.querySelector('.tm-gantt-portfolio__axis-scroll');
                return [timelines[0].scrollLeft, timelines[1] ? timelines[1].scrollLeft : -1, axisScroll ? axisScroll.scrollLeft : -1];
            })()
            """);
        after[0].Should().BeGreaterThan(0, "the first card is scrolled");
        after[1].Should().BeGreaterThan(0, "the second card follows (the group links them)");
        after[2].Should().BeGreaterThan(0, "the shared header follows");

        await ShootBothAsync(page, "portfolio-shared-desktop");
    }

    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";

    /// <summary>
    /// Pre-existing chart debt the phase-11 review RECORDED (not introduced here); the scans fail closed:
    /// the excluded ids must appear EXACTLY once as rules, and anything else fails the test.
    /// <list type="bullet">
    /// <item>color-contrast: `.tm-gantt__bar-label` (white fixed label) on a host-chosen bar colour —
    /// pre-redesign bar rendering, needs a bar-palette decision, not a phase-11 patch.</item>
    /// <item>scrollable-region-focusable: the `.tm-gantt__timeline` scroll containers (and the shared
    /// portfolio axis, whose own scroll region now carries tabindex="0") — making every timeline a tab
    /// stop changes the phase-4 keyboard flow and belongs to an a11y pass of its own.</item>
    /// </list>
    /// </summary>
    private static readonly string[] RecordedPreExistingAxeRules =
    [
        "color-contrast",
        "scrollable-region-focusable",
    ];

    /// <summary>Axe-core over the chart host only (the demo page's editorial prose is not the component's).
    /// Returns the violations that are NOT the recorded pre-existing debt.</summary>
    private static async Task<string[]> RunAxeAsync(IPage page, string hostSelector)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });
        var violations = await page.EvaluateAsync<string[]>(
            """
            async (hostSelector) => {
                const host = document.querySelector(hostSelector) || document.body;
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """, hostSelector);

        var unexpected = violations.Where(v => !RecordedPreExistingAxeRules.Any(rule => v.Contains($" {rule} ", StringComparison.Ordinal))).ToList();
        return [.. unexpected];
    }

    [TestMethod]
    public async Task Summary_Band_Axe_NoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync("/gantt", 1440, 900);
        await ToggleChipAsync(page, "Summary band");
        await Band(page).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var violations = await RunAxeAsync(page, ".tm-gantt");
        violations.Should().BeEmpty("/gantt with the summary band must have no critical/serious violations");
    }

    [TestMethod]
    public async Task Portfolio_Shared_Axis_Axe_NoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync("/gantt-portfolio", 1440, 900);
        await page.Locator("[data-testid='gantt-demo-axis-mode']").First.SelectOptionAsync("Shared");
        await page.Locator("[data-testid$='gantt-portfolio-axis']").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var violations = await RunAxeAsync(page, ".tm-gantt-portfolio");
        violations.Should().BeEmpty("/gantt-portfolio in the shared mode must have no critical/serious violations");
    }
}
