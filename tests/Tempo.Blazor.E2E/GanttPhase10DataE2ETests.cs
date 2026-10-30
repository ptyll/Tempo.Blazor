using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TmGantt redesign, phase 10: the import mapping finally reaches Gantt data (WBS hierarchy,
/// predecessor dependencies, assignee people, warnings shown without blocking) and the export
/// dialog's built-in preview follows every option. Screenshots (light + dark) go to
/// <c>TestResults/gantt-phase10</c> for the UX review against the
/// <c>Scheduler/gantt-export-dialog--desktop.png</c> and <c>gantt-import-mapping-dialog--desktop.png</c> mockups.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class GanttPhase10DataE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "gantt-phase10");

    private static string SamplePath
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                current = current.Parent!;
            }

            return Path.Combine(current!.FullName,
                "src", "Tempo.Blazor.Demo.SharedUI", "wwwroot", "sample-data", "gantt-import-sample.xlsx");
        }
    }

    private async Task<IPage> OpenAsync(string route = "/gantt", int width = 1440, int height = 900)
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

    private static ILocator Chart(IPage page) => page.Locator(".tm-gantt").First;

    /// <summary>Toggles a visually hidden checkbox / radio input (a DOM click = what a real pointer through the label does).</summary>
    private static async Task ToggleHiddenInputAsync(ILocator scope, string testId, bool checkedState)
    {
        var input = scope.Locator($"[data-testid='{testId}'] input");
        if (await input.IsCheckedAsync() != checkedState)
        {
            await input.EvaluateAsync("el => el.click()");
        }
    }

    /// <summary>
    /// Clicks a toolbar command whether it sits on its bar or has left into that bar's "More" menu —
    /// at a tablet container the bar decides by width, so a test must not assume either
    /// (the same contract <c>GanttPhase7TabletE2ETests</c> taps).
    /// </summary>
    private static async Task OpenToolbarCommandAsync(IPage page, string testId)
    {
        var gantt = Chart(page);
        var onBar = gantt.Locator($".tm-gantt__toolbar [data-testid='{testId}']:not(.tm-toolbar-item--collapsed)").First;
        if (await onBar.CountAsync() > 0)
        {
            await onBar.ClickAsync();
            return;
        }

        var toggles = gantt.Locator(".tm-gantt__toolbar .tm-toolbar-more");
        for (var i = 0; i < await toggles.CountAsync(); i++)
        {
            await toggles.Nth(i).ClickAsync();
            var entry = page.Locator($"[data-menu-testid='{testId}']").First;
            if (await entry.CountAsync() > 0)
            {
                await entry.ClickAsync();
                return;
            }

            await page.Keyboard.PressAsync("Escape");
        }

        Assert.Fail($"'{testId}' is neither on a toolbar bar nor in a More menu");
    }

    private static async Task SetThemeAsync(IPage page, string theme) =>
        await page.EvaluateAsync(
            "t => { document.documentElement.setAttribute('data-theme', t); document.querySelectorAll('[data-theme]').forEach(el => el.setAttribute('data-theme', t)); }",
            theme);

    private static async Task ShootBothAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await page.WaitForTimeoutAsync(150);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ShotDir, $"{name}-{theme}.png"), FullPage = false, Timeout = 60_000 });
        }

        await SetThemeAsync(page, "light");
    }

    /// <summary>Imports the shared sample workbook through the dialog's mapping step and leaves the warning alert on screen.</summary>
    private async Task ImportSampleAsync(IPage page, int width)
    {
        var before = await Chart(page).Locator(".tm-gantt__tree-row").CountAsync();

        await OpenToolbarCommandAsync(page, "gantt-import-btn");
        var dialog = page.Locator(".tm-gantt__import-dialog");
        await dialog.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await dialog.Locator("input.tm-gantt__import-file-input").SetInputFilesAsync(SamplePath);
        await Assertions.Expect(dialog.Locator("[data-testid='import-submit']")).ToBeEnabledAsync();
        await dialog.Locator("[data-testid='import-submit']").ClickAsync();

        // The mapping step opens with the sample's own headers, auto-mapped by name.
        var mapping = page.Locator(".tm-gantt__import-mapping-dialog");
        await mapping.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await Assertions.Expect(mapping.Locator("[data-testid='mapping-select-Task Name'] select")).ToHaveValueAsync("Title");
        await Assertions.Expect(mapping.Locator("[data-testid='mapping-select-wbs'] select")).ToHaveValueAsync("WBS");
        await Assertions.Expect(mapping.Locator("[data-testid='mapping-select-predecessors'] select")).ToHaveValueAsync("Predecessor");
        await Assertions.Expect(mapping.Locator("[data-testid='mapping-select-assignees'] select")).ToHaveValueAsync("Assignees");
        await ShootBothAsync(page, $"import-mapping-{width}");

        await mapping.Locator("[data-testid='mapping-submit']").ClickAsync();
        await mapping.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        // The sample carries one unknown predecessor: the warning alert shows and the import
        // STILL went through - the dialog stays open so the user can read what to fix, the file
        // is dropped and the footer offers a single primary Close.
        var warnings = dialog.Locator("[data-testid='import-warnings']");
        await warnings.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await Assertions.Expect(warnings).ToContainTextAsync("warning");
        await ShootBothAsync(page, $"import-warnings-{width}");
        await dialog.Locator("[data-testid='import-close']").ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        // Five rows landed (ids are stable per row within the import under a fresh per-import
        // prefix), the WBS built the hierarchy (row 1 "Site preparation" = WBS 1 is a root;
        // "Foundation" = 1.1 nests under it; "Walls" = 1.1.1 nests two levels deep) and the
        // predecessors drew lines.
        var after = await Chart(page).Locator(".tm-gantt__tree-row").CountAsync();
        Assert.AreEqual(before + 5, after, "the sample's five tasks were imported");
        var importedRows = Chart(page).Locator(".tm-gantt__tree-row[data-testid^='tree-row-xlsx-']");
        Assert.AreEqual(5, await importedRows.CountAsync(), "every imported row keeps the dialog's xlsx-…-row- id");
        var sitePreparation = importedRows.Nth(0);
        // WBS 1 is a root; WBS 1.1 nests under it; WBS 1.1.1 nests two levels deep.
        await Assertions.Expect(sitePreparation).ToHaveAttributeAsync("aria-level", "1");
        var foundation = importedRows.Nth(1);
        await Assertions.Expect(foundation).ToHaveAttributeAsync("aria-level", "2");
        var walls = importedRows.Nth(2);
        await Assertions.Expect(walls).ToHaveAttributeAsync("aria-level", "3");
        Assert.IsTrue(await Chart(page).Locator(".tm-gantt__dependency-line").CountAsync() > 0,
            "the predecessor column drew dependency lines");
    }

    [TestMethod]
    [Description("Gantt import at 1440: mapping → WBS hierarchy, dependency lines, workload people; the warning alert shows without blocking")]
    public async Task Import_Mapping_Reaches_The_Data_Desktop()
    {
        var page = await OpenAsync();
        await ImportSampleAsync(page, 1440);

        // The imported assignees planned the tasks: the workload view shows their people.
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Workload", Exact = true }).First.ClickAsync();
        var workload = Chart(page).Locator(".tm-gantt__workload").First;
        await workload.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await Assertions.Expect(workload).ToContainTextAsync("Alice");
        await Assertions.Expect(workload).ToContainTextAsync("Carol");
        await ShootBothAsync(page, "import-workload-1440");
    }

    [TestMethod]
    [Description("Gantt import at 1024 (tablet container): the same flow lands the data")]
    public async Task Import_Mapping_Reaches_The_Data_Tablet()
    {
        var page = await OpenAsync(width: 1024, height: 900);
        await ImportSampleAsync(page, 1024);
    }

    [TestMethod]
    [Description("Gantt export preview: the page aspect ratio follows paper + orientation, Excel shows the selected columns, XML the counts")]
    public async Task Export_Preview_Follows_Every_Option()
    {
        var page = await OpenAsync();
        await OpenToolbarCommandAsync(page, "gantt-export-btn");
        var dialog = page.Locator(".tm-gantt__export-dialog");
        await dialog.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // PDF: the miniature page carries the paper's shape.
        var preview = dialog.Locator(".tm-gantt-export-preview");
        await preview.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var page2 = dialog.Locator(".tm-gantt-export-preview__page");
        await Assertions.Expect(page2).ToHaveAttributeAsync("data-paper", "A4");
        await Assertions.Expect(page2).ToContainTextAsync(""); // sanity: the element exists
        var ratio = await page2.GetAttributeAsync("style");
        StringAssert.Contains(ratio, "aspect-ratio: 210/297", "A4 portrait");
        await ShootBothAsync(page, "export-preview-pdf-1440");

        await dialog.Locator("[data-testid='export-paper-size'] select").SelectOptionAsync("A3");
        await Assertions.Expect(page2).ToHaveAttributeAsync("data-paper", "A3");
        ratio = await page2.GetAttributeAsync("style");
        StringAssert.Contains(ratio, "aspect-ratio: 297/420", "A3 portrait");
        // The checkbox inputs are visually hidden (outside the viewport); a DOM click toggles them
        // the way a real pointer through the label would.
        await ToggleHiddenInputAsync(dialog, "export-landscape", true);
        ratio = await page2.GetAttributeAsync("style");
        StringAssert.Contains(ratio, "aspect-ratio: 420/297", "the orientation swaps the sides");

        // Excel: the table lists the selected columns; unchecking one removes it from the preview.
        await ToggleHiddenInputAsync(dialog, "export-format-xlsx", true);
        var table = dialog.Locator(".tm-gantt-export-preview__table");
        await table.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var headersBefore = await table.Locator("th").AllTextContentsAsync();
        CollectionAssert.Contains(headersBefore.Select(h => h.Trim()).ToArray(), "Progress");
        Assert.IsTrue(headersBefore.Count >= 8, "the exporter's default columns start selected");
        await ShootBothAsync(page, "export-preview-xlsx-1440");

        await ToggleHiddenInputAsync(dialog, "export-column-Progress", false);
        var headersAfter = (await table.Locator("th").AllTextContentsAsync()).Select(h => h.Trim()).ToArray();
        CollectionAssert.DoesNotContain(headersAfter, "Progress", "the unchecked column left the preview");
        await ShootBothAsync(page, "export-preview-xlsx-trimmed-1440");

        // XML: the summary counts.
        await ToggleHiddenInputAsync(dialog, "export-format-xml", true);
        var summary = dialog.Locator(".tm-gantt-export-preview__summary");
        await summary.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await Assertions.Expect(summary).ToContainTextAsync("Tasks");
        await Assertions.Expect(summary).ToContainTextAsync("Dependencies");
        await Assertions.Expect(summary).ToContainTextAsync("Milestones");
        await ShootBothAsync(page, "export-preview-xml-1440");
    }

    [TestMethod]
    [Description("Gantt export preview on a tablet container (1024): the preview renders under the options")]
    public async Task Export_Preview_On_Tablet()
    {
        var page = await OpenAsync(width: 1024, height: 900);
        await OpenToolbarCommandAsync(page, "gantt-export-btn");
        var dialog = page.Locator(".tm-gantt__export-dialog");
        await dialog.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await Assertions.Expect(dialog.Locator(".tm-gantt-export-preview__page")).ToBeVisibleAsync();
        await ShootBothAsync(page, "export-preview-1024");
    }
}
