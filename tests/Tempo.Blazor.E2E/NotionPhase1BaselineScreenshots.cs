using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase-1 evidence captures for the Notion editor redesign (plan c8d4bcd2).
/// Light and dark of the surfaces this phase changed. Lands in
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/notion-*/phase-1/</c>.
/// Evidence only — not a baseline generator, so it is not behind TM_WRITE_BASELINES.
/// </summary>
[TestClass]
[TestCategory("NotionUxBaseline")]
public sealed class NotionPhase1BaselineScreenshots : NotionE2ETestBase
{
    private static string ShotDir(string area)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            "__screenshots__", area, "phase-1");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return dir ?? throw new DirectoryNotFoundException("TempoBlazor.slnx");
    }

    private static async Task SetThemeAsync(IPage page, string theme)
    {
        // The demo shell paints dark from ThemeService (class "dark" + data-theme on .demo-shell).
        // Setting the attribute from the outside is overwritten on the next render, so use the toggle.
        var shell = page.Locator(".demo-shell").First;
        var current = await shell.GetAttributeAsync("data-theme");
        if (!string.Equals(current, theme, StringComparison.OrdinalIgnoreCase))
        {
            // A top-layer popover or modal covers the header toggle; dispatch the click in-page.
            await page.EvaluateAsync("() => document.querySelector('[data-testid=theme-toggle]')?.click()");
            await page.WaitForFunctionAsync(
                "expected => document.querySelector('.demo-shell')?.getAttribute('data-theme') === expected",
                theme);
        }

        await page.WaitForTimeoutAsync(250);
    }

    private static async Task ShotAsync(IPage page, string area, string name, ILocator? region = null)
    {
        var path = Path.Combine(ShotDir(area), name + ".png");
        if (region is not null)
            await region.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        else
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });
    }

    private static async Task CloseByButtonAsync(ILocator root)
    {
        var close = root.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" });
        if (await close.CountAsync() == 0)
            close = root.Locator("button[aria-label='Close']");
        await close.First.ClickAsync();
    }

    [TestMethod]
    [Description("390 editor/page/database with every record detail closed.")]
    public async Task Phase1_Narrow390_DetailClosed()
    {
        var page = await OpenNotionEditorAsync();
        using (var http = DemoApiTestHttp.CreateClient())
        {
            (await http.PostAsync("https://localhost:5100/api/notion/databases/e2e/seed/default", null))
                .EnsureSuccessStatusCode();
        }

        await page.SetViewportSizeAsync(390, 844);
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-db", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30000
        });
        await page.Locator(".tm-dbrd__modal").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });

        await ShotAsync(page, "notion-editor", "editor-390");
        await ShotAsync(page, "notion-page", "page-390", page.Locator(".tm-notion-editor").First);
        var db = page.Locator(".tm-db").First;
        await db.ScrollIntoViewIfNeededAsync();
        await ShotAsync(page, "notion-database", "database-390", db);
    }

    [TestMethod]
    [Description("Calendar overflow popover, light and dark, on a day with more than three events.")]
    public async Task Phase1_CalendarOverflow()
    {
        var page = await OpenNotionEditorAsync();
        await page.SetViewportSizeAsync(1440, 900);
        using (var http = DemoApiTestHttp.CreateClient())
        {
            (await http.PostAsync("https://localhost:5100/api/notion/databases/e2e/seed/calendar-overflow", null))
                .EnsureSuccessStatusCode();
        }

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await db.ScrollIntoViewIfNeededAsync();
        await page.Locator(".tm-db__skeleton").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 15000
        });

        await db.Locator(".tm-db__view-tab").Filter(new LocatorFilterOptions { HasText = "Calendar" }).ClickAsync();
        var calendar = page.Locator(".tm-dbcal").First;
        await calendar.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var overflow = page.GetByTestId("notion-db-calendar-overflow").First;
        await overflow.ClickAsync();
        await page.GetByTestId("notion-db-calendar-day-list").WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        // The popover is a top-layer overlay, so an element shot of the calendar would crop it.
        await ShotAsync(page, "notion-database", "calendar-overflow-light");
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, "notion-database", "calendar-overflow-dark");
        await page.Keyboard.PressAsync("Escape");
    }

    [TestMethod]
    [Description("Delete confirms from the record detail and from the row ⋯ menu.")]
    public async Task Phase1_DeleteConfirms()
    {
        var page = await OpenNotionEditorAsync();
        await page.SetViewportSizeAsync(1440, 900);
        using (var http = DemoApiTestHttp.CreateClient())
        {
            (await http.PostAsync("https://localhost:5100/api/notion/databases/e2e/seed/default", null))
                .EnsureSuccessStatusCode();
        }

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await db.ScrollIntoViewIfNeededAsync();
        await page.Locator(".tm-dbt__row").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        await page.Locator(".tm-dbt__cell--spacer").First.EvaluateAsync("el => el.click()");
        var modal = page.Locator(".tm-dbrd__modal");
        await modal.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await page.GetByTestId("notion-db-record-delete").ClickAsync();
        var confirm = page.Locator(".tm-dialog").Last;
        await confirm.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await ShotAsync(page, "notion-database", "record-delete-light", confirm);
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, "notion-database", "record-delete-dark", confirm);
        await page.Keyboard.PressAsync("Escape");
        await SetThemeAsync(page, "light");
        await CloseByButtonAsync(modal);
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });

        var row = page.Locator(".tm-dbt__row").First;
        await row.HoverAsync();
        await row.GetByTestId("notion-db-table-row-delete").Locator(".tm-dropdown-trigger").ClickAsync();
        await page.Locator(".tm-dropdown-item").Filter(new LocatorFilterOptions { HasText = "Delete" }).Last.ClickAsync();
        confirm = page.Locator(".tm-dialog").Last;
        await confirm.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await ShotAsync(page, "notion-database", "row-delete-light", confirm);
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, "notion-database", "row-delete-dark", confirm);
        await page.Keyboard.PressAsync("Escape");
    }

    [TestMethod]
    [Description("Empty board lane placeholder, light and dark.")]
    public async Task Phase1_EmptyBoardLane()
    {
        var page = await OpenNotionEditorAsync();
        await page.SetViewportSizeAsync(1440, 900);
        using (var http = DemoApiTestHttp.CreateClient())
        {
            (await http.PostAsync("https://localhost:5100/api/notion/databases/e2e/seed/default", null))
                .EnsureSuccessStatusCode();
        }

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await db.ScrollIntoViewIfNeededAsync();
        await db.Locator(".tm-db__view-tab").Filter(new LocatorFilterOptions { HasText = "Board" }).ClickAsync();
        var empty = page.GetByTestId("notion-db-board-empty").First;
        await empty.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var lane = empty.Locator("xpath=ancestor::div[contains(@class,'tm-dbb__col')]");
        await ShotAsync(page, "notion-database", "board-empty-lane-light", lane);
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, "notion-database", "board-empty-lane-dark", lane);
    }

    [TestMethod]
    [Description("Column-resize handle focused, light and dark.")]
    public async Task Phase1_ColumnResizeFocus()
    {
        var page = await OpenNotionEditorAsync();
        await page.SetViewportSizeAsync(1440, 900);
        using (var http = DemoApiTestHttp.CreateClient())
        {
            (await http.PostAsync("https://localhost:5100/api/notion/databases/e2e/seed/default", null))
                .EnsureSuccessStatusCode();
        }

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var db = page.Locator(".tm-db").First;
        await db.ScrollIntoViewIfNeededAsync();
        await page.Locator(".tm-dbt__row").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var handle = page.Locator(".tm-dbt__resize-handle").First;
        await handle.FocusAsync();
        var header = handle.Locator("xpath=ancestor::th");
        await ShotAsync(page, "notion-database", "column-resize-focus-light", header);
        await SetThemeAsync(page, "dark");
        await handle.FocusAsync();
        await ShotAsync(page, "notion-database", "column-resize-focus-dark", header);
    }

    [TestMethod]
    [Description("Comment thread actions menu open (TmDropdown), light and dark.")]
    public async Task Phase1_CommentActionsMenu()
    {
        var page = await OpenNotionEditorAsync();
        await page.SetViewportSizeAsync(1440, 900);

        var block = page.Locator("[data-notion-block]").First;
        await block.HoverAsync();
        await block.Locator(".tm-notion-handle__btn").Last.ClickAsync();
        await page.Locator(".tm-notion-ctx__item").Filter(new LocatorFilterOptions { HasText = "Comment" }).First.ClickAsync();
        var panel = page.Locator(".tm-nbcp").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await panel.Locator(".tm-nbcp__reply-input").First.FillAsync("Phase-1 thread for the actions menu.");
        await panel.Locator(".tm-nbcp__reply-send").First.ClickAsync();
        var entry = panel.Locator(".tm-nbcp__entry").First;
        await entry.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await entry.HoverAsync();
        await entry.GetByTestId("notion-comment-entry-menu").Locator(".tm-dropdown-trigger").ClickAsync();
        await page.GetByTestId("notion-comment-edit-menu").WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        await ShotAsync(page, "notion-comments", "thread-actions-light");
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, "notion-comments", "thread-actions-dark");
    }

    [TestMethod]
    [Description("Shell panels (audit, analytics) with the × close, light and dark.")]
    public async Task Phase1_Panels()
    {
        var page = await OpenNotionEditorAsync();
        await page.SetViewportSizeAsync(1440, 900);

        await ClickNotionCommandAsync(page, "notion-audit-open");
        var audit = page.GetByTestId("notion-audit-panel");
        await audit.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await ShotAsync(page, "notion-panels", "audit-panel-light", audit);
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, "notion-panels", "audit-panel-dark", audit);
        await SetThemeAsync(page, "light");
        await CloseByButtonAsync(audit);

        await ClickNotionCommandAsync(page, "notion-analytics-open");
        var analytics = page.GetByTestId("notion-analytics-panel");
        await analytics.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await ShotAsync(page, "notion-panels", "analytics-panel-light", analytics);
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, "notion-panels", "analytics-panel-dark", analytics);
        await CloseByButtonAsync(analytics);
    }

    [TestMethod]
    [Description("Open menus: page settings, block context, row ⋯.")]
    public async Task Phase1_Menus()
    {
        var page = await OpenNotionEditorAsync();
        await page.SetViewportSizeAsync(1440, 900);
        using (var http = DemoApiTestHttp.CreateClient())
        {
            (await http.PostAsync("https://localhost:5100/api/notion/databases/e2e/seed/default", null))
                .EnsureSuccessStatusCode();
        }

        await page.Locator(".tm-npsm-trigger").First.ClickAsync();
        var settings = page.Locator(".tm-npsm").First;
        await settings.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await ShotAsync(page, "notion-menus", "page-settings-light", settings);
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, "notion-menus", "page-settings-dark", settings);
        await SetThemeAsync(page, "light");
        await page.Locator(".tm-npsm__backdrop").ClickAsync(new LocatorClickOptions { Force = true });

        var block = page.Locator("[data-notion-block]").First;
        await block.HoverAsync();
        await block.Locator(".tm-notion-handle__btn").Last.ClickAsync();
        var ctx = page.Locator(".tm-notion-ctx").First;
        await ctx.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await ShotAsync(page, "notion-menus", "block-context-light", ctx);
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, "notion-menus", "block-context-dark", ctx);
        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-notion-ctx").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 3000
        });
        await SetThemeAsync(page, "light");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var db = page.Locator(".tm-db").First;
        await db.ScrollIntoViewIfNeededAsync();
        await page.Locator(".tm-dbt__row").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var menuRow = page.Locator(".tm-dbt__row").First;
        await menuRow.HoverAsync();
        await menuRow.GetByTestId("notion-db-table-row-delete").Locator(".tm-dropdown-trigger").ClickAsync();
        var menu = page.Locator(".tm-dropdown-menu").Last;
        await menu.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await ShotAsync(page, "notion-menus", "row-menu-light");
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, "notion-menus", "row-menu-dark");
    }

    [TestMethod]
    [Description("Table footer cropped to the footer, light and dark.")]
    public async Task Phase1_TableFooter()
    {
        var page = await OpenNotionEditorAsync();
        await page.SetViewportSizeAsync(1440, 900);
        using (var http = DemoApiTestHttp.CreateClient())
        {
            (await http.PostAsync("https://localhost:5100/api/notion/databases/e2e/seed/default", null))
                .EnsureSuccessStatusCode();
        }

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var db = page.Locator(".tm-db").First;
        await db.ScrollIntoViewIfNeededAsync();
        var footer = page.GetByTestId("notion-db-table-footer");
        await footer.ScrollIntoViewIfNeededAsync();
        await footer.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await ShotAsync(page, "notion-database", "table-footer-light", footer);
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, "notion-database", "table-footer-dark", footer);
    }
}
