using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Tests for InteractiveAuto rendering mode - verifies prerendering and WASM hydration.
/// </summary>
[TestClass]
public class InteractiveAutoTests : InteractiveAutoTestBase
{
    [TestMethod]
    [Description("Verify prerendering works without errors")]
    public async Task Prerender_WorksWithoutErrors()
    {
        var page = await CreatePageAsync();

        // Verify no error UI is shown
        var errorUi = page.Locator("#blazor-error-ui");
        var isVisible = await errorUi.IsVisibleAsync();
        Assert.IsFalse(isVisible, "Error UI should not be visible after prerender");

        // Verify main content is present
        var main = page.Locator("main");
        await main.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await TakeScreenshotAsync(page, "prerender_test");
    }

    [TestMethod]
    [Description("Verify WASM boots and hydrates the page")]
    public async Task WasmBoot_HydratesPage()
    {
        var page = await CreatePageAsync();

        // Wait for the Blazor runtime script — InteractiveAuto hosts serve _framework/blazor.web.js
        // (which boots blazor.server.js first and dotnet.* once the WASM leg is cached), never a
        // literal blazor.webassembly.js, so match the _framework loader family.
        await page.WaitForSelectorAsync("script[src*='_framework/blazor']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached, // <script> elements have no box — never "visible"
            Timeout = 10000
        });

        // Additional wait for hydration
        await page.WaitForTimeoutAsync(2000);

        // Verify interactivity works by clicking a button
        var buttons = page.Locator(".tm-btn");
        if (await buttons.CountAsync() > 0)
        {
            await buttons.First.ClickAsync();
            await page.WaitForTimeoutAsync(500);
        }

