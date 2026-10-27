using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TmGantt redesign, phase 2: the desktop visual layer. Computed-style assertions prove that the tokens
/// (dark mode, the indigo theme) really reach the Gantt - neither bUnit nor the token audit can see that -
/// and the screenshot matrix (main surface, task panel, workload, portfolio) is written for
/// light / dark x default / indigo at desktop 1440. Committed shots are rewritten only with
/// <c>TM_E2E_UPDATE_BASELINE=1</c>; otherwise they land in <c>TestResults/gantt/phase2</c>.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class GanttThemeE2ETests : WasmTestBase
{
    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ShotPath(string name)
    {
        var directory = Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E",
            UpdateBaseline ? Path.Combine("__screenshots__", "gantt", "phase2") : Path.Combine("TestResults", "gantt", "phase2"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{name}.png");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("TempoBlazor.slnx not found above the test output.");
    }

    private async Task<IPage> OpenAsync(string route, bool indigo, bool dark)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            IgnoreHTTPSErrors = true,
            Locale = "en-US",
        });
        await context.AddInitScriptAsync(indigo
            ? "localStorage.setItem('tm-demo-culture', 'en'); localStorage.setItem('tm-demo-color-theme', 'indigo');"
            : "localStorage.setItem('tm-demo-culture', 'en');");
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90_000 });
        await WaitForAppReadyAsync(page);
        await page.Locator(".tm-gantt").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.Locator(".tm-gantt__tree-row").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        if (indigo)
        {
            await page.EvaluateAsync("() => document.documentElement.setAttribute('data-tm-theme', 'indigo')");
        }

        if (dark)
        {
            // Dark has to land on the html root AND on every themed region: data-theme on <html> alone
            // leaves a region the demo shell already themed on the light tokens.
            await page.EvaluateAsync(
                """
                () => {
                    document.documentElement.setAttribute('data-theme', 'dark');
                    document.documentElement.classList.add('dark', 'tm-dark');
                    document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', 'dark'));
                }
                """);
        }

        await page.WaitForTimeoutAsync(400);
        return page;
    }

    private static ILocator MainGantt(IPage page) => page.Locator(".tm-gantt").First;

    private static async Task<string> StyleAsync(ILocator locator, string property) =>
        await locator.EvaluateAsync<string>("(el, p) => getComputedStyle(el).getPropertyValue(p)", property);

    private static async Task<string> ResolveAsync(ILocator locator, string expression, string property = "background-color") =>
        await locator.EvaluateAsync<string>(
            """
            (el, args) => {
                const probe = document.createElement('span');
                probe.style.setProperty(args.property, args.expression);
                probe.style.display = 'none';
                el.appendChild(probe);
                const value = getComputedStyle(probe).getPropertyValue(args.property);
                probe.remove();
                return value;
            }
            """,
            new { expression, property });

    private static async Task ShootAsync(IPage page, ILocator target, string name)
    {
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        await target.ScrollIntoViewIfNeededAsync();
        await target.ScreenshotAsync(new LocatorScreenshotOptions { Path = ShotPath(name), Timeout = 60_000 });
    }

    // ── Computed-style guards ────────────────────────────────────────────────

    [TestMethod]
    [Description("Gantt phase 2: the root is a card (radius, shadow) and the active view is a segment with a strong border")]
    public async Task Gantt_Root_IsACard_And_Segment_IsActive()
    {
        var page = await OpenAsync("/gantt", indigo: false, dark: false);
        var gantt = MainGantt(page);

        Assert.AreNotEqual("0px", await StyleAsync(gantt, "border-top-left-radius"), "the root has the card radius");
        Assert.AreNotEqual("none", await StyleAsync(gantt, "box-shadow"), "the root has the card shadow");

        var segment = gantt.Locator(".tm-gantt__segment");
        Assert.AreEqual("flex", await StyleAsync(segment, "display"), "inline-flex, blockified because it is a flex item of the toolbar group");
        Assert.AreEqual(await ResolveAsync(gantt, "var(--tm-bg-muted)"), await StyleAsync(segment, "background-color"), "the tray is the muted token");
        var active = gantt.Locator(".tm-gantt__segment-btn--active");
        Assert.AreEqual(1, await active.CountAsync(), "exactly one active segment");
        Assert.AreEqual("true", await active.GetAttributeAsync("aria-pressed"));
        Assert.AreEqual("600", await StyleAsync(active, "font-weight"));
        Assert.AreEqual("1px", await StyleAsync(active, "border-top-width"));

        var add = gantt.Locator("[data-testid='gantt-add-task']");
        StringAssert.Contains((await add.InnerTextAsync()).Trim(), "Add task", "the primary action is labelled");
    }

    [TestMethod]
    [Description("Gantt phase 2: the Today label is a pill over a solid line and uses the danger token")]
    public async Task Gantt_Today_Pill_Over_Solid_Line()
    {
        var page = await OpenAsync("/gantt", indigo: false, dark: false);
        var gantt = MainGantt(page);

        var marker = gantt.Locator("[data-testid='today-marker']");
        var label = gantt.Locator("[data-testid='today-label']");
        await label.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual("solid", await StyleAsync(marker, "border-left-style"), "the marker is a full line, not dashed");
        Assert.AreEqual("Today", (await label.InnerTextAsync()).Trim());
        var expected = await ResolveAsync(gantt, "var(--tm-color-danger)");
        Assert.AreEqual(expected, await StyleAsync(label, "background-color"), "the pill paints the danger token");
        Assert.AreNotEqual("0px", await StyleAsync(label, "border-top-left-radius"), "the pill is rounded");

        // The pill straddles the header's bottom edge, centred on the line.
        var labelBox = (await label.BoundingBoxAsync())!;
        var markerBox = (await marker.BoundingBoxAsync())!;
        Assert.AreEqual(markerBox.X, labelBox.X + (labelBox.Width / 2), 3, "the pill is centred on the line");
    }

    [TestMethod]
    [Description("Gantt phase 2: status chips are glyph + text and paint the status tokens")]
    public async Task Gantt_StatusChip_Has_Glyph_And_Token_Colours()
    {
        var page = await OpenAsync("/gantt", indigo: false, dark: false);
        var gantt = MainGantt(page);

        var done = gantt.Locator("[data-testid='status-badge'].tm-gantt__status-badge--done").First;
        await done.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual("✓", (await done.Locator(".tm-gantt__status-icon").InnerTextAsync()).Trim());
        Assert.AreEqual("true", await done.Locator(".tm-gantt__status-icon").GetAttributeAsync("aria-hidden"));
        Assert.AreEqual(await ResolveAsync(gantt, "var(--tm-status-done-bg)"), await StyleAsync(done, "background-color"));
        Assert.AreEqual(await ResolveAsync(gantt, "var(--tm-status-done-fg)", "color"), await StyleAsync(done, "color"));
    }

    [TestMethod]
    [Description("Gantt phase 2: the Theme = Dark setting puts data-theme=dark on the root and the dark tokens repaint it")]
    public async Task Gantt_DarkTheme_Setting_Repaints_Through_Tokens()
    {
        var page = await OpenAsync("/gantt", indigo: false, dark: false);
        var gantt = MainGantt(page);
        var lightSurface = await ResolveAsync(gantt, "var(--tm-bg-surface)");

        await gantt.Locator("[data-testid='gantt-view-settings-btn']").ClickAsync();
        await gantt.Locator(".tm-gantt__view-settings-menu input[type='radio'][value='Dark']").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual("dark", await gantt.GetAttributeAsync("data-theme"), "Theme=Dark sets data-theme on the root");
        var darkSurface = await ResolveAsync(gantt, "var(--tm-bg-surface)");
        Assert.AreNotEqual(lightSurface, darkSurface, "the dark tokens apply under the Gantt root");

        var toolbar = gantt.Locator(".tm-gantt__toolbar");
        Assert.AreEqual(darkSurface, await StyleAsync(toolbar, "background-color"), "the toolbar paints the (dark) surface token");
        Assert.AreEqual(await ResolveAsync(gantt, "var(--tm-text-primary)", "color"), await StyleAsync(gantt, "color"),
            "the root text follows the dark text token");

        // The old local palette painted a hard-coded #1f2937 surface; nothing of the sort is left.
        Assert.AreNotEqual("rgb(31, 41, 55)", await StyleAsync(gantt.Locator(".tm-gantt__tree"), "background-color"),
            "no hard-coded dark palette remains");
    }

    [TestMethod]
    [Description("Gantt phase 2: the indigo theme reaches the Gantt (default bar fill and the workspace)")]
    public async Task Gantt_Indigo_Theme_Reaches_Bars_And_Workspace()
    {
        var defaultPage = await OpenAsync("/gantt", indigo: false, dark: false);
        const string plainBar = ".tm-gantt__bar:not(.tm-gantt__bar--overdue):not(.tm-gantt__bar--critical-path):not(.tm-gantt__bar--baseline):not([style*='#'])";
        var defaultBar = await StyleAsync(defaultPage.Locator(plainBar).First, "background-color");

        var indigoPage = await OpenAsync("/gantt", indigo: true, dark: false);
        var gantt = MainGantt(indigoPage);
        var indigoBar = await StyleAsync(indigoPage.Locator(plainBar).First, "background-color");
        Assert.AreNotEqual(defaultBar, indigoBar, "the default bar fill follows the primary scale of the theme");

        var workspace = await ResolveAsync(gantt, "var(--tm-bg-workspace)");
        Assert.AreEqual(workspace, await StyleAsync(gantt, "background-color"), "the root paints the workspace token");
        Assert.AreNotEqual("rgb(255, 255, 255)", workspace, "the indigo workspace is the off-white canvas");
    }

    [TestMethod]
    [Description("Gantt phase 2: ShowProgressLabel renders a percentage next to the bars")]
    public async Task Gantt_ProgressLabel_Appears_When_Enabled()
    {
        var page = await OpenAsync("/gantt", indigo: false, dark: false);
        var gantt = MainGantt(page);
        Assert.AreEqual(0, await gantt.Locator("[data-testid='bar-progress-label']").CountAsync(), "off by default");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Progress %", Exact = true }).First.ClickAsync();
        var labels = gantt.Locator("[data-testid='bar-progress-label']");
        await labels.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        StringAssert.EndsWith((await labels.First.InnerTextAsync()).Trim(), "%");

        var bar = (await gantt.Locator(".tm-gantt__bar").First.BoundingBoxAsync())!;
        var label = (await labels.First.BoundingBoxAsync())!;
        Assert.IsTrue(label.X >= bar.X + bar.Width - 1, "the percentage sits to the right of the bar, not on it");
    }

    [TestMethod]
    [Description("Gantt phase 2: the workload grid keeps >=40px day cells, a sticky first column and a dash for empty days")]
    public async Task Gantt_Workload_Cells_And_Sticky_Column()
    {
        var page = await OpenAsync("/gantt", indigo: false, dark: false);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Workload", Exact = true }).First.ClickAsync();
        var gantt = MainGantt(page);
        var grid = gantt.Locator(".tm-gantt__workload-grid");
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var narrowest = await grid.Locator(".tm-gantt__workload-header-cell:not(.tm-gantt__workload-assignee-header)")
            .EvaluateAllAsync<double>("els => Math.min(...els.map(e => e.getBoundingClientRect().width))");
        Assert.IsTrue(narrowest >= 39.5, $"a day column is at least 40px, got {narrowest}");

        Assert.AreEqual("sticky", await StyleAsync(grid.Locator(".tm-gantt__workload-name-cell").First, "position"));

        var empty = grid.Locator(".tm-gantt__workload-cell--empty").First;
        await empty.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached });
        StringAssert.Contains(await empty.TextContentAsync(), "–");

        // Scroll the scroller sideways: the first column must stay at the scroller's left edge.
        var scroller = gantt.Locator(".tm-gantt__workload");
        var overflow = await scroller.EvaluateAsync<double>("el => el.scrollWidth - el.clientWidth");
        if (overflow > 20)
        {
            var before = (await grid.Locator(".tm-gantt__workload-name-cell").First.BoundingBoxAsync())!.X;
            await scroller.EvaluateAsync("el => { el.scrollLeft = el.scrollWidth; }");
            await page.WaitForTimeoutAsync(150);
            var after = (await grid.Locator(".tm-gantt__workload-name-cell").First.BoundingBoxAsync())!.X;
            Assert.AreEqual(before, after, 1.5, "the assignee column sticks while the days scroll");
        }
    }

    [TestMethod]
    [Description("Gantt phase 2: the Board columns carry no inline hex and read the status chip tokens")]
    public async Task Gantt_Board_Columns_Use_Status_Chips()
    {
        var page = await OpenAsync("/gantt", indigo: false, dark: false);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Board", Exact = true }).First.ClickAsync();
        var board = MainGantt(page).Locator("[data-testid='gantt-board-view']");
        await board.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // A card accent paints the consumer's own TmWorkItem.Color (data) - the column headers are the Gantt's.
        Assert.AreEqual(0, await board.Locator(".tm-kanban__header [style*='#'], .tm-kanban__header-color").CountAsync(),
            "no hex and no colour dot in a column header");
        Assert.AreEqual(4, await board.Locator(".tm-kanban__header .tm-gantt__status-badge").CountAsync(), "one status chip per column");
    }

    // ── Screenshot matrix (light/dark x default/indigo, desktop 1440) ────────

    [TestMethod]
    [Description("Gantt phase 2 screenshots: main surface, task panel, workload and portfolio in every theme combination")]
    public async Task Gantt_Desktop_Surfaces_LightAndDark_DefaultAndIndigo()
    {
        foreach (var indigo in new[] { false, true })
        {
            foreach (var dark in new[] { false, true })
            {
                var theme = $"{(dark ? "dark" : "light")}-{(indigo ? "indigo" : "default")}";

                var page = await OpenAsync("/gantt", indigo, dark);
                var gantt = MainGantt(page);
                await ShootAsync(page, gantt, $"gantt-main--{theme}");

                await gantt.Locator(".tm-gantt__tree-row").First.ClickAsync();
                await gantt.Locator(".tm-gantt-task-panel").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
                await ShootAsync(page, gantt, $"gantt-task-panel--{theme}");

                await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Workload", Exact = true }).First.ClickAsync();
                await gantt.Locator(".tm-gantt__workload-grid").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
                await ShootAsync(page, gantt, $"gantt-workload--{theme}");

                var portfolioPage = await OpenAsync("/gantt-portfolio", indigo, dark);
                var portfolio = portfolioPage.Locator("[data-testid='gantt-demo-portfolio']");
                await portfolioPage.Locator("[data-testid^='portfolio-project-']").First.WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
                await ShootAsync(portfolioPage, portfolio, $"gantt-portfolio--{theme}");
            }
        }
    }
}
