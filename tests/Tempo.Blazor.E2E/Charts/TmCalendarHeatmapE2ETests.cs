using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// HTTPS WASM coverage and visual baselines for the Calendar Heatmap demo.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmCalendarHeatmapE2ETests : WasmTestBase
{
    [TestMethod]
    [TestCategory("Smoke")]
    public async Task CalendarHeatmap_DemoIsInteractiveReadableAndResponsive()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);

        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        await GotoAppReadyAsync(page, $"{BaseUrl}/charts");

        var section = page.Locator("[data-testid='calendar-heatmap']");
        await section.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        var annual = section.Locator("[data-testid='calendar-heatmap-annual']");
        var range = section.Locator("[data-testid='calendar-heatmap-range']");

        Assert.AreEqual(365, await annual.Locator(".tm-calendar-heatmap__day").CountAsync());
        Assert.AreEqual(90, await range.Locator(".tm-calendar-heatmap__day").CountAsync());
        Assert.AreEqual(12, await annual.Locator(".tm-calendar-heatmap__month-label").CountAsync());
        Assert.AreEqual(3, await range.Locator(".tm-calendar-heatmap__month-label").CountAsync());
        Assert.AreEqual(1, await annual.Locator(".tm-calendar-heatmap--primary").CountAsync());
        Assert.AreEqual(1, await range.Locator(".tm-calendar-heatmap--danger").CountAsync());

        var monthBounds = await annual.Locator(".tm-calendar-heatmap__month-label")
            .EvaluateAllAsync<double[]>(
                "labels => labels.flatMap(label => { const rect = label.getBoundingClientRect(); return [rect.left, rect.right]; })");
        for (var index = 0; index < monthBounds.Length - 2; index += 2)
        {
            Assert.IsTrue(monthBounds[index + 1] <= monthBounds[index + 2],
                $"Month labels {index / 2 + 1} and {index / 2 + 2} must not overlap.");
        }

        var annualColors = await annual.Locator(".tm-calendar-heatmap__day")
            .EvaluateAllAsync<string[]>("cells => [...new Set(cells.map(cell => getComputedStyle(cell).backgroundColor))]");
        Assert.IsTrue(annualColors.Length >= 5,
            $"The annual demo should expose the empty state and four intensity colors; got {string.Join(", ", annualColors)}.");

        await annual.Locator("[data-date='2026-01-01']").ClickAsync();
        await Assertions.Expect(section.Locator("[data-testid='calendar-heatmap-clicked']"))
            .ToContainTextAsync("1/1/2026");

        var emptyDay = annual.Locator(".tm-calendar-heatmap__day--level-0").First;
        await emptyDay.ClickAsync();
        await Assertions.Expect(section.Locator("[data-testid='calendar-heatmap-clicked']"))
            .ToContainTextAsync("No data");
        await CaptureSectionAsync(section, "calendar-heatmap-light.png");

        await page.Locator("button[aria-label='Switch to dark mode']:visible").ClickAsync();
        await page.Locator("[data-theme='dark']").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var emptyContrast = await emptyDay.EvaluateAsync<string[]>(
            "cell => [getComputedStyle(cell).backgroundColor, getComputedStyle(cell).borderColor]");
        Assert.AreNotEqual(emptyContrast[0], emptyContrast[1],
            "Empty days need a visible border against their dark-mode background.");
        await CaptureSectionAsync(section, "calendar-heatmap-dark.png");

        await page.SetViewportSizeAsync(390, 844);
        await annual.ScrollIntoViewIfNeededAsync();
        var pageOverflows = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.IsFalse(pageOverflows,
            "The annual heatmap should scroll inside its own root instead of widening the page.");
        await CaptureSectionAsync(annual, "calendar-heatmap-narrow.png");

        Assert.AreEqual(0, errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", errors));
    }

    private static async Task CaptureSectionAsync(ILocator locator, string fileName)
    {
        var directory = Path.Combine(
            FindRepoRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "__screenshots__",
            "calendar-heatmap");
        Directory.CreateDirectory(directory);
        await locator.ScrollIntoViewIfNeededAsync();
        await locator.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, fileName),
            Type = ScreenshotType.Png
        });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