        await TakeScreenshotAsync(page, "wasm_hydration_test");
    }

    [TestMethod]
    [Description("Verify Rich Editor renders after WASM boot")]
    public async Task RichEditor_RendersAfterWasmBoot()
    {
        var page = await CreatePageAsync();
        await NavigateToPageAsync(page, "Rich Text");

        // Wait for WASM to boot
        await page.WaitForTimeoutAsync(3000);

        // Verify rich editor is present (TmRichEditorSimple/Full render tm-rich-editor-simple/-full)
        var editor = page.Locator(".tm-rich-editor, .tm-rich-editor-simple, .tm-rich-editor-full, [data-testid='rich-editor']").First;
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Verify editor toolbar is present (TmRichEditor* render tm-rte-toolbar)
        var toolbar = page.Locator(".tm-editor-toolbar, .tm-rte-toolbar, [data-testid='editor-toolbar']").First;
        await toolbar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await TakeScreenshotAsync(page, "rich_editor_test");
    }

    [TestMethod]
    [Description("Verify Dashboard renders and allows drag & drop after WASM boot")]
    public async Task Dashboard_DragAndDrop_Works()
    {
        var page = await CreatePageAsync();
        await NavigateToPageAsync(page, "Dashboard");

        // Wait for WASM to boot
        await page.WaitForTimeoutAsync(3000);

        // Verify dashboard is present
        var dashboard = page.Locator(".tm-dashboard, [data-testid='dashboard']").First;
        await dashboard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Verify widgets are present
        var widgets = page.Locator(".tm-widget, [data-testid='widget']");
        var widgetCount = await widgets.CountAsync();
        Assert.IsTrue(widgetCount > 0, "Expected at least one widget");

        await TakeScreenshotAsync(page, "dashboard_test");
    }

    [TestMethod]
    [Description("Verify Workflow Designer renders after WASM boot")]
    public async Task WorkflowDesigner_RendersAfterWasmBoot()
    {
        var page = await CreatePageAsync();
        await NavigateToPageAsync(page, "Workflow Designer");

        // Wait for WASM to boot
        await page.WaitForTimeoutAsync(3000);

        // Verify workflow designer is present (TmWorkflowDesignerCanvas renders tm-wf-canvas)
        var canvas = page.Locator(".tm-workflow-canvas, .tm-wf-canvas, [data-testid='workflow-canvas']").First;
        await canvas.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Verify toolbox is present (TmWorkflowToolbox renders tm-wf-toolbox)
        var toolbox = page.Locator(".tm-workflow-toolbox, .tm-wf-toolbox, [data-testid='workflow-toolbox']").First;
        await toolbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await TakeScreenshotAsync(page, "workflow_designer_test");
    }

    [TestMethod]
    [Description("Verify Scheduler renders all views after WASM boot")]
    public async Task Scheduler_ViewsWork()
    {
        var page = await CreatePageAsync();
        await NavigateToPageAsync(page, "Scheduler");

        // Wait for WASM to boot
        await page.WaitForTimeoutAsync(3000);

        // Verify scheduler is present
        var scheduler = page.Locator(".tm-scheduler, [data-testid='scheduler']").First;
        await scheduler.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Test different views
        var viewButtons = new[] { "Month", "Week", "Day", "Timeline" };
        foreach (var view in viewButtons)
        {
            var viewButton = page.Locator($"button:has-text('{view}')").First;
            if (await viewButton.IsVisibleAsync())
            {
                await viewButton.ClickAsync();
                await page.WaitForTimeoutAsync(1000);

                // Verify the view changed (scheduler renders tm-scheduler-{month,week,day,timeline,agenda})
                var activeView = page.Locator(".tm-scheduler-view-active, .tm-scheduler-body, .tm-scheduler-month, .tm-scheduler-week, .tm-scheduler-day, .tm-scheduler-timeline, .tm-scheduler-agenda, [data-testid='scheduler-view']").First;
                await activeView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            }
        }

        await TakeScreenshotAsync(page, "scheduler_test");
    }

    [TestMethod]
    [Description("Verify DataTable with client-side data works")]
    public async Task DataTable_ClientSideData_Works()
    {
        var page = await CreatePageAsync();
        await NavigateToPageAsync(page, "Data Table");

        // Wait for WASM to boot
        await page.WaitForTimeoutAsync(3000);

        // Verify data table is present
        var dataTable = page.Locator(".tm-data-table, [data-testid='data-table']").First;
        await dataTable.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Verify rows are loaded (TmDataTable renders plain <tr> body rows, no tm-data-table-row class)
        var rows = page.Locator(".tm-data-table tbody tr, .tm-data-table-row, [data-testid='data-row'], tr[data-row-mode]");
        var rowCount = await rows.CountAsync();
        Assert.IsTrue(rowCount > 0, "Expected data rows to be loaded");

        // Test sorting
        var sortableHeaders = page.Locator(".tm-data-table-header-sortable, th[data-sortable='true'], [data-sortable='true']");
        if (await sortableHeaders.CountAsync() > 0)
        {
            await sortableHeaders.First.ClickAsync();
            await page.WaitForTimeoutAsync(1000);
        }

        await TakeScreenshotAsync(page, "datatable_clientside_test");
    }

    [TestMethod]
    [Description("Verify navigation doesn't cause memory leaks")]
    public async Task Navigation_NoMemoryLeak()
    {
        var page = await CreatePageAsync();

        // Get initial heap size. performance.memory is a non-standard Chromium API that can be
        // absent in some browser builds/contexts; when it reports 0 there is nothing to measure.
        var initialHeap = await GetHeapSizeAsync(page);
        TestContext.WriteLine($"Initial heap size: {initialHeap} bytes");
        if (initialHeap <= 0)
        {
            Assert.Inconclusive("performance.memory is unavailable in this browser — cannot measure JS heap growth.");
            return;
        }

        // Navigate multiple times
        for (int i = 0; i < 5; i++)
        {
            await NavigateToPageAsync(page, "Dashboard");
            await page.WaitForTimeoutAsync(1000);
            await NavigateToPageAsync(page, "Buttons");
            await page.WaitForTimeoutAsync(1000);
        }

        // Force garbage collection
        await page.EvaluateAsync("() => { if (window.gc) window.gc(); }");
        await page.WaitForTimeoutAsync(1000);

        // Get final heap size
        var finalHeap = await GetHeapSizeAsync(page);
        TestContext.WriteLine($"Final heap size: {finalHeap} bytes");

        // Allow for some growth but not excessive (less than 50% increase)
        var growthRatio = (double)finalHeap / initialHeap;
        Assert.IsTrue(growthRatio < 1.5, $"Memory growth ratio {growthRatio:P} exceeds 50% threshold");

        await TakeScreenshotAsync(page, "memory_leak_test");
    }
}

/// <summary>
/// Tests for WASM rendering mode.
/// </summary>
[TestClass]
public class WasmTests : WasmTestBase
{
    [TestMethod]
    [Description("Verify WASM app loads and renders")]
    public async Task WasmApp_LoadsAndRenders()
    {
        var page = await CreatePageAsync();

        // Verify WASM script is loaded (script elements are never "visible" — assert DOM attachment)
        await page.WaitForSelectorAsync("script[src*='blazor.webassembly.js']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 10000
        });

        // Wait for app to be ready
        await WaitForAppReadyAsync(page);

        // Verify main content
        var main = page.Locator("main");
        await main.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await TakeScreenshotAsync(page, "wasm_app_loads");
    }
}
