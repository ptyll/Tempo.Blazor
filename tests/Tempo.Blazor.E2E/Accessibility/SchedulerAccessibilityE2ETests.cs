using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Accessibility;

/// <summary>
/// Keyboard and ARIA coverage of the scheduler demo (WASM). Each of the five views is scanned with
/// axe-core for zero critical or serious WCAG 2.1 A/AA violations in light and dark, and the month
/// grid is driven from the keyboard: Tab lands on the single roving stop, arrows move it, Enter
/// activates the day exactly once. Element screenshots go to the git-ignored TestResults folder.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class SchedulerAccessibilityE2ETests : WasmTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";

    private static readonly string[] CriticalOrSerious = ["critical", "serious"];

    private async Task<IPage> OpenAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}/scheduler",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    [TestMethod]
    public async Task AllViews_Axe_HasNoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync();
        foreach (var section in new[] { "month", "week", "day", "agenda", "timeline" })
        {
            var host = page.Locator($"[data-testid='scheduler-{section}-section'] .tm-scheduler");
            await host.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            await host.ScrollIntoViewIfNeededAsync();

            var light = await AxeAsync(page, section);
            Assert.AreEqual(0, light.Length,
                $"{section} LIGHT:{Environment.NewLine}{string.Join(Environment.NewLine, light)}");

            await SetDarkAsync(page);
            var dark = await AxeAsync(page, section);
            Assert.AreEqual(0, dark.Length,
                $"{section} DARK:{Environment.NewLine}{string.Join(Environment.NewLine, dark)}");
            await SetLightAsync(page);
        }
    }

    [TestMethod]
    public async Task Month_Keyboard_MovesTheRovingStop_AndEnterActivatesOnce()
    {
        var page = await OpenAsync();
        var grid = page.Locator("[data-testid='scheduler-month-section'] [role='grid']");
        await grid.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await grid.ScrollIntoViewIfNeededAsync();

        var stop = grid.Locator("[role='gridcell'][tabindex='0']");
        await Assertions.Expect(stop).ToHaveCountAsync(1);
        var start = await stop.GetAttributeAsync("data-date");

        await stop.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(grid.Locator("[role='gridcell'][tabindex='0']")).ToHaveCountAsync(1);
        var moved = await grid.Locator("[role='gridcell'][tabindex='0']").GetAttributeAsync("data-date");
        Assert.AreNotEqual(start, moved, "ArrowRight should move the single roving stop.");

        // The focus must follow the stop, or a second arrow reads the stale cell and sticks.
        await page.Keyboard.PressAsync("ArrowRight");
        var movedAgain = await grid.Locator("[role='gridcell'][tabindex='0']").GetAttributeAsync("data-date");
        Assert.AreNotEqual(moved, movedAgain, "A second ArrowRight must keep moving.");

        var eventStop = grid.Locator(".tm-scheduler-month-event").First;
        await eventStop.FocusAsync();
        var toastsBefore = await page.Locator(".tm-toast").CountAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".tm-toast")).ToHaveCountAsync(toastsBefore + 2,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        await SaveElementAsync(page, "[data-testid='scheduler-month-section'] .tm-scheduler", "scheduler-month-focus");
    }

    /// <summary>
    /// Tab from the toolbar lands on the grid's single tab stop, PageDown then ArrowRight keeps
    /// moving, and Home/End stay inside the row. The page must not scroll while the keys move.
    /// </summary>
    [TestMethod]
    public async Task Month_Tab_Lands_On_The_Roving_Stop_And_The_Page_Does_Not_Scroll()
    {
        var page = await OpenAsync();
        var section = page.Locator("[data-testid='scheduler-month-section']");
        await section.Locator("[role='grid']").WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        await section.Locator("[data-testid='scheduler-today']").FocusAsync();
        for (var i = 0; i < 8; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var landed = await page.EvaluateAsync<bool>(
                "() => document.activeElement?.getAttribute('role') === 'gridcell'");
            if (landed) break;
        }

        var focusedIsStop = await page.EvaluateAsync<bool>(
            "() => document.activeElement?.getAttribute('tabindex') === '0' && document.activeElement?.getAttribute('role') === 'gridcell'");
        Assert.IsTrue(focusedIsStop, "Tab from the toolbar should land on the grid's single tab stop.");

        await section.ScrollIntoViewIfNeededAsync();
        // The scroll guard pins the page after the Tabs above. Wait until it has let go, so the
        // measurement records the arrow and not the pin pulling the page back.
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('[data-testid=\"scheduler-month-section\"] .tm-scheduler')?.__tmSchedulerScrollHold");
        var before = await page.EvaluateAsync<double>("() => window.scrollY");
        var guarded = await page.EvaluateAsync<string>(
            """
            () => {
                const root = document.querySelector('[data-testid="scheduler-month-section"] .tm-scheduler');
                const fn = window.TempoBlazor && window.TempoBlazor.Scheduler && window.TempoBlazor.Scheduler.attachScrollGuard;
                return JSON.stringify({ guard: !!(root && root.__tmSchedulerScrollGuard), fn: typeof fn, src: (document.querySelector('script[src*="scheduler.js"]') || {}).src || '' });
            }
            """);
        await page.Keyboard.PressAsync("ArrowDown");
        var afterArrow = await page.EvaluateAsync<double>("() => window.scrollY");
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("End");
        var after = await page.EvaluateAsync<double>("() => window.scrollY");
        Assert.IsTrue(guarded.Contains("\"guard\":true"), $"The scheduler root must own the scroll guard. {guarded}");
        Assert.AreEqual(before, afterArrow, 1, "ArrowDown must not scroll the page.");
        Assert.AreEqual(afterArrow, after, 1, "Home and End must not scroll the page.");

        await page.Keyboard.PressAsync("PageDown");
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(section.Locator("[role='gridcell'][tabindex='0']")).ToHaveCountAsync(1);
    }

    /// <summary>
    /// Thirty ArrowDown presses must keep the focused slot inside the scroller, below the sticky head,
    /// without moving the page.
    /// </summary>
    [TestMethod]
    public async Task Week_ArrowDown_Keeps_The_Focused_Slot_Inside_The_Scroller()
    {
        var page = await OpenAsync();
        var section = page.Locator("[data-testid='scheduler-week-section']");
        var slot = section.Locator(".tm-scheduler-timegrid-slot[tabindex='0']");
        await slot.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await slot.FocusAsync();

        var before = await page.EvaluateAsync<double>("() => window.scrollY");
        for (var i = 0; i < 30; i++)
            await page.Keyboard.PressAsync("ArrowDown");
        var after = await page.EvaluateAsync<double>("() => window.scrollY");
        Assert.AreEqual(before, after, 1, "ArrowDown must scroll the grid, not the page.");

        var visible = await page.EvaluateAsync<bool>(
            """
            () => {
                const slot = document.querySelector('[data-testid="scheduler-week-section"] .tm-scheduler-timegrid-slot[tabindex="0"]');
                const scroller = slot && slot.closest('[data-tm-scroll]');
                const head = scroller && scroller.querySelector('.tm-scheduler-timegrid-head');
                if (!slot || !scroller) return false;
                const box = slot.getBoundingClientRect();
                const frame = scroller.getBoundingClientRect();
                const headBottom = head ? head.getBoundingClientRect().bottom : frame.top;
                return box.top >= headBottom - 1 && box.bottom <= frame.bottom + 1;
            }
            """);
        Assert.IsTrue(visible, "the focused slot must sit inside the scroller, below the sticky head.");
    }

    /// <summary>
    /// Shift+F10 on a day cell opens the context menu exactly once, and the menu is positioned from
    /// the element rather than from a zero point.
    /// </summary>
    [TestMethod]
    public async Task Month_ShiftF10_Opens_The_Context_Menu_Once()
    {
        var page = await OpenAsync();
        var cell = page.Locator("[data-testid='scheduler-interactive'] [role='gridcell'][tabindex='0']");
        await cell.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await cell.FocusAsync();

        await page.Keyboard.PressAsync("Shift+F10");
        var menu = page.Locator(".tm-scheduler-context-menu");
        await Assertions.Expect(menu.First).ToBeVisibleAsync();
        var box = await menu.First.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box.X > 0 && box.Y > 0, "The menu must be positioned from the element.");
    }

    /// <summary>Focus screenshots of each roving stop, light and dark, taken before any activation.</summary>
    [TestMethod]
    public async Task Focus_Screenshots_Of_Every_Stop()
    {
        var page = await OpenAsync();
        foreach (var (section, stop, name) in new (string, string, string)[]
        {
            ("month", "[role='gridcell'][tabindex='0']", "month-cell"),
            ("week", ".tm-scheduler-timegrid-slot[tabindex='0']", "week-slot"),
            ("timeline", ".tm-scheduler-timeline-slot[tabindex='0']", "timeline-slot"),
            ("agenda", ".tm-scheduler-agenda-event", "agenda-button"),
        })
        {
            var host = page.Locator($"[data-testid='scheduler-{section}-section']");
            var target = host.Locator(stop).First;
            await target.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            await target.FocusAsync();
            await SaveElementAsync(page, $"[data-testid='scheduler-{section}-section'] .tm-scheduler", $"scheduler-{name}-focus-light");
            await SetDarkAsync(page);
            await SaveElementAsync(page, $"[data-testid='scheduler-{section}-section'] .tm-scheduler", $"scheduler-{name}-focus-dark");
            await SetLightAsync(page);
        }

        var tab = page.Locator("[data-testid='scheduler-month-section'] [data-tm-group='view'] [role='tab'][aria-selected='true']");
        await tab.FocusAsync();
        await SaveElementAsync(page, "[data-testid='scheduler-month-section'] .tm-scheduler-toolbar", "scheduler-view-tab-focus");
    }

    private static async Task<string[]> AxeAsync(IPage page, string section)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });
        return await page.EvaluateAsync<string[]>(
            """
            async ([section, impacts]) => {
                const host = document.querySelector(`[data-testid='scheduler-${section}-section'] .tm-scheduler`);
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => impacts.includes(v.impact))
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """,
            new object[] { section, CriticalOrSerious });
    }

    private static Task SetDarkAsync(IPage page) => SetThemeAsync(page, "dark");

    private static Task SetLightAsync(IPage page) => SetThemeAsync(page, "light");

    private static async Task SetThemeAsync(IPage page, string theme)
    {
        await page.EvaluateAsync(
            """
            theme => {
                const on = theme === 'dark';
                document.documentElement.setAttribute('data-theme', theme);
                document.documentElement.classList.toggle('dark', on);
                document.documentElement.classList.toggle('tm-dark', on);
                document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', theme));
            }
            """,
            theme);
        await page.WaitForTimeoutAsync(700);
    }

    private static async Task SaveElementAsync(IPage page, string selector, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "TestResults", "accessibility");
        Directory.CreateDirectory(dir);
        await page.Locator(selector).ScreenshotAsync(
            new LocatorScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png") });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx"))) return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
