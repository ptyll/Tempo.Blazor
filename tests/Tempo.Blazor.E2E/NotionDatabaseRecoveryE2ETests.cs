using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
[DoNotParallelize]
public sealed class NotionDatabaseRecoveryE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB9 captures empty, one-record, and many-record baselines for all database views.")]
    public async Task EB9_DatabaseViews_EmptyOneMany_AreCaptured()
    {
        await CaptureViewSetAsync("empty", expectedRecords: 0);
        await CaptureViewSetAsync("one", expectedRecords: 1);
        await CaptureViewSetAsync("many", expectedRecords: 12);
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("EB9 captures filter no-results, sorted, grouped, and no database provider states.")]
    public async Task EB9_DatabaseControlsAndProviderStates_AreCaptured()
    {
        var db = await OpenDatabaseAsync("many");

        await SwitchViewAsync(db, "All Tasks");
        await OpenFilterPanelAsync(db);
        var filterPanel = Page.Locator(".tm-dbfb").First;
        await filterPanel.Locator(".tm-dbfb__add-btn").First.ClickAsync();
        var condition = filterPanel.Locator(".tm-dbfb__cond").First;
        await condition.Locator(".tm-dbfb__cond-value").FillAsync("EB9_VALUE_THAT_MATCHES_NO_RECORDS");
        await Assertions.Expect(db.Locator(".tm-dbt__empty")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await CaptureBaselineAsync("database-views", "table-filter-no-results", db);

        db = await OpenDatabaseAsync("many");
        await SwitchViewAsync(db, "All Tasks");
        await OpenSortPanelAsync(db);
        var sortPanel = Page.Locator(".tm-dbsb").First;
        await sortPanel.Locator(".tm-dbsb__add-btn").ClickAsync();
        await Assertions.Expect(sortPanel.Locator(".tm-dbsb__row").First).ToBeVisibleAsync();
        await Assertions.Expect(db.Locator(".tm-dbt__row").First).ToContainTextAsync("Add dark mode support");
        await CaptureBaselineAsync("database-views", "table-sorted-name", db);

        db = await OpenDatabaseAsync("many");
        await SwitchViewAsync(db, "All Tasks");
        await OpenGroupPanelAsync(db);
        var groupPanel = Page.Locator(".tm-dbgb").First;
        await groupPanel.Locator(".tm-dbgb__field-item")
            .Filter(new LocatorFilterOptions { HasText = "Status" })
            .First
            .ClickAsync();
        await Assertions.Expect(db.Locator(".tm-dbt__group-row").First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await CaptureBaselineAsync("database-views", "table-grouped-status", db);

        db = await OpenDatabaseAsync("many", "?disableDatabaseProvider=true");
        await Assertions.Expect(db.Locator(".tm-db__error")).ToBeVisibleAsync();
        await CaptureBaselineAsync("database-views", "no-database-provider", db);
    }

    private async Task CaptureViewSetAsync(string seed, int expectedRecords)
    {
        var db = await OpenDatabaseAsync(seed);

        await SwitchViewAsync(db, "All Tasks");
        await AssertTableStateAsync(db, expectedRecords);
        await CaptureBaselineAsync("database-views", $"table-{seed}", db);

        await SwitchViewAsync(db, "Board");
        await AssertBoardStateAsync(db, expectedRecords);
        await CaptureBaselineAsync("database-views", $"board-{seed}", db);

        await SwitchViewAsync(db, "List");
        await AssertListStateAsync(db, expectedRecords);
        await CaptureBaselineAsync("database-views", $"list-{seed}", db);

        await SwitchViewAsync(db, "Gallery");
        await AssertGalleryStateAsync(db, expectedRecords);
        await CaptureBaselineAsync("database-views", $"gallery-{seed}", db);

        await SwitchViewAsync(db, "Calendar");
        await AssertCalendarStateAsync(db);
        await CaptureBaselineAsync("database-views", $"calendar-{seed}", db);

        await SwitchViewAsync(db, "Timeline");
        await AssertTimelineStateAsync(db);
        await CaptureBaselineAsync("database-views", $"timeline-{seed}", db);

        await AssertNoHorizontalPageOverflowAsync();
    }

    private async Task<ILocator> OpenDatabaseAsync(string seed, string query = "")
    {
        await ResetNotionDemoAsync();
        await SeedDatabaseAsync(seed);
        await SetViewportAsync(1366, 820);
        await OpenNotionEditorAsync(query);

        var db = Page.Locator(".tm-db").First;
        await db.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
        await db.ScrollIntoViewIfNeededAsync();
        await Page.WaitForSelectorAsync(".tm-db__skeleton", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 30000
        });
        await Page.WaitForTimeoutAsync(400);
        return db;
    }

    private static async Task ResetNotionDemoAsync()
    {
        using var http = DemoApiTestHttp.CreateClient();

        using var response = await http.PostAsync("/api/notion/reset", null);
        response.EnsureSuccessStatusCode();
    }

    private async Task SwitchViewAsync(ILocator db, string viewName)
    {
        var tab = db.Locator(".tm-db__view-tab").Filter(new LocatorFilterOptions { HasText = viewName }).First;
        await tab.ClickAsync();
        await Page.WaitForTimeoutAsync(500);
    }

    private static async Task AssertTableStateAsync(ILocator db, int expectedRecords)
    {
        await Assertions.Expect(db.Locator(".tm-dbt-wrap")).ToBeVisibleAsync();
        if (expectedRecords == 0)
        {
            await Assertions.Expect(db.Locator(".tm-dbt__empty")).ToBeVisibleAsync();
            return;
        }

        await Assertions.Expect(db.Locator(".tm-dbt__row")).ToHaveCountAsync(expectedRecords);
    }

    private static async Task AssertBoardStateAsync(ILocator db, int expectedRecords)
    {
        await Assertions.Expect(db.Locator(".tm-dbb")).ToBeVisibleAsync();
        await Assertions.Expect(db.Locator(".tm-dbb__col").First).ToBeVisibleAsync();
        await Assertions.Expect(db.Locator(".tm-dbb__card")).ToHaveCountAsync(expectedRecords);
    }

    private static async Task AssertListStateAsync(ILocator db, int expectedRecords)
    {
        await Assertions.Expect(db.Locator(".tm-dblv")).ToBeVisibleAsync();
        if (expectedRecords == 0)
        {
            await Assertions.Expect(db.Locator(".tm-dblv__empty")).ToBeVisibleAsync();
            return;
        }

        await Assertions.Expect(db.Locator(".tm-dblv__row")).ToHaveCountAsync(expectedRecords);
    }

    private static async Task AssertGalleryStateAsync(ILocator db, int expectedRecords)
    {
        await Assertions.Expect(db.Locator(".tm-dbg")).ToBeVisibleAsync();
        await Assertions.Expect(db.Locator(".tm-dbg__card")).ToHaveCountAsync(expectedRecords);
        if (expectedRecords == 0)
        {
            await Assertions.Expect(db.Locator(".tm-dbg__add-card")).ToBeVisibleAsync();
        }
    }

    private static async Task AssertCalendarStateAsync(ILocator db)
    {
        await Assertions.Expect(db.Locator(".tm-dbcal")).ToBeVisibleAsync();
        await Assertions.Expect(db.Locator(".tm-dbcal__month-title")).ToBeVisibleAsync();
    }

    private static async Task AssertTimelineStateAsync(ILocator db)
    {
        await Assertions.Expect(db.Locator(".tm-dbt__zoom-group")).ToBeVisibleAsync();
        await Assertions.Expect(db.Locator(".tm-dbt__gantt")).ToBeVisibleAsync();
    }

    private async Task OpenFilterPanelAsync(ILocator db)
    {
        await db.Locator(".tm-db__tool-btn").Filter(new LocatorFilterOptions { HasText = "Filter" }).First.ClickAsync();
        await Assertions.Expect(Page.Locator(".tm-dbfb").First).ToBeVisibleAsync();
    }

    private async Task OpenSortPanelAsync(ILocator db)
    {
        await db.Locator(".tm-db__tool-btn").Filter(new LocatorFilterOptions { HasText = "Sort" }).First.ClickAsync();
        await Assertions.Expect(Page.Locator(".tm-dbsb").First).ToBeVisibleAsync();
    }

    private async Task OpenGroupPanelAsync(ILocator db)
    {
        await db.Locator(".tm-db__tool-btn").Filter(new LocatorFilterOptions { HasText = "Group" }).First.ClickAsync();
        await Assertions.Expect(Page.Locator(".tm-dbgb").First).ToBeVisibleAsync();
    }

    private async Task AssertNoHorizontalPageOverflowAsync()
    {
        var hasOverflow = await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.IsFalse(hasOverflow, "Database views must keep wide content inside their own scroll containers.");
    }
}
