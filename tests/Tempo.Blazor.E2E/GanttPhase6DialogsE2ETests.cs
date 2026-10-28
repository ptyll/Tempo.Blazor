using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TmGantt redesign, phase 6 (desktop): the export, import and mapping dialogs on <c>TmModal</c>, the typed filter
/// editor (inline and as a dialog) and the chips of the active filters. Every <c>Shoot</c> writes a 1440px
/// light and dark screenshot into <c>TestResults/gantt-phase6</c> for the UX review against the
/// <c>Scheduler/gantt-*--desktop.png</c> mockups.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class GanttPhase6DialogsE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "gantt-phase6");

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

    private static async Task OpenDialogAsync(IPage page, string trigger, string dialogClass)
    {
        await Chart(page).Locator($"button[data-testid='{trigger}']").ClickAsync();
        await page.Locator($".tm-modal-overlay.{dialogClass} .tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

    // ── Export ───────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Gantt export dialog: format tiles are a keyboard radio group; the options follow the format; the footer is Cancel | Export")]
    public async Task Export_Tiles_Options_And_Footer()
    {
        var page = await OpenAsync();
        await OpenDialogAsync(page, "gantt-export-btn", "tm-gantt__export-dialog");
        var dialog = page.Locator(".tm-gantt__export-dialog");

        Assert.AreEqual(4, await dialog.Locator("input[type=radio]").CountAsync());
        Assert.IsTrue(await dialog.Locator("[data-testid='export-include-today'] input").IsCheckedAsync(), "the today line is on by default");
        Assert.AreEqual(0, await dialog.Locator("[data-testid='export-columns']").CountAsync(), "columns are an Excel option");
        await ShootBothAsync(page, "export-pdf-1440");

        // Arrow keys move the radio selection natively (the visually-hidden input is a real tab stop).
        await dialog.Locator("input[type=radio][value=Pdf]").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(dialog.Locator("input[type=radio][value=Png]")).ToBeCheckedAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(dialog.Locator("[data-testid='export-format-xlsx']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("selected"));

        Assert.AreEqual(0, await dialog.Locator("[data-testid='export-include-today']").CountAsync(), "an image option does not apply to a workbook");
        Assert.AreEqual(1, await dialog.Locator("[data-testid='export-columns']").CountAsync());
        await ShootBothAsync(page, "export-xlsx-1440");

        var footer = await dialog.Locator(".tm-modal-footer button").AllTextContentsAsync();
        CollectionAssert.AreEqual(new[] { "Cancel", "Export" }, footer.Select(t => t.Trim()).ToArray());
    }

    [TestMethod]
    [Description("Gantt export dialog: Export raises OnExport with the chosen options and closes the dialog")]
    public async Task Export_Submit_Closes_The_Dialog()
    {
        var page = await OpenAsync();
        await OpenDialogAsync(page, "gantt-export-btn", "tm-gantt__export-dialog");

        await page.Locator("[data-testid='export-submit']").ClickAsync();

        await page.Locator(".tm-modal-overlay.tm-gantt__export-dialog").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    // ── Import ───────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Gantt import dialog: ARIA tabs with arrow keys, choose file, Remove file, supported-formats hint")]
    public async Task Import_Tabs_File_Group_And_Remove()
    {
        var page = await OpenAsync();
        await OpenDialogAsync(page, "gantt-import-btn", "tm-gantt__import-dialog");
        var dialog = page.Locator(".tm-gantt__import-dialog");

        await Assertions.Expect(dialog.Locator(".tm-gantt__import-hint")).ToHaveTextAsync("Supported formats: .xlsx, .xls");
        await Assertions.Expect(dialog.Locator("[data-testid='import-submit']")).ToBeDisabledAsync();
        await ShootBothAsync(page, "import-empty-1440");

        // Arrow keys move the tab selection AND the focus.
        await dialog.Locator("[role=tab][aria-selected=true]").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(dialog.Locator("[role=tab]").Nth(1)).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(dialog.Locator("[role=tab]").Nth(1)).ToBeFocusedAsync();
        await Assertions.Expect(dialog.Locator(".tm-gantt__import-hint")).ToHaveTextAsync("Supported formats: .xml, .mpp");
        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(dialog.Locator("[role=tab]").Nth(2)).ToHaveAttributeAsync("aria-selected", "true");
        Assert.AreEqual(0, await dialog.Locator(".tm-gantt__import-hint").CountAsync(), "the JIRA tab imports from a URL");
        await ShootBothAsync(page, "import-jira-1440");
        await page.Keyboard.PressAsync("Home");

        // Excel tab: choose a file, see the name, remove it again.
        await dialog.Locator("input.tm-gantt__import-file-input").SetInputFilesAsync(
            new FilePayload { Name = "plan_projekt_aurora.xlsx", MimeType = "application/octet-stream", Buffer = new byte[] { 1, 2, 3 } });
        await Assertions.Expect(dialog.Locator(".tm-gantt__import-file-name")).ToHaveTextAsync("plan_projekt_aurora.xlsx");
        await Assertions.Expect(dialog.Locator("[data-testid='import-submit']")).ToBeEnabledAsync();
        await ShootBothAsync(page, "import-file-1440");

        await dialog.Locator("[data-testid='import-remove-file']").ClickAsync();
        await Assertions.Expect(dialog.Locator(".tm-gantt__import-file-name")).ToHaveTextAsync("No file chosen");
        await Assertions.Expect(dialog.Locator("[data-testid='import-remove-file']")).ToHaveCountAsync(0);
        await Assertions.Expect(dialog.Locator("[data-testid='import-submit']")).ToBeDisabledAsync();

        // The same file can be chosen again after Remove (the input was recreated).
        await dialog.Locator("input.tm-gantt__import-file-input").SetInputFilesAsync(
            new FilePayload { Name = "plan_projekt_aurora.xlsx", MimeType = "application/octet-stream", Buffer = new byte[] { 1, 2, 3 } });
        await Assertions.Expect(dialog.Locator(".tm-gantt__import-file-name")).ToHaveTextAsync("plan_projekt_aurora.xlsx");
    }

    [TestMethod]
    [Description("Gantt import dialog: a file that cannot be read shows the error as a TmAlert and keeps the dialog open")]
    public async Task Import_Error_Is_A_TmAlert()
    {
        var page = await OpenAsync();
        await OpenDialogAsync(page, "gantt-import-btn", "tm-gantt__import-dialog");
        var dialog = page.Locator(".tm-gantt__import-dialog");

        await dialog.Locator("input.tm-gantt__import-file-input").SetInputFilesAsync(
            new FilePayload { Name = "broken.xlsx", MimeType = "application/octet-stream", Buffer = new byte[] { 1, 2, 3 } });
        await dialog.Locator("[data-testid='import-submit']").ClickAsync();

        var error = dialog.Locator("[data-testid='import-error']");
        await error.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await Assertions.Expect(error).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-alert--error"));
        await Assertions.Expect(error).ToHaveAttributeAsync("role", "alert");
        await ShootBothAsync(page, "import-error-1440");
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Gantt mapping dialog: columns are auto-mapped, Import is disabled until Title + Start + End/Duration, a duplicate warns")]
    public async Task Mapping_AutoMaps_Validates_And_Warns()
    {
        var page = await OpenAsync("/gantt-portfolio");
        await page.Locator("[data-testid='gantt-demo-mapping-open']").ClickAsync();
        var dialog = page.Locator(".tm-gantt__import-mapping-dialog");
        await dialog.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // "Task Name", "Begin", "Finish", "Done %" arrive pre-mapped.
        await Assertions.Expect(dialog.Locator("[data-testid='mapping-select-Task Name'] select")).ToHaveValueAsync("Title");
        await Assertions.Expect(dialog.Locator("[data-testid='mapping-select-Begin'] select")).ToHaveValueAsync("Start");
        await Assertions.Expect(dialog.Locator("[data-testid='mapping-select-Finish'] select")).ToHaveValueAsync("End");
        await Assertions.Expect(dialog.Locator("[data-testid='mapping-select-Notes'] select")).ToHaveValueAsync("");
        await Assertions.Expect(dialog.Locator("[data-testid='mapping-submit']")).ToBeEnabledAsync();
        await ShootBothAsync(page, "mapping-auto-1440");

        // Each select is named by a <label for>.
        var named = await dialog.Locator(".tm-gantt__mapping-row").EvaluateAllAsync<int>(
            "rows => rows.filter(r => { const s = r.querySelector('select'); return s && r.querySelector(`label[for='${s.id}']`); }).length");
        Assert.AreEqual(5, named, "every mapping select has a label[for]");

        // A duplicate target is a warning, not an error.
        await dialog.Locator("[data-testid='mapping-select-Notes'] select").SelectOptionAsync("Title");
        await Assertions.Expect(dialog.Locator("[data-testid='mapping-duplicate']")).ToBeVisibleAsync();
        await Assertions.Expect(dialog.Locator("[data-testid='mapping-submit']")).ToBeEnabledAsync();
        await ShootBothAsync(page, "mapping-duplicate-1440");
        await dialog.Locator("[data-testid='mapping-select-Notes'] select").SelectOptionAsync("");

        // Un-mapping Start disables Import and names what is missing.
        await dialog.Locator("[data-testid='mapping-select-Begin'] select").SelectOptionAsync("");
        await Assertions.Expect(dialog.Locator("[data-testid='mapping-submit']")).ToBeDisabledAsync();
        await Assertions.Expect(dialog.Locator("[data-testid='mapping-validation']")).ToContainTextAsync("Start");
        await ShootBothAsync(page, "mapping-invalid-1440");
    }

    [TestMethod]
    [Description("Gantt mapping dialog: Escape closes it and the focus returns to the button that opened it")]
    public async Task Mapping_Escape_Returns_Focus_To_The_Opener()
    {
        var page = await OpenAsync("/gantt-portfolio");
        var opener = page.Locator("[data-testid='gantt-demo-mapping-open']");
        await opener.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var overlay = page.Locator(".tm-modal-overlay.tm-gantt__import-mapping-dialog");
        await overlay.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await page.Keyboard.PressAsync("Escape");

        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await Assertions.Expect(opener).ToBeFocusedAsync();
    }

    // ── Filter ───────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Gantt filter (inline): Status is a select, Start a date input; Cancel discards, Apply filters and shows a chip")]
    public async Task Filter_Inline_Typed_Editors_Cancel_Apply_And_Chips()
    {
        var page = await OpenAsync();
        var gantt = Chart(page);
        var rowsBefore = await gantt.Locator(".tm-gantt__tree-row").CountAsync();

        await gantt.Locator("button[data-testid='gantt-filter-btn']").ClickAsync();
        await gantt.Locator("[data-testid='filter-add']").ClickAsync();
        var panel = gantt.Locator("[data-testid='gantt-filter-panel']");
        await panel.Locator("select.tm-gantt__filter-field").SelectOptionAsync("Status");

        // UX review finding: the three controls sit on ONE row (they used to stack as full-width rows).
        var field = await panel.Locator("select.tm-gantt__filter-field").BoundingBoxAsync();
        var op = await panel.Locator("select.tm-gantt__filter-op").BoundingBoxAsync();
        var value = await panel.Locator("select.tm-gantt__filter-value").BoundingBoxAsync();
        Assert.IsTrue(Math.Abs(field!.Y - op!.Y) < 2 && Math.Abs(op.Y - value!.Y) < 2, "field, operator and value share a row");
        Assert.IsTrue(field.X < op.X && op.X < value!.X, "in the order field, operator, value");

        Assert.AreEqual(0, await panel.Locator("input[type=text].tm-gantt__filter-value").CountAsync(), "an enum is picked, never typed");
        await panel.Locator("select.tm-gantt__filter-value").SelectOptionAsync("Done");
        await ShootBothAsync(page, "filter-inline-1440");

        // Cancel throws the edit away: nothing is applied.
        await panel.Locator("[data-testid='filter-cancel']").ClickAsync();
        await Assertions.Expect(gantt.Locator("[data-testid='gantt-filter-panel']")).ToHaveCountAsync(0);
        Assert.AreEqual(rowsBefore, await gantt.Locator(".tm-gantt__tree-row").CountAsync());
        Assert.AreEqual(0, await gantt.Locator(".tm-gantt__filter-chips").CountAsync());

        // Apply filters the chart and shows the chip under the toolbar.
        await gantt.Locator("button[data-testid='gantt-filter-btn']").ClickAsync();
        await gantt.Locator("[data-testid='filter-add']").ClickAsync();
        await panel.Locator("select.tm-gantt__filter-field").SelectOptionAsync("Start");
        Assert.AreEqual(1, await panel.Locator("input[type=date].tm-gantt__filter-value").CountAsync());
        await panel.Locator("select.tm-gantt__filter-op").SelectOptionAsync("After");
        await panel.Locator("input[type=date].tm-gantt__filter-value").FillAsync("2000-01-01");
        await panel.Locator("[data-testid='filter-apply']").ClickAsync();

        var chips = gantt.Locator(".tm-gantt__filter-chips .tm-filter-chip");
        await Assertions.Expect(chips).ToHaveCountAsync(1);
        await Assertions.Expect(chips.First).ToContainTextAsync("Start");
        await ShootBothAsync(page, "filter-chip-1440");

        // The chip's X removes exactly that filter.
        await chips.First.Locator(".tm-filter-chip-remove").ClickAsync();
        await Assertions.Expect(gantt.Locator(".tm-gantt__filter-chips")).ToHaveCountAsync(0);
    }

    [TestMethod]
    [Description("Gantt filter (dialog mode): opens as a TmModal, Escape closes it and returns the focus, Apply closes it")]
    public async Task Filter_Dialog_Mode_Is_A_TmModal()
    {
        var page = await OpenAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Filter dialog", Exact = true }).ClickAsync();
        var button = Chart(page).Locator("button[data-testid='gantt-filter-btn']");

        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var overlay = page.Locator(".tm-modal-overlay.tm-gantt__filter-dialog");
        await overlay.Locator(".tm-modal").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.AreEqual(0, await Chart(page).Locator(".tm-gantt__filter-panel").CountAsync(), "no inline strip in the dialog presentation");

        await overlay.Locator("[data-testid='filter-add']").ClickAsync();
        await overlay.Locator("select.tm-gantt__filter-field").SelectOptionAsync("Priority");
        await ShootBothAsync(page, "filter-dialog-1440");

        await page.Keyboard.PressAsync("Escape");
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await Assertions.Expect(button).ToBeFocusedAsync();

        await button.ClickAsync();
        await overlay.Locator("[data-testid='filter-add']").ClickAsync();
        await overlay.Locator("select.tm-gantt__filter-field").SelectOptionAsync("Priority");
        await overlay.Locator("select.tm-gantt__filter-value").SelectOptionAsync("Highest");
        await overlay.Locator("[data-testid='filter-apply']").ClickAsync();

        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await Assertions.Expect(Chart(page).Locator(".tm-gantt__filter-chips .tm-filter-chip")).ToHaveCountAsync(1);
    }
}
