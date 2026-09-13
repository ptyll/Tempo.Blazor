using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SpreadsheetPhase6BaselineScreenshots : BaselineGeneratorTestBase
{
    private static readonly string BaselineDir =
        BaselineOutput.DirectoryFor(null, "spreadsheet", "phase6");

    [TestMethod]
    public async Task names_01_manager()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        await page.ClickAsync(".tm-spreadsheet-toolbar__tab:text-is('Data')");
        await page.ClickAsync("button[title='Name Manager']");
        await page.WaitForSelectorAsync(".tm-spreadsheet-name-manager", new() { State = WaitForSelectorState.Visible });
        await page.WaitForTimeoutAsync(300);

        Directory.CreateDirectory(BaselineDir);
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(BaselineDir, "names-01-manager.png"),
            FullPage = false
        });
    }

    [TestMethod]
    public async Task names_02_namebox()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        await page.WaitForSelectorAsync(".tm-spreadsheet-formula-bar__ref", new() { State = WaitForSelectorState.Visible });
        await page.ClickAsync(".tm-spreadsheet-formula-bar__ref");
        await page.WaitForTimeoutAsync(200);

        Directory.CreateDirectory(BaselineDir);
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(BaselineDir, "names-02-namebox.png"),
            FullPage = false
        });
    }

    [TestMethod]
    public async Task hyperlink_01_dialog()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        await page.ClickAsync(".tm-spreadsheet-toolbar__tab:text-is('Insert')");
        await page.ClickAsync("button[title='Insert link']");
        await page.WaitForSelectorAsync(".tm-spreadsheet-hyperlink", new() { State = WaitForSelectorState.Visible });
        await page.WaitForTimeoutAsync(300);

        Directory.CreateDirectory(BaselineDir);
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(BaselineDir, "hyperlink-01-dialog.png"),
            FullPage = false
        });
    }

    [TestMethod]
    public async Task hyperlink_02_incell()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/spreadsheet");
        await WaitForAppReadyAsync(page);

        // Insert a hyperlink first
        await page.ClickAsync(".tm-spreadsheet-canvas-grid");
        await page.WaitForTimeoutAsync(300);
        await page.ClickAsync(".tm-spreadsheet-toolbar__tab:text-is('Insert')");
        await page.ClickAsync("button[title='Insert link']");
        await page.WaitForSelectorAsync(".tm-spreadsheet-hyperlink", new() { State = WaitForSelectorState.Visible });
        await page.SelectOptionAsync("#hl-type", "Web");
        await page.FillAsync("#hl-target", "https://example.com");
        await page.FillAsync("#hl-display", "Link");
        await page.ClickAsync(".tm-spreadsheet-hyperlink__btn--ok");
        await page.WaitForTimeoutAsync(500);

        Directory.CreateDirectory(BaselineDir);
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(BaselineDir, "hyperlink-02-incell.png"),
            FullPage = false
        });
    }
}
