using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Generates baseline screenshots for the canvas-only TmSpreadsheet component.
/// After the Phase 0 canvas-only consolidation there is a single rendering engine
/// (the JavaScript canvas engine), so these baselines capture the live canvas surface
/// rather than DOM cells. Run with the BaselineGeneration category against a running
/// WASM demo to (re)generate the PNG baselines under artifacts/baseline/spreadsheet.
/// </summary>
[TestClass]
public partial class SpreadsheetBaselineScreenshots : BaselineGeneratorTestBase
{
    private static string OutputDir
    {
        get
        {
            var dir = BaselineOutput.DirectoryFor(null, "spreadsheet");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private async Task<IPage> OpenSpreadsheetAsync(int width = 1600, int height = 900)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}/spreadsheet", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync(
            "[data-testid='spreadsheet-demo'] .tm-spreadsheet-canvas-grid canvas.tm-spreadsheet-canvas-grid__canvas",
            new PageWaitForSelectorOptions
            {
                State = WaitForSelectorState.Attached,
                Timeout = 60000
            });
        await page.WaitForTimeoutAsync(800);
        return page;
    }

    private static async Task CaptureAsync(IPage page, string fileName, ILocator? locator = null)
    {
        await page.WaitForTimeoutAsync(400);
        var path = Path.Combine(OutputDir, fileName);
        if (locator is not null)
        {
            await locator.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = path,
                Type = ScreenshotType.Png,
                OmitBackground = false
            });
        }
        else
        {
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = path,
                Type = ScreenshotType.Png,
                FullPage = false
            });
        }
        Console.WriteLine($"[baseline] wrote {path}");
    }

    [TestMethod]
    public async Task Baseline_Spreadsheet_DefaultSurface()
    {
        var page = await OpenSpreadsheetAsync();
        var component = page.Locator("[data-testid='spreadsheet-demo']");
        await CaptureAsync(page, "baseline-00-default.png", component);
    }

    [TestMethod]
    public async Task Baseline_Spreadsheet_FullViewport()
    {
        var page = await OpenSpreadsheetAsync();
        await CaptureAsync(page, "baseline-01-viewport.png");
    }

    [TestMethod]
    public async Task Baseline_Spreadsheet_SelectedCell()
    {
        var page = await OpenSpreadsheetAsync();
        // Focus the interactive grid root (tabindex=0) and move the active cell with the keyboard,
        // which the canvas engine handles in JS. This reliably moves the selection away from the
        // default A1 so the captured selection overlay differs.
        var grid = page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-canvas-grid");
        await grid.FocusAsync();
        for (var i = 0; i < 4; i++)
        {
            await page.Keyboard.PressAsync("ArrowDown");
        }
        for (var i = 0; i < 3; i++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
        }
        await page.WaitForTimeoutAsync(500);
        var component = page.Locator("[data-testid='spreadsheet-demo']");
        await CaptureAsync(page, "baseline-02-selected-cell.png", component);
    }

    [TestMethod]
    public async Task Baseline_Spreadsheet_TypeDetection()
    {
        var page = await OpenSpreadsheetAsync();
        var grid = page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-canvas-grid");
        await grid.FocusAsync();
        // Move to an empty column (D1) to avoid the demo's sample data in columns A–B.
        for (var i = 0; i < 3; i++)
            await page.Keyboard.PressAsync("ArrowRight");

        // Type values that A5 type detection should recognise: numbers/dates/currency align right,
        // booleans centre, plain text aligns left.
        string[] values = { "123", "1234.56", "50%", "2024-02-01", "$10", "TRUE", "Plain text" };
        foreach (var value in values)
        {
            await page.Keyboard.TypeAsync(value);
            await page.Keyboard.PressAsync("Enter");
            await page.WaitForTimeoutAsync(150);
        }

        await page.WaitForTimeoutAsync(500);
        var component = page.Locator("[data-testid='spreadsheet-demo']");
        await CaptureAsync(page, "typedetect-01-column.png", component);
    }
}
