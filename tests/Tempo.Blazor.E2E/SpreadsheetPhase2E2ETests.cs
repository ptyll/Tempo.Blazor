using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 2 E2E coverage: status-bar aggregation, zoom, and find/replace against the live canvas
/// engine on the WASM demo's <c>/spreadsheet</c> page (the <c>spreadsheet-demo</c> canvas instance).
/// </summary>
public partial class SpreadsheetE2ETests
{
    private static ILocator DemoGrid(IPage page)
        => page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-canvas-grid");

    private static ILocator DemoComponent(IPage page)
        => page.Locator("[data-testid='spreadsheet-demo']");

    private async Task<IPage> OpenCanvasDemoAsync()
    {
        var page = await CreatePageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/spreadsheet");
        var grid = DemoGrid(page);
        await WaitForCanvasGridReadyAsync(page, grid);
        return page;
    }

    private static Task<double> ReadCellWidthAsync(ILocator grid, int row, int col)
        => grid.EvaluateAsync<double>(
            @"(el, args) => {
                const key = `${Number(args.row)}:${Number(args.col)}`;
                const c = el.__tmSpreadsheetCanvas?.sheetState?.cellStore?.cells?.get(key);
                return c ? Number(c.width ?? c.Width ?? 0) : 0;
            }",
            new { row, col });

    [TestMethod]
    public async Task StatusBar_NumericRange_ShowsAggregations()
    {
        var page = await OpenCanvasDemoAsync();
        var grid = DemoGrid(page);

        // Use the empty D column so the demo's sample data in A/B does not interfere.
        await EditCanvasCellAsync(page, grid, "D1", "10");
        await EditCanvasCellAsync(page, grid, "D2", "20");
        await EditCanvasCellAsync(page, grid, "D3", "30");

        // Select D1:D3.
        var d1 = await GetCanvasCellCenterAsync(grid, "D1");
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = d1.X, Y = d1.Y } });
        await WaitForCanvasActiveRefAsync(grid, "D1");
        await page.Keyboard.PressAsync("Shift+ArrowDown");
        await page.Keyboard.PressAsync("Shift+ArrowDown");

        var aggregations = DemoComponent(page).Locator(".tm-spreadsheet-statusbar__aggregations");
        await page.WaitForFunctionAsync(
            "el => el.textContent.includes('Sum') && el.textContent.includes('60')",
            await aggregations.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000 });

        var text = await aggregations.TextContentAsync() ?? string.Empty;
        StringAssert.Contains(text, "Average");
        StringAssert.Contains(text, "20");
        StringAssert.Contains(text, "Count");
        StringAssert.Contains(text, "3");
    }

    [TestMethod]
    public async Task StatusBar_ZoomIn_EnlargesCellsAndUpdatesPercent()
    {
        var page = await OpenCanvasDemoAsync();
        var grid = DemoGrid(page);
        var percent = DemoComponent(page).Locator(".tm-spreadsheet-statusbar__zoom-percent");

        (await percent.TextContentAsync())!.Trim().Should().Be("100%");
        var widthBefore = await ReadCellWidthAsync(grid, 0, 0);
        widthBefore.Should().BeGreaterThan(0);

        var zoomIn = DemoComponent(page).Locator(".tm-spreadsheet-statusbar__zoom-btn").Nth(1);
        for (var i = 0; i < 5; i++)
            await zoomIn.ClickAsync();

        await page.WaitForFunctionAsync(
            "el => el.textContent.trim() === '150%'",
            await percent.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000 });

        await page.WaitForTimeoutAsync(300);
        var widthAfter = await ReadCellWidthAsync(grid, 0, 0);
        widthAfter.Should().BeGreaterThan(widthBefore * 1.4,
            $"zoom 150% should enlarge cell width (before {widthBefore}, after {widthAfter})");
    }

    [TestMethod]
    public async Task StatusBar_ZoomPercentClick_ResetsToHundred()
    {
        var page = await OpenCanvasDemoAsync();
        var percent = DemoComponent(page).Locator(".tm-spreadsheet-statusbar__zoom-percent");

        var zoomIn = DemoComponent(page).Locator(".tm-spreadsheet-statusbar__zoom-btn").Nth(1);
        await zoomIn.ClickAsync();
        await zoomIn.ClickAsync();
        await page.WaitForFunctionAsync(
            "el => el.textContent.trim() === '120%'",
            await percent.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000 });

        await percent.ClickAsync();
        await page.WaitForFunctionAsync(
            "el => el.textContent.trim() === '100%'",
            await percent.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }

    [TestMethod]
    public async Task FindReplace_CtrlF_OpensPanelAndNavigates()
    {
        var page = await OpenCanvasDemoAsync();
        var grid = DemoGrid(page);

        // Use a token that does not occur in the demo's sample data (A1/B1/A2/B2).
        await EditCanvasCellAsync(page, grid, "D1", "zzqqA");
        await EditCanvasCellAsync(page, grid, "D2", "zzqqB");
        await EditCanvasCellAsync(page, grid, "D3", "banana");

        await grid.ClickAsync();
        await page.Keyboard.PressAsync("Control+f");

        var panel = DemoComponent(page).Locator(".tm-spreadsheet-find");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var query = panel.Locator(".tm-spreadsheet-find__query");
        await query.FillAsync("zzqq");

        var counter = panel.Locator(".tm-spreadsheet-find__counter");
        await page.WaitForFunctionAsync(
            "el => /\\d+ of \\d+/.test(el.textContent)",
            await counter.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000 });

        var counterText = await counter.TextContentAsync() ?? string.Empty;
        StringAssert.Contains(counterText, "of 2");

        // The first hit (D1) should be the active cell.
        await WaitForCanvasActiveRefAsync(grid, "D1");

        // Enter cycles to the next hit (D2).
        await query.PressAsync("Enter");
        await WaitForCanvasActiveRefAsync(grid, "D2");

        // Escape closes the panel.
        await query.PressAsync("Escape");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
    }

    [TestMethod]
    public async Task FindReplace_Replace_ChangesCellValue()
    {
        var page = await OpenCanvasDemoAsync();
        var grid = DemoGrid(page);

        await EditCanvasCellAsync(page, grid, "D1", "alpha");

        await grid.ClickAsync();
        await page.Keyboard.PressAsync("Control+h");

        var panel = DemoComponent(page).Locator(".tm-spreadsheet-find");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await panel.Locator(".tm-spreadsheet-find__query").FillAsync("alpha");
        await panel.Locator(".tm-spreadsheet-find__replace").FillAsync("omega");

        var counter = panel.Locator(".tm-spreadsheet-find__counter");
        await page.WaitForFunctionAsync(
            "el => /1 of 1/.test(el.textContent)",
            await counter.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000 });

        // Click the "Replace" button (first text button in the actions row).
        await panel.Locator(".tm-spreadsheet-find__btn--text").First.ClickAsync();

        await page.WaitForFunctionAsync(
            @"el => {
                const c = el.__tmSpreadsheetCanvas?.sheetState?.cellStore?.cells?.get('0:3');
                const v = c ? (c.value ?? c.Value ?? '') : '';
                return String(v).includes('omega');
            }",
            await grid.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 10000 });
    }
}
