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

        // N210: the _framework/blazor script tag is in the HTML already at SSR prerender — waiting
        // for it says nothing about hydration. [data-blazor-ready] is set by the shared MainLayout
        // from OnAfterRenderAsync(firstRender), which prerendering never calls, so the attribute
        // lands exactly when an interactive runtime has rendered the layout.
        await Assertions.Expect(page.Locator("body[data-blazor-ready]")).ToBeAttachedAsync(
            new() { Timeout = 15000 });

        // Hydration proof that cannot pass on SSR markup: [data-testid='hydration-probe'] is the
        // home page's "Explore Components" button whose @onclick calls Nav.NavigateTo("buttons").
        // Only a live Blazor runtime wires that handler — a click on prerendered markup (or with
        // no runtime at all) is a no-op, so the navigation below can only happen post-hydration.
        // The old `if (await buttons.CountAsync() > 0)` branch is deliberately gone: a missing
        // interactive element is a finding, not a reason to pass untested.
        var probe = page.Locator("[data-testid='hydration-probe']");
        await Assertions.Expect(probe).ToBeVisibleAsync(new() { Timeout = 15000 });
        await probe.ClickAsync();

        // In-page SPA navigation to /buttons — the URL changes and the target page renders.
        await page.WaitForURLAsync("**/buttons", new PageWaitForURLOptions { Timeout = 15000 });
        await Assertions.Expect(page.Locator("[data-testid='buttons-variants-card']"))
            .ToBeVisibleAsync(new() { Timeout = 15000 });

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

        // Wait for the interactive runtime to render the layout — stateful, not a fixed sleep.
        await Assertions.Expect(page.Locator("body[data-blazor-ready]")).ToBeAttachedAsync(
            new() { Timeout = 15000 });

        // N210: the demo page mounts several TmScheduler instances, each with its own toolbar —
        // scope every locator to the "Interactive Demo" section so a click lands on the scheduler
        // under test, not on whichever toolbar happens to sit first in the DOM.
        var interactiveSection = page.Locator("section.demo-section", new PageLocatorOptions
        {
            Has = page.Locator("h2", new PageLocatorOptions { HasTextString = "Interactive Demo" })
        });
        var interactiveScheduler = interactiveSection.Locator(".tm-scheduler");
        await Assertions.Expect(interactiveScheduler).ToBeVisibleAsync(new() { Timeout = 15000 });

        // View → the class its body renders. The previous OR locator listed .tm-scheduler-body —
        // a wrapper present in EVERY view — so it matched before any switching happened. The map
        // asserts the view-specific class absent before the click and present after it, which is
        // what "the view actually switched" means. The sequence starts from the bound default
        // (Month) and never clicks the already-active view, so the before-assert always has teeth.
        var transitions = new (string View, string ExpectedClass)[]
        {
            ("Week", "tm-scheduler-week"),
            ("Day", "tm-scheduler-day"),
            ("Timeline", "tm-scheduler-timeline"),
            ("Month", "tm-scheduler-month"),
        };

        foreach (var (view, expectedClass) in transitions)
        {
            var targetView = interactiveScheduler.Locator($".{expectedClass}");
            Assert.AreEqual(0, await targetView.CountAsync(),
                $".{expectedClass} must not exist inside the interactive scheduler before switching to {view}");

            await interactiveScheduler.Locator($"button[data-view='{view}']").ClickAsync();

            await Assertions.Expect(targetView.First)
                .ToBeVisibleAsync(new() { Timeout = 15000 });
            Assert.AreEqual(1, await targetView.CountAsync(),
                $"exactly one .{expectedClass} body expected after switching to {view}");
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
