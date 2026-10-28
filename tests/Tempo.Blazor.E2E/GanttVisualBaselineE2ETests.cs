using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TmGantt redesign, phase 1 (B21): the first screenshot baseline of the Gantt surfaces and the CSS
/// isolation regression guard. Before the fix the sub-component styles lived in the scoped
/// <c>TmGantt.razor.css</c>, so the export / import / mapping dialogs, the history drawer, the filter
/// panel, the workload grid and the portfolio were rendered without any styling.
/// <para>
/// <b>Screenshots.</b> Every <c>Gantt_Visual_*</c> test writes a 1440px desktop shot into
/// <c>__screenshots__/gantt/{phase}/</c>. <c>TM_GANTT_SHOT_PHASE</c> selects the folder
/// (default <c>after</c>); the pre-fix characterisation was captured with
/// <c>TM_GANTT_SHOT_PHASE=before</c> on the unfixed build.
/// </para>
/// <para>
/// <b>Assertions.</b> Every <c>Gantt_Css_*</c> test asserts COMPUTED styles — the regression guard
/// for CSS isolation: a rule that does not reach its markup fails here, where neither bUnit nor the
/// token audit can see it.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class GanttVisualBaselineE2ETests : WasmTestBase
{
    private static string ShotDir
    {
        get
        {
            var phase = Environment.GetEnvironmentVariable("TM_GANTT_SHOT_PHASE");
            if (string.IsNullOrWhiteSpace(phase)) phase = "after";
            return Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "gantt", phase);
        }
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

    private async Task<IPage> OpenGanttAsync(int width = 1440, int height = 900, string route = "/gantt")
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
        await gantt.Locator(".tm-gantt__tree-row").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return page;
    }

    /// <summary>The main (first) Gantt - the portfolio on the same page embeds further ones.</summary>
    private static ILocator MainGantt(IPage page) => page.Locator(".tm-gantt").First;

    private static async Task ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{name}.png"),
            FullPage = false,
            Timeout = 60_000,
        });
    }

    private static async Task<string> StyleAsync(ILocator locator, params string[] properties)
    {
        return await locator.EvaluateAsync<string>(
            "(el, props) => { const c = getComputedStyle(el); return props.map(p => c.getPropertyValue(p)).join('|'); }",
            properties);
    }

    private static async Task OpenViewAsync(IPage page, string viewName)
    {
        // The view selector (Day..People) is the demo page's own control, outside the Gantt toolbar.
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = viewName, Exact = true }).First.ClickAsync();
    }

    // ── Screenshots (before / after) ─────────────────────────────────────────

    [TestMethod]
    [Description("Gantt baseline screenshot: main surface at 1440")]
    public async Task Gantt_Visual_MainSurface()
    {
        var page = await OpenGanttAsync();
        await ShootAsync(page, "gantt-main-1440");
    }

    [TestMethod]
    [Description("Gantt baseline screenshot: export dialog at 1440")]
    public async Task Gantt_Visual_ExportDialog()
    {
        var page = await OpenGanttAsync();
        await MainGantt(page).Locator("button[data-testid='gantt-export-btn']").ClickAsync();
        await page.Locator(".tm-gantt__export-dialog .tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(page, "gantt-export-dialog-1440");
    }

    [TestMethod]
    [Description("Gantt baseline screenshot: import dialog at 1440")]
    public async Task Gantt_Visual_ImportDialog()
    {
        var page = await OpenGanttAsync();
        await MainGantt(page).Locator("button[data-testid='gantt-import-btn']").ClickAsync();
        await page.Locator(".tm-gantt__import-dialog .tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(page, "gantt-import-dialog-1440");
    }

    [TestMethod]
    [Description("Gantt baseline screenshot: import mapping dialog at 1440")]
    public async Task Gantt_Visual_ImportMappingDialog()
    {
        var page = await OpenGanttAsync(route: "/gantt-portfolio");
        await page.Locator("[data-testid='gantt-demo-mapping-open']").ClickAsync();
        await page.Locator(".tm-gantt__import-mapping-dialog .tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(page, "gantt-import-mapping-dialog-1440");
    }

    [TestMethod]
    [Description("Gantt baseline screenshot: history drawer at 1440")]
    public async Task Gantt_Visual_HistoryDrawer()
    {
        var page = await OpenGanttAsync();
        await MainGantt(page).Locator("button[data-testid='gantt-history-btn']").ClickAsync();
        await MainGantt(page).Locator("[data-testid='gantt-history-drawer']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(page, "gantt-history-drawer-1440");
    }

    [TestMethod]
    [Description("Gantt baseline screenshot: filter panel at 1440")]
    public async Task Gantt_Visual_FilterPanel()
    {
        var page = await OpenGanttAsync();
        await MainGantt(page).Locator("button[data-testid='gantt-filter-btn']").ClickAsync();
        await MainGantt(page).Locator("[data-testid='gantt-filter-panel']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await MainGantt(page).Locator("[data-testid='filter-add']").ClickAsync();
        await ShootAsync(page, "gantt-filter-panel-1440");
    }

    [TestMethod]
    [Description("Gantt baseline screenshot: workload view at 1440")]
    public async Task Gantt_Visual_WorkloadView()
    {
        var page = await OpenGanttAsync();
        await OpenViewAsync(page, "Workload");
        await MainGantt(page).Locator("[data-testid='gantt-workload-view']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(page, "gantt-workload-view-1440");
    }

    [TestMethod]
    [Description("Gantt baseline screenshot: portfolio at 1440")]
    public async Task Gantt_Visual_Portfolio()
    {
        var page = await OpenGanttAsync(route: "/gantt-portfolio");
        var portfolio = page.Locator("[data-testid='gantt-demo-portfolio']");
        await portfolio.ScrollIntoViewIfNeededAsync();
        await page.Locator("[data-testid^='portfolio-project-']").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(page, "gantt-portfolio-1440");
    }

    [TestMethod]
    [Description("Gantt baseline screenshot: task panel at 1440")]
    public async Task Gantt_Visual_TaskPanel()
    {
        var page = await OpenGanttAsync();
        await MainGantt(page).Locator(".tm-gantt__tree-row").First.ClickAsync();
        await MainGantt(page).Locator(".tm-gantt-task-panel").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ShootAsync(page, "gantt-task-panel-1440");
    }

    // ── CSS isolation regression guard (computed styles) ─────────────────────

    [TestMethod]
    [Description("Gantt CSS isolation: the export dialog overlay is a fixed scrim and the dialog is a flex card")]
    public async Task Gantt_Css_ExportDialog_IsStyled()
    {
        var page = await OpenGanttAsync();
        await MainGantt(page).Locator("button[data-testid='gantt-export-btn']").ClickAsync();
        var overlay = page.Locator(".tm-modal-overlay.tm-gantt__export-dialog");
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual("fixed", await StyleAsync(overlay, "position"), "the scrim is viewport-fixed");

        var header = overlay.Locator(".tm-modal-header").First;
        Assert.AreEqual("flex", await StyleAsync(header, "display"), "the dialog header is a flex row");

        var formats = page.Locator(".tm-gantt__export-formats");
        Assert.AreEqual("grid", await StyleAsync(formats, "display"), "the export formats are a grid");

        await ShootAsync(page, "gantt-export-dialog-1440-styled");
    }

    [TestMethod]
    [Description("Gantt CSS isolation: the history drawer sits on the right edge of the Gantt component (not the viewport)")]
    public async Task Gantt_Css_HistoryDrawer_IsAbsoluteRightOfTheComponent()
    {
        var page = await OpenGanttAsync();
        await MainGantt(page).Locator("button[data-testid='gantt-history-btn']").ClickAsync();
        var drawer = page.Locator(".tm-gantt__history-drawer");
        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual("absolute|0px", await StyleAsync(drawer, "position", "right"),
            "the history drawer is positioned against the Gantt root, on its right edge");
        var box = await drawer.BoundingBoxAsync();
        var root = await MainGantt(page).BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsNotNull(root);
        Assert.IsTrue(Math.Abs(box!.Height - root!.Height) <= 2, $"the drawer spans the component height ({root.Height}), got {box.Height}");
        Assert.IsTrue(Math.Abs((box.X + box.Width) - (root.X + root.Width)) <= 2, "the drawer ends at the component's right edge");
    }

    [TestMethod]
    [Description("Gantt CSS isolation: the workload grid is a CSS grid with a padded scroller")]
    public async Task Gantt_Css_WorkloadView_IsGrid()
    {
        var page = await OpenGanttAsync();
        await OpenViewAsync(page, "Workload");
        var workload = MainGantt(page).Locator(".tm-gantt__workload");
        await workload.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual("12px", await StyleAsync(workload, "padding-top"), "the workload scroller is padded");
        var grid = MainGantt(page).Locator(".tm-gantt__workload-grid");
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual("grid", await StyleAsync(grid, "display"), "the workload table is a CSS grid");
    }

    [TestMethod]
    [Description("Gantt CSS isolation: the filter panel rows are flex rows")]
    public async Task Gantt_Css_FilterPanel_IsStyled()
    {
        var page = await OpenGanttAsync();
        await MainGantt(page).Locator("button[data-testid='gantt-filter-btn']").ClickAsync();
        await MainGantt(page).Locator("[data-testid='filter-add']").ClickAsync();
        var row = page.Locator(".tm-gantt__filter-row").First;
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual("flex", await StyleAsync(row, "display"), "the filter row is a flex row");
        Assert.AreEqual("flex", await StyleAsync(page.Locator(".tm-gantt__filter-panel-header"), "display"));
    }

    [TestMethod]
    [Description("Gantt CSS isolation: the mapping dialog is a styled card in a fixed scrim")]
    public async Task Gantt_Css_MappingDialog_IsStyled()
    {
        var page = await OpenGanttAsync(route: "/gantt-portfolio");
        await page.Locator("[data-testid='gantt-demo-mapping-open']").ClickAsync();
        var overlay = page.Locator(".tm-modal-overlay.tm-gantt__import-mapping-dialog");
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual("fixed", await StyleAsync(overlay, "position"));
        Assert.AreEqual("grid", await StyleAsync(page.Locator(".tm-gantt__mapping-row").First, "display"),
            "a mapping row is a two-column grid");
        Assert.AreEqual("680px", await StyleAsync(overlay.Locator(".tm-modal"), "max-width"), "a Large TmModal card");
    }

    [TestMethod]
    [Description("Gantt portfolio: the project card has a header, the embedded Gantt a non-zero height and draws task bars")]
    public async Task Gantt_Css_Portfolio_RendersCardsWithBars()
    {
        var page = await OpenGanttAsync(route: "/gantt-portfolio");
        var project = page.Locator("[data-testid='gantt-demo-portfolio'] [data-testid^='portfolio-project-']").First;
        await project.ScrollIntoViewIfNeededAsync();
        await project.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var header = project.Locator(".tm-gantt-portfolio__project-header");
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", await StyleAsync(header, "background-color"),
            "the project header has its own surface");
        // The header now also holds the range, the task count and the progress bar; only the project name is bold.
        Assert.AreEqual("600", await StyleAsync(header.Locator(".tm-gantt-portfolio__project-name"), "font-weight"));

        var gantt = project.Locator(".tm-gantt");
        var box = await gantt.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.Height > 100, $"the embedded Gantt has a real height, got {box.Height}");

        var bars = await project.Locator("[data-testid*='task-bar-']").CountAsync();
        Assert.IsTrue(bars > 0, "the portfolio draws the project's task bars (it used to render an empty Gantt)");
        var rows = await project.Locator(".tm-gantt__tree-row").CountAsync();
        Assert.IsTrue(rows > 0, "the portfolio draws the project's task rows");
    }

    [TestMethod]
    [Description("Gantt CSS isolation: the task panel colour swatch is a circle")]
    public async Task Gantt_Css_TaskPanel_SwatchIsCircle()
    {
        var page = await OpenGanttAsync();
        await MainGantt(page).Locator(".tm-gantt__tree-row").First.ClickAsync();
        var swatch = MainGantt(page).Locator(".tm-gantt__panel-color-swatch").First;
        await swatch.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.AreEqual("32px|32px|50%", await StyleAsync(swatch, "width", "height", "border-radius"),
            "the swatch is a 32px circle");
    }

    [TestMethod]
    [Description("Gantt import dialog: Import stays disabled until a file is chosen; the error surface is its own class")]
    public async Task Gantt_ImportDialog_ImportDisabledWithoutFile()
    {
        var page = await OpenGanttAsync();
        await MainGantt(page).Locator("button[data-testid='gantt-import-btn']").ClickAsync();
        var dialog = page.Locator(".tm-gantt__import-dialog");
        await dialog.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var importButton = dialog.Locator("[data-testid='import-submit']");
        Assert.IsTrue(await importButton.IsDisabledAsync(), "Import is disabled until a file is chosen");

        await dialog.Locator("input.tm-gantt__import-file-input").SetInputFilesAsync(
            new FilePayload { Name = "tasks.xml", MimeType = "text/xml", Buffer = System.Text.Encoding.UTF8.GetBytes("<x/>") });
        await Assertions.Expect(importButton).ToBeEnabledAsync();
    }

    [TestMethod]
    [Description("Gantt a11y: the view switch exposes aria-pressed and the toolbar icon buttons have accessible names")]
    public async Task Gantt_A11y_ViewSwitchAndIconButtons()
    {
        var page = await OpenGanttAsync();
        var gantt = page.Locator(".tm-gantt").First;

        var pressed = await gantt.Locator(".tm-gantt__toolbar button[aria-pressed='true']").CountAsync();
        Assert.IsTrue(pressed >= 1, "the active view button reports aria-pressed=true");

        var unnamed = await gantt.Locator(".tm-gantt__toolbar button").EvaluateAllAsync<int>(
            "els => els.filter(b => !(b.getAttribute('aria-label') || (b.textContent || '').trim())).length");
        Assert.AreEqual(0, unnamed, "every toolbar button has an accessible name");
    }
}
