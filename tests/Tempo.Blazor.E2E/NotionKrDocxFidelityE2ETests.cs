using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public sealed class NotionKrDocxFidelityE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("F7 visually verifies KR.docx merged-table and Impact color fidelity.")]
    public async Task F7_KrDocxTables_RenderMergedAndImpactFormattingAcrossThemes()
    {
        var page = await OpenNotionEditorAsync(1440, 1000);
        await SeedKrFidelityPageAsync();
        var threshold = page
            .Locator("[data-block-id='f7000000-0000-0000-0000-000000000010']")
            .First;
        var impact = page
            .Locator("[data-block-id='f7000000-0000-0000-0000-000000000020']")
            .First;

        await Assertions.Expect(threshold).ToContainTextAsync(
            "Threshold / Typ udalosti");
        await Assertions.Expect(impact).ToContainTextAsync("Very high");
        await Assertions.Expect(Cell(threshold, 2, 1))
            .ToHaveAttributeAsync("colspan", "7");
        await Assertions.Expect(Cell(threshold, 2, 1))
            .ToHaveAttributeAsync("rowspan", "4");
        await Assertions.Expect(Cell(threshold, 1, 0))
            .ToHaveCSSAsync("background-color", "rgb(253, 233, 217)");
        await Assertions.Expect(Cell(threshold, 1, 1))
            .ToHaveCSSAsync("background-color", "rgb(252, 213, 180)");
        await Assertions.Expect(Cell(impact, 1, 0))
            .ToHaveCSSAsync("background-color", "rgb(255, 0, 0)");
        await Assertions.Expect(Cell(impact, 1, 1))
            .ToHaveCSSAsync("background-color", "rgb(255, 51, 0)");
        await Assertions.Expect(Cell(impact, 2, 0))
            .ToHaveCSSAsync("background-color", "rgb(255, 192, 0)");
        await Assertions.Expect(Cell(impact, 3, 0))
            .ToHaveCSSAsync("background-color", "rgb(255, 255, 0)");
        await Assertions.Expect(Cell(impact, 4, 0))
            .ToHaveCSSAsync("background-color", "rgb(118, 147, 60)");
        await Assertions.Expect(Cell(impact, 1, 1))
            .ToHaveCSSAsync("text-align", "center");
        Assert.IsTrue(
            await Cell(impact, 1, 0).Locator("strong").CountAsync() == 1,
            "Impact labels imported from KR.docx should retain bold markup.");
        await CaptureAsync(page, "normal-light-desktop");
        await CaptureTableAsync(threshold, "normal-light-desktop-threshold-left");
        await CaptureTableAsync(impact, "normal-light-desktop-impact");

        var desktopWrapper = threshold.Locator(".tm-notion-table-block__wrapper");
        await desktopWrapper.EvaluateAsync(
            "element => element.scrollLeft = element.scrollWidth");
        await CaptureTableAsync(threshold, "normal-light-desktop-threshold-right");
        await desktopWrapper.EvaluateAsync("element => element.scrollLeft = 0");

        await SetThemeAsync(page, true);
        await CaptureAsync(page, "normal-dark-desktop");
        await CaptureTableAsync(impact, "normal-dark-desktop-impact");
        await SetThemeAsync(page, false);

        await page.SetViewportSizeAsync(390, 844);
        var visibleSidebar = page.Locator(".tm-notion-sidebar--visible").First;
        if (await visibleSidebar.CountAsync() > 0)
        {
            var overlay = page.Locator(".tm-notion-sidebar-overlay").First;
            if (await overlay.IsVisibleAsync())
            {
                await overlay.ClickAsync(new LocatorClickOptions { Force = true });
            }
            else
            {
                await page.Locator(".tm-notion-sidebar-toggle").First
                    .EvaluateAsync("element => element.click()");
            }
            await page.Locator(".tm-notion-sidebar--hidden").First.WaitForAsync(
                new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Attached,
                    Timeout = 5_000
                });
        }
        await threshold.ScrollIntoViewIfNeededAsync();
        var wrapper = threshold.Locator(".tm-notion-table-block__wrapper");
        var scrollWidth = await wrapper.EvaluateAsync<double>(
            "element => element.scrollWidth");
        var clientWidth = await wrapper.EvaluateAsync<double>(
            "element => element.clientWidth");
        Assert.IsTrue(
            scrollWidth > clientWidth,
            "The eight-column KR table should remain reachable through local horizontal scrolling.");
        await CaptureAsync(page, "edge-light-mobile");
        await CaptureTableAsync(threshold, "edge-light-mobile-threshold-left");
        await wrapper.EvaluateAsync("element => element.scrollLeft = element.scrollWidth");
        await CaptureTableAsync(threshold, "edge-light-mobile-threshold-right");
    }

    private static ILocator Cell(ILocator table, int row, int column)
        => table.Locator(
            $"[data-tm-row='{row}'][data-tm-col='{column}']").First;

    private static async Task SetThemeAsync(IPage page, bool dark)
    {
        await page.EvaluateAsync(
            """
            dark => {
                document.documentElement.toggleAttribute('data-theme', dark);
                if (dark) {
                    document.documentElement.setAttribute('data-theme', 'dark');
                    document.body.classList.add('tm-dark');
                } else {
                    document.documentElement.removeAttribute('data-theme');
                    document.body.classList.remove('tm-dark');
                }
            }
            """,
            dark);
        await page.WaitForTimeoutAsync(200);
    }

    private async Task CaptureAsync(IPage page, string name)
    {
        var output = BaselineOutput.DirectoryFor(TestContext, "notion",
            "kr-docx-fidelity");
        Directory.CreateDirectory(output);
        var fullPath = Path.Combine(output, $"{name}.png");
        var regionPath = Path.Combine(output, $"{name}.region.png");
        var region = page.Locator(".tm-notion-page").First;
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            FullPage = true,
            Type = ScreenshotType.Png
        });
        await region.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = regionPath,
            Type = ScreenshotType.Png
        });
        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(regionPath);
    }

    private async Task CaptureTableAsync(ILocator table, string name)
    {
        var output = BaselineOutput.DirectoryFor(TestContext, "notion",
            "kr-docx-fidelity");
        Directory.CreateDirectory(output);
        var path = Path.Combine(output, $"{name}.png");
        await table.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Type = ScreenshotType.Png
        });
        TestContext.AddResultFile(path);
    }
}
